using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>Turns the world's placed decals into triangles and material runs for the overlay pass (B415).</summary>
public static class DecalMesh
{
    /// <summary>Every placed decal as triangle fans, grouped by the material that draws it.</summary>
    /// <param name="decals">The placed decals, in pool order.</param>
    /// <param name="materialIndex">The table index of a decal's drawn material, or −1 when it is not loaded.</param>
    /// <param name="unlit">
    /// For a table material that ignores the lightmap, as `DecalModulate` does, the vertex light its corners carry;
    /// null for one lit by the face beneath.
    /// </param>
    /// <param name="lightmaps">Where each face's lightmap sits in the atlas, by face index.</param>
    /// <param name="vertices">Cleared, then filled.</param>
    /// <param name="batches">Cleared, then filled: one run per material.</param>
    /// <param name="entityOrigin">Where a brush entity carrying decals stands now, or null when it is gone.</param>
    /// <param name="entityBatches">
    /// Cleared, then filled with the runs of decals on brush entities, which draw with their entity after the world's —
    /// `R_DrawBrushModel` draws a model's decals right after its surfaces; null folds them into <paramref name="batches"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A lit decal takes the light of the face it lies on**, its corners remapped into that face's atlas rectangle as
    /// an overlay's are. **An unlit one takes the atlas's reserved white texel** — a zero rectangle — and the vertex
    /// light its caller names: `DecalModulate`'s pixel shader samples its texture and nothing else, and its mod2x blend
    /// multiplies the surface below, lightmap and all.
    /// </remarks>
    public static void Build(
        IReadOnlyList<PlacedDecal> decals,
        Func<DecalMaterial, int> materialIndex,
        Func<int, float?> unlit,
        IReadOnlyList<AtlasRect> lightmaps,
        ICollection<WorldVertex> vertices,
        ICollection<WorldBatch> batches,
        Func<int, Vector3?>? entityOrigin = null,
        ICollection<WorldBatch>? entityBatches = null)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ArgumentNullException.ThrowIfNull(materialIndex);
        ArgumentNullException.ThrowIfNull(unlit);
        ArgumentNullException.ThrowIfNull(lightmaps);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        vertices.Clear();
        batches.Clear();
        entityBatches?.Clear();

        List<int> order = [];
        Dictionary<int, List<PlacedDecal>> byMaterial = [];

        foreach (PlacedDecal decal in decals)
        {
            int material = materialIndex(decal.Material);

            if (material < 0 || decal.Polygon.Count < 3)
            {
                continue;
            }

            if (!byMaterial.TryGetValue(material, out List<PlacedDecal>? group))
            {
                byMaterial[material] = group = [];
                order.Add(material);
            }

            group.Add(decal);
        }

        // The world's decals, then the brush entities' — into their own runs when the caller draws those with the entities.
        foreach ((bool onEntity, ICollection<WorldBatch> into) in new[] { (false, batches), (true, entityBatches ?? batches) })
        {
            foreach (int material in order)
            {
                int first = vertices.Count;
                float? white = unlit(material);

                foreach (PlacedDecal decal in byMaterial[material])
                {
                    if (decal.Entity >= 0 == onEntity)
                    {
                        Fan(decal, white, lightmaps, entityOrigin, vertices);
                    }
                }

                if (vertices.Count > first)
                {
                    into.Add(new WorldBatch(material, first, vertices.Count - first, Category: SurfaceCategory.Overlay));
                }
            }
        }
    }

    /// <summary>One placed decal as a triangle fan — the clip keeps the polygon convex.</summary>
    private static void Fan(
        PlacedDecal decal, float? white, IReadOnlyList<AtlasRect> lightmaps, Func<int, Vector3?>? entityOrigin, ICollection<WorldVertex> vertices)
    {
        // A decal on a brush entity is in its model's frame and draws where the entity now stands; one whose entity is gone
        // draws nothing.
        Vector3 offset = Vector3.Zero;

        if (decal.Entity >= 0)
        {
            if (entityOrigin?.Invoke(decal.Entity) is not { } origin)
            {
                return;
            }

            offset = origin;
        }

        float light = white ?? 1f;
        AtlasRect face = white is null && decal.Face >= 0 && decal.Face < lightmaps.Count ? lightmaps[decal.Face] : default;
        IReadOnlyList<DecalVertex> polygon = decal.Polygon;

        for (int corner = 1; corner + 1 < polygon.Count; corner++)
        {
            vertices.Add(Corner(polygon[0], face, light, offset));
            vertices.Add(Corner(polygon[corner], face, light, offset));
            vertices.Add(Corner(polygon[corner + 1], face, light, offset));
        }
    }

    private static WorldVertex Corner(DecalVertex corner, AtlasRect face, float light, Vector3 offset) =>
        new(
            corner.Position.X + offset.X,
            corner.Position.Y + offset.Y,
            corner.Position.Z + offset.Z,
            corner.U,
            corner.V,
            face.U + (Math.Clamp(corner.LightU, 0f, 1f) * face.Width),
            face.V + (Math.Clamp(corner.LightV, 0f, 1f) * face.Height),
            0f,
            light,
            light,
            light);
}
