using System;
using System.Collections.Generic;

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
        ICollection<WorldBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ArgumentNullException.ThrowIfNull(materialIndex);
        ArgumentNullException.ThrowIfNull(unlit);
        ArgumentNullException.ThrowIfNull(lightmaps);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        vertices.Clear();
        batches.Clear();

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

        foreach (int material in order)
        {
            int first = vertices.Count;
            float? white = unlit(material);
            float light = white ?? 1f;

            foreach (PlacedDecal decal in byMaterial[material])
            {
                AtlasRect face = white is null && decal.Face >= 0 && decal.Face < lightmaps.Count
                    ? lightmaps[decal.Face]
                    : default;
                IReadOnlyList<DecalVertex> polygon = decal.Polygon;

                // A fan: the clip keeps the polygon convex.
                for (int corner = 1; corner + 1 < polygon.Count; corner++)
                {
                    vertices.Add(Corner(polygon[0], face, light));
                    vertices.Add(Corner(polygon[corner], face, light));
                    vertices.Add(Corner(polygon[corner + 1], face, light));
                }
            }

            batches.Add(new WorldBatch(material, first, vertices.Count - first, Category: SurfaceCategory.Overlay));
        }
    }

    private static WorldVertex Corner(DecalVertex corner, AtlasRect face, float light) =>
        new(
            corner.Position.X,
            corner.Position.Y,
            corner.Position.Z,
            corner.U,
            corner.V,
            face.U + (Math.Clamp(corner.LightU, 0f, 1f) * face.Width),
            face.V + (Math.Clamp(corner.LightV, 0f, 1f) * face.Height),
            0f,
            light,
            light,
            light);
}
