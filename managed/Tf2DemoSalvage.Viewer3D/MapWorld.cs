using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>A map turned into triangles the renderer can draw in a few calls.</summary>
/// <param name="Vertices">Every triangle corner, grouped so one material's are contiguous.</param>
/// <param name="Batches">The runs, one per material actually used.</param>
/// <param name="Decals">Overlay quads, drawn after the world with a depth bias.</param>
internal readonly record struct MapWorld(
    IReadOnlyList<WorldVertex> Vertices,
    IReadOnlyList<WorldBatch> Batches,
    IReadOnlyList<WorldBatch> Decals);

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
    /// <param name="overlays">The map decals, or null to draw none.</param>
    /// <param name="categoryColours">Flat colours by surface kind instead of the map's own light.</param>
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
        MapBounds? area,
        bool categoryColours = false,
        IReadOnlyList<BspOverlay>? overlays = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(props);

        // The height range is no longer needed here: the vertices carry world Z and the camera
        // projects it (D21). MainForm reads the same range through HeightRange to build the
        // matrix, so the arithmetic still happens exactly once - somewhere a free camera can also
        // reach it.

        // **Counted and logged, because a picture is a poor way to notice a category is empty.**
        // Every defect chased this session showed up in these numbers before it showed up on
        // screen: terrain that was culled, props that were skipped, a material that was dropped.
        int brushFaces = 0;
        int terrainFaces = 0;
        int missingMaterials = 0;

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

            // Per vertex rather than per material, because a batch spans many faces and each one
            // has its own lightmap size. Valve carries the same number as a vertex attribute.
            float lightStep = surface.FaceIndex < atlas.DirectionalSteps.Count
                ? atlas.DirectionalSteps[surface.FaceIndex]
                : 0f;

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
            if (materialIndex(surface) < 0)
            {
                missingMaterials++;
            }

            if (surface.IsDisplacement)
            {
                terrainFaces++;

                IReadOnlyList<SurfaceVertex> subdivided = ReadTerrain(terrain, surface);

                foreach (SurfaceVertex corner in subdivided)
                {
                    (float red, float green, float blue) = categoryColours
                        ? CategoryColour(SurfaceCategory.Terrain)
                        : (1f, 1f, 1f);

                    Append(vertices, corner, rectangle, lightStep, red, green, blue);
                }

                if (subdivided.Count > 0)
                {
                    continue;
                }
            }

            brushFaces++;

            // A fan from the first corner: faces out of a BSP are convex by construction.
            IReadOnlyList<SurfaceVertex> corners = surface.Vertices;

            (float brushRed, float brushGreen, float brushBlue) = categoryColours
                ? CategoryColour(SurfaceCategory.Brush)
                : (1f, 1f, 1f);

            for (int index = 1; index + 1 < corners.Count; index++)
            {
                Append(vertices, corners[0], rectangle, lightStep, brushRed, brushGreen, brushBlue);
                Append(vertices, corners[index], rectangle, lightStep, brushRed, brushGreen, brushBlue);
                Append(vertices, corners[index + 1], rectangle, lightStep, brushRed, brushGreen, brushBlue);
            }
        }

        AppendProps(props, byMaterial, area, categoryColours);

        ViewerLog.Write(
            "render",
            $"world: {brushFaces} brush faces, {terrainFaces} terrain faces, " +
            $"{props.Count / 3} prop triangles, {missingMaterials} faces with no material");

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

        List<WorldBatch> decals = AppendDecals(
            all, overlays, surfaces, atlas, area);

        return new MapWorld(all, batches, decals);
    }

    /// <summary>Turns each overlay into a quad lit by the face it is pinned to.</summary>
    /// <remarks>
    /// **A decal takes its light from the surface underneath, not from one of its own.** The
    /// overlay lump has no lightmap; the quad lies on a face that does, so its corners are
    /// projected through that face's luxel mapping. Drawn unlit instead, every sign and scorch mark
    /// glows in a dark room — which reads as a deliberate effect rather than a defect.
    ///
    /// **Unclipped, deliberately and measurably.** The engine clips each quad to the faces it
    /// names and that code was never released. Sampled on cp_process_final, a median of 100% of
    /// each quad already lands on a face it names and the mean is 93.7%, so clipping is worth about
    /// six per cent of decal area — a refinement rather than a precondition.
    ///
    /// The one face chosen is the first the overlay names that shares its plane. An overlay
    /// wrapping a corner names faces on both sides, and lighting the whole quad from one of them is
    /// the same approximation as not clipping it.
    /// </remarks>
    private static List<WorldBatch> AppendDecals(
        List<WorldVertex> all,
        IReadOnlyList<BspOverlay>? overlays,
        IReadOnlyList<BspSurface> surfaces,
        LightmapAtlas atlas,
        MapBounds? area)
    {
        List<WorldBatch> decals = [];

        if (overlays is null || overlays.Count == 0)
        {
            return decals;
        }

        Dictionary<int, BspSurface> byFace = [];

        foreach (BspSurface surface in surfaces)
        {
            byFace[surface.FaceIndex] = surface;
        }

        Dictionary<int, List<WorldVertex>> byMaterial = [];
        int placed = 0;
        int unlit = 0;

        foreach (BspOverlay overlay in overlays)
        {
            if (overlay.MaterialIndex < 0)
            {
                continue;
            }

            BspSurface? on = null;

            foreach (int face in overlay.Faces)
            {
                if (byFace.TryGetValue(face, out BspSurface? candidate) &&
                    Math.Abs(
                        (overlay.BasisNormal.X * candidate.Normal.X) +
                        (overlay.BasisNormal.Y * candidate.Normal.Y) +
                        (overlay.BasisNormal.Z * candidate.Normal.Z)) > 0.9f)
                {
                    on = candidate;
                    break;
                }
            }

            if (on is null)
            {
                // Reported rather than skipped silently: an overlay that lies flat on nothing is
                // either a reader defect or a map quirk, and both are worth knowing about.
                unlit++;
                continue;
            }

            IReadOnlyList<(float X, float Y, float Z)> quad = overlay.WorldCorners;

            if (area is { } bounds && !quad.Any(corner =>
                    corner.X >= bounds.MinX && corner.X <= bounds.MaxX &&
                    corner.Y >= bounds.MinY && corner.Y <= bounds.MaxY))
            {
                continue;
            }

            AtlasRect rectangle = on.FaceIndex < atlas.Rectangles.Count
                ? atlas.Rectangles[on.FaceIndex]
                : default;

            // The overlay's own texture coordinates: its quad spans StartU..EndU across and
            // StartV..EndV down, corner by corner in the order vbsp wrote them.
            (float U, float V)[] texture =
            [
                (overlay.U.Start, overlay.V.Start),
                (overlay.U.End, overlay.V.Start),
                (overlay.U.End, overlay.V.End),
                (overlay.U.Start, overlay.V.End),
            ];

            List<WorldVertex> corners = [];

            for (int index = 0; index < 4; index++)
            {
                (float x, float y, float z) = quad[index];
                (float lightU, float lightV) = on.Lighting.Project(x, y, z);

                corners.Add(new WorldVertex(
                    x,
                    y,

                    // **World height, not a depth.** D21: the camera projects it, so the same
                    // geometry serves an overhead view, a free camera and a first-person one.
                    z,
                    texture[index].U,
                    texture[index].V,
                    rectangle.U + (Math.Clamp(lightU, 0f, 1f) * rectangle.Width),
                    rectangle.V + (Math.Clamp(lightV, 0f, 1f) * rectangle.Height),
                    0f));
            }

            if (!byMaterial.TryGetValue(overlay.MaterialIndex, out List<WorldVertex>? into))
            {
                into = [];
                byMaterial[overlay.MaterialIndex] = into;
            }

            // Two triangles from the quad, wound as the corners are given.
            into.Add(corners[0]);
            into.Add(corners[1]);
            into.Add(corners[2]);
            into.Add(corners[0]);
            into.Add(corners[2]);
            into.Add(corners[3]);

            placed++;
        }

        foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
        {
            decals.Add(new WorldBatch(group.Key, all.Count, group.Value.Count));
            all.AddRange(group.Value);
        }

        ViewerLog.Write(
            "map",
            $"{placed} decals placed across {decals.Count} materials, {unlit} lying flat on nothing");

        return decals;
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
        bool categoryColours)
    {
        for (int corner = 0; corner + 2 < props.Count; corner += 3)
        {
            PropVertex first = props[corner];

            // **A prop whose material resolved to nothing is DRAWN, in the missing-material
            // chequer.** It used to be skipped, on the reasoning that a white rock reads as a
            // rendering fault - which was true and was the wrong conclusion. A hole reads as
            // nothing at all, and nothing at all is what nobody investigates. Magenta gets
            // reported.

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

                SurfaceCategory category = vertex.MaterialIndex < 0
                    ? SurfaceCategory.Missing
                    : SurfaceCategory.Prop;

                (float red, float green, float blue) = categoryColours
                    ? CategoryColour(category)
                    : (vertex.Red, vertex.Green, vertex.Blue);

                Append(
                    vertices,
                    new SurfaceVertex(vertex.X, vertex.Y, vertex.Z, vertex.U, vertex.V, 0f, 0f),
                    default,
                    // A prop takes its light from its own baked vertex colours, not from a
                    // lightmap, so it never steps along the atlas.
                    0f,
                    red,
                    green,
                    blue);
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

    /// <summary>
    /// Finds the vertical extent of the PLAY AREA, which is what depth is measured against.
    /// </summary>
    /// <remarks>
    /// **Measured over the play area, not the whole file, for the same reason the camera frames
    /// MainBounds.** A TF2 map keeps its 3D skybox as ordinary geometry far outside the level, and
    /// on cp_process_f12 that puts the file's vertical span at -14,673 to 3,152 while everything a
    /// player can stand on lives between roughly -72 and 2,240. Normalised against the file, the
    /// entire playable map occupies 13% of the depth range.
    ///
    /// That wastes seven eighths of the depth buffer's precision on empty space, and it made the
    /// height cut useless: the slice spent most of its travel above anything that exists.
    /// </remarks>
    internal static (float Lowest, float Highest) HeightRange(
        IReadOnlyList<BspSurface> surfaces, MapBounds? area)
    {
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;

        foreach (BspSurface surface in surfaces)
        {
            if (area is { } bounds && !Touches(surface, bounds))
            {
                continue;
            }

            foreach (float z in surface.Vertices.Select(vertex => vertex.Z))
            {
                lowest = Math.Min(lowest, z);
                highest = Math.Max(highest, z);
            }
        }

        return float.IsFinite(lowest) && highest > lowest ? (lowest, highest) : (0f, 1f);
    }

    private static void Append(
        List<WorldVertex> vertices,
        SurfaceVertex corner,
        AtlasRect rectangle,
        float lightStep,
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

        // **World height, passed through.** It used to be flattened into a depth here - looking
        // straight down, a higher surface is nearer, and D3D treats smaller depth as nearer, so
        // the tallest geometry mapped to zero. That arithmetic still happens and still means the
        // same thing; it is in TopDownCamera.WithHeights now, because a projection belongs to the
        // camera and geometry flattened for one camera cannot serve another (D21).
        float depth = corner.Z;

        // Clamped before remapping: a corner can sit a fraction outside its own lightmap, and in a
        // shared atlas that fraction is another face's light rather than empty space.
        // A zero-width rectangle is a face with no baked light, and its U and V are the atlas's
        // reserved white texel - so the arithmetic below lands exactly there and the surface draws
        // at full texture brightness rather than black.
        float lightU = rectangle.U + (Math.Clamp(corner.LightU, 0f, 1f) * rectangle.Width);
        float lightV = rectangle.V + (Math.Clamp(corner.LightV, 0f, 1f) * rectangle.Height);

        vertices.Add(new WorldVertex(
            x, y, depth, corner.U, corner.V, lightU, lightV, corner.Alpha, red, green, blue,
            lightStep));
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

    /// <summary>Flat colours naming what a surface IS, for the diagnostic view.</summary>
    /// <remarks>
    /// **Answers in one glance what a textured picture hides.** Several defects this session looked
    /// like art direction: terrain that was not drawn, a material dropped by a category rule, props
    /// standing in for holes. "Is anything here at all, and what kind of thing is it" is a different
    /// question from "does this look right", and it needs a different picture.
    /// </remarks>
    private static (float Red, float Green, float Blue) CategoryColour(SurfaceCategory category) =>
        category switch
        {
            SurfaceCategory.Terrain => (0.25f, 0.85f, 0.35f),
            SurfaceCategory.Prop => (1f, 0.6f, 0.15f),
            SurfaceCategory.Missing => (1f, 0f, 1f),
            _ => (0.55f, 0.6f, 0.72f),
        };

    /// <summary>What a drawn surface is, for the diagnostic view.</summary>
    private enum SurfaceCategory
    {
        /// <summary>Ordinary world brushwork.</summary>
        Brush,

        /// <summary>A displacement's subdivided terrain.</summary>
        Terrain,

        /// <summary>A placed model.</summary>
        Prop,

        /// <summary>Anything whose material could not be resolved.</summary>
        Missing,
    }

    /// <summary>A surface's material, or -1 when it names one the map does not have.</summary>
    private static int materialIndex(BspSurface surface) => surface.MaterialIndex;

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
