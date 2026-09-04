using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One animation layer accumulated over the base pose.</summary>
/// <param name="Sequence">The layer's sequence.</param>
/// <param name="Frame">Its frame.</param>
/// <param name="FrameFraction">How far past that frame it is, as <c>CalcPoseSingle</c>'s <c>s</c>.</param>
/// <param name="Weight">
/// The layer's own weight, <c>m_flWeight</c>. Clamped to one before it is used, and skipped
/// entirely at or below zero.
/// </param>
/// <param name="BoneWeights">
/// The layer sequence's per-bone weight list — <c>seqdesc.weight( i )</c>. Multiplied by
/// <paramref name="Weight"/> to give <c>SlerpBones</c>' <c>pS2[i]</c>. A bone past the end of this
/// list is left alone, which matches a weightless bone rather than a fully weighted one.
/// </param>
/// <param name="Delta">
/// Whether the layer's sequence carries <c>STUDIO_DELTA</c>, meaning its animation holds a
/// DIFFERENCE rather than a pose. <c>SlerpBones</c> composes those additively instead of blending
/// toward them (<c>bone_setup.cpp:1434</c>), and every TF2 player gesture is one.
/// </param>
/// <param name="Post">
/// Whether a delta layer composes after the base rather than before — <c>STUDIO_POST</c>, which
/// chooses <c>QuaternionMA</c> over <c>QuaternionSM</c>. Meaningless without
/// <paramref name="Delta"/>.
/// </param>
/// <param name="Locks">
/// The IK chains this layer's sequence pins while it plays — <c>mstudioseqdesc_t::pIKLock</c>, and
/// null for the sequences that declare none. Every `AccumulatePose` is bracketed by
/// `AddSequenceLocks` and `SolveSequenceLocks`, so the locks belong to the layer rather than to the
/// entity (B311).
/// </param>
/// <remarks>
/// **The per-bone list is the whole mechanism, not a refinement.** A gesture's weight list is 1 on
/// the arms and 0 on the legs, which is how a reload plays on a running player without stopping
/// the run. A layer applied without it replaces the entire skeleton.
///
/// **Sampled through the same delegate as the base pose**, so a layer cannot disagree with the
/// base about what a sequence looks like — one route to the model, not two.
/// </remarks>
public readonly record struct PoseLayer(
    int Sequence,
    int Frame,
    float FrameFraction,
    float Weight,
    IReadOnlyList<float> BoneWeights,
    bool Delta = false,
    bool Post = false,
    IReadOnlyList<StudioIkLock>? Locks = null);

/// <summary>
/// A real studio skeleton, driven by whatever the animation says its bones are doing.
/// </summary>
/// <remarks>
/// **The adapter between the architecture and a model.** <see cref="AnimatingEntity"/> owns the
/// caching, the masks and the recursion and knows nothing about <c>.mdl</c> files; this knows about
/// one model and nothing about when it is asked. That is where the SDK splits too —
/// <c>C_BaseAnimating::SetupBones</c> against <c>IBoneSetup</c> and <c>BuildTransformations</c>.
///
/// **The parent transform is read out of the ACCESSOR, not from a private array**, which is the
/// whole reason this composes with the merge. <c>c_baseanimating.cpp:1595</c> is
/// <c>ConcatTransforms( GetBone( hdr-&gt;boneParent(i) ), bonematrix, GetBoneForWrite( i ) )</c> —
/// the same array the merge has already written into — so a bone whose parent came from a wearer
/// rides the wearer's position without anything here knowing a merge happened.
/// </remarks>
public sealed class SkeletonPose : IBonePose
{
    private readonly IReadOnlyList<StudioBone> _bones;
    private readonly Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>>
        _animation;

    /// <summary>Creates a pose source over one model's skeleton.</summary>
    /// <param name="bones">The skeleton, as <see cref="StudioBones.Read"/> returned it.</param>
    /// <param name="animation">
    /// Given a sequence, a frame and the pose parameter values, the local positions and rotations
    /// that animation overrides. Bones it omits keep their rest values, which is most of the
    /// skeleton for most animations.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SkeletonPose(
        IReadOnlyList<StudioBone> bones,
        Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>> animation)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(animation);

        _bones = bones;
        _animation = animation;
        _local = new float[bones.Count][];
        _layered = new StudioBonePose[bones.Count];
        _adjusted = new StudioBonePose[bones.Count];
        _overrideOf = new int[bones.Count];

        for (int bone = 0; bone < bones.Count; bone++)
        {
            _local[bone] = new float[12];
        }
    }

    /// <summary>Diagnostic: what every skeleton has spent decoding and blending animation.</summary>
    /// <remarks>
    /// **Static because the question is about the whole pose phase, not one skeleton**, and
    /// plumbing a per-instance total up through AnimatingEntity and EntityModelSet to reach the one
    /// caller that reads it would be a lot of surface for a number that exists to answer one
    /// question: does a 131 ms pose spike come from MORE animation calls or SLOWER ones (B189)?
    ///
    /// Read as a delta either side of a call, the way LightingTicks is. Single-threaded today; if
    /// the threaded bone setup of D88 lands, this needs to become per-thread or go.
    /// </remarks>
    public static long AnimationTicks { get; set; }

    /// <summary>Diagnostic: how many times the animation callback has been asked for a pose.</summary>
    public static int AnimationCalls { get; set; }

    /// <summary>Which entry of the animation overrides each bone, or −1.</summary>
    /// <remarks>
    /// Allocated once per entity and refilled per build, per D87: the size is known when the model
    /// is, and a frame has a deadline that RAM does not.
    /// </remarks>
    private readonly int[] _overrideOf;

    /// <summary>The base pose with layers accumulated into it, for a build that has layers.</summary>
    /// <remarks>
    /// **Allocated once per entity, per D87**, and refilled from the rest pose on every build that
    /// uses it. `Accumulate` returns the base list untouched when there are no layers, so an
    /// entity that never gestures never reads this at all — but a player mid-reload would
    /// otherwise allocate a hundred bone poses every frame.
    /// </remarks>
    private readonly StudioBonePose[] _layered;

    /// <summary>Which sequence this entity is playing.</summary>
    /// <remarks>
    /// **State on the entity, not an argument, because that is where the engine keeps it.**
    /// <c>StandardBlendingRules</c> reads <c>GetSequence()</c>, <c>GetCycle()</c> and
    /// <c>GetPoseParameters()</c> off the entity rather than receiving them
    /// (<c>c_baseanimating.cpp:1957</c>) — and it has to, because <c>SetupBones</c> can be reached
    /// through a merge from a child that knows nothing about what its wearer is doing.
    ///
    /// The previous shape took demo time and a closure, which quietly assumed every caller of
    /// SetupBones had the animation state to hand. The merge is exactly the caller that does not.
    /// </remarks>
    public int Sequence { get; set; }

    /// <summary>Which frame of it.</summary>
    public int Frame { get; set; }

    /// <summary>How far past that frame toward the next — the engine's <c>s</c>.</summary>
    /// <remarks>
    /// **<c>CalcPoseSingle</c> keeps a frame AND a fraction** (<c>bone_setup.cpp:915</c>):
    /// <c>iFrame = (int)fFrame; s = (fFrame - iFrame);</c>, and every bone it samples is
    /// <c>CalcBoneQuaternion( iFrame, s, … )</c> — a blend of that frame with the next.
    ///
    /// This project had only the frame, so an animation played its authored poses and nothing
    /// between them: about thirty a second against a viewer drawing several hundred, which is what
    /// stepping is (B279). Zero reproduces the old behaviour exactly, which is what a caller with
    /// nothing to blend — a single-frame pose holder, the end of a one-shot — should pass.
    /// </remarks>
    public float FrameFraction { get; set; }

    /// <summary>Every pose parameter's value, normalised, in this model's own order.</summary>
    /// <remarks>
    /// Normalised because that is how the engine stores them — <c>m_flPoseParameter</c> is sent over
    /// 0..1 (<c>baseanimating.cpp:243</c>) and the blend grid expects the same range.
    /// </remarks>
    public IReadOnlyList<float> PoseValues { get; set; } = [];

    /// <summary>Where this entity stands, as a row-major 3×4, or null to build in model space.</summary>
    /// <remarks>
    /// **This is what makes the result WORLD space, and it is what makes a merge need no transform
    /// bookkeeping at all.** <c>BuildTransformations</c> concatenates it into every ROOT bone —
    /// <c>ConcatTransforms( cameraTransform, bonematrix, GetBoneForWrite( i ) )</c> at
    /// <c>c_baseanimating.cpp:1591</c>, where <c>cameraTransform</c> came from
    /// <c>AngleMatrix( GetRenderAngles(), GetRenderOrigin(), parentTransform )</c> — and children
    /// inherit it through their parents.
    ///
    /// So a bone-merged item does not need its wearer's origin passed to it, or stored beside its
    /// bones, or applied afterwards: the matrices it copies are already in world space. The
    /// arrangement this replaces carried the wearer's transform alongside the bones and applied it
    /// at draw time, which is the bookkeeping that made a three-deep chain mix two spaces (B180).
    ///
    /// **Null means model space**, which is what a caller wants when it is measuring a skeleton
    /// rather than drawing it — the bind-pose tests do exactly that.
    /// </remarks>
    public IReadOnlyList<float>? EntityTransform { get; set; }

    /// <summary>The layers accumulated over the base pose, in order.</summary>
    /// <remarks>
    /// **<c>C_BaseAnimatingOverlay::AccumulateLayers</c>, in order of <c>m_nOrder</c>**
    /// (<c>c_baseanimatingoverlay.cpp:294</c>). Each layer is accumulated onto the RESULT of the
    /// last, not onto the original base, because the engine's <c>AccumulatePose</c> reads and
    /// writes the same <c>pos</c>/<c>q</c> arrays every time.
    ///
    /// **For a TF2 player these are gestures and nothing else.** The layer array itself is excluded
    /// from a player's send table (<c>tf_player.cpp:774</c>), so what fills these is the
    /// <c>CTEPlayerAnimEvent</c> stream — a reload, a flinch, an attack (B282). For everything that
    /// does send layers, a sentry or a dispenser, they are the wire's own.
    /// </remarks>
    public IReadOnlyList<PoseLayer> Layers { get; set; } = [];

    /// <summary>This entity's bone controller values, normalised, by input index.</summary>
    /// <remarks>
    /// **<c>CalcBoneAdj</c>'s input** (<c>bone_setup.cpp:2462</c>), which
    /// <c>StandardBlendingRules</c> applies after the layers and the autoplay sequences. They bend
    /// one bone each — a sentry's barrel, a door's hinge — and are networked, so unlike most of
    /// what drives a player they are genuinely recoverable from a demo (B288).
    /// </remarks>
    public IReadOnlyList<float> BoneControllers { get; set; } = [];

    /// <summary>The model's own controllers, which say which bone each input drives.</summary>
    /// <remarks>
    /// **Separate from the values, because they come from different places** — this is the model's
    /// and <see cref="BoneControllers"/> is the demo's. A controller names its input through
    /// <c>inputfield</c> rather than by position, so the two are joined by that number and not by
    /// index.
    /// </remarks>
    public IReadOnlyList<StudioBoneController> Controllers { get; set; } = [];

    /// <summary>Where a bone built on an unbuilt parent is reported, or null for nowhere.</summary>
    /// <remarks>
    /// **The one condition that makes a skeleton silently wrong** (B222). A bone whose parent failed
    /// the mask is concatenated onto a slot nothing wrote, so it inherits a stale transform and
    /// lands somewhere unrelated to its siblings. Nothing else in the pipeline can see it: the bone
    /// is finite, non-zero, correctly shaped, and simply in the wrong place.
    /// </remarks>
    public ILogger? Log { get; set; }

    /// <summary>Scratch space for one bone's local transform, reused across frames.</summary>
    /// <remarks>
    /// Allocated once per entity, per D87. This runs once per bone per entity per frame, and a
    /// fresh twelve-float array each time is kilobytes a frame through the collector for nothing.
    /// </remarks>
    private readonly float[][] _local;

    /// <inheritdoc/>
    public int BoneCount => _bones.Count;

    /// <inheritdoc/>
    public int FlagsOf(int bone) => _bones[bone].Flags;

    /// <inheritdoc/>
    public string NameOf(int bone) => _bones[bone].Name;

    /// <inheritdoc/>
    /// <remarks>
    /// **Three reasons a bone is skipped, and they are not the same reason.**
    ///
    /// <list type="bullet">
    /// <item><b>Already written</b> — the merge or, later, the IK solver put it there, and its
    /// children concatenate onto it. Rebuilding it from this model's own animation would undo the
    /// merge (<c>c_baseanimating.cpp:1519</c>).</item>
    /// <item><b>Outside the mask</b> — the caller does not need it. Valve's own first line in the
    /// loop (<c>:1516</c>). This assumes a bone's parents carry at least its own use bits, which
    /// studiomdl guarantees when it compiles the flags; a model that violated it would build a
    /// child on a stale parent.</item>
    /// <item><b>No parent yet built</b> — a malformed skeleton whose parent index points forward.
    /// The bone is written from its local transform alone rather than from a matrix that is still
    /// identity, so it draws unmoved instead of somewhere arbitrary.</item>
    /// </list>
    ///
    /// **Procedural bones are NOT handled here and that is a filed gap, not an oversight** (B182).
    /// A bone with <c>BONE_ALWAYS_PROCEDURAL</c> and a rule falls through to the ordinary path, so
    /// it holds its animated position instead of the rule's — which for a jiggle bone is a hat that
    /// does not sway rather than a hat in the wrong place.
    /// </remarks>
    public void Build(int boneMask, double currentTime, BoneAccessor into, BoneBitList alreadyWritten)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(alreadyWritten);

        long animatedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        IReadOnlyList<StudioBonePose> animated =
            _animation(Sequence, Frame, FrameFraction, PoseValues);

        AnimationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - animatedAt;
        AnimationCalls++;

        // **The layers, over the base pose and in order** (B282). `StandardBlendingRules` runs
        // `AccumulateLayers` straight after the main sequence, and each layer accumulates onto the
        // RESULT of the last rather than onto the original.
        animated = Accumulate(animated);

        // **`CalcBoneAdj`, which `StandardBlendingRules` runs after the layers** (B288). It bends
        // individual bones by the entity's own controller values — a sentry's barrel, a door's
        // hinge — and is the last thing to touch the local pose before it is concatenated.
        animated = Adjust(animated);

        // **Indexed once, not searched per bone — and the first version of this did the latter.**
        // It set a `bool` here and then scanned `animated` again INSIDE the per-bone loop, which is
        // O(bones × animated): about 6,400 iterations for an eighty-bone player, times every
        // animated entity, every frame. Measured 2026-08-25: bone posing had gone from ~220 ms of
        // every second to ~400 while lighting IMPROVED, and this was most of it.
        //
        // The arrangement it replaced (StudioBones.Posed) applied overrides by index and was O(n).
        // Losing that was a silent cost — the pose is identical either way, so nothing but a
        // stopwatch could see it.
        Array.Fill(_overrideOf, -1);

        for (int entry = 0; entry < animated.Count; entry++)
        {
            int bone = animated[entry].Bone;

            if (bone >= 0 && bone < _bones.Count)
            {
                _overrideOf[bone] = entry;
            }
        }

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            if (alreadyWritten.IsMarked(bone) || (_bones[bone].Flags & boneMask) == 0)
            {
                continue;
            }

            StudioBone rest = _bones[bone];

            (float X, float Y, float Z, float W) rotation = rest.Rotation;
            (float X, float Y, float Z) position = rest.Position;

            if (_overrideOf[bone] is var entry && entry >= 0)
            {
                StudioBonePose moved = animated[entry];

                // **An animated bone that has TRAVELLED is the shape of a bad track** (B222).
                // `c_demo_arms` bones 16 and 17 have the same parent and an identical rest
                // transform — separation zero in the file — and end up 92 units apart in the
                // viewer. Animation moves a bone by a few units; ninety is a decoded position that
                // is wrong, not a pose.
                //
                // Reported against the bone's OWN rest position, so the number is the displacement
                // the animation claims rather than a distance between two bones, which is what
                // makes it attributable to one track.
                if (Log is { } moved_log && moved_log.IsEnabled(LogLevel.Debug))
                {
                    float dx = moved.Position.X - rest.Position.X;
                    float dy = moved.Position.Y - rest.Position.Y;
                    float dz = moved.Position.Z - rest.Position.Z;

                    float travelled = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (travelled > 20f)
                    {
                        moved_log.LogDebug(
                            "{Message}",
                            $"BONE TRAVELLED: {NameOf(bone)}[{bone}] parent {rest.Parent} moved " +
                            $"{travelled:0.#} units from its rest position by animation — " +
                            $"rest ({rest.Position.X:0.##}, {rest.Position.Y:0.##}, " +
                            $"{rest.Position.Z:0.##}) -> ({moved.Position.X:0.##}, " +
                            $"{moved.Position.Y:0.##}, {moved.Position.Z:0.##})");
                    }
                }

                rotation = moved.Rotation;
                position = moved.Position;
            }

            // Written in place. The allocating overload returns a fresh twelve floats per bone per
            // entity per frame, which measured as 34 gen0 collections a second.
            StudioBones.FromQuaternion(rotation, position, _local[bone]);

            float[] destination = into.BoneForWrite(bone);

            if (rest.Parent >= 0 && rest.Parent < bone)
            {
                // **A bone built on a parent that was never built this pass is built on garbage**
                // (B222). The loop above skips any bone whose flags miss the mask, and a skipped
                // bone's slot is never written — so concatenating onto it uses whatever the accessor
                // happened to hold. The child then lands somewhere unrelated to its siblings and
                // moves erratically while they move smoothly, which is exactly what `c_demo_arms`
                // bone 17 (`vm_weapon_bone_1`) does: 92 units from bone 16 on a 30-unit model.
                //
                // Reported rather than repaired here, because the fix is a question about the MASK
                // — Valve's studiomdl marks a parent as used by whatever uses its children, and if
                // ours does not see that, the mask is what needs widening, not this concatenate.
                if ((_bones[rest.Parent].Flags & boneMask) == 0 &&
                    !alreadyWritten.IsMarked(rest.Parent) &&
                    Log is { } log && log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug(
                        "{Message}",
                        $"STALE PARENT: bone {NameOf(bone)}[{bone}] flags " +
                        $"0x{_bones[bone].Flags:X} built on parent {NameOf(rest.Parent)}" +
                        $"[{rest.Parent}] flags 0x{_bones[rest.Parent].Flags:X}, which the mask " +
                        $"0x{boneMask:X} skipped");
                }

                StudioBones.Concatenate(into.Bone(rest.Parent), _local[bone], destination);
            }
            else if (EntityTransform is { } placement)
            {
                // **Refused rather than ignored, and that distinction cost a crash to learn.** This
                // read `is { Count: 12 }`, so a matrix of the wrong shape simply did not apply — the
                // model would build in its own space and draw at the map origin, with nothing said
                // anywhere. A caller handed it a sixteen-float MODEL matrix on the first night this
                // existed (D88); the length check that caught that one was somewhere else, by luck.
                //
                // A silent skip on a wrong-shaped input is the failure mode this whole project keeps
                // meeting. Twelve or an exception.
                if (placement.Count != 12)
                {
                    throw new InvalidOperationException(
                        $"An entity placement is a matrix3x4_t of twelve floats, not " +
                        $"{placement.Count}. A sixteen-float model matrix goes through " +
                        $"MatrixConvention.ToBoneMatrix first.");
                }

                // A ROOT bone, and the only place the entity's own position enters. Everything
                // below it inherits world space through its parent — which is why a merged item
                // needs no transform of its own.
                StudioBones.Concatenate(AsSpan(placement), _local[bone], destination);
            }
            else
            {
                _local[bone].CopyTo(destination, 0);
            }

            // **`STUDIO_PROC_JIGGLE`, and it runs on the matrix the concatenate just produced**
            // (B293). That matrix IS Valve's `goalMX` — the bone's local transform on its parent,
            // which is exactly what `BuildTransformations` has in hand where it branches
            // (`c_baseanimating.cpp:1557`). The simulation reads the goal's axes out of it and
            // overwrites it with where the spring actually swung.
            Jiggle(bone, currentTime, destination);

            alreadyWritten.Mark(bone);
        }

        ReachWithIk(boneMask, into, alreadyWritten);

        // **The duck-jump correction, BEFORE the scales** (B314), which is the order
        // `C_TFPlayer::BuildTransformations` uses: the base transformations, then this, then the
        // per-bone scales, then the meathook (`c_tf_player.cpp:8764`).
        //
        // **Every bone, by the same vector.** It is a correction to the whole model's placement
        // rather than a pose change — the origin moved when the hull shrank and this cancels it —
        // so a partial application would tear the skeleton rather than shift it.
        if (DuckJumpOffset != 0f)
        {
            for (int bone = 0; bone < _bones.Count; bone++)
            {
                float[] matrix = into.BoneForWrite(bone);

                matrix[11] -= DuckJumpOffset;
            }
        }

        // **TF2's three per-bone scales, last of all** (B312), which is where
        // `C_TFPlayer::BuildTransformations` runs them — after the base transformations, after the
        // duck offset, on the finished matrices (`c_tf_player.cpp:8815`). Each is a no-op at 1,
        // which is every value in an ordinary match and the reason nothing missed them.
        PlayerBoneScales.Head(into, _bones, HeadScale);
        PlayerBoneScales.Torso(into, _bones, TorsoScale);
        PlayerBoneScales.Hands(into, _bones, HandScale);
    }

    /// <summary>Pulls each chain's end to where its rules ask — <c>CIKContext::SolveDependencies</c>.</summary>
    /// <remarks>
    /// **After the bones are built, which is where <c>SetupBones</c> runs it.** IK reads WORLD
    /// matrices — a chain's end has to be somewhere before it can be moved somewhere else — so it
    /// cannot happen inside the per-bone loop.
    ///
    /// **Only <c>IK_SELF</c> reaches the solver, and that is measured rather than assumed** (B296).
    /// Of the scout's 2035 rules, 1829 are <c>IK_RELEASE</c> and solve nothing, 206 are
    /// <c>IK_SELF</c>, and TF2 declares none of the other four types anywhere.
    ///
    /// **The descendants have to be rebuilt afterwards.** Moving a chain's three bones leaves
    /// everything hanging off them concatenated onto where they used to be — a hand solved onto a
    /// weapon would drag its fingers behind it. Valve avoids this by rebuilding chains on demand
    /// through <c>BuildBoneChain</c>; here the bones are already ordered parents-before-children,
    /// so one more pass from the lowest bone that moved is enough and is cheaper.
    /// </remarks>
    private void ReachWithIk(int boneMask, BoneAccessor into, BoneBitList alreadyWritten)
    {
        if (IkChains.Count == 0 || IkErrors.Count == 0)
        {
            return;
        }

        _ik ??= new IkContext();

        // The parent list, built once and only for a model that actually has chains — which is
        // players and nothing else.
        _parents ??= [.. _bones.Select(bone => bone.Parent)];

        _ik.Solve(IkChains, IkErrors, into, _parents, _local);

        if (_ik.Solved == 0)
        {
            return;
        }

        // The lowest bone any solved chain touched: everything below it is still correct, and
        // everything above it may be hanging off a bone that moved.
        int from = int.MaxValue;

        foreach (StudioIkChain chain in IkChains)
        {
            foreach (StudioIkLink link in chain.Links)
            {
                if (link.Bone >= 0 && link.Bone < from)
                {
                    from = link.Bone;
                }
            }
        }

        for (int bone = from + 1; bone < _bones.Count; bone++)
        {
            int parent = _bones[bone].Parent;

            // **Only a bone whose PARENT was rebuilt, and only where both were built this pass.**
            // A bone the mask skipped has no matrix to concatenate onto, and re-running one that
            // the chain itself just solved would undo the solve.
            if (parent < 0 ||
                parent >= bone ||
                !alreadyWritten.IsMarked(bone) ||
                !alreadyWritten.IsMarked(parent) ||
                (_bones[bone].Flags & boneMask) == 0 ||
                IsChainBone(bone))
            {
                continue;
            }

            StudioBones.Concatenate(into.Bone(parent), _local[bone], into.BoneForWrite(bone));
        }
    }

    /// <summary>Whether a bone is one of the three a chain owns.</summary>
    private bool IsChainBone(int bone)
    {
        foreach (StudioIkChain chain in IkChains)
        {
            foreach (StudioIkLink link in chain.Links)
            {
                if (link.Bone == bone)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The model's IK chains, or empty when it declares none.</summary>
    /// <remarks>
    /// **The ROOT model's**, like the bone controllers and the jiggle parameters: the chains index
    /// the skeleton being posed. Every TF2 player model declares four — <c>rhand</c>, <c>lhand</c>,
    /// <c>rfoot</c>, <c>lfoot</c>, three links each — and a prop declares none.
    /// </remarks>
    public IReadOnlyList<StudioIkChain> IkChains { get; set; } = [];

    /// <summary>The MAIN sequence's IK locks, if it declares any.</summary>
    /// <remarks>
    /// **A layer's locks ride on the layer; these are the base sequence's** (B311), because the
    /// engine brackets every `AccumulatePose` and the first of them is the main sequence. Its
    /// "before" is the bind pose, since that is what `InitPose` left in `pos`/`q`.
    /// </remarks>
    public IReadOnlyList<StudioIkLock> Locks { get; set; } = [];

    /// <summary>How far to lower the whole skeleton for a crouch jump, in units.</summary>
    /// <remarks>
    /// **Already multiplied by the hull difference** — the caller owns the twenty units, because
    /// the hull sizes are TF2's game rules rather than anything a skeleton knows
    /// (`tf_gamerules.cpp:1313`). This carries the finished distance so the pose applies it without
    /// re-deriving it, which is the B243 rule about a value having one route.
    ///
    /// **Zero for everything that is not an airborne crouching player** (B314), which is most of a
    /// frame — a prop, a weapon, a player on the ground.
    /// </remarks>
    public float DuckJumpOffset { get; set; }

    /// <summary>TF2's per-bone head scale — <c>m_flHeadScale</c>, 1 for everything ordinary.</summary>
    /// <remarks>
    /// **Three fields, because the engine runs three different passes** and only one of them is a
    /// scale in the ordinary sense; see <see cref="PlayerBoneScales"/> (B312).
    /// </remarks>
    public float HeadScale { get; set; } = 1f;

    /// <summary>TF2's per-bone torso scale — <c>m_flTorsoScale</c>.</summary>
    public float TorsoScale { get; set; } = 1f;

    /// <summary>TF2's per-bone hand scale — <c>m_flHandScale</c>.</summary>
    public float HandScale { get; set; } = 1f;

    /// <summary>The lock bracket, built once for a model that has one.</summary>
    /// <remarks>
    /// **Lazily, like the IK context beside it**, because a prop declares no chains and most
    /// sequences no locks — so the arrays this allocates would be per-entity waste on almost
    /// everything drawn.
    /// </remarks>
    private IkLocks Held()
    {
        _parents ??= [.. _bones.Select(bone => bone.Parent)];

        return _held ??= new IkLocks(_parents, _bones.Count);
    }

    /// <summary>Every rule that asked for something this frame, with its target and weight.</summary>
    /// <remarks>
    /// **Computed on the scene side because the blend weights live there.** A rule's influence is
    /// accumulated across the same animations its sequence blends, so the caller that knows which
    /// they are and how much each counts is the one that can weigh a rule — carrying the answer
    /// here rather than recomputing it from the model (B243).
    ///
    /// **Gathered from EVERY accumulated sequence, not just the main one** (B297).
    /// `AccumulatePose` calls `AddDependencies` for each sequence it accumulates, and
    /// `AddSequenceLayers` then recurses — so an autolayer's rules count too. That matters here
    /// more than anywhere: TF2's aim matrices are autolayers of the movement sequences, and every
    /// solving rule in the game lives on them.
    /// </remarks>
    public IReadOnlyList<(StudioIkRule Rule, Vector3 Position, Quaternion Rotation, float Weight)>
        IkErrors { get; set; } = [];

    /// <summary>How many chains the last build actually solved.</summary>
    public int SolvedChains => _ik?.Solved ?? 0;

    /// <summary>How many sequence IK locks were applied while this pose was built.</summary>
    /// <remarks>
    /// **The wiring question, answerable only from production** (B311). The unit tests prove a lock
    /// pins an effector when `IkLocks` is called; this says whether anything calls it on a real
    /// demo, which is the half that has shipped broken three times this session.
    /// </remarks>
    public int AppliedLocks => _held?.Applied ?? 0;

    /// <summary>Of those, how many moved the effector, and the furthest one.</summary>
    /// <remarks>
    /// **"It ran" and "it mattered" are different claims** (B311). A lock whose remembered position
    /// already equals where the sequence left the foot solves to the same place, so a count of
    /// solves cannot tell a pose needing no correction from a correction computing zero.
    /// </remarks>
    public (int Moved, float Furthest) LockEffect =>
        (_held?.Moved ?? 0, _held?.FurthestMove ?? 0f);

    /// <summary>The IK state, created on the first entity that actually has a chain.</summary>
    private IkContext? _ik;

    private IkLocks? _held;

    /// <summary>Each bone's parent, built once for a model with chains.</summary>
    private int[]? _parents;

    /// <summary>Accumulates every layer over the base pose, as <c>AccumulateLayers</c> does.</summary>
    /// <param name="basePose">What the main sequence produced.</param>
    /// <returns>The base pose with the layers accumulated into it.</returns>
    /// <remarks>
    /// **<c>C_BaseAnimatingOverlay::AccumulateLayers</c> then <c>SlerpBones</c>**
    /// (<c>c_baseanimatingoverlay.cpp:294</c>, <c>bone_setup.cpp:1373</c>). Each layer:
    ///
    /// <code>
    ///   if (fWeight &gt; 1) fWeight = 1;                 // clamped, never extrapolated
    ///   pS2[i] = s * seqdesc.weight( i );              // per bone
    ///   if ( s2 &lt;= 0.0f ) continue;                    // untouched, not blended by zero
    ///   s1 = 1.0 - s2;
    ///   QuaternionSlerp( q2[i], q1[i], s1, q3 );
    ///   pos1[i] = pos1[i] * s1 + pos2[i] * s2;
    /// </code>
    ///
    /// **Returns the base pose unchanged when there is nothing to do**, which is the common case —
    /// most entities have no layers and most gesture slots are empty — so a frame pays one branch
    /// rather than an allocation.
    ///
    /// **The rest pose fills a bone the base sequence did not override.** The base pose is a sparse
    /// list: a sequence that animates only the arms returns only arm bones. A layer weighted onto a
    /// bone the base left out has to blend against something, and the engine's <c>q1</c> holds the
    /// bind pose there because <c>InitPose</c> seeded it.
    ///
    /// **Reproduced:** <c>BONE_FIXED_ALIGNMENT</c>, which chooses <c>QuaternionSlerpNoAlign</c> over
    /// <c>QuaternionSlerp</c> in Valve's own <c>SlerpBones</c> (<c>bone_setup.cpp:1492</c>) — see the
    /// branch below, keyed on <see cref="StudioBoneFlags.FixedAlignment"/>. This was believed unread
    /// by this project's <c>.mdl</c> parser; it is read (<c>StudioBones</c> decodes bone flags at
    /// offset 160) and this method has used it since. **Measured on <c>tf2-2026-pub-pov-clean</c> at
    /// tick 14051: no model sets the flag** — 0 of 924 bones across 37 skinned models — so the branch
    /// is correct and currently unexercised by real content.
    /// </remarks>
    private IReadOnlyList<StudioBonePose> Accumulate(IReadOnlyList<StudioBonePose> basePose)
    {
        if (Layers.Count == 0 && Locks.Count == 0)
        {
            return basePose;
        }

        StudioBonePose[] result = _layered;

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            result[bone] = new StudioBonePose(
                bone, _bones[bone].Position, _bones[bone].Rotation);
        }

        // **The MAIN sequence's bracket, and its "before" is the BIND pose** (B311). The engine's
        // first `AccumulatePose` runs on `pos`/`q` straight out of `InitPose`, so
        // `AddSequenceLocks` records where the chain ends in the rest skeleton — which is why a
        // locked sequence holds a foot at its bind position rather than at wherever the previous
        // frame left it.
        if (Locks.Count > 0 && IkChains.Count > 0)
        {
            Held().Capture(Locks, IkChains, result);
        }

        for (int entry = 0; entry < basePose.Count; entry++)
        {
            StudioBonePose moved = basePose[entry];

            if (moved.Bone >= 0 && moved.Bone < result.Length)
            {
                result[moved.Bone] = moved;
            }
        }

        if (Locks.Count > 0 && IkChains.Count > 0)
        {
            Held().Solve(Locks, IkChains, result);
        }

        foreach (PoseLayer layer in Layers)
        {
            // `if (fWeight > 0)` — a slot at zero is not accumulated at all.
            if (layer.Weight <= 0f)
            {
                continue;
            }

            float weight = MathF.Min(1f, layer.Weight);

            // **Each layer is its own `AccumulatePose`, so each carries its own lock bracket**
            // (B311). This is the case TF2 actually ships: every one of the 814 locking sequences
            // under `models/player/` is an aim matrix or an attack stand, and the aim matrices
            // arrive here as autolayers. Their "before" is the pose as the previous layers left it,
            // not the bind pose — which is what the engine's `pos`/`q` hold at that point.
            bool holds = layer.Locks is { Count: > 0 } && IkChains.Count > 0;

            if (holds)
            {
                Held().Capture(layer.Locks!, IkChains, result);
            }

            IReadOnlyList<StudioBonePose> sampled =
                _animation(layer.Sequence, layer.Frame, layer.FrameFraction, PoseValues);

            foreach (StudioBonePose over in sampled)
            {
                int bone = over.Bone;

                if (bone < 0 || bone >= result.Length ||
                    layer.BoneWeights is not { } weights || bone >= weights.Count)
                {
                    continue;
                }

                float s2 = weight * weights[bone];

                if (s2 <= 0f)
                {
                    continue;
                }

                if (s2 > 1f)
                {
                    s2 = 1f;
                }

                StudioBonePose under = result[bone];

                // **A delta layer ADDS; it does not blend toward** (B284). `SlerpBones` splits on
                // the sequence's `STUDIO_DELTA` before it does anything else
                // (`bone_setup.cpp:1434`):
                //
                //     if ( seqdesc.flags & STUDIO_POST ) QuaternionMA( q1[i], s2, q2[i], q1[i] );
                //     else                               QuaternionSM( s2, q2[i], q1[i], q1[i] );
                //     pos1[i] = pos1[i] + pos2[i] * s2;
                //
                // **Every TF2 player gesture takes this branch**, measured on `scout.mdl`:
                // `PRIMARY_reload_start` and `jumpland_primary` both carry the delta bit on the
                // sequence AND on the animation behind it. Slerping toward one instead replaces the
                // skeleton with a difference — which is not a pose at all — and lays the player
                // flat on the ground.
                if (layer.Delta)
                {
                    result[bone] = new StudioBonePose(
                        bone,
                        (under.Position.X + (over.Position.X * s2),
                         under.Position.Y + (over.Position.Y * s2),
                         under.Position.Z + (over.Position.Z * s2)),
                        layer.Post
                            ? StudioBones.ScaleAfter(under.Rotation, s2, over.Rotation)
                            : StudioBones.ScaleBefore(s2, over.Rotation, under.Rotation));

                    continue;
                }

                float s1 = 1f - s2;

                // **`BONE_FIXED_ALIGNMENT` picks the blend that does NOT re-align** (B292):
                //
                //     if ( pStudioHdr->boneFlags(i) & BONE_FIXED_ALIGNMENT )
                //         QuaternionSlerpNoAlign( q2[i], q1[i], s1, q3 );
                //     else
                //         QuaternionSlerp( q2[i], q1[i], s1, q3 );
                //
                // (`bone_setup.cpp:1492`). Aligning negates the target when it points the long way
                // round, which is normally what keeps a limb from swinging through the body — but
                // on a bone the animator has declared constrained, that negation flips it out of
                // its authored range instead.
                //
                // **Valve's argument order is kept — layer first, base second, at `s1`.** For the
                // aligning form the two orders agree, since the trig is symmetric under swapping
                // the pair and the fraction. `SlerpNoAlign`'s ANTIPODAL arm is not: it builds a
                // perpendicular out of its SECOND argument, so writing the pair the other way round
                // would silently change the result in exactly the case the flag exists for.
                result[bone] = new StudioBonePose(
                    bone,
                    ((under.Position.X * s1) + (over.Position.X * s2),
                     (under.Position.Y * s1) + (over.Position.Y * s2),
                     (under.Position.Z * s1) + (over.Position.Z * s2)),
                    (_bones[bone].Flags & StudioBoneFlags.FixedAlignment) != 0
                        ? StudioBones.SlerpNoAlign(over.Rotation, under.Rotation, s1)
                        : StudioBones.Slerp(under.Rotation, over.Rotation, s2));
            }

            // `if (seqdesc.numiklocks) seq_ik.SolveSequenceLocks( seqdesc, pos, q );` — the closing
            // half, after the layer has been composed and before the next one begins.
            if (holds)
            {
                Held().Solve(layer.Locks!, IkChains, result);
            }
        }

        return result;
    }

    /// <summary>Bends bones by the entity's controller values — <c>CalcBoneAdj</c>.</summary>
    /// <param name="pose">The pose so far.</param>
    /// <returns>The pose with the controllers applied.</returns>
    /// <remarks>
    /// **<c>bone_setup.cpp:2462</c>**, and the whole of it:
    ///
    /// <code>
    ///   i = pbonecontroller->inputfield;
    ///   value = controllers[i];
    ///   if (value &lt; 0) value = 0;
    ///   if (value &gt; 1.0) value = 1.0;
    ///   value = (1.0 - value) * pbonecontroller->start + value * pbonecontroller->end;
    ///   switch(pbonecontroller->type &amp; STUDIO_TYPES)
    ///   {
    ///   case STUDIO_XR: a0.Init( value * (M_PI / 180.0), 0, 0 ); AngleQuaternion( a0, q0 );
    ///                   QuaternionSM( 1.0, q0, q[k], q[k] ); break;
    ///   ...
    ///   case STUDIO_X:  pos[k].x += value; break;
    ///   }
    /// </code>
    ///
    /// **A rotation is in DEGREES and a translation is in units**, which the engine shows by
    /// converting only the former. Scaling both the same way would rotate a bone by a fraction of a
    /// degree or slide it fifty units.
    ///
    /// **The rotation composes with `QuaternionSM` at weight one**, which is the same additive
    /// composition a delta layer uses — the controller turns the bone FROM where the animation left
    /// it rather than replacing it.
    ///
    /// **Returns the pose untouched when there is nothing to do**, which is almost every entity:
    /// a model with no controllers, or a demo that never sent a value.
    /// </remarks>
    private IReadOnlyList<StudioBonePose> Adjust(IReadOnlyList<StudioBonePose> pose)
    {
        if (Controllers.Count == 0 || BoneControllers.Count == 0)
        {
            return pose;
        }

        StudioBonePose[] adjusted = _adjusted;

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            adjusted[bone] = new StudioBonePose(
                bone, _bones[bone].Position, _bones[bone].Rotation);
        }

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < adjusted.Length)
            {
                adjusted[moved.Bone] = moved;
            }
        }

        bool touched = false;

        foreach (StudioBoneController controller in Controllers)
        {
            int bone = controller.Bone;

            if (bone < 0 || bone >= adjusted.Length ||
                controller.InputField < 0 || controller.InputField >= BoneControllers.Count)
            {
                continue;
            }

            // **`if (pStudioHdr->boneFlags( k ) & boneMask)`** (`bone_setup.cpp:2480`), which wraps
            // the whole body of the engine's loop — a bone outside the mask does not even have its
            // lerp computed. Production asks for `BONE_USED_BY_ANYTHING`, so this rejects a bone
            // used by no hitbox, no attachment and no vertex at any LOD.
            //
            // **Every one of those flags reads "bone (or CHILD) is used by"**, so such a bone has no
            // descendant used by anything either and bending it was already invisible. This is
            // Valve's economy rather than Valve's correctness — and it becomes correctness the
            // moment a caller asks for a mask narrower than everything.
            if ((_bones[bone].Flags & StudioBoneFlags.UsedByAnything) == 0)
            {
                continue;
            }

            float value = controller.Value(BoneControllers[controller.InputField]);

            StudioBonePose current = adjusted[bone];

            (float X, float Y, float Z) position = current.Position;
            (float X, float Y, float Z, float W) rotation = current.Rotation;

            switch (controller.Axis)
            {
                case StudioBoneController.TranslateX:
                    position.X += value;
                    break;

                case StudioBoneController.TranslateY:
                    position.Y += value;
                    break;

                case StudioBoneController.TranslateZ:
                    position.Z += value;
                    break;

                case StudioBoneController.RotateX:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(Radians(value), 0f, 0f), rotation);
                    break;

                case StudioBoneController.RotateY:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(0f, Radians(value), 0f), rotation);
                    break;

                case StudioBoneController.RotateZ:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(0f, 0f, Radians(value)), rotation);
                    break;

                default:
                    continue;
            }

            adjusted[bone] = new StudioBonePose(bone, position, rotation);
            touched = true;
        }

        return touched ? adjusted : pose;
    }

    /// <summary>Degrees as radians, which is the engine's <c>value * (M_PI / 180.0)</c>.</summary>
    /// <param name="degrees">The controller's value.</param>
    /// <returns>The same angle in radians.</returns>
    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);

    /// <summary>Runs a bone's spring physics over the matrix the concatenate produced.</summary>
    /// <param name="bone">Which bone.</param>
    /// <param name="currentTime">Now.</param>
    /// <param name="destination">The bone's matrix, read as the goal and written as the result.</param>
    /// <remarks>
    /// **The gate is Valve's PAIR** (<c>c_baseanimating.cpp:1545</c>):
    /// <c>(boneFlags(i) &amp; BONE_ALWAYS_PROCEDURAL) &amp;&amp; (pBone-&gt;proctype &amp;
    /// STUDIO_PROC_JIGGLE)</c>. <see cref="StudioJiggleBones.Read"/> applies the second half, so a
    /// bone that is procedural by some other rule reads back null and falls through unchanged —
    /// which is what the engine does too, since `CalcProceduralBone` handles those four earlier and
    /// they never reach this branch.
    ///
    /// **Returns before doing anything when the model carries no jiggle bone at all**, which is
    /// almost every model: 22 of 379 bones on a real map, and most of those on two cosmetics.
    ///
    /// **Not reproduced: the parent's unscale**, and it is provably inert here rather than merely
    /// believed to be. Valve divides a parent matrix out by its own scale before building the goal
    /// so a big-head effect does not inflate the chain hanging off it (<c>:1567</c>), under a guard
    /// that only fires when that matrix is actually scaled:
    ///
    /// <code>
    ///   float fScale = Square( parentMX[0][0] ) + Square( parentMX[1][0] ) + Square( parentMX[2][0] );
    ///   if ( fScale > Square( 1.0001f ) ) { … MatrixScaleBy( 1/sqrt(fScale), parentMX ); }
    /// </code>
    ///
    /// **Checked: nothing in this project scales a bone matrix.** `m_flModelScale` is applied at the
    /// ENTITY transform, alongside position and angles, so every bone matrix reaching here is
    /// unscaled and `fScale` is one. The branch would run and do nothing.
    ///
    /// **What would make it reachable is `BuildBigHeadTransformations`** (`c_tf_player.cpp:8482`),
    /// a per-BONE scale that TF2 runs on every player build from `m_flHeadScale` — a networked
    /// field present in the send tables of every demo checked, decoded by nothing here and applied
    /// by nothing here (B312). At its default of 1 it too is inert, which is why no measurement has
    /// ever shown its absence.
    /// </remarks>
    private void Jiggle(int bone, double currentTime, float[] destination)
    {
        if (JiggleSource is not { } model)
        {
            return;
        }

        if ((_bones[bone].Flags & StudioBoneFlags.AlwaysProcedural) == 0)
        {
            return;
        }

        if (StudioJiggleBones.Read(model, bone) is not { } jiggle)
        {
            return;
        }

        _jiggle ??= new JiggleBones();

        _jiggle.Build(
            bone,
            (float)currentTime,
            jiggle,
            destination,
            destination,

            // **False, and it is a viewmodel question rather than a jiggle one.**
            // `ShouldFlipViewModel` is true for a left-handed viewmodel (`cl_flipviewmodels`),
            // which this viewer does not implement — see B292's neighbour. When it is, this is
            // where the flag arrives.
            flipped: false);
    }

    /// <summary>The model bytes a jiggle bone's parameters are read from, or null for none.</summary>
    /// <remarks>
    /// **The ROOT model's**, because `pProcedure()` is an offset from the bone structure and the
    /// bones being posed are the root model's. An included animation model has its own bone table
    /// and its own offsets, and reading one against the other lands on arbitrary bytes.
    /// </remarks>
    public ReadOnlyMemory<byte>? JiggleSource { get; set; }

    /// <summary>How many of this entity's bones the spring simulation has actually run on.</summary>
    /// <remarks>
    /// **Carried from where the work happened, not recomputed** (B243). A count built by asking the
    /// model how many bones carry the flag would be a second reading of the model and would report
    /// the same number whether or not `Jiggle` was ever reached — which is exactly the wiring
    /// question worth asking, since the reader, the flag and the simulation were all correct in
    /// isolation and none of that says production calls them.
    /// </remarks>
    public int JigglingBones => _jiggle?.Simulated ?? 0;

    /// <summary>The spring state, created on the first jiggle bone this entity actually has.</summary>
    /// <remarks>
    /// **Lazily, exactly as the engine does it** — `if (!m_pJiggleBones) m_pJiggleBones = new
    /// CJiggleBones;` inside the branch (`c_baseanimating.cpp:1580`). Most entities have no jiggle
    /// bone and allocate nothing.
    /// </remarks>
    private JiggleBones? _jiggle;

    /// <summary>Scratch for the controller pass, allocated once per entity.</summary>
    private readonly StudioBonePose[] _adjusted;

    /// <summary>The entity transform as a span, copied only when it is not already an array.</summary>
    /// <remarks>
    /// A <c>float[]</c> passes through without allocating, which is what every caller on the draw
    /// path supplies. The copy exists so the property can take any list, and it costs twelve floats
    /// on a path nobody hot uses.
    /// </remarks>
    private ReadOnlySpan<float> AsSpan(IReadOnlyList<float> placement)
    {
        if (placement is float[] array)
        {
            return array;
        }

        for (int cell = 0; cell < 12; cell++)
        {
            _placement[cell] = placement[cell];
        }

        return _placement;
    }

    /// <summary>Scratch for the entity transform when it arrives as something other than an array.</summary>
    private readonly float[] _placement = new float[12];
}
