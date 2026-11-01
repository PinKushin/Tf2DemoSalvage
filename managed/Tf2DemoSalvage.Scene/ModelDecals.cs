using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which light a drawn model's decals took (B642), <see cref="ModelDecals.SourceOf"/>.</summary>
public enum DecalLightSource
{
    /// <summary>An entity's: the model's own cube, sun and lamps, studiorender's standing state.</summary>
    Entity,

    /// <summary>A colour-mesh static prop's decal light, from its pose or placed after it.</summary>
    DecalLight,

    /// <summary>A bump-mapped static prop's own light, which is already its full state (flags 7): right.</summary>
    OwnFullState,

    /// <summary>A colour-mesh static prop drawn with its own flags-6 light: the defect.</summary>
    Fallback,
}

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
/// with the model's bones. **Their corners are white whatever the material** (B639 review):
/// - **DecalModulate** reads no vertex colour (`VERTEXCOLOR false`, DecalModulate_dx9.cpp:89), and its mod2x compensation
///   is the blend's own, on the target's stored values (B641).
/// - **A lit studio decal** (`decals/bigshot1model`, `break1model`, `bproof1model` and `beersplashmodel` are
///   VertexLitGeneric) is white-cornered in Valve too: studiorender's decal vertex builder writes colour `0xffffffff`
///   (`0x1800096c0`, B642). Until B639 every studio decal carried 2^2.2/4 here, which only ever compensated a gamma mod2x
///   blend (*arithmetic*). Its LIGHT is the model's cube and lamps, handed to `WorldRenderer.DrawModelDecals` after the
///   model; a colour-mesh static prop's is the full state at its lighting origin (`ModelInstance.DecalLight`, B642).
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

    /// <summary>`r_maxmodeldecal`, read from the viewer's config: each model's cap, and 1.5 times it the pool's.</summary>
    public int PerModel { get; set; } = MaximumPerModel;

    /// <summary>How many decals are held across every model.</summary>
    public int Count => _age.Count;

    /// <summary>Changes whenever a decal is kept or cleared, so an unchanged pool costs its reader nothing.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Changes only when a static prop's decals are kept, retired or cleared (B642 review 3), so a player's decal, heal or
    /// cloak does not make <see cref="EntityModelSet.LightDecalHolders"/> re-light every static prop.
    /// </summary>
    public int StaticPropVersion { get; private set; }

    /// <summary>The keys of every model holding decals.</summary>
    public IEnumerable<int> Models => _byEntity.Keys;

    /// <summary>The static props' keys whose models hold a decal, into a list the caller reuses (B642 review 3).</summary>
    /// <param name="into">Cleared, then filled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>Over the dictionary's own key collection, whose enumerator is a struct: no allocation per call.</remarks>
    internal void StaticPropHolders(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (int key in _byEntity.Keys)
        {
            if (DecalReplay.IsStaticPropKey(key) && Holds(key))
            {
                into.Add(key);
            }
        }
    }

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

    /// <summary>A corner on the atlas's reserved white texel, coloured white (see the class remarks).</summary>
    private static WorldVertex Unlit(WorldVertex corner) =>
        corner with { LightU = 0f, LightV = 0f, Red = 1f, Green = 1f, Blue = 1f };

    /// <summary>`AddDecal`'s first two limits: the oldest of any model past 1.5 · r_maxmodeldecal, then this model's past it.</summary>
    private void RetireForOneMore(int entity)
    {
        if (_age.Count >= PerModel * 1.5)
        {
            Retire(_age.First!.Value);
        }

        if (_byEntity.TryGetValue(entity, out List<Decal>? held) && held.Count >= PerModel)
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
        StaticPropChanged(entity);
    }

    /// <summary>Whether a model holds any decal, without building its draw list — the engine's `record + 0x10 != 0` (B642).</summary>
    /// <param name="key">The model's key in this pool.</param>
    /// <returns>True when at least one decal is held under it.</returns>
    public bool Holds(int key) => _byEntity.TryGetValue(key, out List<Decal>? held) && held.Count > 0;

    /// <summary>
    /// Decal lights for static props whose decals were placed after this frame's model pass, by pool key (B642).
    /// </summary>
    /// <remarks>
    /// The decal replay places a static prop's decals after the pose: on a map's first frame, after a seek, and on any frame
    /// a bullet lands. That pose saw no holder, so the instance carries no <see cref="ModelInstance.DecalLight"/>, and a
    /// paused viewer poses once per build. Written by <see cref="EntityModelSet.LightDecalHolders"/> after the decal step,
    /// read by the draw.
    /// </remarks>
    private readonly Dictionary<int, DecalLighting> _lateLights = [];

    /// <summary>The decal light placed for a key after the pose, or null (B642).</summary>
    /// <param name="key">The model's key in this pool.</param>
    /// <returns>The light, or null.</returns>
    public DecalLighting? LateLightOf(int key) => _lateLights.GetValueOrDefault(key);

    /// <summary>Replaces every late decal light with <paramref name="lights"/> (B642).</summary>
    /// <param name="lights">The lights by key; a list, so the copy enumerates without allocating.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    internal void SetLateLights(List<KeyValuePair<int, DecalLighting>> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);

        _lateLights.Clear();

        foreach ((int key, DecalLighting light) in lights)
        {
            _lateLights[key] = light;
        }
    }

    /// <summary>
    /// The light a drawn model's decals take (B642): a colour-mesh static prop's decal light, from its pose or, where the
    /// decal was placed after the pose, from <paramref name="pool"/>; else the model's own cube, sun and lamps — the lighting
    /// state studiorender leaves standing.
    /// </summary>
    /// <param name="instance">The model just drawn.</param>
    /// <param name="pool">The pool its decals come from, for a light placed after the pose; null for none.</param>
    /// <returns>The cube, sun and lamps to draw its decals with, and whether that is a decal light.</returns>
    public static (AmbientCube? Light, SunLight? Sun, IReadOnlyList<LocalLight>? Locals, bool DecalLit) LightFor(
        in ModelInstance instance, ModelDecals? pool) =>
        (instance.DecalLight ?? pool?.LateLightOf(DecalReplay.KeyOf(instance.EntityIndex))) is { } decal
            ? (decal.Cube, decal.Sun, decal.Locals, true)
            : (instance.Light, instance.Sun, instance.Locals, false);

    /// <summary>Which light a drawn model's decals took, for the device's counters and its first-use log (B642 review 3).</summary>
    /// <param name="instance">The model drawn.</param>
    /// <param name="decalLit">What <see cref="LightFor"/> answered.</param>
    /// <returns>
    /// An entity's own light; a static prop's decal light; a bump-mapped prop's own light, which is its full state and so
    /// right; or the fallback, a colour-mesh prop drawn without its decal light, which is the defect B642 fixed.
    /// </returns>
    public static DecalLightSource SourceOf(in ModelInstance instance, bool decalLit)
    {
        if (instance.EntityIndex < PropModels.FirstStaticPropEntityIndex)
        {
            return DecalLightSource.Entity;
        }

        if (decalLit)
        {
            return DecalLightSource.DecalLight;
        }

        return instance.BakedColours is null ? DecalLightSource.OwnFullState : DecalLightSource.Fallback;
    }

    /// <summary>
    /// Whether a model drawn in the translucent pass draws its decals there (B642): a static prop that is not two-pass.
    /// `DrawModelStaticProp` draws a prop's decals in whichever pass draws the prop; a two-pass prop's were drawn with its
    /// opaque half, and an entity's are drawn only after its opaque draw, as before.
    /// </summary>
    /// <param name="instance">The model.</param>
    /// <param name="twoPass">Whether only its translucent half is being drawn.</param>
    /// <returns>True when its decals follow its translucent draw.</returns>
    public static bool DrawsAfterTranslucent(in ModelInstance instance, bool twoPass) =>
        !twoPass && instance.EntityIndex >= PropModels.FirstStaticPropEntityIndex;

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
        StaticPropChanged(entity);
    }

    /// <summary>Every decal of the models whose keys match gone — one caller's share of a shared pool, on a seek.</summary>
    /// <param name="key">Which keys to clear.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public void ClearWhere(Func<int, bool> key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (int entity in new List<int>(_byEntity.Keys))
        {
            if (key(entity))
            {
                Clear(entity);
            }
        }
    }

    /// <summary>Every model's decals gone — a seek backwards, or a new map.</summary>
    public void ClearAll()
    {
        _byEntity.Clear();
        _age.Clear();
        _built.Clear();
        _lateLights.Clear();
        Version++;
        StaticPropVersion++;
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
            StaticPropChanged(oldest.Entity);
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

    /// <summary>Advances <see cref="StaticPropVersion"/> when the key changed is a static prop's.</summary>
    private void StaticPropChanged(int key)
    {
        if (DecalReplay.IsStaticPropKey(key))
        {
            StaticPropVersion++;
        }
    }

    private sealed record Decal(int Material, WorldVertex[] Vertices);
}
