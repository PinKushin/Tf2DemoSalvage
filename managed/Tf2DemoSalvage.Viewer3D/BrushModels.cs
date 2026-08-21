using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Builds the geometry for a map's brush entities — doors, lifts, carts — keyed by <c>*N</c>.
/// </summary>
/// <remarks>
/// **A brush entity is a run of faces, not a file.** A health pack names
/// <c>models/items/medkit_small.mdl</c>; a door names <c>*12</c>, an index into the map's own
/// models lump. Valve's comment on that lump says how it is meant to be read: "submodels just draw
/// faces without walking the bsp tree" — there is no visibility structure to consult, just
/// <c>firstface</c> and <c>numfaces</c>.
///
/// **Why they need their own geometry rather than being part of the world.** The faces lump holds
/// the world model's faces first and every entity model's after, so a builder that walks all of it
/// bakes doors into the static vertex buffer at the position they were COMPILED in. That is not a
/// missing door — it is a door that can never move, and from a screenshot the two are identical
/// (B71). The engine draws it the other way round, and says so in `C_BaseEntity::DrawBrushModel`:
/// "Identity brushes are drawn in view->DrawWorld as an optimization", with everything else going
/// to <c>render->DrawBrushModelEx( this, model, GetAbsOrigin(), GetAbsAngles(), mode )</c>.
///
/// **Vertices stay in world space, which is not what a studio model does and is deliberate.** A
/// `.mdl` is authored around its own origin and placed by a matrix; a brush is compiled in place
/// and its entity's networked origin is an OFFSET from there, zero for a door sitting closed. So
/// the same transform the entity path already applies puts a closed door exactly where it was
/// compiled, and moves it when the demo says it moved.
///
/// **The rotation pivot needs no correction here, and the compiler is why.** The first draft of
/// this comment assumed <c>dmodel_t::origin</c> was the pivot and flagged the omission as a known
/// gap for rotating doors. It is not the pivot — the field is annotated <c>// for sounds or
/// lights</c> in <c>public/bspfile.h</c> — and vbsp has already done the work:
///
/// <code>
/// // origin brushes are removed, but they set
/// // the rotation origin for the rest of the brushes
/// // in the entity.  After the entire entity is parsed,
/// // the planenums and texinfos will be adjusted for
/// // the origin brush
/// </code>
///
/// (<c>utils/vbsp/map.cpp</c>.) A mapper's origin brush becomes the entity's <c>origin</c>
/// keyvalue and the entity's remaining brushes are shifted to be relative to it; an entity with
/// no origin brush keeps world-space vertices and an origin of zero. So in both cases
/// <c>world = entityOrigin + Rotate(angles) × vertexAsStored</c>, which is the transform the
/// entity path already applies. A rotating door is right for the same reason a sliding one is.
/// </remarks>
internal static class BrushModels
{
    /// <summary>The prefix a brush entity's model name carries instead of a path.</summary>
    public const char SubmodelPrefix = '*';

    /// <summary>
    /// Builds one frame of geometry per brush entity the map defines.
    /// </summary>
    /// <param name="models">The map's models lump. Index 0 is the world and is skipped.</param>
    /// <param name="surfaces">Every surface read from the map, world and entity alike.</param>
    /// <param name="atlas">
    /// The packed lightmaps, so a door's faces can be looked up in the same atlas the wall's are.
    /// Required rather than optional: omitting it produces a door lit like a model, which is a
    /// plausible picture and was B131.
    /// </param>
    /// <returns>Geometry keyed by <c>*N</c>, ready to be looked up by an entity's model name.</returns>
    /// <remarks>
    /// Index 0 is skipped rather than included, because the world is drawn by the static path and
    /// no entity ever references <c>*0</c>: it is `worldspawn`, which does not move.
    /// </remarks>
    public static IReadOnlyDictionary<string, PropModels.ModelFrames> Build(
        IReadOnlyList<BspModel> models,
        IReadOnlyList<BspSurface> surfaces,
        LightmapAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(atlas);

        Dictionary<string, PropModels.ModelFrames> built =
            new(StringComparer.OrdinalIgnoreCase);

        // Faces are addressed by index, and the surface list is not guaranteed to be dense or in
        // order once tool materials and unreadable faces have been dropped.
        Dictionary<int, BspSurface> byFace = [];

        foreach (BspSurface surface in surfaces)
        {
            byFace[surface.FaceIndex] = surface;
        }

        for (int index = 1; index < models.Count; index++)
        {
            BspModel model = models[index];
            List<PropVertex> corners = [];

            for (int face = model.FirstFace; face < model.FirstFace + model.FaceCount; face++)
            {
                if (!byFace.TryGetValue(face, out BspSurface? surface) ||
                    surface.Vertices.Count < 3)
                {
                    continue;
                }

                // **The face's own rectangle in the shared atlas, exactly as MapWorld looks it up.**
                // Rectangles are indexed by face, and the atlas is packed from every face in the
                // lump — world and entity alike — so a door's faces are already in it. A face with
                // no baked light gets a zero-width rectangle, which lands on the reserved white
                // texel and draws at full texture brightness rather than black.
                AtlasRect rectangle = surface.FaceIndex < atlas.Rectangles.Count
                    ? atlas.Rectangles[surface.FaceIndex]
                    : default;

                // Per vertex rather than per material, for the same reason the world path carries
                // it: a batch spans many faces and each has its own lightmap size.
                float lightStep = surface.FaceIndex < atlas.DirectionalSteps.Count
                    ? atlas.DirectionalSteps[surface.FaceIndex]
                    : 0f;

                // A fan from the first corner, as the world path does: a face out of a BSP is
                // convex by construction.
                for (int corner = 1; corner + 1 < surface.Vertices.Count; corner++)
                {
                    Append(corners, surface, 0, rectangle, lightStep);
                    Append(corners, surface, corner, rectangle, lightStep);
                    Append(corners, surface, corner + 1, rectangle, lightStep);
                }
            }

            if (corners.Count == 0)
            {
                // No geometry is a real answer for a submodel whose faces are all tool textures -
                // a trigger volume is a brush entity too, and drawing nothing is correct for it.
                continue;
            }

            built[SubmodelPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                new PropModels.ModelFrames([corners], new Dictionary<int, (int, int, float)>(), [], []);
        }

        return built;
    }

    private static void Append(
        List<PropVertex> corners,
        BspSurface surface,
        int index,
        AtlasRect rectangle,
        float lightStep)
    {
        SurfaceVertex vertex = surface.Vertices[index];

        // **Clamped before remapping, as MapWorld.Append does.** A corner can sit a fraction
        // outside its own lightmap, and in a shared atlas that fraction is another face's light
        // rather than empty space — a door would take a stripe of the wall next to it.
        float lightU = rectangle.U + (Math.Clamp(vertex.LightU, 0f, 1f) * rectangle.Width);
        float lightV = rectangle.V + (Math.Clamp(vertex.LightV, 0f, 1f) * rectangle.Height);

        corners.Add(new PropVertex(
            vertex.X, vertex.Y, vertex.Z,
            vertex.U, vertex.V,
            surface.MaterialIndex,
            NormalX: surface.Normal.X,
            NormalY: surface.Normal.Y,
            NormalZ: surface.Normal.Z,
            LightU: lightU,
            LightV: lightV,
            LightStep: lightStep));
    }
}
