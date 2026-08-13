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
    /// <param name="props">The map's placed models, in world space.</param>
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
        IReadOnlyList<PropVertex> props,
        TopDownCamera camera,
        MapBounds? area)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(props);

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
                    Append(vertices, corner, rectangle, lowest, highest);
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
                Append(vertices, corners[0], rectangle, lowest, highest);
                Append(vertices, corners[index], rectangle, lowest, highest);
                Append(vertices, corners[index + 1], rectangle, lowest, highest);
            }
        }

        AppendProps(props, byMaterial, area, lowest, highest);

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

    /// <summary>
    /// Adds the map's placed models to the batches the brushwork already filled.
    /// </summary>
    /// <remarks>
    /// **A prop's light comes from its own vertex colours, not from the lightmap.** The compiler
    /// bakes a colour per vertex per placement into the map's pakfile, because the same model
    /// stands in many places under different light and one lightmap could not serve them all. The
    /// zero-width atlas rectangle sends every corner to the reserved white texel, so the lightmap
    /// term is an identity and the vertex colour does the work.
    ///
    /// A placement whose lighting is missing or does not match its model keeps white, which draws
    /// it at its texture's own brightness. Visible and slightly wrong beats a hole.
    ///
    /// **No upward-facing filter.** Brush faces are culled by normal because a ceiling seen from
    /// above should not hide the room; a prop is a closed solid whose far side is hidden by its own
    /// near side under the depth buffer, so there is nothing to cull and a normal test would delete
    /// half of every rock.
    /// </remarks>
    private static void AppendProps(
        IReadOnlyList<PropVertex> props,
        Dictionary<int, List<WorldVertex>> byMaterial,
        MapBounds? area,
        float lowest,
        float highest)
    {
        for (int corner = 0; corner + 2 < props.Count; corner += 3)
        {
            PropVertex first = props[corner];

            if (first.MaterialIndex < 0)
            {
                // A material that resolved to nothing. Drawing it white would be worse than the
                // hole it leaves, since a white rock reads as a rendering fault.
                continue;
            }

            if (area is { } bounds && !Inside(first, bounds))
            {
                // **Judged by the placement's origin, not by its triangles.** A TF2 map keeps a
                // miniature copy of the surrounding scenery in a separate room far outside the
                // play area, drawn at a fraction of world scale; those are ordinary prop_static
                // entries whose triangles are perfectly valid shapes at perfectly valid positions.
                // Nothing about a triangle distinguishes them - only where its prop stands does.
                //
                // The earlier per-triangle test kept a prop if ANY corner fell inside, which let
                // whole skybox buildings through wherever one touched the boundary. Visible in a
                // screenshot as structures scattered well outside the map's own outline.
                continue;
            }

            if (!byMaterial.TryGetValue(first.MaterialIndex, out List<WorldVertex>? vertices))
            {
                vertices = [];
                byMaterial[first.MaterialIndex] = vertices;
            }

            for (int offset = 0; offset < 3; offset++)
            {
                PropVertex vertex = props[corner + offset];

                Append(
                    vertices,
                    new SurfaceVertex(vertex.X, vertex.Y, vertex.Z, vertex.U, vertex.V, 0f, 0f),
                    default,
                    lowest,
                    highest,
                    vertex.Red,
                    vertex.Green,
                    vertex.Blue);
            }
        }
    }

    private static bool Inside(PropVertex vertex, MapBounds bounds) =>
        vertex.OriginX >= bounds.MinX && vertex.OriginX <= bounds.MaxX &&
        vertex.OriginY >= bounds.MinY && vertex.OriginY <= bounds.MaxY;

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
        float lowest,
        float highest,
        float red = 1f,
        float green = 1f,
        float blue = 1f)
    {
        // **World coordinates, not projected ones.** The camera is a matrix the vertex shader
        // applies, so these vertices are uploaded once per map and survive every resize, zoom and
        // pan. Baking the projection here is what made a viewport change cost a rebuild of two and
        // a half million vertices.
        //
        (float x, float y) = (corner.X, corner.Y);

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

        vertices.Add(new WorldVertex(
            x, y, depth, corner.U, corner.V, lightU, lightV, corner.Alpha, red, green, blue));
    }

    /// <summary>
    /// Whether a material is one the engine never draws, and cannot be told from its flags.
    /// </summary>
    /// <remarks>
    /// **Exactly one material needs this, and the blanket rule it replaces was hiding a real
    /// surface.** Matching every path under <c>tools/</c> looked safe and was not: measured on
    /// cp_process_final,
    ///
    /// <code>
    ///   TOOLSINVISIBLEDISPLACEMENT  518 faces, 518 visible, flags Translucent
    ///   TOOLSSKYBOX                 361 faces,   0 visible, flags Sky, NoLight
    ///   TOOLSTRIGGER                318 faces,   0 visible, flags Trigger, NoLight
    ///   TOOLSBLACK                   80 faces,  80 visible, flags None
    /// </code>
    ///
    /// Sky and trigger carry flags, so the visibility check already excludes them and this was
    /// never needed for either. <c>toolsblack</c> carries NO flags because it is an ordinary drawn
    /// surface — mappers use it for the void behind a window, under a grate, inside a vent, and
    /// the engine draws it as black. Skipping it left 4.8 million square units of the map unpainted,
    /// showing the background through, which read as dark blobs and survived four separate
    /// explanations about lighting.
    ///
    /// So only <c>toolsinvisibledisplacement</c> is matched by name — the one material that is
    /// genuinely never drawn and carries nothing to say so. It is collision-only terrain laid under
    /// what the player actually sees, which is a static prop.
    ///
    /// **The lesson is in the shape of the mistake**: a rule written from a category ("tool
    /// materials are not drawn") rather than from the data, which was right about the case that
    /// prompted it and wrong about a sibling nobody checked.
    /// </remarks>
    private static bool IsToolMaterial(int materialIndex, IReadOnlyList<BspMaterial> materials)
    {
        if (materialIndex < 0 || materialIndex >= materials.Count)
        {
            return false;
        }

        return materials[materialIndex].Name.Contains(
            "toolsinvisibledisplacement", StringComparison.OrdinalIgnoreCase);
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
