using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>Every model decal the client holds, per entity — `CStudioRender`'s decal lists (B415).</summary>
/// <remarks>
/// **The limits are `CStudioRender::AddDecal`'s, read out of `studiorender.dll`** (`0x180004c80`), with
/// <c>r_maxmodeldecal</c> at its shipped <c>"50"</c> (`engine.dll` `0x180006fe0`):
///
/// <code>
/// if ( total ≥ r_maxmodeldecal · 1.5 )           retire the oldest decal of any model
/// if ( this model's count ≥ r_maxmodeldecal )     retire this model's oldest
/// while ( this material's corners on the model &gt; 2048 )  retire this model's oldest
/// </code>
///
/// A decal's corners are the model's own vertices carrying the decal's UV and material, so the renderer draws them
/// with the model's bones. They light as an unlit mod2x decal does: `DecalModulate` samples its texture and nothing
/// else (<see cref="UnlitLight"/>).
/// </remarks>
public sealed class ModelDecals
{
    /// <summary><c>r_maxmodeldecal</c>'s shipped value.</summary>
    public const int MaximumPerModel = 50;

    /// <summary>The most corners one material may hold on one model — `0x800`.</summary>
    public const int MaximumCornersPerMaterial = 2048;

    private readonly Dictionary<int, List<Decal>> _byEntity = [];
    private readonly LinkedList<(int Entity, Decal Decal)> _age = [];
    private readonly Dictionary<int, (IReadOnlyList<WorldVertex> Vertices, IReadOnlyList<WorldBatch> Batches)> _built = [];

    /// <summary>A mod2x decal's corner light, so this pipeline reproduces the engine's blend — the renderer's own figure.</summary>
    public float UnlitLight { get; init; } = 1f;

    /// <summary>How many decals are held across every model.</summary>
    public int Count => _age.Count;

    /// <summary>Changes whenever a decal is kept or cleared, so an unchanged pool costs its reader nothing.</summary>
    public int Version { get; private set; }

    /// <summary>The keys of every model holding decals.</summary>
    public IEnumerable<int> Models => _byEntity.Keys;

    /// <summary>Projects one decal onto a model and keeps it — `CModelRender::AddDecal` → `CStudioRender::AddDecal`.</summary>
    /// <param name="entity">The entity whose model takes it.</param>
    /// <param name="model">The model's packed vertices, a triangle list.</param>
    /// <param name="poseToWorld">The model's skinning matrices at this moment.</param>
    /// <param name="start">The ray's start.</param>
    /// <param name="delta">The ray's length and direction, already bloated as `AddDecal` bloats it.</param>
    /// <param name="radius">Half the decal's larger side, scaled by `$decalScale`.</param>
    /// <param name="material">The drawn material's table index — the decal's `$modelmaterial`.</param>
    /// <param name="drawn">Whether a vertex belongs to a drawn body part, or null for all of them.</param>
    /// <returns>Whether any triangle took the decal.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool Add(
        int entity,
        IReadOnlyList<WorldVertex> model,
        IReadOnlyList<float[]> poseToWorld,
        Vector3 start,
        Vector3 delta,
        float radius,
        int material,
        Func<int, bool>? drawn = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(poseToWorld);

        RetireForOneMore(entity);

        IReadOnlyList<StudioDecalCorner> corners = StudioDecalProjection.Project(
            model, poseToWorld, start, delta, Vector3.UnitZ, radius, noPokeThru: false, drawn);

        if (corners.Count == 0)
        {
            return false;
        }

        WorldVertex[] vertices = new WorldVertex[corners.Count];

        for (int index = 0; index < corners.Count; index++)
        {
            StudioDecalCorner corner = corners[index];

            vertices[index] = Unlit(model[corner.Vertex] with { U = corner.U, V = corner.V });
        }

        Keep(entity, material, vertices);

        return true;
    }

    /// <summary>
    /// Projects one decal onto a one-bone model already placed in the world — a static prop, whose `AddDecal` takes the
    /// clipped path with `noPokeThru` (<see cref="StudioDecalProjection.ProjectClipped"/>).
    /// </summary>
    /// <param name="key">The model's key in this pool.</param>
    /// <param name="model">The model's vertices in the world, a triangle list.</param>
    /// <param name="start">The ray's start.</param>
    /// <param name="delta">The ray's length and direction.</param>
    /// <param name="radius">Half the decal's larger side, scaled by `$decalScale`.</param>
    /// <param name="material">The drawn material's table index.</param>
    /// <returns>Whether any triangle took the decal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public bool AddClipped(int key, IReadOnlyList<WorldVertex> model, Vector3 start, Vector3 delta, float radius, int material)
    {
        ArgumentNullException.ThrowIfNull(model);

        RetireForOneMore(key);

        IReadOnlyList<WorldVertex> corners = StudioDecalProjection.ProjectClipped(
            model, start, delta, Vector3.UnitZ, radius, noPokeThru: true);

        if (corners.Count == 0)
        {
            return false;
        }

        WorldVertex[] vertices = new WorldVertex[corners.Count];

        for (int index = 0; index < corners.Count; index++)
        {
            vertices[index] = Unlit(corners[index]);
        }

        Keep(key, material, vertices);

        return true;
    }

    private WorldVertex Unlit(WorldVertex corner) =>
        corner with { LightU = 0f, LightV = 0f, Red = UnlitLight, Green = UnlitLight, Blue = UnlitLight };

    /// <summary>`AddDecal`'s first two limits: the oldest of any model past 1.5 · r_maxmodeldecal, then this model's past it.</summary>
    private void RetireForOneMore(int entity)
    {
        if (_age.Count >= MaximumPerModel * 1.5)
        {
            Retire(_age.First!.Value);
        }

        if (_byEntity.TryGetValue(entity, out List<Decal>? held) && held.Count >= MaximumPerModel)
        {
            Retire((entity, held[0]));
        }
    }

    /// <summary>Keeps one projected decal, retiring this model's oldest while its material holds too many corners.</summary>
    private void Keep(int entity, int material, WorldVertex[] vertices)
    {
        _byEntity.TryGetValue(entity, out List<Decal>? held);

        if (held is null)
        {
            held = [];
            _byEntity[entity] = held;
        }

        while (Corners(held, material) + vertices.Length > MaximumCornersPerMaterial && held.Count > 0)
        {
            Retire((entity, held[0]));
        }

        Decal decal = new(material, vertices);

        held.Add(decal);
        _age.AddLast((entity, decal));
        _built.Remove(entity);
        Version++;
    }

    /// <summary>`RemoveAllDecals` on one entity's model.</summary>
    /// <param name="entity">The entity.</param>
    public void Clear(int entity)
    {
        if (!_byEntity.Remove(entity))
        {
            return;
        }

        _built.Remove(entity);

        for (LinkedListNode<(int Entity, Decal Decal)>? node = _age.First; node is not null;)
        {
            LinkedListNode<(int Entity, Decal Decal)>? next = node.Next;

            if (node.Value.Entity == entity)
            {
                _age.Remove(node);
            }

            node = next;
        }

        Version++;
    }

    /// <summary>Every model's decals gone — a seek backwards, or a new map.</summary>
    public void ClearAll()
    {
        _byEntity.Clear();
        _age.Clear();
        _built.Clear();
        Version++;
    }

    /// <summary>One entity's decals as corners and one run per decal material, or null for none.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>What the renderer draws after the model.</returns>
    public (IReadOnlyList<WorldVertex> Vertices, IReadOnlyList<WorldBatch> Batches)? For(int entity)
    {
        if (!_byEntity.TryGetValue(entity, out List<Decal>? held) || held.Count == 0)
        {
            return null;
        }

        if (_built.TryGetValue(entity, out (IReadOnlyList<WorldVertex> Vertices, IReadOnlyList<WorldBatch> Batches) built))
        {
            return built;
        }

        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        // In the order they were added within each material, as the engine appends to a material's list.
        foreach (int material in Materials(held))
        {
            int first = vertices.Count;

            foreach (Decal decal in held)
            {
                if (decal.Material == material)
                {
                    vertices.AddRange(decal.Vertices);
                }
            }

            batches.Add(new WorldBatch(material, first, vertices.Count - first, Category: SurfaceCategory.Overlay));
        }

        built = (vertices, batches);
        _built[entity] = built;

        return built;
    }

    private static List<int> Materials(List<Decal> held)
    {
        List<int> materials = [];

        foreach (Decal decal in held)
        {
            if (!materials.Contains(decal.Material))
            {
                materials.Add(decal.Material);
            }
        }

        return materials;
    }

    private static int Corners(List<Decal> held, int material)
    {
        int total = 0;

        foreach (Decal decal in held)
        {
            if (decal.Material == material)
            {
                total += decal.Vertices.Length;
            }
        }

        return total;
    }

    private void Retire((int Entity, Decal Decal) oldest)
    {
        if (_byEntity.TryGetValue(oldest.Entity, out List<Decal>? held))
        {
            held.Remove(oldest.Decal);
            _built.Remove(oldest.Entity);
        }

        for (LinkedListNode<(int Entity, Decal Decal)>? node = _age.First; node is not null; node = node.Next)
        {
            if (ReferenceEquals(node.Value.Decal, oldest.Decal))
            {
                _age.Remove(node);
                break;
            }
        }
    }

    private sealed record Decal(int Material, WorldVertex[] Vertices);
}
