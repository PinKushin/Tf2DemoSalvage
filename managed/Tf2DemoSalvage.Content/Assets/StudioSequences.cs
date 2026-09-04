using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One of a model's sequences.</summary>
/// <param name="Animation">The local animation it plays, for a sequence with no blending.</param>
/// <param name="Flags">Its flags, of which looping is the one that matters here.</param>
/// <param name="Label">Its name, which is what several models' sequences are merged by.</param>
/// <param name="Activity">
/// The activity it answers to, as a name — <c>ACT_MP_RUN</c> and the like. Empty for a sequence that
/// claims no activity, which is most of a weapon model's.
/// </param>
/// <param name="ActivityWeight">
/// How strongly it claims that activity. Several sequences may share one, and the engine's
/// <c>SelectWeightedSequence</c> picks between them in proportion to this; a weight of zero is never
/// chosen even though the name is present.
/// </param>
/// <param name="Blend">The grid of animations it blends between, or null for a plain sequence.</param>
/// <param name="Events">
/// The animation events it fires, or null for a sequence declaring none. Read
/// <see cref="FiredEvents"/> instead, which answers empty rather than null.
/// </param>
/// <param name="AutoLayers">
/// How many other sequences this one layers over itself — <c>numautolayers</c>. **The count is read
/// here; the accumulation lives in `EntityModels.AutoLayersFor`**
/// (<c>managed/Tf2DemoSalvage.Scene/EntityModels.cs:1615</c>), whose loop over
/// `skinned.AutoLayersOf(sequence)` covers both `AddSequenceLayers` (<c>bone_setup.cpp:2125</c>) and
/// `AddLocalLayers` (<c>bone_setup.cpp:2218</c>). This field was declared first so the question "does
/// any TF2 content use them" could be measured before committing to the implementation; the answer
/// came back yes (B294 — `sentry3`, `c_rocketpack`, `c_engineer_arms`), and the implementation above
/// followed.
/// </param>
public readonly record struct StudioSequence(
    int Animation,
    int Flags,
    string Label = "",
    StudioBlendGrid? Blend = null,
    string Activity = "",
    int ActivityWeight = 0,
    IReadOnlyList<StudioEvent>? Events = null,
    int AutoLayers = 0)
{
    /// <summary>The events this sequence fires, in file order; never null.</summary>
    /// <remarks>
    /// **On the sequence because that is where the model puts them** — `mstudioseqdesc_t` carries
    /// `numevents` and `eventindex` (`studio.h:817`), and `C_BaseAnimating::DoAnimationEvents`
    /// reads them off the sequence it is currently playing.
    ///
    /// Empty rather than null for a sequence that declares none, which is most of them: a caller
    /// walking events should not have to ask whether there are any first.
    /// </remarks>
    public IReadOnlyList<StudioEvent> FiredEvents => Events ?? [];

    /// <summary>Whether the sequence loops.</summary>
    /// <remarks>
    /// <c>STUDIO_LOOPING</c>, which <c>studio.h</c> documents as "ending frame should be the same
    /// as the starting frame". That duplicate is the whole reason this matters: playing every
    /// frame of a looping animation shows one pose twice and stalls for a frame each loop.
    /// </remarks>
    public bool Loops => (Flags & Looping) != 0;

    /// <summary>Whether the sequence's cycle comes from the clock rather than the entity.</summary>
    /// <remarks>
    /// <c>STUDIO_REALTIME</c> (<c>studio.h:3086</c>) — *"cycle index is taken from a real-time
    /// clock, not the animations cycle index"*. `CalcPoseSingle` acts on it before anything else it
    /// does with a cycle (<c>bone_setup.cpp:1955</c>), DISCARDING what the entity carries rather
    /// than correcting it.
    ///
    /// **Its wrap is a plain truncation, not <c>StudioSequences.ClampCycle</c>.** `cycle - (int)cycle`
    /// ignores <see cref="Loops"/> entirely, so a non-looping realtime sequence still wraps — which
    /// is the one place the two normalisations disagree.
    /// </remarks>
    public bool Realtime => (Flags & RealtimeCycle) != 0;

    /// <summary>Whether this is a name held open for an included model to fill in.</summary>
    /// <remarks>
    /// <c>STUDIO_OVERRIDE</c>, which <c>studio.h</c> describes as "a forward declared sequence
    /// (empty)". A player model declares the name of every sequence it can play with a one-frame
    /// animation behind it, and the real animation arrives with an included model. Treating a
    /// declaration as real resolves every named animation a class has to a single frame.
    /// </remarks>
    public bool IsForwardDeclaration => (Flags & ForwardDeclared) != 0;

    /// <summary>Whether this sequence is a DELTA, meant to be layered rather than played.</summary>
    /// <remarks>
    /// <c>STUDIO_DELTA</c> is <c>0x4</c> (<c>studio.h</c>). A delta sequence carries a difference
    /// from the rest pose, not a pose — the engine adds it on top of whatever is already posed
    /// (<c>AccumulatePose</c>), and playing one as if it were an ordinary sequence gives a skeleton
    /// built from differences with nothing underneath.
    ///
    /// The tell is a bone left at identity where its rest rotation carried something: measured on
    /// <c>c_demo_arms.mdl</c>, whose root is a permutation matrix at one sequence and identity at
    /// another, taking the whole model's up-axis with it.
    /// </remarks>
    public bool IsDelta => (Flags & DeltaSequence) != 0;

    /// <summary>Whether a delta sequence composes AFTER the base — <c>STUDIO_POST</c>.</summary>
    /// <remarks>
    /// **Meaningful only alongside <see cref="IsDelta"/>**, where it chooses which side the scaled
    /// difference is composed on: <c>QuaternionMA( q1, s2, q2, q1 )</c> with it,
    /// <c>QuaternionSM( s2, q2, q1, q1 )</c> without (<c>bone_setup.cpp:1441-1456</c>). Read rather
    /// than assumed, because the two give different rotations and nothing downstream can tell.
    /// </remarks>
    public bool IsPost => (Flags & PostSequence) != 0;

    /// <summary>Whether entering this sequence CUTS rather than cross-fades — <c>STUDIO_SNAP</c>.</summary>
    /// <remarks>
    /// **An authored cut, honoured by emptying the transition queue**:
    /// <c>if ((seqdesc.flags &amp; STUDIO_SNAP) || !bInterpolate) m_animationQueue.RemoveAll()</c>
    /// (<c>sequence_Transitioner.cpp:41</c>). Fading into such a sequence would add a blend the
    /// animator deliberately removed.
    /// </remarks>
    public bool Snaps => (Flags & SnapSequence) != 0;

    /// <summary>Whether this sequence plays on its own, off the clock — <c>STUDIO_AUTOPLAY</c>.</summary>
    /// <remarks>
    /// **The membership test IS the autoplay list.** `studiohdr_t::CountAutoplaySequences` and
    /// `CopyAutoplaySequences` (<c>studio.cpp:658</c>, <c>:672</c>) build the list by walking every
    /// sequence and testing this bit, so nothing is stored on disk to read — which is why adding
    /// the mechanism needed no new parsing.
    ///
    /// **`CalcAutoplaySequences` tests it a second time** on every index the list hands back
    /// (<c>bone_setup.cpp:4478</c>), though its own producer already filtered on it. Reproduced
    /// rather than optimised away: the redundancy is Valve's, and the list is cached per model
    /// while the sequences are not, so the second test is what catches a stale one.
    /// </remarks>
    public bool AutoPlays => (Flags & AutoplaySequence) != 0;

    /// <summary>Whether this sequence runs a local layer pass — <c>STUDIO_LOCAL</c>.</summary>
    /// <remarks>
    /// **It gates the pass, not the layers.** `AddLocalLayers` returns immediately without it
    /// (<c>bone_setup.cpp:2229</c>), so a sequence declaring `STUDIO_AL_LOCAL` autolayers and not
    /// this flag has layers nothing will ever apply. Measured on `c_engineer_arms`: `throw_draw`,
    /// `throw_idle` and `throw_fire` carry both, which is what makes them the real case.
    ///
    /// **It also changes how the sequence is SEEDED.** `AccumulatePose` starts a local sequence
    /// from a fresh bind pose (<c>:2431</c>) rather than from whatever the scratch buffers held.
    /// </remarks>
    public bool HasLocalLayers => (Flags & LocalSequence) != 0;

    /// <summary><c>STUDIO_LOOPING</c> from <c>studio.h</c>.</summary>
    private const int Looping = StudioFlags.SequenceLooping;

    private const int RealtimeCycle = StudioFlags.SequenceRealtime;

    /// <summary><c>STUDIO_OVERRIDE</c> from <c>studio.h</c>.</summary>
    private const int ForwardDeclared = StudioFlags.SequenceForwardDeclared;

    private const int DeltaSequence = StudioFlags.SequenceDelta;

    /// <summary><c>STUDIO_POST</c>, which only a delta sequence uses.</summary>
    private const int PostSequence = StudioFlags.SequencePost;

    /// <summary><c>STUDIO_SNAP</c>, which refuses the cross-fade into this sequence.</summary>
    private const int SnapSequence = StudioFlags.SequenceSnap;

    /// <summary><c>STUDIO_AUTOPLAY</c>, which plays whatever the entity is doing.</summary>
    private const int AutoplaySequence = StudioFlags.SequenceAutoplay;

    /// <summary><c>STUDIO_LOCAL</c>, which turns on the sequence's own layer pass.</summary>
    private const int LocalSequence = StudioFlags.SequenceLocal;
}

/// <summary>
/// A model's sequences, and which animation each one plays.
/// </summary>
/// <remarks>
/// **A demo networks <c>m_nSequence</c>, and that is not an animation index.** A sequence is a
/// layer above: it names one or more animations arranged in a blend grid, and the engine picks
/// between them with pose parameters. Treating the sequence number as an animation index draws
/// some other animation of the same model — motion that looks deliberate and is wrong.
///
/// The lookup is Valve's <c>mstudioseqdesc_t::anim</c>: a short array at <c>animindexindex</c>,
/// indexed <c>y * groupsize[0] + x</c> with both clamped into the group. This reads the corner of
/// that grid, which is the whole of it for an unblended sequence — a health pack bobbing, a door
/// sliding, a capture point's hologram turning.
///
/// **Blends are not resolved, and that is stated rather than hidden.** A blended sequence needs
/// pose parameters the demo does not carry for a prop, and taking the corner is the same choice
/// the engine makes when every parameter is zero.
/// </remarks>
public static class StudioSequences
{
    /// <summary>Most sequences a model may declare, as a guard against a malformed header.</summary>
    private const int MaximumSequences = StudioReaderLimits.Sequences;

    /// <summary>A model is untrusted input; TF2's classes declare about two dozen.</summary>
    private const int MaximumPoseParameters = StudioReaderLimits.PoseParameters;

    /// <summary>Reads a model's sequences.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The sequences in order, so <c>m_nSequence</c> indexes this list directly.</returns>
    public static IReadOnlyList<StudioSequence> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderSequenceIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceIndexOffset..]);

        if (count <= 0 || count > MaximumSequences)
        {
            return [];
        }

        if (at < 0 || (long)at + ((long)count * SequenceStride) > bytes.Length)
        {
            return [];
        }

        List<StudioSequence> sequences = new(count);

        for (int index = 0; index < count; index++)
        {
            int start = at + (index * SequenceStride);
            ReadOnlySpan<byte> sequence = bytes.Slice(start, SequenceStride);

            int flags = BinaryPrimitives.ReadInt32LittleEndian(sequence[SequenceFlagsOffset..]);
            int blends = BinaryPrimitives.ReadInt32LittleEndian(sequence[SequenceAnimationIndexOffset..]);
            int groupX = BinaryPrimitives.ReadInt32LittleEndian(sequence[SequenceGroupSizeOffset..]);
            int groupY = BinaryPrimitives.ReadInt32LittleEndian(sequence[(SequenceGroupSizeOffset + 4)..]);

            // **The offsets are relative to the sequence description, not to the file.** Every
            // index inside a studio structure is measured from the structure itself, which is the
            // convention that bites hardest because a file-relative read still lands on data.
            int table = start + blends;

            sequences.Add(new StudioSequence(
                groupX > 0 && groupY > 0 && table >= 0 && table + 2 <= bytes.Length
                    ? BinaryPrimitives.ReadInt16LittleEndian(bytes[table..])
                    : 0,
                flags,
                StudioStrings.At(
                    bytes, start + BinaryPrimitives.ReadInt32LittleEndian(sequence[SequenceLabelOffset..])),
                GridOf(bytes, sequence, table, groupX, groupY),

                // **The activity's NAME, because the number beside it is not in the file.**
                // studio.h annotates mstudioseqdesc_t.activity "initialized at loadtime to game DLL
                // values", so a model ships szactivitynameindex -- ACT_MP_RUN and the like -- and the
                // game resolves it against its own enum. Reading the number would be reading a slot
                // the compiler left blank for the engine.
                StudioStrings.At(
                    bytes,
                    start + BinaryPrimitives.ReadInt32LittleEndian(
                        sequence[SequenceActivityNameOffset..])),

                BinaryPrimitives.ReadInt32LittleEndian(
                    sequence[SequenceActivityWeightOffset..]),

                // **Read from the whole file, not from the sequence slice**, because `eventindex`
                // points outside the description: the events sit elsewhere in the model and the
                // offset is measured from the sequence's own start. Passing the slice would bound
                // the read to 212 bytes and find nothing.
                StudioEvent.Read(bytes, start),

                // **The COUNT only, and deliberately not the layers themselves.** Reading them
                // would be building `AddSequenceLayers` without deciding to; reading how many
                // exist is a measurement, and the same measurement turned five unimplemented
                // procedural rules into one that matters.
                BinaryPrimitives.ReadInt32LittleEndian(
                    sequence[SequenceAutoLayerCountOffset..])));
        }

        return sequences;
    }

    /// <summary>Reads a sequence's whole blend grid, or null when it has only one animation.</summary>
    /// <remarks>
    /// **Null for the ordinary case on purpose.** A map places thousands of props and almost every
    /// one has a one-by-one grid; allocating a grid object for each would be a per-prop cost for a
    /// structure that says nothing. The single animation is already carried on the sequence itself.
    /// </remarks>
    private static StudioBlendGrid? GridOf(
        ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> sequence,
        int table,
        int groupX,
        int groupY)
    {
        if (groupX <= 0 || groupY <= 0 || (groupX == 1 && groupY == 1))
        {
            return null;
        }

        int cells = groupX * groupY;

        if (table < 0 || table + (cells * 2) > bytes.Length)
        {
            return null;
        }

        int[] animations = new int[cells];

        for (int cell = 0; cell < cells; cell++)
        {
            animations[cell] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(table + (cell * 2))..]);
        }

        return new StudioBlendGrid(
            groupX,
            groupY,
            animations,
            BinaryPrimitives.ReadInt32LittleEndian(sequence[SequenceParameterIndexOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(sequence[(SequenceParameterIndexOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(sequence[SequenceParameterStartOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(sequence[SequenceParameterEndOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(sequence[(SequenceParameterStartOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(sequence[(SequenceParameterEndOffset + 4)..]));
    }

    /// <summary>Every pose parameter a model declares, in the order its sequences index them.</summary>
    /// <param name="file">The whole <c>.mdl</c>.</param>
    /// <returns>The parameters, empty when the model has none.</returns>
    /// <remarks>
    /// <c>numlocalposeparameters</c> and <c>localposeparamindex</c> at 300 and 304, each entry a
    /// <c>mstudioposeparamdesc_t</c>: name index, flags, start, end, loop.
    /// </remarks>
    public static IReadOnlyList<StudioPoseParameter> PoseParameters(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderPoseParameterIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderPoseParameterCountOffset..]);
        int index = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderPoseParameterIndexOffset..]);

        if (count <= 0 || count > MaximumPoseParameters || index <= 0)
        {
            return [];
        }

        List<StudioPoseParameter> parameters = new(count);

        for (int entry = 0; entry < count; entry++)
        {
            int start = index + (entry * PoseParameterStride);

            if (start < 0 || start + PoseParameterStride > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> pose = bytes.Slice(start, PoseParameterStride);

            parameters.Add(new StudioPoseParameter(
                StudioStrings.At(bytes, start + BinaryPrimitives.ReadInt32LittleEndian(pose)),
                BinaryPrimitives.ReadSingleLittleEndian(pose[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(pose[12..]),
                BinaryPrimitives.ReadSingleLittleEndian(pose[16..])));
        }

        return parameters;
    }

    /// <summary>Which frame a cycle lands on.</summary>
    /// <param name="cycle">How far through the sequence, where one is the end.</param>
    /// <param name="frames">How many frames the animation has.</param>
    /// <returns>A frame index inside the animation.</returns>
    /// <remarks>
    /// **Wrapped rather than clamped, because a cycle is a phase.** <c>m_flCycle</c> is
    /// interpolated between packets and a looping interpolation can carry it just past one;
    /// clamping there stalls every looping animation for a frame at its end, which reads as a
    /// stutter rather than as a defect.
    ///
    /// **Cycle exactly one is the end rather than the start**, which is the one value where
    /// wrapping and clamping disagree. A non-looping sequence stops there and must hold its last
    /// pose; a looping one is wrapped below one before it arrives, so nothing reaches this by the
    /// looping route.
    ///
    /// A single-frame animation answers zero without dividing, since <c>frames - 1</c> is the
    /// divisor and a static prop has exactly one frame — that division would send every vertex of
    /// it to NaN and lose the model.
    /// </remarks>
    public static int FrameFor(float cycle, int frames) => FrameFor(cycle, frames, loops: false);

    /// <summary>Which frame a cycle lands on.</summary>
    /// <param name="cycle">How far through the sequence, where one is the end.</param>
    /// <param name="frames">How many frames the animation has.</param>
    /// <param name="loops">Whether the sequence loops, from <c>STUDIO_LOOPING</c>.</param>
    /// <returns>A frame index inside the animation.</returns>
    /// <remarks>
    /// **A looping animation has one fewer distinct pose than it has frames.** <c>studio.h</c>
    /// says so directly: <c>STUDIO_LOOPING</c> means "ending frame should be the same as the
    /// starting frame". Playing all of them therefore draws one pose twice in a row, which is a
    /// single frame of hesitation once per loop - measured on cp_process's ammo boxes, which
    /// stalled briefly after every rotation.
    ///
    /// So a loop is mapped onto <c>frames - 1</c> poses and wraps, while a one-shot sequence keeps
    /// its final frame. A door opening genuinely ends on its last frame and must hold it; dropping
    /// that for everything would leave every door a frame short of shut.
    ///
    /// **Floored rather than rounded for a loop**, because rounding at the top of the range lands
    /// back on the duplicate this exists to avoid.
    /// </remarks>
    public static int FrameFor(float cycle, int frames, bool loops)
    {
        if (frames <= 1)
        {
            return 0;
        }

        if (!float.IsFinite(cycle))
        {
            return 0;
        }

        // **Exactly one is the END, not the beginning again.** A non-looping sequence finishes at
        // cycle one and has to hold its final pose; wrapping there snaps it back to its first
        // frame for one frame of playback. A looping sequence never reaches one, because the
        // interpolation wraps it below one first - so the two cases do not collide.
        float wrapped = cycle is >= 0f and <= 1f ? cycle : cycle - MathF.Floor(cycle);

        if (!loops)
        {
            return Math.Clamp((int)MathF.Round(wrapped * (frames - 1)), 0, frames - 1);
        }

        int distinct = frames - 1;
        int frame = (int)MathF.Floor(wrapped * distinct);

        // Modulo rather than clamp: cycle exactly one is the start of the next loop, not the
        // duplicate end of this one.
        return ((frame % distinct) + distinct) % distinct;
    }

    /// <summary>Where a cycle lands: the frame, and how far past it.</summary>
    /// <param name="cycle">How far through the sequence, where one is the end.</param>
    /// <param name="frames">How many frames the animation has.</param>
    /// <param name="loops">Whether the sequence loops, from <c>STUDIO_LOOPING</c>.</param>
    /// <returns>The frame, and the fraction from it toward the next.</returns>
    /// <remarks>
    /// **<c>CalcPoseSingle</c>, <c>public/bone_setup.cpp:915</c>**, both lines:
    ///
    /// <code>
    /// float fFrame = cycle * (animdesc.numframes - 1);
    ///
    /// iFrame = (int)fFrame;
    /// s = (fFrame - iFrame);
    /// </code>
    ///
    /// **The fraction is the half this project never had** (B279). Every bone the engine samples is
    /// <c>CalcBoneQuaternion( iFrame, s, … )</c> — a blend of frame <c>iFrame</c> with the next —
    /// so dropping <c>s</c> plays an animation as its authored frames and nothing between them.
    /// That is roughly thirty poses a second against a viewer drawing several hundred, and it is
    /// what stepping is.
    ///
    /// **Truncated, not rounded**, which is what <c>(int)</c> does in C++ and what leaves a
    /// fraction in [0, 1). <see cref="FrameFor(float, int, bool)"/> rounds on its one-shot path, so
    /// the two disagree by half a frame there — this is the one that matches the engine.
    ///
    /// **Never returns a frame the animation does not have**, so a caller may always ask for
    /// <c>Frame + 1</c> clamped to the last: at the end the fraction is zero, so the next frame is
    /// not wanted at all.
    /// </remarks>
    public static (int Frame, float Fraction) FrameAt(float cycle, int frames, bool loops)
    {
        if (frames <= 1 || !float.IsFinite(cycle))
        {
            return (0, 0f);
        }

        float wrapped = cycle is >= 0f and <= 1f ? cycle : cycle - MathF.Floor(cycle);

        int distinct = frames - 1;
        float exact = wrapped * distinct;

        int frame = (int)exact;

        if (frame >= distinct)
        {
            // **The end, and the two kinds reach it differently.** A one-shot holds its last pose
            // there; a loop never arrives, because `ClampCycle` wraps it below one first. Either
            // way there is no next frame to blend toward.
            return (loops ? 0 : distinct, 0f);
        }

        return (frame, exact - frame);
    }

    /// <summary>Brings an advanced cycle back into range, wrapping only if the sequence loops.</summary>
    /// <param name="cycle">How far through the sequence, advanced and possibly past the end.</param>
    /// <param name="loops">Whether the sequence loops, from <c>STUDIO_LOOPING</c>.</param>
    /// <returns>A cycle inside the sequence.</returns>
    /// <remarks>
    /// **<c>C_BaseAnimating::ClampCycle</c>, <c>client/c_baseanimating.cpp:1431</c>:**
    ///
    /// <code>
    ///   if (isLooping)
    ///   {
    ///       flCycle -= (int)flCycle;
    ///       if (flCycle &lt; 0.0f) { flCycle += 1.0f; }
    ///   }
    ///   else
    ///   {
    ///       flCycle = clamp( flCycle, 0.0f, 0.999f );
    ///   }
    /// </code>
    ///
    /// **This has to happen where the cycle is ADVANCED, not where the frame is chosen.**
    /// <see cref="FrameFor(float, int, bool)"/> holds a one-shot sequence's final pose correctly and takes the loop
    /// flag to do it — but a caller that has already wrapped the cycle into [0,1) has destroyed the
    /// only evidence that the sequence ended, so the branch can never run. Two callers did exactly
    /// that, both spelled <c>advanced - Math.Floor(advanced)</c>, which is the looping case applied
    /// to everything. Honouring a flag one layer too late looks exactly like honouring it.
    ///
    /// Measured symptom: `models/props_gameplay/resupply_locker.mdl` carries `idle`, `open` and
    /// `close`, all of them <c>flags 0x0</c>, and the spawn cabinet opened and shut for ever.
    ///
    /// **<c>0.999</c> rather than <c>1</c> is Valve's, and it is kept** (D89). It lands on the last
    /// frame through <see cref="FrameFor(float, int, bool)"/> exactly as one does, so the choice is invisible here —
    /// which is precisely why it should be theirs rather than ours.
    ///
    /// **The negative cases are not symmetric.** A looping cycle below zero wraps forward, because
    /// C's <c>(int)</c> truncates toward zero and the guard then adds one; a one-shot cycle below
    /// zero clamps to the START, because a sequence that has not begun holds its first pose.
    /// </remarks>
    public static float ClampCycle(float cycle, bool loops)
    {
        if (!loops)
        {
            return Math.Clamp(cycle, 0f, EndOfOneShotCycle);
        }

        float wrapped = cycle - (int)cycle;

        return wrapped < 0f ? wrapped + 1f : wrapped;
    }

    /// <summary>Valve's ceiling for a one-shot sequence that has finished.</summary>
    private const float EndOfOneShotCycle = 0.999f;
}
