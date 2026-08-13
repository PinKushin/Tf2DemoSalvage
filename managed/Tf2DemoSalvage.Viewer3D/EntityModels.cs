using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Poses the models a demo's entities are wearing, ready for the renderer.
/// </summary>
/// <remarks>
/// **The same shape as a static prop, posed by the demo instead of by the map.** A model is read
/// once in its own coordinates and placed per instance through <see cref="PropTransform"/> — the
/// same transform the map's props use, because the engine has one: an origin, a QAngle and a scale
/// go into <c>AngleMatrix</c> whatever produced them.
///
/// **World space out, always.** Vertices carry world X, Y and Z and the camera projects them
/// (D21), so the same geometry serves the overhead view, a free camera and a first-person one.
/// Nothing here knows which camera is looking.
///
/// **The loader is injected** rather than reached for. A model comes from the map's pakfile or the
/// game's archives, neither of which a test should need, and posing is the part with the
/// arithmetic worth checking.
/// </remarks>
internal static class EntityModels
{
    /// <summary>Poses every studio model in a moment.</summary>
    /// <param name="props">What exists at this tick, from the timeline.</param>
    /// <param name="load">Reads a model in its own coordinates, or answers null.</param>
    /// <param name="vertices">Filled with world-space triangle corners; cleared first.</param>
    /// <param name="batches">Filled with one run per material; cleared first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Brush models and sprites are skipped, and that is not laziness.** A <c>*N</c> reference is
    /// an inline BSP submodel whose geometry lives in the map rather than in a <c>.mdl</c>, and a
    /// sprite is a camera-facing quad; both need their own path, and handing either to a studio
    /// loader draws nothing while reporting nothing.
    ///
    /// A model that will not load is skipped here and reported by the loader, not silently
    /// dropped — the difference between "this entity has no model" and "this entity's model is
    /// missing" is the whole value of the log line.
    /// </remarks>
    public static void Build(
        IReadOnlyList<SceneProp> props,
        Func<string, IReadOnlyList<PropVertex>?> load,
        List<WorldVertex> vertices,
        List<WorldBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        vertices.Clear();
        batches.Clear();

        // Grouped by material so one bind draws every instance sharing it - the same reason the
        // map's own geometry is batched, and it matters more here because a match can carry a
        // hundred copies of one rocket.
        Dictionary<int, List<WorldVertex>> byMaterial = [];

        foreach (SceneProp prop in props)
        {
            if (prop.Kind != SceneModelKind.Studio)
            {
                continue;
            }

            if (load(prop.ModelPath) is not { Count: > 0 } corners)
            {
                continue;
            }

            ScenePose pose = prop.Pose;

            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            foreach (PropVertex corner in corners)
            {
                (float x, float y, float z) = transform.Apply(corner.X, corner.Y, corner.Z);

                if (!byMaterial.TryGetValue(corner.MaterialIndex, out List<WorldVertex>? into))
                {
                    into = [];
                    byMaterial[corner.MaterialIndex] = into;
                }

                // **No lightmap.** A studio model is lit by its own vertex colours in the engine
                // too, and the zero-width atlas rectangle sends every corner to the reserved white
                // texel so the lightmap term is an identity rather than darkness.
                into.Add(new WorldVertex(x, y, z, corner.U, corner.V, 0f, 0f, 0f));
            }
        }

        foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
        {
            batches.Add(new WorldBatch(group.Key, vertices.Count, group.Value.Count));
            vertices.AddRange(group.Value);
        }
    }
}
