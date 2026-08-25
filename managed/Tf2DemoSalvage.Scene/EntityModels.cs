using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

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
/// <param name="Mirrored">
/// Whether this is a viewmodel, drawn mirrored — which reverses its winding, so the cull has to
/// flip with it or the weapon draws inside out.
/// </param>
public readonly record struct ModelInstance(
    string ModelPath,
    float[] Matrix,

    // **Null means "lightmapped", not "unlit" (B131).** A studio model carries a cube sampled at
    // its illumination point; a brush entity carries lightmap coordinates on its vertices instead,
    // and the shader's cube branch would overwrite them. The renderer reads null as "no cube was
    // supplied" and leaves the atlas sample standing, which is what LightmappedGeneric does.
    AmbientCube? Light,
    SunLight? Sun,
    int Frame = 0,
    float Blend = 0f,
    IReadOnlyList<float[]>? Bones = null,
    IReadOnlyDictionary<int, int>? SkinSwap = null,
    IReadOnlyList<(int Base, int Count)>? BodyParts = null,
    int Body = 0,
    bool Mirrored = false);

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
public sealed class EntityModelSet
{
    /// <summary>What was posed and how, reported under `props` as the prop loader does.</summary>
    private readonly ILogger _props;

    /// <summary>What could not be drawn, reported under `render` (D83).</summary>
    /// <remarks>
    /// **Two categories because this writes to two areas**, and folding them would reclassify
    /// lines somebody greps for. A model lit by nothing is a `render` fact; the pose it was given
    /// is a `props` one.
    /// </remarks>
    private readonly ILogger _render;

    /// <summary>Creates an empty set.</summary>
    /// <param name="loggers">
    /// Where it reports, or <c>null</c> for nowhere. Optional with a null-object default because
    /// every test that builds one of these wants geometry rather than commentary.
    /// </param>
    public EntityModelSet(ILoggerFactory? loggers = null)
    {
        ILoggerFactory factory = loggers ?? NullLoggerFactory.Instance;

        _props = factory.CreateLogger("props");
        _render = factory.CreateLogger("render");

        // **Three collaborators, and each was a slab of the draw loop** (B181). Constructed here
        // rather than injected because each one's state is this set's state — the lighting cache is
        // keyed by entity, and the report sets remember what has already been said about which
        // model. Handing them in would mean two sets could share a "have we said this" flag, which
        // is a silence nobody could explain.
        _lighting = new ModelLighting(IlluminationPoint, _render);
        _reports = new ModelReports(_render);
        _tally = new DrawTally(_props);
        _loggers = factory;
    }

    /// <summary>Where the entities this set builds report, so the bone merge can say what paired.</summary>
    private readonly ILoggerFactory _loggers;

    /// <summary>The ambient cube and sun each model draws with.</summary>
    private readonly ModelLighting _lighting;

    /// <summary>Stopwatch ticks spent lighting models, accumulated until the caller resets it.</summary>
    /// <remarks>
    /// **Kept on this type after the lighting moved out** (B181), because the viewer's per-frame
    /// ledger reads it and moving the property would have changed a public surface for no reason
    /// beyond where the code lives. It forwards to <see cref="ModelLighting.Ticks"/>.
    ///
    /// Posing owned about nine hundred milliseconds of every second (B99) doing two different jobs,
    /// and this is what separated them: bones are per-frame work an animation genuinely needs, while
    /// a stationary model's lighting cannot have changed since the last frame.
    /// </remarks>
    public long LightingTicks
    {
        get => _lighting.Ticks;
        set => _lighting.Ticks = value;
    }

    /// <summary>What <c>SetupBones</c> has cost, ever — the pose itself and any merge it drives.</summary>
    /// <remarks>
    /// **The last split in the pose phase, and every earlier one came back flat** (B189). On a
    /// moment where bone work took 136 ms against a 3 ms median, lighting was 1.5 ms, the viewmodel
    /// 0.2 ms, animation 0.7 ms over 50 calls, and nothing was newly built — so the cost is inside
    /// one of these two and a single "bones" column could not say which.
    /// </remarks>
    public long SetupTicks { get; set; }

    /// <summary>What composing skinning matrices has cost, ever.</summary>
    public long SkinTicks { get; set; }

    /// <summary>What bringing every entity's state up to date has cost, ever.</summary>
    public long SimulateTicks { get; set; }

    /// <summary>Every pose-phase counter at one instant, for diffing across a call.</summary>
    /// <remarks>
    /// **One value instead of ten out-parameters.** Each counter is a running total, so a caller
    /// wanting "what did THIS call cost" reads them either side and subtracts — which was ten pairs
    /// of local variables at the call site and thirteen parameters on the report that consumed
    /// them. The arithmetic is identical; only the bookkeeping moves.
    ///
    /// A per-second total cannot attribute a single freeze: it says how much was spent, never
    /// whether it was spent all at once. That is why these are read per call rather than reported
    /// per second (B189, B191).
    /// </remarks>
    public readonly record struct PoseCounters(
        long Lighting,
        long Simulate,
        long WornLight,
        long Report,
        long ReportLog,
        long Setup,
        long Skin,
        long Animation,
        int AnimationCalls,
        int Built)
    {
        /// <summary>What happened between an earlier snapshot and this one.</summary>
        /// <param name="before">The earlier snapshot.</param>
        /// <returns>The difference, field by field.</returns>
        public PoseCounters Since(PoseCounters before) =>
            new(
                Lighting - before.Lighting,
                Simulate - before.Simulate,
                WornLight - before.WornLight,
                Report - before.Report,
                ReportLog - before.ReportLog,
                Setup - before.Setup,
                Skin - before.Skin,
                Animation - before.Animation,
                AnimationCalls - before.AnimationCalls,
                Built - before.Built);
    }

    /// <summary>Every pose-phase counter as it stands now.</summary>
    public PoseCounters Counters => new(
        _lighting.Ticks,
        SimulateTicks,
        WornLightTicks,
        ReportTicks,
        _reports.LogTicks,
        SetupTicks,
        SkinTicks,
        SkeletonPose.AnimationTicks,
        SkeletonPose.AnimationCalls,
        EntitiesBuilt);

    /// <summary>What per-prop reporting has cost, ever.</summary>
    public long ReportTicks { get; set; }

    /// <summary>How much of <see cref="ReportTicks"/> was spent inside the log sink.</summary>
    public long ReportLogTicks => _reports.LogTicks;

    /// <summary>What sampling light for WORN items has cost, ever — the uncached path.</summary>
    /// <remarks>
    /// **Valve has both halves of this and we have one.** `m_hLightingOrigin` makes an entity take
    /// its light from somewhere other than its own position (<c>c_baseanimating.cpp:3301-3309</c>),
    /// which is exactly what a bone-merged item needs and what this does. But the engine also holds
    /// a `LightCacheHandle_t` per model instance
    /// (<c>ivmodelrender.h:122</c>, `CreateInstance( IClientRenderable*, LightCacheHandle_t* )`),
    /// so the sample is not recomputed per frame — and that half is missing here.
    /// </remarks>
    public long WornLightTicks { get; set; }

    /// <summary>How many animating entities have been built, ever.</summary>
    /// <remarks>
    /// A running total rather than a per-call one, so a caller reads it either side of
    /// <see cref="Instances"/> and gets the count for that moment. The same shape as
    /// <see cref="LightingTicks"/> and for the same reason: a per-second total cannot attribute a
    /// single freeze.
    /// </remarks>
    public int EntitiesBuilt { get; private set; }

    /// <summary>What the draw loop says about each model, once.</summary>
    private readonly ModelReports _reports;

    /// <summary>How many props were asked for, drew, or were rejected and why.</summary>
    private readonly DrawTally _tally;

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

    /// <summary>Skinned models whose posed extents have been reported.</summary>
    private readonly HashSet<string> _reportedPoses = new(StringComparer.OrdinalIgnoreCase);

    // **Six fields and a depth sort used to live here, and they are gone** (D88, B181): _ordered,
    // _worn, _wanted, _wearerBones, _parents and Depth(). Together they guaranteed that a wearer was
    // posed before anything hanging off it, because the loop that followed had no other way to know.
    //
    // The engine guarantees it by asking. CBoneMergeCache::MergeMatchingBones calls SetupBones on
    // the followed entity where it stands (bone_merge_cache.cpp:130), and the readable-bones
    // early-out makes a repeat one integer comparison — so a player worn by six items is posed once
    // with no list, no pass and no sort anywhere in it.
    //
    // Kept as a comment rather than deleted silently because the departure was DECLARED under D86
    // and the reasoning behind it was wrong in a way worth remembering: it was defended as a
    // trade-off against Valve's recursion, and Valve has no ordering code at all. It was forty
    // lines against zero.

    /// <summary>One animating entity per drawn entity index, holding its own bones.</summary>
    /// <remarks>
    /// **The registry that replaced the ordering** (D88, B181). What used to be six fields and a
    /// depth sort is this dictionary plus <see cref="AnimatingEntity.SetupBones(int, double)"/>
    /// being idempotent: an entity that needs its parent asks for it, and a repeat is an integer
    /// comparison.
    /// </remarks>
    private readonly Dictionary<int, AnimatingEntity> _entities = [];

    /// <summary>Which model each entity's skeleton was built for.</summary>
    /// <remarks>
    /// A player who switches class, or a weapon slot reused by a different weapon, keeps its entity
    /// index and changes its model. The skeleton has to be rebuilt for that, and the alternative —
    /// posing a heavy's animation onto a scout's bone count — reads as a scrambled model rather
    /// than as an error.
    /// </remarks>
    private readonly Dictionary<int, string> _entityModels = [];

    /// <summary>Which frame the bone caches belong to; advanced once per call.</summary>
    private readonly BoneFrameCounter _boneFrames = new();

    /// <summary>Scratch for one entity's placement, so the transform is not reallocated per frame.</summary>
    private readonly Dictionary<int, float[]> _placements = [];

    /// <summary>The raw geometry of each packed model, for checking a pose against.</summary>
    private readonly Dictionary<string, IReadOnlyList<PropVertex>> _raw =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Brings every entity's animation state up to date, before any bones are built.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="seconds">Demo time, for advancing cycles.</param>
    /// <remarks>
    /// **A separate pass, and it is NOT an ordering pass.** This is the engine's own phase split:
    /// <c>SimulateEntities()</c> runs, then <c>ThreadedBoneSetup()</c>, then rendering
    /// (<c>cdll_client_int.cpp:2206-2210</c>). Updating what an entity is DOING is a different job
    /// from deciding when its bones get built, and separating them is what lets the second job be
    /// demand-driven.
    ///
    /// It matters here for a concrete reason: with no ordering, a prop can be reached through a
    /// merge before the loop gets to it, so its sequence and placement must already be right when
    /// that happens. The old code guaranteed that with a sort. This guarantees it by doing all the
    /// state first, which is both simpler and what the engine does.
    /// </remarks>
    private void Simulate(IReadOnlyList<SceneProp> props, double seconds)
    {
        // **Indexed first, because a placement follows its parent chain up** and the chain can
        // point anywhere in the list. This is what CalcAbsolutePosition walks (c_baseentity.cpp:4387).
        _propsByEntity.Clear();

        foreach (SceneProp prop in props)
        {
            _propsByEntity[prop.EntityIndex] = prop;
        }

        foreach (SceneProp prop in props)
        {
            if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) ||
                entry.Skinned is not { } skinned)
            {
                continue;
            }

            AnimatingEntity animating = EntityFor(prop, skinned);

            if (animating.Pose is not SkeletonPose posed)
            {
                continue;
            }

            ScenePose where = prop.Pose;
            int sequence = Math.Max(0, where.Sequence);

            // **Advanced from demo time, because nothing networks a player's cycle.** The client
            // runs its own in C_BaseAnimating::FrameAdvance and treats any sent cycle as a
            // correction; a player's is never sent at all, so replaying it holds one frame of a
            // real animation — a convincing statue.
            double advanced = where.Cycle + (seconds * skinned.CyclesPerSecond(sequence));
            float phase = (float)(advanced - Math.Floor(advanced));

            posed.Sequence = sequence;
            posed.Frame = StudioSequences.FrameFor(
                phase, skinned.Frames(sequence), skinned.Loops(sequence));
            posed.PoseValues = PoseValues(skinned, where, sequence);

            // **The placement, which is what makes the built bones WORLD space.** A merged item
            // takes its wearer's bones and those already carry the wearer's placement, so nothing
            // downstream has to know where a wearer stands — see D88 and finding 35 section 7a.
            posed.EntityTransform = PlacementOf(prop);

            // **IsEnabled first, because the KEY allocates.** `path + "#skin"` builds a string per
            // prop per frame before the set is even consulted, and this sits in Simulate, which
            // walks every prop (B191).
            if (_render.IsEnabled(LogLevel.Debug) && _reportedFrames.Add(prop.ModelPath + "#skin"))
            {
                _render.LogDebug(
                    "{Message}",
                    $"skinned {prop.ModelPath}: sequence {sequence}" +
                    $"{(skinned.IsDelta(sequence) ? " DELTA" : string.Empty)}" +
                    $"{skinned.UnimplementedFor(sequence)}, " +
                    $"{skinned.Frames(sequence)} frames at " +
                    $"{skinned.CyclesPerSecond(sequence):0.###} cycles a second, " +
                    $"phase {phase:0.###} -> frame {posed.Frame}");
            }
        }

        // Parent links second, so every entity exists before anything points at one.
        _drawnPlacements.Clear();

        foreach (SceneProp prop in props)
        {
            _lightPoints[prop.EntityIndex] = IlluminationPoint(prop, prop.Pose);

            // Recorded only for props that will actually be drawn, so "the wearer is not being
            // drawn" is answerable without depending on the draw loop's order — which is the whole
            // point of there no longer being one.
            if (IsDrawable(prop.Kind) &&
                Batches(prop.ModelPath, SelectFor(prop, seconds).Frame).Count > 0)
            {
                ScenePose at = prop.Pose;

                _drawnPlacements[prop.EntityIndex] =
                    new PropTransform(at.X, at.Y, at.Z, at.Pitch, at.Yaw, at.Roll, at.Scale);
            }

            if (!_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                continue;
            }

            animating.Follows = prop.AttachedTo is { } wearer
                ? _entities.GetValueOrDefault(wearer)
                : null;

            Attach(prop, animating, seconds);
        }
    }

    /// <summary>Places an item that hangs from a named attachment point rather than merging.</summary>
    /// <remarks>
    /// **A hat shares bone names with the player and takes their matrices; a halo, an MvM canteen, a
    /// spellbook and a spy's sapper share none** — the spellbook's only bone is called <c>mvm</c>
    /// and no player has one — so the merge matches nothing and the item would keep its own pose,
    /// which for an attached entity is the map origin (B82).
    ///
    /// The engine hangs those off the WEARER's attachment table:
    /// <c>ConcatTransforms( GetBone( iBone ), pattachment.local, world )</c>, one-based.
    ///
    /// **Resolved into the item's own placement rather than into a draw-time matrix**, which is the
    /// change D88 makes here. An attachment gives a WORLD transform, and world is where this
    /// project's bones now live — so it belongs where the entity's placement belongs, and the merge
    /// overwrites whatever bones do match on top of it. Setting both is correct precisely because
    /// the two cases do not overlap: an item either shares bone names or it does not.
    ///
    /// **Asking the wearer for bones here is Valve's own shape**, not a layering slip:
    /// <c>CalcAttachments()</c> is a call to <c>SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT,
    /// ... )</c>. Resolving an attachment IS a bone setup, and the cache makes the draw pass's
    /// repeat free.
    /// </remarks>
    private void Attach(SceneProp prop, AnimatingEntity animating, double seconds)
    {
        if (prop.AttachmentPoint is not { } point ||
            animating.Follows is not { } wearer ||
            animating.Pose is not SkeletonPose posed ||
            !_entityModels.TryGetValue(prop.AttachedTo ?? -1, out string? wearerModel) ||
            !_frames.TryGetValue(wearerModel, out PropModels.ModelFrames? worn) ||
            worn.Attachments is not { Count: > 0 } attachments ||
            point < 1 || point > attachments.Count)
        {
            return;
        }

        StudioAttachment attachment = attachments[point - 1];

        if (attachment.Bone < 0 ||
            attachment.Bone >= wearer.Bones.Count ||
            !wearer.SetupBones(StudioBoneFlags.UsedByAnything, seconds))
        {
            return;
        }

        // Identity for the wearer's own transform, because its bones are already in world space —
        // the placement it used to need is folded into them (finding 35 section 7a).
        //
        // Back through MatrixConvention, because AttachmentPlacement returns a MODEL matrix and an
        // entity placement is a matrix3x4_t. Same boundary as PlacementOf, same one crossing point.
        posed.EntityTransform = MatrixConvention.ToBoneMatrix(
            AttachmentPlacement.Matrix(
                wearer.Bones.Bone(attachment.Bone).ToArray(),
                attachment.Local,
                PropTransform.Identity.ToMatrix(),
                attachment.IsWorldAligned));

        if (_props.IsEnabled(LogLevel.Debug) && _reportedPoses.Add(prop.ModelPath + "#attached"))
        {
            _props.LogDebug(
                "{Message}",
                $"attached {prop.ModelPath} to {attachment.Name} " +
                $"(point {point}, bone {attachment.Bone}) on {wearerModel}");
        }
    }

    /// <summary>Where each entity's light is sampled, so a worn item can borrow its wearer's.</summary>
    private readonly Dictionary<int, (float X, float Y, float Z)> _lightPoints = [];

    /// <summary>Every DRAWABLE entity's placement, for items that have no bones to carry one.</summary>
    /// <remarks>
    /// **A model this project bakes rather than skins still gets worn on things**, and it has no
    /// skeleton to fold its wearer's placement into — so it needs the wearer's transform the way
    /// everything did before D88. Two viewer tests caught this within minutes of the swap, which is
    /// exactly what they are for.
    ///
    /// **The engine has no such case, and that is worth being precise about rather than glossing.**
    /// Valve's equivalent is <c>STUDIOHDR_FLAGS_STATIC_PROP</c>, which still goes through
    /// <c>SetupBones</c> and gets ONE bone — <c>MatrixCopy( parentTransform, GetBoneForWrite( 0 ) )</c>
    /// at <c>c_baseanimating.cpp:2953</c>. Ours is a different distinction: a performance choice
    /// about which models are worth skinning at all, made by this project and not by the format. So
    /// the fallback is ours to carry, and it is declared here rather than left to look like Valve's.
    ///
    /// Keyed only for props that pass the drawable and batch checks, so "the wearer is not being
    /// drawn" is answerable without depending on which order the draw loop reaches them in.
    /// </remarks>
    private readonly Dictionary<int, PropTransform> _drawnPlacements = [];

    /// <summary>This entity's animating object, rebuilt when its model changes.</summary>
    private AnimatingEntity EntityFor(SceneProp prop, PropModels.SkinnedModel skinned)
    {
        if (_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? existing) &&
            _entityModels.TryGetValue(prop.EntityIndex, out string? was) &&
            string.Equals(was, prop.ModelPath, StringComparison.Ordinal))
        {
            return existing;
        }

        // The factory rather than nothing, so the bone-merge pairing reaches the log. That line was
        // deleted with EntityModelSet.Merge and its absence left a viewer run unable to say whether
        // weapons had paired with the players holding them.
        // **Counted because a spike needs a cause and this is the only per-moment discontinuity in
        // the pose path.** Everything else here runs for every entity every frame; this runs the
        // first time an entity is seen and when its model changes. Five of seven slow moments were
        // dominated by bone work at 50-162 ms against a 3 ms median, and a median that low says the
        // steady path is not what spikes (B189).
        EntitiesBuilt++;

        AnimatingEntity animating = new(
            new SkeletonPose(skinned.Bones, skinned.Locals), _boneFrames, _loggers);

        _entities[prop.EntityIndex] = animating;
        _entityModels[prop.EntityIndex] = prop.ModelPath;

        return animating;
    }

    /// <summary>Where a prop stands, as a row-major 3×4, reusing the array it had last frame.</summary>
    /// <remarks>
    /// **A bone-merged entity takes its PARENT's placement, and that is Valve's, not bookkeeping.**
    /// <c>CalcAbsolutePosition</c> branches on it explicitly:
    ///
    /// <code>
    /// if ( IsEffectActive(EF_BONEMERGE) )
    /// {
    ///     MoveToAimEnt();
    ///     return;
    /// }
    /// </code>
    ///
    /// at <c>c_baseentity.cpp:4387</c>, and <c>MoveToAimEnt</c> is
    /// <c>GetAimEntOrigin( GetMoveParent(), … )</c> followed by <c>SetAbsOrigin</c> /
    /// <c>SetAbsAngles</c> (<c>:4294</c>). So a followed entity's ABSOLUTE origin is its parent's,
    /// while its LOCAL origin is the zero <c>FollowEntity</c> wrote — and <c>SetupBones</c> builds
    /// its <c>parentTransform</c> from <c>GetRenderOrigin()</c>, which is the absolute one.
    ///
    /// **This was deleted on 2026-08-24 and put back the same night**, which is the part worth
    /// recording. The old code did it as <c>transform = worn.Where</c> and D88 removed it as
    /// leftover bookkeeping, on the strength of having read <c>BuildTransformations</c> and not
    /// <c>CalcAbsolutePosition</c>. The result was eight of nine weapons drawn at the map origin:
    /// a weapon shares few or no bone names with a player, so the merge places little, and what is
    /// left builds at the entity's own placement — which for a followed entity is (0,0,0) unless
    /// this resolves it.
    ///
    /// The owner's diagnosis was the right one and arrived before the reading did: *"if everything
    /// was following valve then it would work"*. It was not following Valve. It had deleted a piece
    /// of Valve while believing the opposite.
    /// </remarks>
    private float[] PlacementOf(SceneProp prop)
    {
        if (!_placements.TryGetValue(prop.EntityIndex, out float[]? placement))
        {
            placement = new float[12];
            _placements[prop.EntityIndex] = placement;
        }

        ScenePose pose = Absolute(prop, AnimatingEntity.MaximumFollowDepth);

        // **Through MatrixConvention, because the two forms are not the same shape OR the same
        // layout.** ToMatrix is the shader's row-vector 4×4 with translation in the last ROW;
        // EntityTransform is Valve's matrix3x4_t with translation in column 3. Crossing that by
        // hand is what threw `Destination array was not long enough` on the first frame of
        // playback — and the length was the lucky half, since a transpose of the same size would
        // have drawn a plausible wrong pose instead of failing.
        MatrixConvention.ToBoneMatrix(
                new PropTransform(
                    pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale).ToMatrix())
            .CopyTo(placement, 0);

        return placement;
    }

    /// <summary>The pose a prop is actually AT, following its parent chain up.</summary>
    /// <remarks>
    /// **<c>CalcAbsolutePosition</c>'s bone-merge branch** (<c>c_baseentity.cpp:4387</c>): a
    /// followed entity's absolute origin and angles are its parent's, however deep the chain runs.
    /// An attachment on a weapon on a player therefore ends up at the player, which is what lets
    /// its unmatched bones land somewhere sensible.
    ///
    /// Bounded by the same depth <see cref="AnimatingEntity"/> uses, for the same reason: the wire
    /// should never carry a cycle and a demo this project exists to open may carry anything. A
    /// chain that runs past it keeps the prop's own pose, which draws it in the wrong place rather
    /// than not at all — the milder of the two failures, and the one that leaves something to see.
    /// </remarks>
    private ScenePose Absolute(SceneProp prop, int budget)
    {
        if (prop.AttachedTo is not { } wearer || budget <= 0)
        {
            return prop.Pose;
        }

        return _propsByEntity.TryGetValue(wearer, out SceneProp parent)
            ? Absolute(parent, budget - 1)
            : prop.Pose;
    }

    /// <summary>This frame's props by entity index, so a parent chain can be walked.</summary>
    private readonly Dictionary<int, SceneProp> _propsByEntity = [];

    /// <summary>Bone-to-world folded with the model's bind pose, which is what the shader skins by.</summary>
    /// <remarks>
    /// **The one place <c>poseToBone</c> is applied, and it belongs here** — finding 35 section 7a.
    /// <c>IStudioRender::DrawModel</c> takes bone-to-world and nothing else
    /// (<c>istudiorender.h:329</c>); the composition happens at the boundary that owns the vertices.
    /// Keeping it out of the pose path is what leaves one array per entity with nothing to choose
    /// wrongly between.
    /// </remarks>
    private float[][] Skinning(int entity, IReadOnlyList<StudioBone> bones, BoneAccessor accessor)
    {
        // **Reused per entity, because this ran once per bone per drawn prop per FRAME.** At around
        // 250 props of eighty bones that is twenty thousand twelve-float arrays every frame, and
        // the allocating overload was being called for every one of them.
        //
        // Measured 2026-08-25: gen0 was collecting 34 times a second. The first attempt at this
        // fixed FromQuaternion and the O(n²) override scan and bought about 20 ms of the 545 —
        // because THIS was the cost, and it was left alone. A plausible fix to the wrong line reads
        // exactly like a fix that did not help.
        //
        // Safe to reuse because an instance list is consumed within the frame that built it, and
        // the viewmodel's entities carry their own indices (4096-4098) so they cannot collide with
        // a world entity's buffer.
        if (!_skinning.TryGetValue(entity, out float[][]? buffer) ||
            buffer.Length != accessor.Count)
        {
            buffer = new float[accessor.Count][];

            for (int bone = 0; bone < buffer.Length; bone++)
            {
                buffer[bone] = new float[12];
            }

            _skinning[entity] = buffer;
        }

        for (int bone = 0; bone < accessor.Count; bone++)
        {
            StudioBones.Concatenate(
                accessor.Bone(bone), bones[bone].PoseToBone.Span, buffer[bone]);
        }

        return buffer;
    }

    /// <summary>Each entity's skinning matrices, reused between frames.</summary>
    private readonly Dictionary<int, float[][]> _skinning = [];

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
        // **The root bone's own matrix, because extents cannot separate a rotation from a move.**
        // Two poses of one model came out as the same three ranges with the axes cycled, which is
        // a basis change rather than a different pose — but the extents alone cannot say whether
        // the translation came with it. The matrix can: its last column is where the root sits and
        // its first three are what it does to the axes.
        string root = bones.Count > 0 && bones[0] is { Length: >= 12 } first
            ? $"root [{first[0]:0.##} {first[1]:0.##} {first[2]:0.##} | " +
              $"{first[4]:0.##} {first[5]:0.##} {first[6]:0.##} | " +
              $"{first[8]:0.##} {first[9]:0.##} {first[10]:0.##}] " +
              $"at ({first[3]:0.#}, {first[7]:0.#}, {first[11]:0.#})"
            : "root none";

        // **Debug, and this one had the largest volume of the lot** — 338 lines in four minutes,
        // because it fires the first time each model, each worn item and each corner comparison is
        // posed, and models keep first appearing throughout a match. Each line is a disk flush
        // (B191), and this call also does real work to produce it: extents over every corner, plus
        // a second full skeleton built for the CORNER comparison.
        _props.LogDebug(
            "{Message}",
            $"posed {label ?? modelPath}: {weighted} of {corners.Count} corners weighted, " +
            $"{bones.Count} bones, x {minimumX:0.#}..{maximumX:0.#} " +
            $"y {minimumY:0.#}..{maximumY:0.#} z {minimumZ:0.#}..{maximumZ:0.#}, {root}");
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

    /// <summary>The first sequence of a model whose activity contains a fragment.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="fragment">Part of an activity name, such as <c>VM_IDLE</c>.</param>
    /// <returns>A merged sequence number, or −1 when nothing claims it.</returns>
    /// <remarks>
    /// **Asked by NAME because a demo's sequence number cannot be checked against anything.** The
    /// viewmodel arms were playing merged sequence 1, which on an arms model is <c>r_handposes</c> —
    /// a one-frame pose holder that leaves the root bone at identity and the model in its authored
    /// Y-up space. The animations a viewmodel actually plays begin at 2 and carry
    /// <c>ACT_*_VM_IDLE</c> and its neighbours.
    /// </remarks>
    public int SequenceByActivity(string modelPath, string fragment)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames))
        {
            // **Three ways to answer "no" and they are different faults**, which is the whole
            // reason they are separated: the model was never packed, the model was packed without
            // a skeleton, or the skeleton simply has no such activity. A single −1 sent this
            // investigation at a sequence list that turned out to contain exactly what was being
            // looked for.
            _render.LogWarning(
                "no packed frames for {Model}, so no activity lookup", modelPath);
            return -1;
        }

        if (frames.Skinned is not { } skinned)
        {
            _render.LogWarning(
                "{Model} was baked rather than skinned, so it has no sequence table", modelPath);
            return -1;
        }

        return skinned.SequenceByActivity(fragment);
    }

    /// <summary>Chooses each drawn player's sequence, now that their models are loaded.</summary>
    /// <param name="drawn">The draw list, updated in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    /// <remarks>
    /// **Named for Valve's own pass, because it IS Valve's own pass.**
    /// <c>C_BaseAnimating::UpdateClientSideAnimations()</c> (<c>c_baseanimating.cpp:6368</c>) is a
    /// static batch walk over <c>g_ClientSideAnimationList</c> calling
    /// <c>UpdateClientSideAnimation()</c> on each — a loop over a list, exactly this shape, rather
    /// than something each entity does for itself. Per entity it reaches
    /// <c>CMultiPlayerAnimState::ComputeMainSequence</c> (<c>multiplayer_animstate.cpp:1125</c>),
    /// which is what <see cref="SequenceFor"/> stands in for.
    ///
    /// **And the ORDER is Valve's too**, which is worth stating because it is the part that is easy
    /// to get wrong later. `cdll_client_int.cpp:2188-2210` runs
    /// `UpdateClientSideAnimations()` → `SimulateEntities()` → `ThreadedBoneSetup()`, so sequence
    /// selection happens BEFORE simulation and before any bone is built. Ours matches: this runs
    /// before <see cref="Instances"/>, which does <c>Simulate</c> and then the bones.
    ///
    /// **After the models are loaded, and that is a real constraint rather than a convenience.**
    /// Nothing on the wire carries a player's sequence, and choosing one needs the model's merged
    /// sequence table — which does not exist until <see cref="Add"/> has read it. Asked earlier it
    /// answers -1, and -1 is a real answer meaning "no such sequence", so an early call looks like a
    /// lookup that failed rather than one that ran too soon.
    ///
    /// Lived in <c>MainForm.ShowMoment</c> until 2026-08-25 (B188).
    /// </remarks>
    public void UpdateClientSideAnimations(IList<SceneProp> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            if (prop.Pose.Speed is not { } speed)
            {
                continue;
            }

            int chosen = SequenceFor(
                prop.ModelPath,
                speed,
                prop.Pose.Flags,

                // **True because the dead never reach here, not because death is ignored.**
                // `PlayerProps.ModelFor` refuses a player the engine would not draw, and TF2 turns
                // a dead player off with EF_NODRAW while a separate CTFRagdoll becomes the corpse.
                //
                // An earlier comment claimed a ragdoll was already doing that job, which was false
                // in both directions: nothing here draws ragdolls, and dead players WERE reaching
                // this call. With their ground flag clear they were then given ACT_MP_JUMP_FLOAT,
                // so seventeen seconds of a respawn drew a soldier falling through the air.
                alive: true,

                // The weapon's suffix, or the primary forms when nothing resolved it — which is
                // what the engine falls back to as well.
                slot: prop.Pose.Slot ?? "PRIMARY",

                // Splits the jump into its push-off and its float.
                airborneSeconds: prop.Pose.AirborneSeconds,

                // Supersedes the jump for a fast-rising player.
                airwalking: prop.Pose.Airwalking,

                // Waist deep turns a jump into a swim.
                waterLevel: prop.Pose.WaterLevel);

            // **A negative answer is left alone rather than written.** -1 means "this model has no
            // such sequence", and storing it would replace a working sequence with one that decodes
            // to nothing — a model frozen on frame zero, which reads as a broken animation rather
            // than as a failed lookup.
            if (chosen >= 0)
            {
                drawn[index] = prop with { Pose = prop.Pose with { Sequence = chosen } };
            }
        }
    }

    /// <summary>Which sequence a player of this model should play.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="flags">
    /// The player's <c>m_fFlags</c>, carrying the crouch and ground bits, or null when the recording
    /// did not say. Not a POV-versus-SourceTV distinction, as this said before B103: the property is
    /// on <c>DT_BasePlayer</c> and reaches every player in the PVS.
    /// </param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="slot">The suffix the held weapon drives, such as <c>SECONDARY</c>.</param>
    /// <param name="airborneSeconds">How long since they left the ground, or null.</param>
    /// <param name="airwalking">Whether they are air-walking, which supersedes the jump.</param>
    /// <param name="waterLevel">How deep in water they are; 2 or more is waist deep.</param>
    /// <returns>A merged sequence number, or −1 when the model is not skinned or has neither.</returns>
    /// <remarks>
    /// Asked of the set rather than of the model directly, because only the set knows whether a
    /// model was loaded skinned - a baked model has no merged sequence table to search.
    /// </remarks>
    public int SequenceFor(
        string modelPath,
        float speed,
        int? flags = null,
        bool alive = true,
        string slot = "PRIMARY",
        float? airborneSeconds = null,
        bool airwalking = false,
        int? waterLevel = null) =>
        _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames) &&
        frames.Skinned is { } skinned
            ? PlayerAnimation.For(
                skinned, speed, flags, alive, slot, airborneSeconds, airwalking, waterLevel)
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

                    // **Model space, untouched.** The shader's model matrix places it.
                    //
                    // **The lightmap coordinates are zero for a studio model and real for a brush
                    // entity (B131).** A `.mdl` is lit by its own vertex colours in the engine too,
                    // and (0, 0) is the atlas's reserved white texel, so the lightmap term is an
                    // identity rather than darkness. A door is the opposite case: vrad lights every
                    // model's faces (vrad.cpp:703) and its samples are already in the same atlas as
                    // the wall's, so passing them is all that separates a lightmapped door from a
                    // flat one. BrushModels fills them; PropModels leaves them at zero.
                    into.Add(new WorldVertex(
                        corner.X, corner.Y, corner.Z, corner.U, corner.V,
                        corner.LightU, corner.LightV, 0f,
                        LightStep: corner.LightStep,
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

                    // Debug: fires from the draw path as models are first seen, and every line is a
                    // disk flush (B191).
                    _render.LogDebug(
                        "{Message}",
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
            // Debug, like every other line this loop writes: it fires as models are first drawn,
            // which is a stream through a match rather than a burst at load, and each is a flush.
            _props.LogDebug(
                "{Message}",
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

        // **There is no ordering here any more, and that is the change** (D88, B181). The engine has
        // none either: a merged entity asks its parent for bones where it stands
        // (`bone_merge_cache.cpp:130`) and `SetupBones` being idempotent within a frame makes a
        // repeat an integer comparison. What this replaced was six fields and a depth sort solving a
        // problem Valve's structure never creates.
        //
        // Two phases instead, which is the engine's own split
        // (`cdll_client_int.cpp:2206-2210`): bring every entity's state up to date, then build
        // bones on demand while drawing. State first is what lets a merge reach an entity the draw
        // loop has not got to yet.
        _boneFrames.Advance();

        // **Timed as a phase because it IS one** — Valve's first of two
        // (`cdll_client_int.cpp:2206-2210`) — and because everything else in the pose path has now
        // been measured flat while the total swings 3 ms to 136 ms. Splitting SetupBones (1 ms) and
        // Skinning (0.2 ms) out left 127 ms in the remainder, and this is the larger half of it: a
        // pass over EVERY prop, not just the animated ones (B189).
        long simulatedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        Simulate(props, seconds);

        SimulateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - simulatedAt;

        // **Every prop that does not draw is counted with its reason.** A silent `continue` here is
        // how "all the props went away" became a guessing game: the scene said 14 models, the map
        // showed one, and nothing in between reported which test rejected the other thirteen.
        //
        // Four categories, per the project's rule: asked for, what we have, what was produced, what
        // is missing and why.
        _tally.Begin(props.Count);

        // In the order the scene gave them, because nothing needs any other order now.
        foreach (SceneProp prop in props)
        {
            (int frame, int _, float blend) = SelectFor(prop, seconds);

            int skin = prop.Pose.Skin;

            if (!IsDrawable(prop.Kind))
            {
                _tally.NotDrawable(prop);
                continue;
            }

            if (Batches(prop.ModelPath, frame).Count == 0)
            {
                _tally.NoGeometry(prop.ModelPath);
                continue;
            }

            _tally.Drawn();

            ScenePose pose = prop.Pose;

            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            // **Lit, logged and counted by collaborators rather than here** (B181). Each of these
            // was sixty to eighty lines inside this loop, and none of them is about posing a model —
            // which is how the body reached two hundred lines of code with five jobs in it and the
            // engine's stage boundaries invisible.
            ModelLight lit = _lighting.For(prop, lightAt, sunAt);

            AmbientCube? light = lit.Light;
            SunLight? sun = lit.Sun;

            // **The last unmeasured thing in this loop, and it runs three times per prop per
            // frame.** Everything else has been split and come back at a millisecond or less while
            // the total holds a deterministic ~130 ms (B189). Reporting is the remaining candidate
            // and it is the one that builds strings.
            long reportedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            _reports.BrushMoved(prop, seconds);

            if (lightAt is not null)
            {
                _reports.Lit(prop, lit, skin);
            }

            _reports.Animating(prop, frame, AllFrames(prop.ModelPath).Count, blend);

            ReportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - reportedAt;

            // **A skinned model is posed here, per instance.** Its geometry was uploaded once and
            // unposed, so the matrices are what puts it in a pose at all - without them it draws
            // in whatever position the artist modelled it, which for a player is lying on its
            // side.
            IReadOnlyList<float[]>? bones = null;

            if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) &&
                entry.Skinned is { } skinned &&
                _entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                // **One call, and it builds this entity's parents too if it merges onto any.**
                // Everything the old code did with a sort happens inside here now.
                //
                // BONE_USED_BY_ANYTHING because this project draws one level of detail and asks for
                // everything; a narrower mask is the optimisation the accessor exists to allow and
                // is not worth guessing at before something measures it.
                long setupAt = System.Diagnostics.Stopwatch.GetTimestamp();

                bool setUp = animating.SetupBones(StudioBoneFlags.UsedByAnything, seconds);

                SetupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - setupAt;

                if (!setUp)
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // hanging at the map origin, which is worse than not drawing it.
                    continue;
                }

                long skinAt = System.Diagnostics.Stopwatch.GetTimestamp();

                bones = Skinning(prop.EntityIndex, skinned.Bones, animating.Bones);

                SkinTicks += System.Diagnostics.Stopwatch.GetTimestamp() - skinAt;

                // **The bones are already in world space**, so the model matrix must not place the
                // model a second time (finding 35 section 7a). This is where the merged item's
                // `transform = worn.Where` used to be, and it is gone rather than moved: an item
                // takes its wearer's bones and those already carry the wearer's placement.
                transform = PropTransform.Identity;
            }

            // **Applies the matrices the GPU is about to use, on the processor, and reports the
            // result.** A skinned model that draws wrong could be a bad pose or a bad shader, and
            // an overhead camera cannot tell them apart. If these extents stand the model up, the
            // pose is right and the fault is in the drawing; if they do not, the pose is the
            // fault and the shader is innocent.
            // **The IsEnabled guard covers the WORK, not just the write** (B191, CA1873). Below
            // this, extents are walked over every corner and a SECOND full skeleton is built for
            // the corner comparison — both purely to produce a diagnostic line. A production run
            // was paying for both and then discarding the result.
            if (bones is { Count: > 0 } &&
                _props.IsEnabled(LogLevel.Debug) &&
                !_reportedPoses.Contains(prop.ModelPath))
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
                if (!_drawnPlacements.TryGetValue(wearer, out PropTransform stands))
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // at the map origin, which is worse than not drawing it. A SKINNED item is
                    // already refused by SetupBones returning false; this is the same rule for one
                    // with no skeleton to refuse with.
                    continue;
                }

                if (bones is null)
                {
                    // A model this project baked rather than skinned has no bones to carry a
                    // placement, so it takes its wearer's outright — which is what every worn item
                    // did before D88, and is still correct for one that cannot be merged onto
                    // anything.
                    transform = stands;
                }

                // **Lit where its wearer stands, not where its own pose says.** A merged item's own
                // pose is (0,0,0) by construction, so sampling the ambient cube from it asks the
                // leaf at the map origin — usually solid, carrying no light, drawing every cosmetic
                // in the match black. It showed in the log as "rocketboots is lit by nothing at
                // (0,0,0)", which reads as a lighting quirk rather than as a light sampled before
                // the item had been given a position.
                if (_lightPoints.TryGetValue(wearer, out (float X, float Y, float Z) at))
                {
                    // **Timed separately because it is neither cached NOR counted, and both are
                    // defects** (B189). `ModelLighting.For` exists to cache exactly this sample —
                    // keyed on the entity and the quantised point, because a model that has not
                    // moved cannot have changed brightness — and this call goes straight past it to
                    // the sampler. So every worn item on every player re-traces its lighting every
                    // frame, and because the call sits outside `LightingTicks` the cost was landing
                    // in a column arrived at by subtraction.
                    long wornAt = System.Diagnostics.Stopwatch.GetTimestamp();

                    light = lightAt is null ? default : lightAt(at.X, at.Y, at.Z);

                    WornLightTicks += System.Diagnostics.Stopwatch.GetTimestamp() - wornAt;
                }

                // Guarded before `FirstTime`, so a production run does not even build the
                // `path + "#worn"` key — a string allocated per worn prop per frame (B191).
                if (bones is { Count: > 0 } &&
                    _props.IsEnabled(LogLevel.Debug) &&
                    _reports.FirstTime(prop.ModelPath + "#worn"))
                {
                    ReportPosedExtents(prop.ModelPath, bones, prop.ModelPath + " WORN");
                }
            }

            into.Add(new ModelInstance(
                prop.ModelPath,
                transform.ToMatrix(),
                light,

                // Cached alongside the cube, because the sun costs more than it looks: it traces a
                // ray through the BSP to ask whether the sky is visible from here. It was also
                // being asked TWICE per model — once here and once for the wearer record — and
                // neither answer could differ.
                sun,
                frame,
                blend,
                bones,
                SkinSwap(prop.ModelPath, skin),
                _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? parts)
                    ? parts.BodyParts
                    : null,
                prop.Pose.Body));
        }

        _tally.Report();
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
    private static float[] PoseValues(PropModels.SkinnedModel model, ScenePose pose, int sequence)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        if (parameters.Count == 0)
        {
            return [];
        }

        float[] values = Filled(parameters, pose.MoveX, pose.MoveY, pose.EyePitch, pose.AimYaw);

        // **The speed scaling, and it happens HERE rather than in the scene layer because only this
        // side can open a model.** ComputePoseParam_MoveYaw finishes with
        //
        //     float flMaxSpeed = GetSequenceGroundSpeed( GetSequence() );
        //     if ( flMaxSpeed > flSpeed ) { x *= flSpeed / flMaxSpeed; y *= flSpeed / flMaxSpeed; }
        //
        // which pulls a player moving slower than their animation was authored for back towards the
        // middle of the blend grid. Without it a scout walking at 100 units a second animated with
        // the same full-magnitude stride as one sprinting at 400.
        //
        // **The two-pass shape is Valve's, not an accident of this port.** The engine sets move_x
        // and move_y, reads the ground speed WITH THOSE IN PLACE — the parameters choose which
        // cells of the grid are blended, so they choose whose authored speed is being asked about —
        // and only then rescales and sets them again. Reading the speed first would ask about
        // whichever cells the previous frame happened to leave behind.
        if (pose.Speed is not { } speed || speed <= 0f)
        {
            return values;
        }

        float authored = model.GroundSpeed(sequence, values);

        // Valve's guard is `if ( flMaxSpeed > flSpeed )`, so a player moving FASTER than their
        // animation was authored for is left alone rather than scaled past the edge of the grid.
        if (authored <= speed)
        {
            return values;
        }

        float scale = speed / authored;

        return Filled(
            parameters, pose.MoveX * scale, pose.MoveY * scale, pose.EyePitch, pose.AimYaw);
    }

    /// <summary>Every pose parameter's stored value, given the two this project computes.</summary>
    /// <remarks>
    /// Anything not computed stays at a raw zero, which is what the engine leaves an unset
    /// parameter at — and note that zero is normalised like any other value, so a parameter running
    /// −1 to 1 lands in the MIDDLE of its range rather than at the bottom.
    /// </remarks>
    private static float[] Filled(
        IReadOnlyList<StudioPoseParameter> parameters,
        float moveX,
        float moveY,
        float? eyePitch,
        float? aimYaw)
    {
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => moveX,
                "move_y" => moveY,

                // **Negated, which is the whole of ComputePoseParam_AimPitch:**
                // `SetPoseParameter( m_iAimPitch, -flAimPitch )` with flAimPitch the eye pitch. The
                // sign lives here rather than in the stored value, so what the scene carries still
                // matches what the wire said.
                //
                // Zero when the recording sent no eye angles, which is level — the same answer this
                // gave before aiming existed, rather than a guess at where they were looking.
                "body_pitch" => -(eyePitch ?? 0f),

                // **Already negated**, unlike body_pitch above. The twist is computed where the
                // feet are simulated, because it is the difference between two values only that
                // state machine holds — and SetPoseParameter( m_iAimYaw, -flAimYaw ) is applied
                // there with them.
                "body_yaw" => aimYaw ?? 0f,

                _ => 0f,
            };

            // Stored normalised, as the engine stores it - see StudioBlendGrid.Normalize.
            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        return values;
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
