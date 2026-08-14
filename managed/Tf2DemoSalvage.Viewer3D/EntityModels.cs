using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One model to draw, where it stands, and the light reaching it.</summary>
/// <param name="ModelPath">Which packed model to draw.</param>
/// <param name="Matrix">Sixteen floats, row major, for the shader's model constant.</param>
/// <param name="Light">The ambient cube of the leaf it stands in.</param>
/// <param name="Sun">The sun, when this model traced to sky; null when it stands in shade.</param>
internal readonly record struct ModelInstance(
    string ModelPath, float[] Matrix, AmbientCube Light, SunLight? Sun);

/// <summary>
/// The models a demo's entities wear, packed once and posed by the GPU.
/// </summary>
/// <remarks>
/// **This is the engine's arrangement, and the reason for it is speed.**
/// <c>IMaterialSystem::LoadBoneMatrix</c> hands a transform to the shader as a constant and the GPU
/// moves the vertices; <c>imesh.h</c> carries a bone weight and index per vertex, and the material
/// system has a <c>MATERIAL_MODEL</c> matrix mode for exactly this. It is why TF2 draws a great
/// many animated models without noticing them.
///
/// So a model's geometry is read once, in its own coordinates, into a buffer that never changes,
/// and an instance is a matrix. Transforming vertices on the processor every frame — the obvious
/// first implementation, and the one this replaced — is precisely the work that path exists to
/// avoid, and a viewer that did it would feel slow where the game does not.
///
/// A rigid entity is the one-bone case. Animation adds a matrix per bone and a weight per vertex;
/// nothing about the packing changes.
/// </remarks>
internal sealed class EntityModelSet
{
    private readonly List<WorldVertex> _vertices = [];

    private readonly Dictionary<string, List<WorldBatch>> _byModel =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every packed model's triangles, in model space.</summary>
    /// <remarks>
    /// Uploaded once. The vertices never move again — that is the whole point of the arrangement.
    /// </remarks>
    public IReadOnlyList<WorldVertex> Vertices => _vertices;

    /// <summary>How many distinct models have been packed.</summary>
    public int Count => _byModel.Count;

    /// <summary>Every packed model's path.</summary>
    public IEnumerable<string> Paths => _byModel.Keys;

    /// <summary>The batches for one model, or empty when it is not packed.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One run per material, indexing into <see cref="Vertices"/>.</returns>
    public IReadOnlyList<WorldBatch> Batches(string modelPath) =>
        _byModel.TryGetValue(modelPath, out List<WorldBatch>? batches) ? batches : [];

    /// <summary>Packs whatever a moment needs that is not packed already.</summary>
    /// <param name="props">What exists at this tick, from the timeline.</param>
    /// <param name="load">Reads a model in its own coordinates, or answers null.</param>
    /// <returns>Whether anything was added, so the caller knows to re-upload.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Brush models and sprites are not studio models.** A <c>*N</c> reference is an inline BSP
    /// submodel whose geometry lives in the map, and a sprite is a camera-facing quad; handing
    /// either to a <c>.mdl</c> loader draws nothing and reports nothing.
    ///
    /// A model that fails to load is remembered as empty rather than retried every frame — the
    /// loader reports it once, and asking again sixty times a second would bury the log in the
    /// same line.
    /// </remarks>
    public bool Add(IReadOnlyList<SceneProp> props, Func<string, IReadOnlyList<PropVertex>?> load)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(load);

        bool added = false;

        foreach (SceneProp prop in props)
        {
            if (prop.Kind != SceneModelKind.Studio || _byModel.ContainsKey(prop.ModelPath))
            {
                continue;
            }

            List<WorldBatch> batches = [];

            _byModel[prop.ModelPath] = batches;
            added = true;

            if (load(prop.ModelPath) is not { Count: > 0 } corners)
            {
                continue;
            }

            // Grouped by material so one bind covers every triangle of this model that shares it.
            Dictionary<int, List<WorldVertex>> byMaterial = [];

            foreach (PropVertex corner in corners)
            {
                if (!byMaterial.TryGetValue(corner.MaterialIndex, out List<WorldVertex>? into))
                {
                    into = [];
                    byMaterial[corner.MaterialIndex] = into;
                }

                // **Model space, untouched.** The shader's model matrix places it. No lightmap
                // either: a studio model is lit by its own vertex colours in the engine too, and a
                // zero-width atlas rectangle sends every corner to the reserved white texel so the
                // lightmap term is an identity rather than darkness.
                into.Add(new WorldVertex(
                    corner.X, corner.Y, corner.Z, corner.U, corner.V, 0f, 0f, 0f));
            }

            foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
            {
                batches.Add(new WorldBatch(group.Key, _vertices.Count, group.Value.Count));
                _vertices.AddRange(group.Value);
            }

            // **A model's own bounding box, logged for every model.** Whether a model stands up is
            // not answerable from an overhead camera - a squat prop looks the same lying down, so
            // the whole prop set can be tipped and read as correct. A humanoid is the first model
            // tall enough to show it, which means the picture noticed a defect the props had been
            // hiding since they were added.
            //
            // In Source's model space a player is about 83 units tall and far narrower, so an
            // upright model has Z much the largest extent. If Z is the smallest, the model is on
            // its side and the fault is in the transform rather than in any missing animation.
            float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
            float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;

            foreach (PropVertex corner in corners)
            {
                minimumX = MathF.Min(minimumX, corner.X);
                minimumY = MathF.Min(minimumY, corner.Y);
                minimumZ = MathF.Min(minimumZ, corner.Z);
                maximumX = MathF.Max(maximumX, corner.X);
                maximumY = MathF.Max(maximumY, corner.Y);
                maximumZ = MathF.Max(maximumZ, corner.Z);
            }

            float spanX = maximumX - minimumX;
            float spanY = maximumY - minimumY;
            float spanZ = maximumZ - minimumZ;

            ViewerLog.Write(
                "props",
                $"extents {prop.ModelPath}: " +
                $"x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
                $"(z from {minimumZ:0.#} to {maximumZ:0.#}), " +
                $"tallest axis {Tallest(spanX, spanY, spanZ)}");
        }

        return added;
    }

    /// <summary>Where each model stands at this moment.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="into">Filled with one entry per drawable entity; cleared first.</param>
    /// <param name="lightAt">The ambient cube at a world position, or null to leave models unlit.</param>
    /// <param name="sunAt">The sun at a world position, or null to apply no direct light.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// One matrix per entity, which is all that changes between frames. The geometry it points at
    /// was uploaded once and stays where it is.
    /// </remarks>
    public void Instances(
        IReadOnlyList<SceneProp> props,
        ICollection<ModelInstance> into,
        Func<float, float, float, AmbientCube>? lightAt = null,
        Func<float, float, float, SunLight?>? sunAt = null)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (SceneProp prop in props)
        {
            if (prop.Kind != SceneModelKind.Studio || Batches(prop.ModelPath).Count == 0)
            {
                continue;
            }

            ScenePose pose = prop.Pose;

            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            // **Lit from where it stands, which is what the engine does.** A model has no
            // lightmap, so vrad's per-leaf ambient cube is the light it gets - sampled at the
            // origin rather than per vertex, exactly as the client samples it once per model.
            AmbientCube light = lightAt is null
                ? default
                : lightAt(pose.X, pose.Y, pose.Z);

            into.Add(new ModelInstance(
                prop.ModelPath,
                transform.ToMatrix(),
                light,
                sunAt?.Invoke(pose.X, pose.Y, pose.Z)));
        }
    }

    /// <summary>Which axis a model is longest along, named for the log.</summary>
    /// <remarks>
    /// "z, upright" is the expected answer for anything that stands up. Anything else on a
    /// humanoid means the model is on its side.
    /// </remarks>
    private static string Tallest(float spanX, float spanY, float spanZ)
    {
        if (spanZ >= spanX && spanZ >= spanY)
        {
            return "z, upright";
        }

        return spanX >= spanY ? "x, ON ITS SIDE" : "y, ON ITS SIDE";
    }
}
