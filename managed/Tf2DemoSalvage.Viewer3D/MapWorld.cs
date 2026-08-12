using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>A map turned into triangles the renderer can draw in a few calls.</summary>
/// <param name="Vertices">Every triangle corner, grouped so one material's are contiguous.</param>
/// <param name="Batches">The runs, one per material actually used.</param>
internal readonly record struct MapWorld(
    IReadOnlyList<WorldVertex> Vertices, IReadOnlyList<WorldBatch> Batches);

/// <summary>
/// Turns a map's surfaces into batched, projected triangles.
/// </summary>
/// <remarks>
/// **Grouped by material, because that is what decides the draw call count.** A map has thirteen
/// thousand faces and two hundred materials; drawn face by face that is thirteen thousand binds,
/// and grouped it is two hundred. Nothing else about the geometry changes.
///
/// **Lightmap coordinates are remapped into the atlas here**, not in the shader. Each face's
/// coordinates arrive in its own 0..1 space, and the atlas rectangle says where that square landed
/// in the shared texture — so the vertex carries the final coordinate and the shader stays a
/// sample and a multiply.
///
/// The clamp before remapping matters: a corner can sit a fraction outside its own lightmap, and
/// without it that fraction reaches into a neighbouring face's light in the atlas.
/// </remarks>
internal static class MapWorldBuilder
{
    /// <summary>Builds the drawable world.</summary>
    /// <param name="terrain">The map's displacement lumps, or null when it has none.</param>
    /// <param name="surfaces">The map's surfaces.</param>
    /// <param name="materials">The map's texture table, for identifying tool materials.</param>
    /// <param name="atlas">Where each face's lighting sits.</param>
    /// <param name="camera">Projection from world to clip space.</param>
    /// <param name="area">Ground-plane area to keep, or null for all of it.</param>
    /// <returns>The triangles and their batches.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Only upward-facing, visible surfaces are kept, which is the same rule the outline view uses:
    /// it is the engine's own backface culling for a camera looking straight down, so a ceiling
    /// disappears and the roof a soldier stands on does not.
    /// </remarks>
    public static MapWorld Build(
        BspTerrain? terrain,
        IReadOnlyList<BspSurface> surfaces,
        IReadOnlyList<BspMaterial> materials,
        LightmapAtlas atlas,
        TopDownCamera camera,
        MapBounds? area)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(atlas);

        (float lowest, float highest) = HeightRange(surfaces);

        // Grouped first so each material's triangles end up contiguous, then flattened. A
        // dictionary keeps the grouping O(n) rather than sorting thirteen thousand faces.
        Dictionary<int, List<WorldVertex>> byMaterial = [];

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsVisible || surface.Normal.Z < 0f || surface.Vertices.Count < 3)
            {
                continue;
            }

            if (area is { } bounds && !Touches(surface, bounds))
            {
                continue;
            }

            // **Tool materials, by name, because the flags do not catch them all.** 518 of
            // cp_process_final's 578 displacement faces are painted with
            // tools/toolsinvisibledisplacement - collision-only terrain the engine never draws.
            // Its VMT is LightmappedGeneric, so no surface flag and no shader check identifies it,
            // and its texture is black: drawn, it is a black blob over exactly the areas that
            // should be grass.
            if (IsToolMaterial(surface.MaterialIndex, materials))
            {
                continue;
            }

            AtlasRect rectangle = surface.FaceIndex < atlas.Rectangles.Count
                ? atlas.Rectangles[surface.FaceIndex]
                : default;

            if (!byMaterial.TryGetValue(surface.MaterialIndex, out List<WorldVertex>? vertices))
            {
                vertices = [];
                byMaterial[surface.MaterialIndex] = vertices;
            }

            // **A displacement is not its face.** Its real surface is a heightfield subdividing
            // the quad, and drawing the quad gives a flat slab painted with only the first of the
            // material's two textures - a dirt field where a grassy hillside belongs.
            if (surface.IsDisplacement)
            {
                IReadOnlyList<SurfaceVertex> subdivided = ReadTerrain(terrain, surface);

                foreach (SurfaceVertex corner in subdivided)
                {
                    Append(vertices, corner, rectangle, camera, lowest, highest);
                }

                if (subdivided.Count > 0)
                {
                    continue;
                }
            }

            // A fan from the first corner: faces out of a BSP are convex by construction.
            IReadOnlyList<SurfaceVertex> corners = surface.Vertices;

            for (int index = 1; index + 1 < corners.Count; index++)
            {
                Append(vertices, corners[0], rectangle, camera, lowest, highest);
                Append(vertices, corners[index], rectangle, camera, lowest, highest);
                Append(vertices, corners[index + 1], rectangle, camera, lowest, highest);
            }
        }

        List<WorldVertex> all = [];
        List<WorldBatch> batches = [];

        foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
        {
            if (group.Value.Count == 0)
            {
                continue;
            }

            batches.Add(new WorldBatch(group.Key, all.Count, group.Value.Count));
            all.AddRange(group.Value);
        }

        return new MapWorld(all, batches);
    }

    /// <summary>Reads a displacement's terrain, or nothing if it cannot be read.</summary>
    /// <remarks>
    /// A malformed displacement costs its own terrain and nothing else: the face falls back to its
    /// base quad, which is where it was before this existed.
    /// </remarks>
    private static IReadOnlyList<SurfaceVertex> ReadTerrain(
        BspTerrain? terrain, BspSurface surface)
    {
        if (terrain is null)
        {
            return [];
        }

        try
        {
            return terrain.ReadTriangles(surface);
        }
        catch (System.IO.InvalidDataException)
        {
            return [];
        }
    }

    /// <summary>Finds the map's vertical extent, which is what depth is measured against.</summary>
    private static (float Lowest, float Highest) HeightRange(IReadOnlyList<BspSurface> surfaces)
    {
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;

        foreach (float z in surfaces.SelectMany(surface => surface.Vertices.Select(v => v.Z)))
        {
            lowest = Math.Min(lowest, z);
            highest = Math.Max(highest, z);
        }

        return float.IsFinite(lowest) && highest > lowest ? (lowest, highest) : (0f, 1f);
    }

    private static void Append(
        List<WorldVertex> vertices,
        SurfaceVertex corner,
        AtlasRect rectangle,
        TopDownCamera camera,
        float lowest,
        float highest)
    {
        (float x, float y) = camera.Project(corner.X, corner.Y);

        // **Height becomes depth, inverted.** Looking straight down, a higher surface is NEARER,
        // and D3D treats smaller depth as nearer - so the tallest geometry maps to zero. Without
        // this the draw order decides what covers what, and batching by material makes that order
        // arbitrary: ground-level terrain painted over the buildings standing on it.
        float depth = 1f - Math.Clamp((corner.Z - lowest) / (highest - lowest), 0f, 1f);

        // Clamped before remapping: a corner can sit a fraction outside its own lightmap, and in a
        // shared atlas that fraction is another face's light rather than empty space.
        // A zero-width rectangle is a face with no baked light, and its U and V are the atlas's
        // reserved white texel - so the arithmetic below lands exactly there and the surface draws
        // at full texture brightness rather than black.
        float lightU = rectangle.U + (Math.Clamp(corner.LightU, 0f, 1f) * rectangle.Width);
        float lightV = rectangle.V + (Math.Clamp(corner.LightV, 0f, 1f) * rectangle.Height);

        vertices.Add(new WorldVertex(x, y, depth, corner.U, corner.V, lightU, lightV, corner.Alpha));
    }

    /// <summary>Whether a material is one of the compiler's tools rather than a surface.</summary>
    /// <remarks>
    /// Matched on the path, which is the one thing every tool material shares: they all live under
    /// <c>materials/tools</c>, by a convention the engine and Hammer both rely on. The alternative
    /// - reading each VMT and guessing from its shader - fails on exactly the case that matters,
    /// since toolsinvisibledisplacement declares itself LightmappedGeneric like any wall.
    /// </remarks>
    private static bool IsToolMaterial(int materialIndex, IReadOnlyList<BspMaterial> materials)
    {
        if (materialIndex < 0 || materialIndex >= materials.Count)
        {
            return false;
        }

        string name = materials[materialIndex].Name;

        return name.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tools\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Touches(BspSurface surface, MapBounds bounds)
    {
        foreach (SurfaceVertex vertex in surface.Vertices)
        {
            if (vertex.X >= bounds.MinX && vertex.X <= bounds.MaxX &&
                vertex.Y >= bounds.MinY && vertex.Y <= bounds.MaxY)
            {
                return true;
            }
        }

        return false;
    }
}
