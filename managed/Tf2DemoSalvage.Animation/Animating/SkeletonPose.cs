using System;
using System.Collections.Generic;

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
    IReadOnlyList<float> BoneWeights);

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

            alreadyWritten.Mark(bone);
        }
    }

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
    /// **Not reproduced:** <c>BONE_FIXED_ALIGNMENT</c>, which chooses
    /// <c>QuaternionSlerpNoAlign</c> over <c>QuaternionSlerp</c>. It matters only for bones whose
    /// animations are authored without alignment, and the flag is not read by this project's
    /// <c>.mdl</c> parser yet — named here rather than left silent.
    /// </remarks>
    private IReadOnlyList<StudioBonePose> Accumulate(IReadOnlyList<StudioBonePose> basePose)
    {
        if (Layers.Count == 0)
        {
            return basePose;
        }

        StudioBonePose[] result = _layered;

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            result[bone] = new StudioBonePose(
                bone, _bones[bone].Position, _bones[bone].Rotation);
        }

        for (int entry = 0; entry < basePose.Count; entry++)
        {
            StudioBonePose moved = basePose[entry];

            if (moved.Bone >= 0 && moved.Bone < result.Length)
            {
                result[moved.Bone] = moved;
            }
        }

        foreach (PoseLayer layer in Layers)
        {
            // `if (fWeight > 0)` — a slot at zero is not accumulated at all.
            if (layer.Weight <= 0f)
            {
                continue;
            }

            float weight = MathF.Min(1f, layer.Weight);

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

                float s1 = 1f - s2;

                StudioBonePose under = result[bone];

                result[bone] = new StudioBonePose(
                    bone,
                    ((under.Position.X * s1) + (over.Position.X * s2),
                     (under.Position.Y * s1) + (over.Position.Y * s2),
                     (under.Position.Z * s1) + (over.Position.Z * s2)),
                    StudioBones.Slerp(under.Rotation, over.Rotation, s2));
            }
        }

        return result;
    }

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
