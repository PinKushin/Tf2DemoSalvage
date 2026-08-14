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
/// <param name="Frame">Which baked animation frame to draw, from the demo's sequence and cycle.</param>
internal readonly record struct ModelInstance(
    string ModelPath, float[] Matrix, AmbientCube Light, SunLight? Sun, int Frame = 0);

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

    /// <summary>Every packed model's batches, one list per baked animation frame.</summary>
    /// <remarks>
    /// **A frame is a vertex range, not a transform.** Each of an animated model's frames is
    /// skinned once at load and packed like a separate model, so drawing one is picking a range —
    /// no per-frame work on either processor. A model that does not animate has exactly one entry
    /// and costs what it always did.
    /// </remarks>
    private readonly Dictionary<string, List<List<WorldBatch>>> _byModel =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PropModels.ModelFrames> _frames =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models already reported as animating, so the log states it once.</summary>
    private readonly HashSet<string> _reportedFrames = new(StringComparer.OrdinalIgnoreCase);

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
    public IReadOnlyList<WorldBatch> Batches(string modelPath) => Batches(modelPath, 0);

    /// <summary>The batches for one model at one baked frame.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="frame">Which baked frame; clamped into what was packed.</param>
    /// <returns>One run per material, indexing into <see cref="Vertices"/>.</returns>
    /// <remarks>
    /// **Clamped rather than refused.** A demo can name a sequence whose frame count differs from
    /// the model on this machine — a later game version, or a model replaced by a mod — and
    /// holding the last frame is a better answer than drawing nothing.
    /// </remarks>
    public IReadOnlyList<WorldBatch> Batches(string modelPath, int frame)
    {
        if (!_byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames) ||
            frames.Count == 0)
        {
            return [];
        }

        return frames[Math.Clamp(frame, 0, frames.Count - 1)];
    }

    /// <summary>Every baked frame's batches for one model.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One entry per baked frame, each a list of runs.</returns>
    public IReadOnlyList<IReadOnlyList<WorldBatch>> AllFrames(string modelPath) =>
        _byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames)
            ? frames
            : [];

    /// <summary>Which baked frame a prop's sequence and cycle select.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>A frame index for <see cref="Batches(string, int)"/>.</returns>
    public int FrameFor(SceneProp prop, double seconds) =>
        _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames)
            ? frames.Frame(prop.Pose.Sequence, prop.Pose.Cycle, seconds)
            : 0;

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
    public bool Add(IReadOnlyList<SceneProp> props, Func<string, PropModels.ModelFrames?> load)
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

            List<List<WorldBatch>> frames = [];

            _byModel[prop.ModelPath] = frames;
            added = true;

            if (load(prop.ModelPath) is not { Geometry.Count: > 0 } model)
            {
                continue;
            }

            _frames[prop.ModelPath] = model;

            foreach (IReadOnlyList<PropVertex> corners in model.Geometry)
            {
                List<WorldBatch> batches = [];
                frames.Add(batches);

                // Grouped by material so one bind covers every triangle of this frame that shares
                // it. Every frame carries the same corners in the same order, so the batching is
                // identical between them and only the positions differ.
                Dictionary<int, List<WorldVertex>> byMaterial = [];

                foreach (PropVertex corner in corners)
                {
                    if (!byMaterial.TryGetValue(corner.MaterialIndex, out List<WorldVertex>? into))
                    {
                        into = [];
                        byMaterial[corner.MaterialIndex] = into;
                    }

                    // **Model space, untouched.** The shader's model matrix places it. No lightmap
                    // either: a studio model is lit by its own vertex colours in the engine too,
                    // and a zero-width atlas rectangle sends every corner to the reserved white
                    // texel so the lightmap term is an identity rather than darkness.
                    into.Add(new WorldVertex(
                        corner.X, corner.Y, corner.Z, corner.U, corner.V, 0f, 0f, 0f,
                        NormalX: corner.NormalX,
                        NormalY: corner.NormalY,
                        NormalZ: corner.NormalZ));
                }

                foreach (KeyValuePair<int, List<WorldVertex>> group in byMaterial)
                {
                    batches.Add(new WorldBatch(group.Key, _vertices.Count, group.Value.Count));
                    _vertices.AddRange(group.Value);
                }
            }

            // **A model's own bounding box, logged for every model.** Whether a model stands up is
            // not answerable from an overhead camera - a squat prop looks the same lying down, so
            // the whole prop set can be tipped and read as correct. A humanoid is the first model
            // tall enough to show it.
            //
            // In Source's model space a player is about 83 units tall and far narrower, so an
            // upright model has Z much the largest extent. If Z is the smallest, the model is on
            // its side and the fault is in the transform rather than in any missing animation.
            float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
            float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;

            foreach (PropVertex corner in model.Geometry[0])
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
                $"extents {prop.ModelPath}: x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
                $"(z from {minimumZ:0.#} to {maximumZ:0.#}), " +
                $"tallest axis {Tallest(spanX, spanY, spanZ)}, {frames.Count} baked frames");
        }

        return added;
    }

    /// <summary>Where each model stands at this moment.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="into">Filled with one entry per drawable entity; cleared first.</param>
    /// <param name="lightAt">The ambient cube at a world position, or null to leave models unlit.</param>
    /// <param name="sunAt">The sun at a world position, or null to apply no direct light.</param>
    /// <param name="seconds">Demo time, for advancing animation cycles.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// One matrix per entity, which is all that changes between frames. The geometry it points at
    /// was uploaded once and stays where it is.
    /// </remarks>
    public void Instances(
        IReadOnlyList<SceneProp> props,
        ICollection<ModelInstance> into,
        Func<float, float, float, AmbientCube>? lightAt = null,
        Func<float, float, float, SunLight?>? sunAt = null,
        double seconds = 0d)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (SceneProp prop in props)
        {
            int frame = FrameFor(prop, seconds);

            if (prop.Kind != SceneModelKind.Studio || Batches(prop.ModelPath, frame).Count == 0)
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

            if (!_reportedFrames.Contains(prop.ModelPath))
            {
                _reportedFrames.Add(prop.ModelPath);

                ViewerLog.Write(
                    "render",
                    $"animating {prop.ModelPath}: sequence {pose.Sequence} cycle {pose.Cycle:0.###} " +
                    $"-> baked frame {frame} of {AllFrames(prop.ModelPath).Count}");
            }

            into.Add(new ModelInstance(
                prop.ModelPath,
                transform.ToMatrix(),
                light,
                sunAt?.Invoke(pose.X, pose.Y, pose.Z),
                frame));
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

        // **Only flagged when the model is clearly long in the wrong direction.** A medkit is
        // 24 by 17 by 23 and legitimately near-cubic; calling that "on its side" cries wolf on a
        // correct model, which is how a real warning stops being read.
        string axis = spanX >= spanY ? "x" : "y";

        return MathF.Max(spanX, spanY) > spanZ * 1.5f ? axis + ", ON ITS SIDE" : axis;
    }
}
