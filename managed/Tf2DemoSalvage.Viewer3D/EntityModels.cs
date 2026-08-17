using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One model to draw, where it stands, and the light reaching it.</summary>
/// <param name="ModelPath">Which packed model to draw.</param>
/// <param name="Matrix">Sixteen floats, row major, for the shader's model constant.</param>
/// <param name="Light">The ambient cube of the leaf it stands in.</param>
/// <param name="Sun">The sun, when this model traced to sky; null when it stands in shade.</param>
/// <param name="Frame">Which baked animation frame to draw, from the demo's sequence and cycle.</param>
/// <param name="Blend">How far toward the next baked frame, so the shader can smooth between them.</param>
/// <param name="Bones">Bone matrices for a model skinned on the GPU, or null when it is baked.</param>
/// <param name="SkinSwap">Which material replaces which for its team, or null.</param>
/// <param name="BodyParts">The model's body parts, for reading its body number.</param>
/// <param name="Body">Which alternative each body part shows, as m_nBody packs it.</param>
internal readonly record struct ModelInstance(
    string ModelPath,
    float[] Matrix,
    AmbientCube Light,
    SunLight? Sun,
    int Frame = 0,
    float Blend = 0f,
    IReadOnlyList<float[]>? Bones = null,
    IReadOnlyDictionary<int, int>? SkinSwap = null,
    IReadOnlyList<(int Base, int Count)>? BodyParts = null,
    int Body = 0);

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

    /// <summary>Which material replaces which, per model and skin family.</summary>
    /// <remarks>
    /// **A skin is a substitution, not a second model.** The batching, the vertex ranges and the
    /// geometry are identical between a RED player and a BLU one; only which material paints each
    /// run differs. So this is a handful of integers per model rather than a copy of anything, and
    /// resolving it at draw time means a player who switches teams is right on the next frame.
    /// </remarks>
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<int, int>>> _swaps =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models already reported as drawing unlit.</summary>
    private readonly HashSet<string> _reportedDark = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a model's light should be sampled, in world space.</summary>
    private (float X, float Y, float Z) IlluminationPoint(SceneProp prop, ScenePose pose)
    {
        if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry))
        {
            return (pose.X, pose.Y, pose.Z);
        }

        (float x, float y, float z) = entry.Illumination;

        if (x == 0f && y == 0f && z == 0f)
        {
            return (pose.X, pose.Y, pose.Z);
        }

        float radians = pose.Yaw * (MathF.PI / 180f);
        (float sine, float cosine) = MathF.SinCos(radians);

        return (
            pose.X + (x * cosine) - (y * sine),
            pose.Y + (x * sine) + (y * cosine),
            pose.Z + z);
    }

    /// <summary>Whether an ambient cube carries no light at all.</summary>
    private static bool IsUnlit(AmbientCube cube) =>
        cube.PositiveX == (0f, 0f, 0f) &&
        cube.NegativeX == (0f, 0f, 0f) &&
        cube.PositiveY == (0f, 0f, 0f) &&
        cube.NegativeY == (0f, 0f, 0f) &&
        cube.PositiveZ == (0f, 0f, 0f) &&
        cube.NegativeZ == (0f, 0f, 0f);

    /// <summary>Skinned models whose posed extents have been reported.</summary>
    private readonly HashSet<string> _reportedPoses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This frame's props, wearers before the things worn on them.</summary>
    private readonly List<SceneProp> _ordered = [];

    /// <summary>This frame's worn items, held aside while their wearers are posed first.</summary>
    private readonly List<SceneProp> _worn = [];

    /// <summary>Entity indices something is worn on this frame, so only those are recorded.</summary>
    private readonly HashSet<int> _wanted = [];

    /// <summary>This frame's drawn entities that something else is merged onto, by entity index.</summary>
    private readonly Dictionary<int, Worn> _wearerBones = [];

    /// <summary>Bone name matches, keyed by worn model and wearer model together.</summary>
    private readonly Dictionary<string, int[]> _mergeMaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The raw geometry of each packed model, for checking a pose against.</summary>
    private readonly Dictionary<string, IReadOnlyList<PropVertex>> _raw =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Measures a skinned model with its pose applied on the processor.</summary>
    private void ReportPosedExtents(
        string modelPath, IReadOnlyList<float[]> bones, string? label = null)
    {
        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners))
        {
            return;
        }

        float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
        float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;
        int weighted = 0;

        foreach (PropVertex corner in corners)
        {
            float total = corner.Weights.First + corner.Weights.Second + corner.Weights.Third;

            if (total <= 0f)
            {
                continue;
            }

            weighted++;

            Span<byte> which = [corner.Bones.First, corner.Bones.Second, corner.Bones.Third];
            Span<float> howMuch =
                [corner.Weights.First, corner.Weights.Second, corner.Weights.Third];

            float x = 0f, y = 0f, z = 0f;

            for (int slot = 0; slot < 3; slot++)
            {
                if (howMuch[slot] <= 0f || which[slot] >= bones.Count)
                {
                    continue;
                }

                float[] matrix = bones[which[slot]];
                float share = howMuch[slot] / total;

                x += share * ((matrix[0] * corner.X) + (matrix[1] * corner.Y) + (matrix[2] * corner.Z) + matrix[3]);
                y += share * ((matrix[4] * corner.X) + (matrix[5] * corner.Y) + (matrix[6] * corner.Z) + matrix[7]);
                z += share * ((matrix[8] * corner.X) + (matrix[9] * corner.Y) + (matrix[10] * corner.Z) + matrix[11]);
            }

            minimumX = MathF.Min(minimumX, x);
            minimumY = MathF.Min(minimumY, y);
            minimumZ = MathF.Min(minimumZ, z);
            maximumX = MathF.Max(maximumX, x);
            maximumY = MathF.Max(maximumY, y);
            maximumZ = MathF.Max(maximumZ, z);
        }

        // **All three ranges, because which axis is "up" is a property of the model.** A hat is a
        // few inches tall wherever it is, so its SIZE says nothing about whether it is on a head;
        // where it sits does. Reporting only the z range assumed z was up and this bind pose is
        // Y-up - a player model measures 84 along Y and bip_head rests at (0, 75, -1) - so a hat
        // correctly on the head reads as "z from -16 to -2" and looks like a hat on the floor.
        //
        // That mistake cost a full round of investigation, and it is the same one as measuring at
        // a tick the demo does not contain: an instrument answering confidently about the wrong
        // quantity.
        ViewerLog.Write(
            "props",
            $"posed {label ?? modelPath}: {weighted} of {corners.Count} corners weighted, " +
            $"{bones.Count} bones, x {minimumX:0.#}..{maximumX:0.#} " +
            $"y {minimumY:0.#}..{maximumY:0.#} z {minimumZ:0.#}..{maximumZ:0.#}");
    }

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

    /// <summary>Which sequence a player of this model should play at a given speed.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <returns>A merged sequence number, or −1 when the model is not skinned or has neither.</returns>
    /// <remarks>
    /// Asked of the set rather than of the model directly, because only the set knows whether a
    /// model was loaded skinned - a baked model has no merged sequence table to search.
    /// </remarks>
    public int SequenceFor(string modelPath, float speed) =>
        _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames) &&
        frames.Skinned is { } skinned
            ? PlayerAnimation.For(skinned, speed)
            : -1;

    /// <summary>Every baked frame's batches for one model.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One entry per baked frame, each a list of runs.</returns>
    public IReadOnlyList<IReadOnlyList<WorldBatch>> AllFrames(string modelPath) =>
        _byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames) ? frames : [];

    /// <summary>Which material replaces which for a skin family, or null for the model's own.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="skin">Which family; zero is the model's own and substitutes nothing.</param>
    /// <returns>The substitution to apply when binding, or null.</returns>
    public IReadOnlyDictionary<int, int>? SkinSwap(string modelPath, int skin) =>
        skin > 0 &&
        _swaps.TryGetValue(modelPath, out IReadOnlyList<IReadOnlyDictionary<int, int>>? swaps) &&
        skin - 1 < swaps.Count
            ? swaps[skin - 1]
            : null;

    /// <summary>Which baked frame a prop's sequence and cycle select.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>A frame index for <see cref="Batches(string, int)"/>.</returns>
    public int FrameFor(SceneProp prop, double seconds) => SelectFor(prop, seconds).Frame;

    /// <summary>Which baked frames a prop falls between, and how far.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>The frame to draw, the one after it, and the blend between them.</returns>
    public (int Frame, int Next, float Blend) SelectFor(SceneProp prop, double seconds) =>
        _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames)
            ? frames.Select(prop.Pose.Sequence, prop.Pose.Cycle, seconds, prop.Pose.PlaybackRate)
            : (0, 0, 0f);

    /// <summary>Whether a model kind has geometry this renderer can draw.</summary>
    /// <param name="kind">What the model reference resolved to.</param>
    /// <returns>Whether it can be packed and drawn.</returns>
    /// <remarks>
    /// **One predicate, because it was two tests that had to agree and nothing made them.** The
    /// packing loop and the draw loop each carried their own <c>Kind != Studio</c>, so admitting
    /// brush entities meant changing the same rule in two places — and the two failures are not
    /// alike: a model packed but never drawn is silent, while one drawn but never packed is a
    /// lookup miss reported as a load failure.
    ///
    /// A sprite is a camera-facing quad with no geometry of its own and is still not drawn.
    /// Unknown is <c>mod_bad</c>: the reference never resolved, so there is nothing to look up.
    /// </remarks>
    private static bool IsDrawable(SceneModelKind kind) =>
        kind is SceneModelKind.Studio or SceneModelKind.Brush;

    /// <summary>Packs whatever a moment needs that is not packed already.</summary>
    /// <param name="props">What exists at this tick, from the timeline.</param>
    /// <param name="load">Reads a model in its own coordinates, or answers null.</param>
    /// <returns>Whether anything was added, so the caller knows to re-upload.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A <c>*N</c> reference is an inline BSP submodel**, so its geometry comes from the map
    /// rather than from a <c>.mdl</c> — but it arrives through the same loader as a model like
    /// any other, which is why one packing path serves both. A sprite is a camera-facing quad
    /// with no geometry at all, and handing one to a model loader draws nothing and reports
    /// nothing.
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
            // **Brush entities pack like studio models, because by here they are models.** `*12`
            // resolves through the same loader to geometry the map built, so the only thing that
            // ever made this test about `.mdl` files was that nothing else had geometry yet.
            // Sprites still have none, and Unknown means the model reference was never resolved.
            if (!IsDrawable(prop.Kind) || _byModel.ContainsKey(prop.ModelPath))
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
                _raw[prop.ModelPath] = model.Geometry[0];

                if (model.SkinSwaps is { Count: > 0 } families)
                {
                    _swaps[prop.ModelPath] = families;
                }

            for (int slot = 0; slot < model.Geometry.Count; slot++)
            {
                IReadOnlyList<PropVertex> corners = model.Geometry[slot];

                // **The frame this one blends toward, packed into the same vertex.** Both poses
                // reach the shader without a second buffer or a fetch, and a model with one frame
                // carries itself in both and blends to itself.
                IReadOnlyList<PropVertex> onward = model.NextOf(slot);

                List<WorldBatch> batches = [];
                frames.Add(batches);


                // Grouped by material so one bind covers every triangle of this frame that shares
                // it. Every frame carries the same corners in the same order, so the batching is
                // identical between them and only the positions differ.
                // **Keyed by the body part and alternative as well as the material**, because a
                // batch that spanned two alternatives could not be skipped for one of them. A
                // capture point's three signs share a material; merged on material alone they
                // become one run and no per-entity choice can separate them again.
                Dictionary<(int Material, int Part, int Model), List<WorldVertex>> byMaterial = [];

                for (int index = 0; index < corners.Count; index++)
                {
                    PropVertex corner = corners[index];

                    PropVertex ahead = index < onward.Count ? onward[index] : corner;

                    (int Material, int Part, int Model) key =
                        (corner.MaterialIndex, corner.BodyPart, corner.BodyModel);

                    if (!byMaterial.TryGetValue(key, out List<WorldVertex>? into))
                    {
                        into = [];
                        byMaterial[key] = into;
                    }

                    // **Model space, untouched.** The shader's model matrix places it. No lightmap
                    // either: a studio model is lit by its own vertex colours in the engine too,
                    // and a zero-width atlas rectangle sends every corner to the reserved white
                    // texel so the lightmap term is an identity rather than darkness.
                    into.Add(new WorldVertex(
                        corner.X, corner.Y, corner.Z, corner.U, corner.V, 0f, 0f, 0f,
                        NormalX: corner.NormalX,
                        NormalY: corner.NormalY,
                        NormalZ: corner.NormalZ,
                        NextX: ahead.X,
                        NextY: ahead.Y,
                        NextZ: ahead.Z,
                        NextNormalX: ahead.NormalX,
                        NextNormalY: ahead.NormalY,
                        NextNormalZ: ahead.NormalZ,

                        // **Without these the shader skins by nothing.** A skinned model's
                        // geometry is uploaded unposed, so the bones are the only thing that
                        // stands it up - and a vertex with no weights is left exactly where the
                        // artist modelled it, which for a player is lying along Y. The fields
                        // existed on the vertex and in the packer and were never filled in here,
                        // so every player drew in its raw modelling pose while being lit
                        // correctly, which reads as a lighting change rather than a missing
                        // transform.
                        BoneA: corner.Bones.First,
                        BoneB: corner.Bones.Second,
                        BoneC: corner.Bones.Third,
                        WeightA: corner.Weights.First,
                        WeightB: corner.Weights.Second,
                        WeightC: corner.Weights.Third));
                }

                foreach (KeyValuePair<(int Material, int Part, int Model), List<WorldVertex>> group
                    in byMaterial)
                {
                    batches.Add(new WorldBatch(
                        group.Key.Material,
                        _vertices.Count,
                        group.Value.Count,
                        group.Key.Part,
                        group.Key.Model));

                    _vertices.AddRange(group.Value);
                }

                // **Whether the alternatives survived packing, said once per model.** A model whose
                // batches all carry alternative zero cannot be varied per entity however faithfully
                // m_nBody is decoded, and the picture is then identical to a body number that never
                // arrived — which is exactly the state this was in.
                if (slot == 0 && model.BodyParts is { Count: > 0 } &&
                    _reportedFrames.Add(prop.ModelPath + "#body"))
                {
                    int alternatives = 0;

                    foreach ((int _, int _, int alternative) in byMaterial.Keys)
                    {
                        alternatives = Math.Max(alternatives, alternative + 1);
                    }

                    ViewerLog.Write(
                        "render",
                        $"bodygroups {prop.ModelPath}: {model.BodyParts.Count} parts, " +
                        $"{batches.Count} batches spanning {alternatives} alternatives");
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

            // **Say which pose this measures, or the number lies.** A baked model's geometry is
            // posed already, so "on its side" means something is wrong. A skinned model's is
            // stored unposed and is SUPPOSED to be lying along Y - the shader stands it up - so
            // the same warning about the same numbers would be false.
            //
            // This is the overlogging failure in miniature: a line that measured the right thing
            // for one kind of model and kept its wording when a second kind arrived.
            ViewerLog.Write(
                "props",
                model.IsSkinned
                    ? $"extents {prop.ModelPath}: x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
                      $"UNPOSED, skinned on the GPU - the shader poses it, so these are the " +
                      $"artist's coordinates rather than how it is drawn"
                    : $"extents {prop.ModelPath}: x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
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

        // **Owners are posed before the things hanging off them.** A bone-merged entity has no
        // pose of its own — it takes its parent's matrices for every bone they share by NAME — so
        // the parent's must already exist when the child is reached, and nothing orders the list.
        // So they are ordered here — wearers first, worn second — rather than the loop below being
        // run twice or nested. One pass over a reordered list keeps the body a single block.
        _ordered.Clear();
        _worn.Clear();

        foreach (SceneProp prop in props)
        {
            (prop.AttachedTo is null ? _ordered : _worn).Add(prop);
        }

        _ordered.AddRange(_worn);
        _wearerBones.Clear();
        _wanted.Clear();

        foreach (SceneProp prop in _worn)
        {
            _wanted.Add(prop.AttachedTo!.Value);
        }

        // **Every prop that does not draw is counted with its reason.** A silent `continue` here is
        // how "all the props went away" became a guessing game: the scene said 14 models, the map
        // showed one, and nothing in between reported which test rejected the other thirteen.
        //
        // Four categories, per the project's rule: asked for, what we have, what was produced, what
        // is missing and why.
        int askedFor = _ordered.Count;
        int notStudio = 0;
        int noBatches = 0;
        int drawnCount = 0;
        Dictionary<string, int> noBatchesBy = [];
        Dictionary<string, int> notStudioBy = [];

        foreach (SceneProp prop in _ordered)
        {
            (int frame, int _, float blend) = SelectFor(prop, seconds);

            int skin = prop.Pose.Skin;

            if (!IsDrawable(prop.Kind))
            {
                notStudio++;

                // **Inline BSP submodels collapse to one entry.** A map's doors and moving brushes
                // are `*1`, `*2`, ... and process names 141 of them, which turns the line into a
                // wall that hides the entry that matters. They are one gap, not 141 findings.
                string rejectedName;

                if (prop.ModelPath.Length == 0)
                {
                    rejectedName = "<no model>";
                }
                else if (prop.ModelPath.StartsWith('*'))
                {
                    rejectedName = "<inline submodel>";
                }
                else
                {
                    rejectedName = System.IO.Path.GetFileName(prop.ModelPath);
                }

                string rejected = $"{rejectedName}#{prop.Kind}";

                notStudioBy[rejected] = notStudioBy.GetValueOrDefault(rejected) + 1;
                continue;
            }

            if (Batches(prop.ModelPath, frame).Count == 0)
            {
                noBatches++;

                // Named per model, because "no batches" for one model is a load failure and for all
                // of them is a frame-selection failure, and the two need different fixes.
                string name = System.IO.Path.GetFileName(prop.ModelPath);
                noBatchesBy[name] = noBatchesBy.GetValueOrDefault(name) + 1;
                continue;
            }

            drawnCount++;

            ScenePose pose = prop.Pose;

            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            // **Lit from where it stands, which is what the engine does.** A model has no
            // lightmap, so vrad's per-leaf ambient cube is the light it gets - sampled at the
            // origin rather than per vertex, exactly as the client samples it once per model.
            // **Lit at the model's illumination centre, not at its origin.** studiohdr_t carries
            // an illumposition for exactly this: a player's origin is at its feet, and a point
            // resting on a floor plane lands in the solid leaf beneath, which holds no light. The
            // model then draws black - seen on a medic, a soldier, a scout and a resupply locker.
            //
            // The offset turns with the model, because it is a point on the model rather than a
            // direction in the world.
            (float lightX, float lightY, float lightZ) = IlluminationPoint(prop, pose);

            AmbientCube light = lightAt is null
                ? default
                : lightAt(lightX, lightY, lightZ);

            // **A model lit by nothing draws black, and that is worth saying out loud.** The cube
            // comes from the leaf a model stands in, and a player's origin is at its FEET - so a
            // point resting exactly on a floor plane can land in the solid leaf below it, which
            // carries no light at all. It shows as a player turning black in some places and
            // recovering in others, which reads as a lighting quirk rather than a lookup landing
            // in solid.
            //
            // Logged with the position, because the defect is positional and a count would not
            // let anyone go and look at the spot.
            if (lightAt is not null && IsUnlit(light) && _reportedDark.Add(prop.ModelPath))
            {
                ViewerLog.Warn(
                    "render",
                    $"{prop.ModelPath} is lit by nothing at ({pose.X:0},{pose.Y:0},{pose.Z:0}); " +
                    $"its leaf carries no ambient light, so it draws black");
            }

            if (!_reportedFrames.Contains(prop.ModelPath))
            {
                _reportedFrames.Add(prop.ModelPath);

                ViewerLog.Write(
                    "render",
                    $"animating {prop.ModelPath}: sequence {pose.Sequence} cycle {pose.Cycle:0.###} " +
                    $"-> baked frame {frame} of {AllFrames(prop.ModelPath).Count} " +
                    $"blend {blend:0.###} yaw {pose.Yaw:0.##} at ({pose.X:0},{pose.Y:0},{pose.Z:0})");
            }

            // **A skinned model is posed here, per instance.** Its geometry was uploaded once and
            // unposed, so the matrices are what puts it in a pose at all - without them it draws
            // in whatever position the artist modelled it, which for a player is lying on its
            // side.
            IReadOnlyList<float[]>? bones = null;
            IReadOnlyList<float[]>? boneToWorld = null;

            if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) &&
                entry.Skinned is { } skinned)
            {
                int sequence = Math.Max(0, pose.Sequence);

                // **Advanced from demo time, because nothing networks a player's cycle.** The
                // client runs its own in C_BaseAnimating::FrameAdvance and treats any sent cycle
                // as a correction; a player's is never sent at all, so replaying it holds one
                // frame of a real animation - a convincing statue.
                double advanced = pose.Cycle + (seconds * skinned.CyclesPerSecond(sequence));
                float phase = (float)(advanced - Math.Floor(advanced));

                int posedFrame = StudioSequences.FrameFor(
                    phase, skinned.Frames(sequence), skinned.Loops(sequence));

                StudioSkeleton posed = skinned.Skeleton(
                    sequence, posedFrame, PoseValues(skinned, pose));

                bones = posed.Matrices;

                // Kept separately: anything merged onto this entity needs where its BONES are, and
                // a skinning matrix has the bind pose already folded in.
                boneToWorld = posed.BoneToWorld;

                // **Report the frame actually applied, not the baked one.** A skinned model has a
                // single baked slot, so the baked-frame line below says "frame 0 of 1" for every
                // player however they are moving - true, and about the wrong quantity.
                if (_reportedFrames.Add(prop.ModelPath + "#skin"))
                {
                    ViewerLog.Write(
                        "render",
                        $"skinned {prop.ModelPath}: sequence {sequence}, " +
                        $"{skinned.Frames(sequence)} frames at " +
                        $"{skinned.CyclesPerSecond(sequence):0.###} cycles a second, " +
                        $"phase {phase:0.###} -> frame {posedFrame}");
                }
            }

            // **Applies the matrices the GPU is about to use, on the processor, and reports the
            // result.** A skinned model that draws wrong could be a bad pose or a bad shader, and
            // an overhead camera cannot tell them apart. If these extents stand the model up, the
            // pose is right and the fault is in the drawing; if they do not, the pose is the
            // fault and the shader is innocent.
            if (bones is { Count: > 0 } && !_reportedPoses.Contains(prop.ModelPath))
            {
                _reportedPoses.Add(prop.ModelPath);
                ReportPosedExtents(prop.ModelPath, bones);

                // **The same frame posed WITHOUT the blend, side by side.** Resolving the blend
                // grid was this project's change and taking the grid's corner is what came before
                // it. Three animations mixed at wrong weights crumple a skeleton - a run forward
                // blended halfway against a run backward is not a stand, it is a heap - and that
                // is indistinguishable from a broken decode unless both are measured together.
                if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? both) &&
                    both.Skinned is { } plain)
                {
                    ReportPosedExtents(
                        prop.ModelPath,
                        plain.Skeleton(Math.Max(0, prop.Pose.Sequence), 0).Matrices,
                        prop.ModelPath + " CORNER, no pose parameters");
                }
            }

            // **A merged entity takes its wearer's matrices, not its own pose.** This is what
            // EF_BONEMERGE means: the client walks the child's bones, finds the parent's bone of
            // the same name, and uses that matrix outright. A hat has a `bip_head` bone and no
            // animation of its own — posing it from its own rest skeleton puts it at the player's
            // feet facing north, which is what "cosmetics do not work" looked like.
            //
            // Bones the parent does not have keep the child's own, which is the same fallback
            // Remap's −1 already means: an item with a part the player has no bone for keeps the
            // shape the artist gave it rather than collapsing to the origin.
            if (prop.AttachedTo is { } wearer)
            {
                if (!_wearerBones.TryGetValue(wearer, out Worn worn))
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model
                    // that failed to load. Drawing the hat anyway would leave it hanging in the
                    // air at the map origin, which is worse than not drawing it.
                    continue;
                }

                bones = Merge(prop.ModelPath, bones, worn);
                transform = worn.Where;

                // **Measured AFTER the merge, which is the only measurement that answers it.** The
                // extents reported above are of the item's own pose, before it was put on anybody
                // - so they say nothing about where it ends up. What decides whether a hat is on a
                // head is its height in the WEARER's space: a scout's head is around 64 units up
                // and their origin is at their feet, so a hat reporting a z near zero is a hat on
                // the floor however well its bones matched.
                if (bones is { Count: > 0 } && _reportedPoses.Add(prop.ModelPath + "#worn"))
                {
                    ReportPosedExtents(prop.ModelPath, bones, prop.ModelPath + " WORN");
                }

                // **Lit where its wearer stands, not where its own pose says.** A merged item's
                // pose is (0,0,0) by construction, so sampling the ambient cube from it asks the
                // leaf at the map origin - which is usually solid, carries no light, and draws
                // every cosmetic in the match black. It showed in the log as "rocketboots is lit
                // by nothing at (0,0,0)", which reads as a lighting quirk rather than as a light
                // sampled before the item had been given a position.
                light = lightAt is null
                    ? default
                    : lightAt(worn.LightX, worn.LightY, worn.LightZ);

                (lightX, lightY, lightZ) = (worn.LightX, worn.LightY, worn.LightZ);
            }
            else if (_wanted.Contains(prop.EntityIndex))
            {
                // **Recorded even with no bones, which is not a detail.** A wearer cheap enough to
                // have been baked has no skeleton here, and requiring one would drop every item on
                // it - silently, since the wearer itself still draws and only the hat vanishes.
                // Merge handles the boneless case by keeping the item's own pose and taking only
                // the transform, so it moves with the wearer even without following a bone.
                _wearerBones[prop.EntityIndex] = new Worn(
                    prop.ModelPath, boneToWorld ?? [], transform, lightX, lightY, lightZ);
            }

            into.Add(new ModelInstance(
                prop.ModelPath,
                transform.ToMatrix(),
                light,
                sunAt?.Invoke(lightX, lightY, lightZ),
                frame,
                blend,
                bones,
                SkinSwap(prop.ModelPath, skin),
                _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? parts)
                    ? parts.BodyParts
                    : null,
                prop.Pose.Body));
        }

        // **The four categories, reported only when they change.** Asked for, produced, and what was
        // rejected with the reason — because "the props went away" was diagnosable from the map and
        // from nothing in the log, which is the gap this closes.
        //
        // Keyed on the whole tuple rather than on the drawn count alone: thirteen props failing for
        // a new reason while the drawn count holds steady is exactly the change worth seeing.
        (int, int, int, int) state = (askedFor, drawnCount, notStudio, noBatches);

        if (state != _lastDrawState)
        {
            _lastDrawState = state;

            string missing = noBatchesBy.Count == 0
                ? "none"
                : string.Join(", ", noBatchesBy.Select(entry => $"{entry.Value}x{entry.Key}"));

            ViewerLog.Write(
                "props",
                $"asked for {askedFor}, produced {drawnCount}; " +
                $"skipped {notStudio} not-studio [{(notStudioBy.Count == 0 ? "none" : string.Join(", ", notStudioBy.Select(e => $"{e.Value}x{e.Key}")))}], " +
                $"{noBatches} no-batches [{missing}]");
        }
    }

    /// <summary>The last reported draw tally, so the line prints on change rather than per frame.</summary>
    private (int AskedFor, int Drawn, int NotStudio, int NoBatches) _lastDrawState = (-1, -1, -1, -1);

    /// <summary>Replaces a model's bone matrices with its wearer's, matched by bone name.</summary>
    /// <param name="modelPath">The worn model, whose skeleton decides which bones are wanted.</param>
    /// <param name="own">Its own matrices, kept for any bone the wearer has no counterpart for.</param>
    /// <param name="wearer">The wearer's matrices, in the wearer's own bone order.</param>
    /// <returns>Matrices in the worn model's bone order.</returns>
    /// <remarks>
    /// **The name match is Valve's, and it is the whole mechanism.** <c>CBoneMergeCache</c> pairs
    /// the two skeletons by bone name and copies the parent's matrix across; the same
    /// name-matching this project already does for animation retargeting through
    /// <see cref="StudioBones.Remap"/>, which is Valve's <c>masterBone</c>.
    ///
    /// The remap is cached because it depends only on the two skeletons, never on the frame, and
    /// a match plays a few dozen worn items at sixty frames a second.
    /// </remarks>
    private IReadOnlyList<float[]>? Merge(
        string modelPath,
        IReadOnlyList<float[]>? own,
        Worn wearer)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? entry) ||
            entry.Skinned is not { } skinned ||
            !_frames.TryGetValue(wearer.ModelPath, out PropModels.ModelFrames? host) ||
            host.Skinned is not { } hostSkinned)
        {
            // A worn item cheap enough to have been baked has no skeleton here to merge onto. It
            // still takes its wearer's transform, which the caller has already applied, so it
            // moves with the player even though it cannot follow a bone.
            return own;
        }

        // **Keyed by BOTH models.** A scout's skeleton is not a heavy's, so one remap per worn
        // item would pose every hat with whichever class wore it first - wrong by a bone or two,
        // which reads as a hat sitting slightly off rather than as a bug.
        string key = modelPath + "|" + wearer.ModelPath;

        if (!_mergeMaps.TryGetValue(key, out int[]? map))
        {
            map = StudioBones.Remap(skinned.Bones, hostSkinned.Bones);
            _mergeMaps[key] = map;

            int matched = 0;

            foreach (int index in map)
            {
                matched += index >= 0 ? 1 : 0;
            }

            // **Counted, because a merge that matches nothing looks identical to one that works.**
            // Both draw the item; only one puts it on the head. A zero here is the whole defect.
            ViewerLog.Write(
                "render",
                $"bone merge {System.IO.Path.GetFileName(modelPath)} onto " +
                $"{System.IO.Path.GetFileName(wearer.ModelPath)}: " +
                $"{matched} of {map.Length} bones matched" +
                (matched == map.Length
                    ? ""
                    : $"; matched {Matched(skinned.Bones, map)}" +
                      $"; missing {Unmatched(skinned.Bones, map)}") +
                $"; {WearerBoneAt(skinned.Bones, hostSkinned.Bones, map, wearer.Bones)}");
        }

        // **The unmatched bones are built from their parents, not left where they were.** Valve
        // copies only the matches, but the worn model has already run its own full SetupBones, so
        // an unmatched bone holds a position walked down the worn model's OWN hierarchy from its
        // parent - which may itself have been merged. Leaving it at its rest position in model
        // space instead tears the item across the map: a ghostly_gibus matched 1 bone of 8, the
        // other seven stayed at the model origin, and the triangles between them stretched from
        // the scout's head to his feet as a flat sheet.
        return StudioBones.MergeOnto(skinned.Bones, wearer.Bones, map);
    }

    /// <summary>A value for each pose parameter the model declares, in its own order.</summary>
    /// <remarks>
    /// **Matched by NAME rather than by position**, because a pose parameter's index is a property
    /// of the model: a scout and a heavy declare their own lists and there is no guarantee
    /// <c>move_x</c> lands at the same index in both. Filling an array positionally works right up
    /// until a class orders them differently, and then that class alone animates from the wrong
    /// input — the kind of defect that looks like a bad animation rather than a bad lookup.
    ///
    /// Anything this project does not compute stays at zero, which is what the engine leaves an
    /// unset parameter at.
    /// </remarks>
    private static float[] PoseValues(PropModels.SkinnedModel model, ScenePose pose)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        if (parameters.Count == 0)
        {
            return [];
        }

        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => pose.MoveX,
                "move_y" => pose.MoveY,
                _ => 0f,
            };

            // Stored normalised, as the engine stores it - see StudioBlendGrid.Normalize.
            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        return values;
    }

    /// <summary>Where the wearer's matched bone actually is, in the wearer's own space.</summary>
    /// <remarks>
    /// **The one number that separates a merge problem from a space problem.** A scout's head sits
    /// around sixty-four units above their origin, which is at their feet. If the bone this item
    /// merges onto reports that height then the wearer's side is right and any remaining fault is
    /// in the item; if it reports nearly zero then the matrices being handed over are not in the
    /// space they are assumed to be, and every worn item in the game will be at ankle height
    /// regardless of which bone it found.
    /// </remarks>
    private static string WearerBoneAt(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBone> hostBones,
        int[] map,
        IReadOnlyList<float[]> wearer)
    {
        for (int index = 0; index < bones.Count && index < map.Length; index++)
        {
            int host = map[index];

            if (host < 0 || host >= wearer.Count)
            {
                continue;
            }

            float[] matrix = wearer[host];
            string name = host < hostBones.Count ? hostBones[host].Name : "?";

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{name} is at ({matrix[3]:0.#},{matrix[7]:0.#},{matrix[11]:0.#}) in wearer space");
        }

        return "no matched bone to place it by";
    }

    /// <summary>The names of the worn bones that DID find a counterpart on the wearer.</summary>
    /// <remarks>
    /// **Which bone matched decides where the item hangs, and the count cannot say.** An item
    /// matching one bone of eight is correct when that one is <c>bip_head</c> and its seven
    /// children are the jiggle joints hanging off it; it is an item lying on the floor when the
    /// one is a root both skeletons happen to share and the head is not among them. Both print
    /// "1 of 8", which is the same shape of mistake as reporting a count without the walked-command
    /// total.
    /// </remarks>
    private static string Matched(IReadOnlyList<StudioBone> bones, int[] map)
    {
        List<string> found = [];

        for (int index = 0; index < bones.Count && found.Count < 6; index++)
        {
            if (index < map.Length && map[index] >= 0)
            {
                found.Add(bones[index].Name);
            }
        }

        return found.Count == 0 ? "nothing" : string.Join(", ", found);
    }

    /// <summary>The names of the worn bones the wearer had no counterpart for.</summary>
    /// <remarks>
    /// **A count says how bad it is; the names say what it means.** A hat matching 1 bone of 8 is
    /// fine when the one is <c>bip_head</c> and its seven children hang off it, and is a hat lying
    /// on the grass when the one is a root the wearer happens to share and the head is missing.
    /// The two are indistinguishable from the number alone, and the second was on screen.
    /// </remarks>
    private static string Unmatched(IReadOnlyList<StudioBone> bones, int[] map)
    {
        List<string> missing = [];

        for (int index = 0; index < bones.Count && missing.Count < 6; index++)
        {
            if (index < map.Length && map[index] >= 0)
            {
                continue;
            }

            // **With its parent, because that is what decides where it ends up.** An unmatched
            // bone is built by walking down from its parent, so one whose parent is the merged
            // head rides the head correctly and one with no parent at all sits at the wearer's
            // origin - which on a player is their feet. Both print the same name without this.
            int parent = bones[index].Parent;

            missing.Add(
                parent >= 0 && parent < bones.Count
                    ? $"{bones[index].Name}<-{bones[parent].Name}"
                    : $"{bones[index].Name}<-ROOT");
        }

        return string.Join(", ", missing);
    }

    /// <summary>A drawn entity something else hangs off: its model, its pose and where it is.</summary>
    /// <remarks>
    /// The position is carried separately from the transform because a merged item is lit at its
    /// wearer's place and <see cref="PropTransform"/> keeps its origin private.
    /// </remarks>
    private readonly record struct Worn(
        string ModelPath,
        IReadOnlyList<float[]> Bones,
        PropTransform Where,
        float LightX,
        float LightY,
        float LightZ);

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
