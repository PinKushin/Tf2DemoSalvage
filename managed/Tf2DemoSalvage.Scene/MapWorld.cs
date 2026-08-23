using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A map turned into triangles the renderer can draw in a few calls.</summary>
/// <param name="Vertices">Every triangle corner, grouped so one material's are contiguous.</param>
/// <param name="Batches">World surfaces, one run per material actually used, drawn first.</param>
/// <param name="Decals">Overlay fragments, drawn with the world and after its surfaces.</param>
/// <param name="Props">
/// Static props, drawn AFTER the overlays because the engine draws them as opaque renderables
/// rather than as world geometry — <c>CBaseWorldView::DrawExecute</c>,
/// <c>game/client/viewrender.cpp:5487</c>. Merged into <see cref="Batches"/> they landed in the
/// depth buffer before the overlay pass, and any bias on that pass then let a stripe paint over a
/// pipe standing in front of the wall (B135).
/// </param>
public readonly record struct MapWorld(
    IReadOnlyList<WorldVertex> Vertices,
    IReadOnlyList<WorldBatch> Batches,
    IReadOnlyList<WorldBatch> Decals,
    IReadOnlyList<WorldBatch> Props);

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
public static class MapWorldBuilder
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
    /// <param name="models">The map's models, so entity brushwork can be counted apart from the world.</param>
    /// <returns>The triangles and their batches.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Every visible surface is kept. This used to drop downward-facing ones and call it "the
    /// engine's own backface culling for a camera looking straight down", which was a workaround
    /// wearing a principle's clothes: the engine culls per frame against the frustum and the PVS,
    /// and culling once by the sign of a normal only matches that for a camera that never moves.
    /// Backface culling still happens, in the rasteriser, per frame.
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
        IReadOnlyList<BspOverlay>? overlays = null,
        IReadOnlyList<BspModel>? models = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(props);

        // The height range is no longer needed here: the vertices carry world Z and the camera
        // projects it (D35). MainForm reads the same range through HeightRange to build the
        // matrix, so the arithmetic still happens exactly once - somewhere a free camera can also
        // reach it.

        // **Counted and logged, because a picture is a poor way to notice a category is empty.**
        // Every defect chased this session showed up in these numbers before it showed up on
        // screen: terrain that was culled, props that were skipped, a material that was dropped.
        int brushFaces = 0;
        int terrainFaces = 0;
        int missingMaterials = 0;
        int movingFaces = 0;

        // **What was DROPPED, named by its material, because the totals cannot say.** The counts
        // above answer "how many did we draw"; every defect where a piece of the map is simply
        // absent needs the other question, and a face that is skipped leaves no trace at all. A
        // skip whose material is `tools/`, `sky` or `nodraw` is the compiler's own scaffolding and
        // is meant to go; a skip naming a real material is geometry the player should be seeing.
        //
        // Keyed by reason AND material so the two are never conflated — 815 faces dropped for
        // being tool surfaces and 815 dropped for a bug in the visibility flags produce the same
        // number and mean opposite things.
        Dictionary<string, int> dropped = [];

        // Faces actually BUILT, by material and by kind — the question neither the totals nor the
        // drop ledger can answer: what a surface IS.
        Dictionary<int, (int Faces, SurfaceCategory Kind)> drawn = [];

        void Built(int materialIndex, SurfaceCategory kind)
        {
            int faces = drawn.TryGetValue(
                materialIndex, out (int Faces, SurfaceCategory Kind) seen) ? seen.Faces : 0;

            drawn[materialIndex] = (faces + 1, kind);
        }

        void Drop(string reason, int materialIndex)
        {
            string material = materialIndex >= 0 && materialIndex < materials.Count
                ? materials[materialIndex].Name
                : "<no material>";

            string key = reason + " " + material;

            dropped[key] = dropped.TryGetValue(key, out int seen) ? seen + 1 : 1;
        }

        // **Where the world model ends and the brush entities begin.** models[0] is the world by
        // definition, so every face at or beyond its count belongs to a door, a lift, a cart or
        // some other entity that has to be free to move.
        //
        // int.MaxValue when the models lump was not read, which builds everything - the behaviour
        // before this boundary existed. An unknown boundary must not silently delete geometry:
        // "we do not know which faces move" and "no faces move" are different facts, and only one
        // of them is safe to act on.
        int worldFaceCount = models is { Count: > 0 } ? models[0].FaceCount : int.MaxValue;

        // Grouped first so each material's triangles end up contiguous, then flattened. A
        // dictionary keeps the grouping O(n) rather than sorting thirteen thousand faces.
        Dictionary<int, List<WorldVertex>> byMaterial = [];

        foreach (BspSurface surface in surfaces)
        {
            // **No normal cull, and its removal is the point.** This used to discard every face
            // whose normal pointed downward, which was free when the only camera looked straight
            // down: a face pointing away from an overhead view can never be seen from one.
            //
            // A camera that can go anywhere makes that assumption false, and it was deleting real
            // geometry — ceilings, undersides, and any wall whose normal tips even slightly below
            // horizontal. It also produced the "floating decals" chased all evening: an overlay
            // pinned to a culled face draws correctly in mid-air, with the wall that should be
            // behind it simply absent.
            //
            // **This is the deviation from the engine.** Valve culls per frame against the view
            // frustum and the PVS, from wherever the camera actually is. Culling once, at build
            // time, by the sign of a normal, is only equivalent for a camera that never moves.
            // Backface culling in the rasteriser still removes what genuinely faces away, per
            // frame, which is where that decision belongs.
            if (!surface.IsVisible || surface.Vertices.Count < 3)
            {
                Drop(surface.IsVisible ? "degenerate" : "not-drawn-flag", surface.MaterialIndex);

                continue;
            }

            // **A brush entity's faces are not the world's, and baking them here freezes them.**
            // The faces lump holds the world model's faces first and every other model's after,
            // so walking all of it draws doors, lifts and payload carts at the position they were
            // COMPILED in - which is not a missing door, it is a door that can never move. On
            // cp_process_f12 that is 1,030 surfaces, and a door compiled retracted sits inside the
            // ceiling and reads as absent (B71).
            //
            // Valve's own comment on the models lump says how a submodel is meant to be used:
            // "submodels just draw faces without walking the bsp tree". They are drawn, per frame,
            // at their entity's networked origin - by the entity path, not this one.
            if (surface.FaceIndex >= worldFaceCount)
            {
                movingFaces++;
                continue;
            }

            if (area is { } bounds && !Touches(surface, bounds))
            {
                // **This skip had no ledger entry, and its absence made the ledger lie.** The
                // report read "every dropped face is a tool material", which is a clean bill of
                // health, while the one rule that discards geometry by POSITION was not counted at
                // all. An instrument that omits a category cannot distinguish "nothing was dropped
                // here" from "I did not look here".
                Drop("outside-play-area", surface.MaterialIndex);

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
                Drop("tool-material", surface.MaterialIndex);

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

                Built(surface.MaterialIndex, SurfaceCategory.Terrain);

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

            Built(surface.MaterialIndex, SurfaceCategory.Brush);
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

        // **Props go in their OWN batches, because the engine draws them after the overlays
        // (B135).** `CBaseWorldView::DrawExecute` at game/client/viewrender.cpp:5487 runs
        // `DrawWorld` — world surfaces and their overlay fragments — and only then
        // `DrawOpaqueRenderables`, which is where `DrawOpaqueRenderables_DrawStaticProps` lives.
        //
        // Merged into `byMaterial` they were drawn with the world, so a pipe was already in the
        // depth buffer when the overlay pass ran, and any bias on that pass let a stripe paint over
        // it. That is the pipes, the light fixtures, and the overlay seen through a wall — one
        // symptom, and it was the ORDER rather than the bias all along.
        Dictionary<int, List<WorldVertex>> propsByMaterial = [];

        (int propTriangles, float furthestPropX, float furthestPropY) =
            AppendProps(props, propsByMaterial, area, categoryColours);

        // **How many of those faces belong to a moving entity rather than to the world.** A door,
        // a lift and a payload cart are each their own BSP model, and their faces sit in the same
        // lump after the world's — so a reader that walks the whole lump draws them, STATICALLY, at
        // whatever position they were compiled in. That is a completely different defect from not
        // drawing them at all, and the two are indistinguishable from a picture: a door compiled
        // retracted is invisible either way.
        //
        // Counted rather than assumed, because the question decided what to build next and the
        // answer was not in evidence. Now counted BY the skip rather than by a second pass over
        // the same list: two loops asking one question is where the two answers drift apart.
        ViewerLog.Write(
            "render",
            $"world: {brushFaces} brush faces, {terrainFaces} terrain faces, " +
            $"{propTriangles} of {props.Count / 3} prop triangles drawn, reaching " +
            $"{furthestPropX:0} x {furthestPropY:0} from the origin, " +
            $"{missingMaterials} faces with no material; " +
            $"{movingFaces} faces held back for entity models rather than baked into the world");

        // **Every dropped face, by reason and material, most numerous first.** Read this when a
        // piece of the map is absent: a real material's name in this list is geometry the player
        // should be seeing, and the reason beside it says which rule removed it.
        // **What the map is actually MADE OF, by material and by kind.** The counts above say how
        // many faces were drawn and the ledger says what was dropped; neither says what a surface
        // IS, which is the question a category view exists to answer and the one that cannot be
        // answered from a screenshot. Asked for after an evening of inferring a floor's nature from
        // its appearance: "look at the bsp to see what the fuck the actual things are made from so
        // we know what color it should be".
        //
        // The largest twenty-five, because a floor or a wall is large by definition and the tail is
        // hundreds of trim pieces.
        foreach ((int material, (int faces, SurfaceCategory kind)) in drawn
            .OrderByDescending(entry => entry.Value.Faces)
            .Take(25))
        {
            string name = material >= 0 && material < materials.Count
                ? materials[material].Name
                : "<no material>";

            ViewerLog.Write("render", $"  built {faces} x {kind} '{name}' (material {material})");
        }

        foreach ((string what, int count) in dropped.OrderByDescending(entry => entry.Value))
        {
            ViewerLog.Write("render", $"  dropped {count} x {what}");
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

        List<WorldBatch> decals = AppendDecals(
            all, overlays, materials, surfaces, atlas, area, categoryColours);

        // **After the decals in the buffer as well as in the pass list**, so the three runs read in
        // the order they are drawn. Nothing requires it — a batch names its own range — but a vertex
        // buffer whose layout matches the frame is one less thing to hold in mind when reading a
        // capture.
        List<WorldBatch> propBatches = [];

        foreach (KeyValuePair<int, List<WorldVertex>> group in propsByMaterial)
        {
            if (group.Value.Count == 0)
            {
                continue;
            }

            propBatches.Add(new WorldBatch(group.Key, all.Count, group.Value.Count));
            all.AddRange(group.Value);
        }

        return new MapWorld(all, batches, decals, propBatches);
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
        IReadOnlyList<BspMaterial> materials,
        IReadOnlyList<BspSurface> surfaces,
        LightmapAtlas atlas,
        MapBounds? area,
        bool categoryColours)
    {
        (float red, float green, float blue) = categoryColours
            ? CategoryColour(SurfaceCategory.Overlay)
            : (1f, 1f, 1f);

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

        // Fragments against faces named: the pair, because one without the other cannot say
        // whether a shortfall is the overlay covering less than its list or the list being short.
        int totalFragments = 0;
        int namedFaces = 0;

        foreach (BspOverlay overlay in overlays)
        {
            if (overlay.MaterialIndex < 0)
            {
                continue;
            }

            // **The second orientation filter, and it gated the WHOLE overlay.** A pre-pass looked
            // for any one face within 25 degrees of the basis and skipped the overlay entirely if it
            // found none — so an overlay lying only on chamfers drew nothing at all, and was
            // reported as "lying flat on nothing" rather than as refused.
            //
            // Deleted rather than widened. The surface it found was never used for anything but its
            // own null check, and the `fragments == 0` test below already reports an overlay that
            // clipped away everywhere — for the real reason, after actually trying. Two gates
            // answering one question is how the first one survived B68's fix.
            IReadOnlyList<(float X, float Y, float Z)> quad = overlay.WorldCorners;

            if (area is { } bounds && !quad.Any(corner =>
                    corner.X >= bounds.MinX && corner.X <= bounds.MaxX &&
                    corner.Y >= bounds.MinY && corner.Y <= bounds.MaxY))
            {
                continue;
            }

            // **The corner order is measured, not assumed, because the SDK cannot answer it.**
            // vbsp copies uv0-uv3 through from the VMF untouched (utils/vbsp/overlay.cpp) and
            // nothing in the released source reads them back: the overlay renderer is engine-side.
            //
            // The map answers it instead, because a decal's texture and its quad are the same
            // shape. On cp_process_f12, measuring each quad along BasisU against BasisV:
            //
            //   signs/capture_zone       512x128 (4.000)   quad 128x32   (4.000)
            //   signs/sign069            256x512 (0.500)   quad  36x70   (0.511)
            //   signs/factory_label02    256x256 (1.000)   quad  43x43   (1.007)
            //   overlays/floor_stain003  512x512 (1.000)   quad 128x128  (1.000)
            //
            // So U runs along the corners' first component and V along their second, and the
            // corners arrive anticlockwise from the U/V minimum. Transposed - which is what this
            // did - capture_zone maps a 4:1 banner onto a 1:4 strip, which drew the lettering
            // ninety degrees out and squeezed into a narrow column.
            if (!byMaterial.TryGetValue(overlay.MaterialIndex, out List<WorldVertex>? into))
            {
                into = [];
                byMaterial[overlay.MaterialIndex] = into;
            }

            // **An overlay's face list is the set of surfaces to CLIP against, not a list of
            // candidates to pick one from.** This used to take the first face sharing an
            // orientation and draw a single flat quad from the overlay's own corners.
            //
            // For a sign on one wall that is right, and cp_process's REDSTONE CARGO lettering and
            // its arrows have always looked correct. For anything spanning a corner it is not: an
            // `overlays/stripe_red` names up to EIGHTEEN faces (median three), so its quad is a
            // flat plane cutting straight through the building where the wall turns, hanging in
            // the air on both sides of it. Forty-five red stripes and forty-three blue ones on
            // this map alone.
            //
            // The engine clips the overlay polygon against each face it names and draws a fragment
            // per face, which is why its stripes follow the geometry around corners. Same here:
            // clip to the face's own edges, drop the fragment onto that face's plane, and light it
            // from that face's lightmap rectangle.
            int fragments = 0;

            namedFaces += overlay.Faces.Count;

            foreach (int face in overlay.Faces)
            {
                // **Every face the overlay names, with no orientation test.** This used to refuse
                // any face more than about 25 degrees off the overlay's basis, which is the same
                // mistake B68 was filed for — choosing from the list rather than clipping against
                // it — surviving inside the fix for it.
                //
                // vbsp puts no such condition on the list. `Overlay_AddFaceToLists`
                // (utils/vbsp/overlay.cpp:171) adds a face because it came from a SIDE the mapper
                // assigned the overlay to, and tests nothing but whether it is already there. The
                // list is a statement of intent.
                //
                // Measured on cp_process_f12 before removing it: 108 of 634 named faces refused,
                // across 38 overlays, and **every one of them on `overlays/stripe_red` or
                // `concrete/stripe_blue`** — the red and blue wall stripes, which is precisely what
                // the owner reported as missing from walls it belongs on. 90 of the 108 sit at
                // roughly 45 degrees: chamfered corners, where the projection below works perfectly
                // well. See OverlayFaceFilterProbe, which measures this on demand.
                //
                // **The stated limit, since one exists.** A face at 90 degrees to the overlay has
                // its fragment projected onto its own plane along its own normal, and a polygon
                // lying in a plane that CONTAINS that normal projects to a line — zero area,
                // nothing drawn. Two of cp_process's faces are in that position. Reproducing them
                // needs the engine's own fragment builder, which clips the FACE against the
                // overlay's extruded boundary instead of projecting the overlay onto the face, and
                // that routine is not published. Recorded rather than approximated with a
                // threshold, because a threshold that catches those two also throws away ninety
                // faces that were fine.
                if (!byFace.TryGetValue(face, out BspSurface? piece))
                {
                    continue;
                }

                List<(float X, float Y, float Z)> fragment = ClipFaceToOverlay(piece, overlay, quad);

                if (fragment.Count < 3)
                {
                    continue;
                }

                AtlasRect onFace = piece.FaceIndex < atlas.Rectangles.Count
                    ? atlas.Rectangles[piece.FaceIndex]
                    : default;

                List<WorldVertex> corners = new(fragment.Count);

                foreach ((float x, float y, float z) in fragment)
                {
                    (float lightU, float lightV) = piece.Lighting.Project(x, y, z);
                    (float u, float v) = TextureAt(overlay, x, y, z);

                    corners.Add(new WorldVertex(
                        x,
                        y,

                        // **World height, not a depth.** D35: the camera projects it, so the same
                        // geometry serves an overhead view, a free camera and a first-person one.
                        z,
                        u,
                        v,
                        onFace.U + (Math.Clamp(lightU, 0f, 1f) * onFace.Width),
                        onFace.V + (Math.Clamp(lightV, 0f, 1f) * onFace.Height),
                        0f,
                        red,
                        green,
                        blue));
                }

                // A fan, because clipping a convex quad against convex edges stays convex.
                for (int corner = 1; corner + 1 < corners.Count; corner++)
                {
                    into.Add(corners[0]);
                    into.Add(corners[corner]);
                    into.Add(corners[corner + 1]);
                }

                fragments++;
            }

            totalFragments += fragments;

            if (fragments == 0)
            {
                // Named faces it shares a plane with, and clipped to nothing on all of them.
                unlit++;
                continue;
            }

            placed++;
        }

        foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
        {
            decals.Add(new WorldBatch(group.Key, all.Count, group.Value.Count));
            all.AddRange(group.Value);
        }

        // **The FRAGMENT total, not just the overlay total, and the difference is the whole of
        // B134.** An overlay wrapping a chamfered corner draws one fragment per face it covers, so
        // an orientation filter that refused 108 of cp_process's 634 named faces still reported all
        // 222 overlays as "placed" — every one of them kept at least one face. The count that would
        // have shown the loss was the one nobody logged.
        ViewerLog.Write(
            "map",
            $"{placed} decals placed across {decals.Count} materials, {totalFragments} fragments " +
            $"over {namedFaces} faces named by {overlays?.Count ?? 0} overlays, " +
            $"{unlit} lying flat on nothing");

        // **Named with their transparency, because a decal drawn opaque is a square of paint.**
        // An overlay blends onto the surface it marks; if its material is not carrying alpha, the
        // blend has nothing to work with and the quad's whole extent is painted.
        foreach (int material in decals.Select(batch => batch.MaterialIndex))
        {
            string name = material >= 0 && material < materials.Count
                ? materials[material].Name
                : "none";

            ViewerLog.Write("map", $"  decal material {material} {name}");
        }

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
    /// <returns>How many prop triangles were actually appended, and how far they reach.</returns>
    private static (int Triangles, float FurthestX, float FurthestY) AppendProps(
        IReadOnlyList<PropVertex> props,
        Dictionary<int, List<WorldVertex>> byMaterial,
        MapBounds? area,
        bool categoryColours)
    {
        // **Counted on the way OUT, because the count on the way in cannot see a cull.** The world
        // log reported `props.Count / 3` for months, which is what this method was HANDED — so
        // removing the play-area cull moved the brush count by exactly the 133 faces the ledger
        // predicted and left the prop figure identical, and neither number was wrong. The prop one
        // simply was not measuring the thing it was being read for.
        //
        // The furthest reach comes with it because that is the question the count cannot answer:
        // a TF2 map keeps its 3D skybox as ordinary props far outside the level, so "are they in"
        // is a question about DISTANCE, and a total says nothing about where anything is.
        int appended = 0;
        float furthestX = 0f;
        float furthestY = 0f;

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

            appended++;

            furthestX = Math.Max(furthestX, Math.Abs(first.OriginX));
            furthestY = Math.Max(furthestY, Math.Abs(first.OriginY));
        }

        return (appended, furthestX, furthestY);
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
    public static (float Lowest, float Highest) HeightRange(
        IReadOnlyList<BspSurface> surfaces, MapBounds? area)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

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
        // camera and geometry flattened for one camera cannot serve another (D35).
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
            // Violet, chosen to sit away from all four of the others rather than to look nice: an
            // overlay lies ON brushwork and next to props, so it has to be told from grey-blue and
            // orange at a glance and at a distance.
            SurfaceCategory.Overlay => (0.62f, 0.4f, 0.92f),
            // **White, so Valve's chequer shows in its own colours.** The renderer binds the
            // magenta-and-black missing-material chequer under this category instead of the
            // measurement grid, and a tint would only muddy a pattern that is already the most
            // recognisable "this is broken" signal in Source. White multiplies to no change.
            //
            // This is what the collision with Hammer's default resolved to. Magenta belongs to
            // Hammer's uncoloured entity; an unresolved material is a CHEQUER rather than a colour,
            // so the two never needed the same hue — they are told apart by pattern, and both are
            // Valve's rather than one being ours.
            SurfaceCategory.Missing => (1f, 1f, 1f),
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

        /// <summary>An overlay fragment — a marking clipped to the surface it lies on.</summary>
        /// <remarks>
        /// **Added because its absence was read as an answer.** Overlay fragments carried no vertex
        /// colour, so they took the default of white — which is not a category colour but the lack
        /// of one, and there was no legend entry saying so. During the B154 hunt that white was
        /// read first as "an uncoloured surface" and then as the sign being investigated, and it
        /// was neither. A diagnostic view that omits a category cannot answer "is anything here"
        /// for that category, which is the one question it exists to answer.
        /// </remarks>
        Overlay,

        /// <summary>Anything whose material could not be resolved.</summary>
        Missing,
    }

    /// <summary>A surface's material, or -1 when it names one the map does not have.</summary>
    private static int materialIndex(BspSurface surface) => surface.MaterialIndex;

    /// <summary>Clips one face to the volume an overlay projects, keeping the part it marks.</summary>
    /// <param name="face">The face the overlay names.</param>
    /// <param name="overlay">The overlay, for its basis and its quad.</param>
    /// <param name="quad">The overlay's four world corners, already computed by the caller.</param>
    /// <returns>The marked part of the face, empty when the overlay does not reach it.</returns>
    /// <remarks>
    /// **This clips the FACE to the overlay, where it used to clip the overlay to the face.** The
    /// two sound like the same operation and are not, and the difference is what a wall stripe on
    /// cp_process looks like.
    ///
    /// The old way took the overlay's quad, cut it down with the face's own edge planes, and then
    /// dropped the survivor onto the face's plane. Every fragment was therefore bounded by BSP
    /// SPLITS rather than by the overlay, so a band that should be a uniform height arrived as a
    /// run of trapezoids of differing heights with gaps between them — and on anything not parallel
    /// to the overlay, the drop onto the face plane moved each corner by a different distance and
    /// skewed the piece as well.
    ///
    /// **An overlay is a projection, so the fragment is the part of the surface inside its volume.**
    /// The quad's four edges, each swept along the basis normal, bound an infinite prism; clipping
    /// the face's own polygon against those four half-spaces leaves exactly the marked part. Three
    /// things follow for free, and all three are what the reference screenshots show:
    ///
    /// - The fragment is a subset of the face, so it lies ON the wall by construction. Nothing can
    ///   hover, and no projection step is needed at all.
    /// - Adjacent faces tile, because they share edges and the clip planes are the same for both.
    ///   The gaps close without any slack fudge.
    /// - The band's height is the overlay's V extent everywhere, because two of the four planes ARE
    ///   the band's edges. That is the uniform stripe the game draws.
    ///
    /// **Evidence class: interpolated, and it has to be.** `Overlay_AddFaceToLists` and
    /// `Overlay_EmitOverlayFace` are published in `utils/vbsp/overlay.cpp` and this project's reader
    /// matches them field for field — the basis packed into the unused z of the first three UV
    /// points, the V flip in the fourth, the face count masked out of
    /// `m_nFaceCountAndRenderOrder`. What builds the fragments is `engine/overlay.cpp`, which Valve
    /// has never published, and nothing in source-sdk-2013 references the lump outside vbsp. So the
    /// algorithm here is derived from what an overlay IS rather than transcribed, and is flagged as
    /// interpolated per D44.
    /// </remarks>
    internal static List<(float X, float Y, float Z)> ClipFaceToOverlay(
        BspSurface face,
        BspOverlay overlay,
        IReadOnlyList<(float X, float Y, float Z)> quad)
    {
        IReadOnlyList<SurfaceVertex> outline = face.Vertices;

        if (outline.Count < 3 || quad.Count < 3)
        {
            return [];
        }

        // The face itself is what gets cut down, so the result is always part of the wall.
        List<(float X, float Y, float Z)> polygon =
            [.. outline.Select(corner => (corner.X, corner.Y, corner.Z))];

        // **The quad's centre settles which side is inside**, exactly as the face's centroid used
        // to for the old direction of the clip. A quad's winding is not guaranteed either.
        (float X, float Y, float Z) middle = (0f, 0f, 0f);

        foreach ((float x, float y, float z) in quad)
        {
            middle = (middle.X + x, middle.Y + y, middle.Z + z);
        }

        middle = (middle.X / quad.Count, middle.Y / quad.Count, middle.Z / quad.Count);

        for (int edge = 0; edge < quad.Count && polygon.Count > 0; edge++)
        {
            (float X, float Y, float Z) from = quad[edge];
            (float X, float Y, float Z) to = quad[(edge + 1) % quad.Count];

            (float X, float Y, float Z) along = (to.X - from.X, to.Y - from.Y, to.Z - from.Z);

            // The plane through this edge containing the basis normal: its normal is the edge
            // crossed with the normal, which points sideways out of the prism.
            (float X, float Y, float Z) inward = (
                (along.Y * overlay.BasisNormal.Z) - (along.Z * overlay.BasisNormal.Y),
                (along.Z * overlay.BasisNormal.X) - (along.X * overlay.BasisNormal.Z),
                (along.X * overlay.BasisNormal.Y) - (along.Y * overlay.BasisNormal.X));

            float length = MathF.Sqrt(
                (inward.X * inward.X) + (inward.Y * inward.Y) + (inward.Z * inward.Z));

            if (length < 1e-6f)
            {
                continue;
            }

            inward = (inward.X / length, inward.Y / length, inward.Z / length);

            float offset = (inward.X * from.X) + (inward.Y * from.Y) + (inward.Z * from.Z);

            if ((inward.X * middle.X) + (inward.Y * middle.Y) + (inward.Z * middle.Z) < offset)
            {
                inward = (-inward.X, -inward.Y, -inward.Z);
                offset = -offset;
            }

            // **No slack here, unlike the old clip.** That version needed a unit of give because
            // its fragments were bounded by face edges and the seams between them showed; these are
            // bounded by the overlay itself and tile exactly, so a unit of give would only paint a
            // unit beyond what the mapper drew.
            polygon = ClipToHalfSpace(polygon, inward, offset);
        }

        return polygon.Count < 3 ? [] : polygon;
    }

    /// <summary>Keeps the part of a polygon on the inward side of one plane.</summary>
    private static List<(float X, float Y, float Z)> ClipToHalfSpace(
        List<(float X, float Y, float Z)> polygon,
        (float X, float Y, float Z) normal,
        float offset)
    {
        List<(float X, float Y, float Z)> kept = new(polygon.Count + 1);

        for (int index = 0; index < polygon.Count; index++)
        {
            (float X, float Y, float Z) current = polygon[index];
            (float X, float Y, float Z) next = polygon[(index + 1) % polygon.Count];

            float here =
                (normal.X * current.X) + (normal.Y * current.Y) + (normal.Z * current.Z) - offset;

            float there =
                (normal.X * next.X) + (normal.Y * next.Y) + (normal.Z * next.Z) - offset;

            if (here >= 0f)
            {
                kept.Add(current);
            }

            // Crossing the plane in either direction adds the point where it crosses.
            if ((here >= 0f) != (there >= 0f) && MathF.Abs(here - there) > 1e-9f)
            {
                float step = here / (here - there);

                kept.Add((
                    current.X + ((next.X - current.X) * step),
                    current.Y + ((next.Y - current.Y) * step),
                    current.Z + ((next.Z - current.Z) * step)));
            }
        }

        return kept;
    }

    /// <summary>Where a point on an overlay falls in its texture.</summary>
    /// <remarks>
    /// **Recovered from the basis rather than carried on the corners**, because clipping creates
    /// points that were never corners. An overlay is planar and its basis is orthonormal, so the
    /// distance along each axis from the origin IS the position in the overlay's own space, and the
    /// texture range maps linearly onto the corners' extent in that space.
    /// </remarks>
    private static (float U, float V) TextureAt(BspOverlay overlay, float x, float y, float z)
    {
        (float X, float Y, float Z) from = (
            x - overlay.Origin.X, y - overlay.Origin.Y, z - overlay.Origin.Z);

        float along =
            (from.X * overlay.BasisU.X) + (from.Y * overlay.BasisU.Y) + (from.Z * overlay.BasisU.Z);

        float down =
            (from.X * overlay.BasisV.X) + (from.Y * overlay.BasisV.Y) + (from.Z * overlay.BasisV.Z);

        float leftmost = overlay.Corners.Min(corner => corner.X);
        float rightmost = overlay.Corners.Max(corner => corner.X);
        float topmost = overlay.Corners.Min(corner => corner.Y);
        float bottommost = overlay.Corners.Max(corner => corner.Y);

        return (
            Lerp(overlay.U.Start, overlay.U.End, Fraction(along, leftmost, rightmost)),
            Lerp(overlay.V.Start, overlay.V.End, Fraction(down, topmost, bottommost)));
    }

    private static float Fraction(float value, float start, float end) =>
        MathF.Abs(end - start) < 1e-6f ? 0f : (value - start) / (end - start);

    private static float Lerp(float start, float end, float fraction) =>
        start + ((end - start) * fraction);

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
