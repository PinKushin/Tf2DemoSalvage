using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone's position and rotation at one frame.</summary>
/// <param name="Bone">Which bone this moves.</param>
/// <param name="Position">Where it sits relative to its parent.</param>
/// <param name="Rotation">How it is turned relative to its parent.</param>
public readonly record struct StudioBonePose(
    int Bone,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z, float W) Rotation);

/// <summary>
/// A model's animation data, decoded to bone poses.
/// </summary>
/// <remarks>
/// **Without this a player model lies on its side, and that is not a bug in the reader.** A TF2
/// player's vertices are stored in the model's rest pose, and that rest pose is genuinely lying
/// along Y — measured as 84 units on Y against 25 on Z, where a player is 83 units tall. Applying
/// the rest skeleton changes the vertices by nothing at all, because <c>BoneToWorld</c> is the
/// exact inverse of <c>poseToBone</c> there. Only an animation moves a bone off its rest position,
/// so only an animation stands the model up.
///
/// **The layout is Valve's, from <c>studio.h</c> and <c>bone_setup.cpp</c>.** Each bone in an
/// animation is an <c>mstudioanim_t</c> — one byte of bone index, one of flags, a short offset to
/// the next, then payload — and the flags say how the payload is stored:
///
/// <code>
///   STUDIO_ANIM_RAWPOS   0x01   Vector48, three float16s
///   STUDIO_ANIM_RAWROT   0x02   Quaternion48, x:16 y:16 z:15 wneg:1
///   STUDIO_ANIM_ANIMPOS  0x04   run-length encoded, scaled by the bone's posscale
///   STUDIO_ANIM_ANIMROT  0x08   run-length encoded, scaled by the bone's rotscale
///   STUDIO_ANIM_DELTA    0x10   relative to the rest pose rather than absolute
///   STUDIO_ANIM_RAWROT2  0x20   Quaternion64
/// </code>
///
/// A bone naming neither form for a channel keeps its rest value, which is why an animation that
/// only turns an elbow is a handful of bytes.
///
/// **Which of those paths is actually exercised was measured, and it is one of them.** TF2's nine
/// player models pose exactly ONE bone at frame zero of their first animation — the root — and it
/// carries <c>STUDIO_ANIM_RAWROT2</c>, a <c>Quaternion64</c>. Everything else inherits, which is
/// why a single root transform stands the whole skeleton up.
///
/// So <c>Quaternion48</c>, the Euler run-length path and the run-length fallback past
/// <c>valid</c> are all written from <c>studio.h</c> and <c>bone_setup.cpp</c> and are **not
/// covered by any model this project loads**. Sabotaging each of them leaves every test green.
/// That is stated rather than hidden because the obvious reading of a green suite is that all of
/// this is verified, and only the Quaternion64 path is: breaking it fails the three posed-model
/// tests and nothing else. Any future model that takes another path is unproven code on first
/// contact.
/// </remarks>
public static class StudioAnimation
{
    private const int RawPosition = StudioFlags.AnimationRawPosition;
    private const int RawRotation = StudioFlags.AnimationRawRotation;
    private const int AnimatedPosition = StudioFlags.AnimationAnimatedPosition;
    private const int AnimatedRotation = StudioFlags.AnimationAnimatedRotation;
    private const int Delta = StudioFlags.AnimationDelta;
    private const int RawRotation64 = StudioFlags.AnimationRawRotation64;

    /// <summary>How many animations a model declares.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The count, or zero when the file is too short to say.</returns>
    public static int Count(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < HeaderAnimationIndexOffset + 4
            ? 0
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationCountOffset..]));
    }

    /// <summary>How many frames one animation has.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <returns>The frame count, or zero when the animation does not exist.</returns>
    /// <remarks>
    /// **Needed to turn a cycle into a frame**, since a cycle is a fraction of the whole sequence
    /// and means nothing without knowing how long that is. One frame means the model does not
    /// animate at all, which is worth being able to state rather than infer from a still picture.
    /// </remarks>
    public static int Frames(ReadOnlyMemory<byte> file, int animation)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file))
        {
            return 0;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        return at < 0 || at + AnimationStride > bytes.Length
            ? 0
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + AnimationFrameCountOffset)..]));
    }

    /// <summary>An animation's own name, such as <c>a_runN_PRIMARY</c>.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which animation, by index within this file.</param>
    /// <returns>Its name, or empty.</returns>
    /// <remarks>
    /// **For telling one kind of nothing from another.** A census reporting that no animation
    /// playing asks for IK is either honest — the rules sit on poses nobody is in — or a sequence
    /// that resolved to the wrong animation, and the two are indistinguishable from counts alone.
    /// The name is what separates `a_runN` from a taunt pose.
    ///
    /// <c>sznameindex</c> is four bytes in, relative to the animation description as every index in
    /// this format is.
    /// </remarks>
    public static string Name(ReadOnlyMemory<byte> file, int animation)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file))
        {
            return string.Empty;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return string.Empty;
        }

        int name = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);

        return StudioStrings.At(bytes, at + name);
    }

    /// <summary>How many IK rules an animation carries — <c>numikrules</c>.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which animation, by index within this file.</param>
    /// <returns>Its rule count, or zero.</returns>
    /// <remarks>
    /// **A chain and a rule are different claims and the difference decides the work.** A model's
    /// `mstudioikchain_t` says a limb CAN be solved; a rule inside an animation is what asks for it
    /// at a given cycle — `AccumulatePose` and `CalcAutoplaySequences` only ever reach the solver
    /// through rules. Every TF2 player model declares four chains (B296), which says nothing at all
    /// about whether any animation asks.
    ///
    /// **The count only, deliberately**, so the question can be measured before the solver is
    /// built. Reading `mstudioikrule_t` itself would be building it without having decided to,
    /// which is the order that turned five procedural rules into one and one autolayer pass into
    /// two.
    ///
    /// **`animblockikruleindex` is a second home for the same data** and is not read here: rules can
    /// live in an external animation block instead of the model. A count of zero from a model whose
    /// animations are blocked would be a fact about where the bytes are, so the caller has to say
    /// which it measured.
    /// </remarks>
    public static int IkRules(ReadOnlyMemory<byte> file, int animation)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file))
        {
            return 0;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        return at < 0 || at + AnimationStride > bytes.Length
            ? 0
            : Math.Max(
                0, BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + AnimationIkRuleCountOffset)..]));
    }

    /// <summary>What an animation carries that this reader does not implement.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <returns>Its local-hierarchy and zero-frame counts, both zero when it uses neither.</returns>
    /// <remarks>
    /// **Two mechanisms sit between an animation's bone tracks and the pose the engine ends up
    /// with, and neither is implemented here.** <c>CalcZeroframeData</c> fills bones the animation
    /// does not mention from a compressed span table, and <c>CalcLocalHierarchyAnimation</c>
    /// reparents a bone for the duration of the animation (<c>bone_setup.cpp:990</c>).
    ///
    /// This exists so the question "does the animation we are posing actually use them" can be
    /// answered before anyone implements either. An unimplemented mechanism that the data never
    /// exercises is not a bug, and this project has spent whole sessions on the difference.
    /// </remarks>
    public static (int LocalHierarchy, int ZeroFrames) Unimplemented(
        ReadOnlyMemory<byte> file, int animation)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file))
        {
            return (0, 0);
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return (0, 0);
        }

        return (
            BinaryPrimitives.ReadInt32LittleEndian(
                bytes[(at + StudioLayout.AnimationLocalHierarchyCountOffset)..]),
            BinaryPrimitives.ReadInt16LittleEndian(
                bytes[(at + StudioLayout.AnimationZeroFrameCountOffset)..]));
    }

    /// <summary>How many cycles a second an animation advances at.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <returns>Cycles per second, or zero when it cannot advance.</returns>
    /// <remarks>
    /// **Valve's <c>GetSequenceCycleRate</c>, and it is why a health pack looks static without
    /// it.** The server does not send a cycle every tick; the client advances its own every frame
    /// in <c>C_BaseAnimating::FrameAdvance</c> — <c>addcycle = flInterval * cyclerate *
    /// m_flPlaybackRate</c> — and treats any networked value as an occasional correction rather
    /// than as the source. A viewer that only replays the networked cycle therefore sees it never
    /// change, which is exactly what was measured: every prop reporting cycle zero forever.
    ///
    /// A cycle spans the whole animation, so the rate is its frames per second divided by the
    /// intervals between its frames — one fewer than the frame count.
    /// </remarks>
    public static float CyclesPerSecond(ReadOnlyMemory<byte> file, int animation)
    {
        int frames = Frames(file, animation);

        if (frames <= 1 || animation < 0 || animation >= Count(file))
        {
            return 0f;
        }

        ReadOnlySpan<byte> bytes = file.Span;

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return 0f;
        }

        float fps = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + AnimationFramesPerSecondOffset)..]);

        return float.IsFinite(fps) && fps > 0f ? fps / (frames - 1) : 0f;
    }
    /// <summary>One animation's own flags — <c>mstudioanimdesc_t.flags</c>.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which animation.</param>
    /// <returns>Its flags, or zero when it cannot be read.</returns>
    /// <remarks>
    /// **Not the SEQUENCE's flags, and the difference decides how a layer composes.**
    /// <c>CalcVirtualAnimation</c> (<c>bone_setup.cpp:933</c>) tests
    /// <c>animdesc.flags &amp; STUDIO_DELTA</c> — the ANIMATION's — to decide whether a bone is
    /// seeded to identity or to the sequence model's bind pose:
    ///
    /// <code>
    ///   if (animdesc.flags &amp; STUDIO_DELTA) { q[i].Init( 0,0,0,1 ); pos[i].Init( 0,0,0 ); }
    ///   else                                { q[i] = pSeqbone[j].quat; pos[i] = pSeqbone[j].pos; }
    /// </code>
    ///
    /// **Reading the sequence's flags instead is how B284 stayed unsolved for an hour.** A TF2
    /// player's gesture sequences carry <c>STUDIO_HIDDEN</c> and no delta bit, while the animations
    /// behind them are additive — so "the layer is not a delta" was true of the field being read
    /// and false of the thing that matters.
    /// </remarks>
    public static int Flags(ReadOnlyMemory<byte> file, int animation)
    {
        if (animation < 0 || animation >= Count(file))
        {
            return 0;
        }

        ReadOnlySpan<byte> bytes = file.Span;

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        return at < 0 || at + AnimationStride > bytes.Length
            ? 0
            : BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + AnimationFlagsOffset)..]);
    }

    /// <summary>Whether one animation is additive — <c>animdesc.flags &amp; STUDIO_DELTA</c>.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which animation.</param>
    /// <returns>Whether it carries the delta bit.</returns>
    /// <remarks>
    /// Here rather than at the caller because <c>StudioFlags</c> is this assembly's, and the bit
    /// is the same value the sequence flag uses (<c>studio.h:3080</c>, <c>0x0004</c>).
    /// </remarks>
    public static bool IsDelta(ReadOnlyMemory<byte> file, int animation) =>
        (Flags(file, animation) & StudioFlags.SequenceDelta) != 0;


    /// <summary>Reads one animation's bone poses at one frame.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="bones">The skeleton, for rest values and compression scales.</param>
    /// <param name="animation">Which local animation to read.</param>
    /// <param name="frame">Which frame of it.</param>
    /// <returns>A pose per bone the animation touches; bones it does not touch are absent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    /// <remarks>
    /// **One frame, not a blend between two.** Valve interpolates between frame and frame+1 by a
    /// fraction <c>s</c>, and takes a cheaper path when <c>s</c> is under a thousandth
    /// (<c>bone_setup.cpp:407</c>); this is that path. Between-frame blending belongs with playback
    /// speed, which is a separate question from whether the model stands up.
    ///
    /// **Animations stored in a block are skipped rather than guessed at.** A non-zero
    /// <c>animblock</c> means the data lives outside the <c>.mdl</c>, in an <c>.ani</c> file this
    /// does not read yet. Returning nothing says so; inventing a pose from the rest skeleton would
    /// draw a confidently wrong model.
    /// </remarks>
    public static IReadOnlyList<StudioBonePose> Pose(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int animation,
        int frame)
    {
        ArgumentNullException.ThrowIfNull(bones);

        List<StudioBonePose> poses = [];

        PoseInto(file, bones, animation, frame, poses, []);

        return poses;
    }

    /// <summary><see cref="Pose"/> into storage the caller owns (finding 88).</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="bones">The skeleton, for rest values and compression scales.</param>
    /// <param name="animation">Which local animation to read.</param>
    /// <param name="frame">Which frame of it.</param>
    /// <param name="poses">Receives what <see cref="Pose"/> returns; cleared first.</param>
    /// <param name="chain">Holds the animation's bone chain while it is read; cleared first.</param>
    /// <returns><paramref name="poses"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static List<StudioBonePose> PoseInto(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int animation,
        int frame,
        List<StudioBonePose> poses,
        List<(int Bone, int Flags, int At)> chain)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(chain);

        poses.Clear();
        chain.Clear();

        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file) || bones.Count == 0)
        {
            return poses;
        }

        // **A delta's absent channel is "no difference", not the rest pose** (B284): an identity rotation and a zero
        // offset. Seeding from the rest instead stretched every limb by its own rest offset (a scout mid-reload, measured).
        // The bone a track does not name is seeded by the ANIMATION's flag (`CalcVirtualAnimation`, `:933`), downstream;
        // a channel a track does not carry by the TRACK's (`CalcBoneQuaternion`, `:395`; `CalcBonePosition`, `:511`), here.
        Chain(bytes, bones, animation, frame, chain, out int wanted);

        poses.EnsureCapacity(chain.Count);

        foreach ((int bone, int flags, int payload) in chain)
        {
            poses.Add(ReadBone(bytes[payload..], bones[bone], bone, flags, wanted));
        }

        return poses;
    }

    /// <summary>The engine's <c>s > 0.001f</c>: at or below it a track reads its frame alone (B614).</summary>
    internal const float BlendThreshold = 0.001f;

    /// <summary>One animation sampled BETWEEN two of its frames, as the engine samples it, in storage the caller owns.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="group">
    /// How the animation's model lands on the posed skeleton: its bones for each track's rest values and scales, the track
    /// map, and the seeds the result is sized and seeded from and whose fixed-alignment flags the blend reads (B621).
    /// </param>
    /// <param name="animation">Which local animation.</param>
    /// <param name="frame">The frame the cycle landed on.</param>
    /// <param name="fraction">How far from it toward the next, the engine's <c>s</c>.</param>
    /// <param name="workspace">The skeleton's pose storage the result is built in.</param>
    /// <param name="slot">Which grid corner this is, so two corners never share storage.</param>
    /// <returns>
    /// Empty when the frame names no bone. Otherwise one entry per seed: the frame alone when <paramref name="fraction"/>
    /// is not above <see cref="BlendThreshold"/> or <paramref name="frame"/> is the animation's last, else the blend of
    /// the frame toward the next. Every bone the frame does not name holds <c>CalcVirtualAnimation</c>'s seed, so a non-delta layer pulls
    /// it toward the bind as the engine's does (<c>bone_setup.cpp:933-947</c>). Valid until the next call with
    /// <paramref name="workspace"/> and <paramref name="slot"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<c>CalcPoseSingle</c> never samples one frame** (<c>bone_setup.cpp:915</c>): every bone is
    /// <c>CalcBoneQuaternion( iFrame, s, … )</c>, a blend of that frame with the next, so an animation plays between
    /// its authored frames rather than stepping (B279).
    ///
    /// **One section, one walk, one blend per track, as Valve's** (B620). <c>CalcAnimation</c> resolves the section once,
    /// from the frame (<c>bone_setup.cpp:1063</c>, <c>studio.cpp:84-96</c>), walks the <c>mstudioanim_t</c> chain once
    /// (<c>:1095-1109</c>, <c>:964-981</c> for a virtual model), and each track blends its two keys itself
    /// (<see cref="BlendBone"/>). The frame after is read from the FRAME's section stream, at <c>local + 1</c>, never
    /// from its own section; there is no second pass. A bone no track names keeps its seed, unblended.
    /// </remarks>
    internal static List<StudioBonePose> PoseBetweenInto(
        ReadOnlyMemory<byte> file,
        StudioRemap group,
        int animation,
        int frame,
        float fraction,
        StudioPoseWorkspace workspace,
        int slot)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(workspace);

        IReadOnlyList<StudioBone> bones = group.Bones;
        int[]? remap = group.Tracks;
        IReadOnlyList<StudioBone> seeds = group.Seeds;
        List<StudioBonePose> pose = workspace.Frame[slot];

        // Valve blends only when `s > 0.001f` (`bone_setup.cpp:408`, `:525`); at or below it each track reads the frame
        // alone (B614). Written as the engine's test negated, so a NaN reads the frame alone as it does there.
        //
        // **At or past the animation's last frame the engine's `s` is zero**: `fFrame = cycle * (numframes - 1)` reaches
        // the last frame only at a cycle of one (`:913-917`). A caller that hands this animation another's frame can
        // land here with a fraction (B742); the frame after would be read past the stream, so the frame alone is read.
        if (!(fraction > BlendThreshold) || frame >= Frames(file, animation) - 1)
        {
            return Seeded(file, group, animation, PoseOfInto(file, bones, remap, animation, frame, workspace, pose), workspace, slot);
        }

        ReadOnlySpan<byte> bytes = file.Span;

        // Stryker disable once all : a mutant turning an || into && skips Locate, leaves 'cursor' and 'first' unassigned (CS0165), and Safe Mode drops the method - B410
        if (animation < 0 || animation >= Count(file) || bones.Count == 0 ||
            !Locate(bytes, animation, frame, out int cursor, out int first))
        {
            pose.Clear();
            return pose;
        }

        // **The section is resolved once, from the frame** (`studio.cpp:84-96`, `bone_setup.cpp:1063`), and both keys are
        // read from that one stream at `local` and `local + 1` (B620): never from the next section's own.
        List<(int Bone, int Flags, int At)> chain = workspace.Chain;

        chain.Clear();
        Walk(bytes, bones.Count, cursor, chain);

        List<StudioBonePose> between = workspace.Between[slot];

        CollectionsMarshal.SetCount(between, seeds.Count);

        Span<StudioBonePose> blended = CollectionsMarshal.AsSpan(between);

        group.SeedInto(IsDelta(file, animation), blended);

        int named = 0;

        foreach ((int bone, int flags, int payload) in CollectionsMarshal.AsSpan(chain))
        {
            int target = Renumbered(bone, remap);

            if (target < 0)
            {
                continue;
            }

            named++;

            if (target < blended.Length)
            {
                blended[target] = BlendBone(bytes[payload..], bones[bone], target, flags, first, fraction);
            }
        }

        if (named == 0)
        {
            pose.Clear();
            return pose;
        }

        return between;
    }

    /// <summary>
    /// One track sampled between its frame and the next, as <c>CalcBoneQuaternion</c> and <c>CalcBonePosition</c> sample
    /// it when <c>s &gt; 0.001</c> (<c>bone_setup.cpp:374-474</c>, <c>:498-550</c>).
    /// </summary>
    /// <param name="payload">The track's payload.</param>
    /// <param name="rest">The bone the track is numbered against, in its own model.</param>
    /// <param name="bone">The index to stamp on the pose.</param>
    /// <param name="flags">The track's <c>STUDIO_ANIM_*</c> flags.</param>
    /// <param name="frame">The frame within the chain's own data; its successor is read from the same stream.</param>
    /// <param name="s">How far toward the next frame.</param>
    /// <returns>The blended pose.</returns>
    /// <remarks>
    /// **The blend is inside the track, as the engine's is (B620)**:
    /// <list type="bullet">
    /// <item>A raw rotation or position is the value; nothing blends it (<c>:379-391</c>, <c>:502-507</c>).</item>
    /// <item>An absent channel is the seed, chosen by the TRACK's delta flag (<c>:393-404</c>, <c>:509-520</c>).</item>
    /// <item>Equal angles convert once, unblended. Unequal ones convert each and go through <c>QuaternionBlend</c>,
    /// compared as floats so <c>-0</c> equals <c>+0</c> (<c>:428-446</c>). A fixed-alignment bone aligns AFTER
    /// (<c>:470-473</c>).</item>
    /// <item>A position channel is <c>v1 * (1.0 - s) + v2 * s</c>, in double for the <c>1.0</c>, stored as a float,
    /// with the base added after (<c>:531</c>, <c>:542-547</c>).</item>
    /// </list>
    /// Expanding two whole poses and blending them, as this did, gave each of these other bits.
    /// </remarks>
    private static StudioBonePose BlendBone(
        ReadOnlySpan<byte> payload, in StudioBone rest, int bone, int flags, int frame, float s)
    {
        bool delta = (flags & Delta) != 0;

        (float X, float Y, float Z, float W) rotation = delta ? (0f, 0f, 0f, 1f) : rest.Rotation;

        if ((flags & RawRotation) != 0 && payload.Length >= 6)
        {
            rotation = Quaternion48(payload);
        }
        else if ((flags & RawRotation64) != 0 && payload.Length >= 8)
        {
            rotation = Quaternion64(payload);
        }
        else if ((flags & AnimatedRotation) != 0)
        {
            (float x1, float x2) = ValuePair(payload, 0,0, frame, rest.RotationScale.X);
            (float y1, float y2) = ValuePair(payload, 0,1, frame, rest.RotationScale.Y);
            (float z1, float z2) = ValuePair(payload, 0,2, frame, rest.RotationScale.Z);

            if (!delta)
            {
                x1 += rest.Euler.X;
                y1 += rest.Euler.Y;
                z1 += rest.Euler.Z;
                x2 += rest.Euler.X;
                y2 += rest.Euler.Y;
                z2 += rest.Euler.Z;
            }

#pragma warning disable S1244 // Valve's own exact comparison (`bone_setup.cpp:428`): -0 equals +0, a NaN differs.
            rotation = Aligned(
                x1 != x2 || y1 != y2 || z1 != z2
                    ? Core.Scene.ValveQuaternion.Blend(FromEulerRadians(x1, y1, z1), FromEulerRadians(x2, y2, z2), s)
                    : FromEulerRadians(x1, y1, z1),
                rest,
                flags);
#pragma warning restore S1244
        }

        (float X, float Y, float Z) position = delta ? (0f, 0f, 0f) : rest.Position;

        if ((flags & RawPosition) != 0 && payload.Length >= RawPositionAt(flags) + 6)
        {
            position = Vector48(payload[RawPositionAt(flags)..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
            ReadOnlySpan<byte> from = PositionChannels(payload, flags);

            position = Based(
                (Between(ValuePair(from, 0,0, frame, rest.PositionScale.X), s),
                 Between(ValuePair(from, 0,1, frame, rest.PositionScale.Y), s),
                 Between(ValuePair(from, 0,2, frame, rest.PositionScale.Z), s)),
                rest,
                flags);
        }

        return new StudioBonePose(bone, position, rotation);
    }

    /// <summary>
    /// Where a raw position sits: after a raw <c>Quaternion48</c>, a raw <c>Quaternion64</c>, or both, as
    /// <c>mstudioanim_t::pPos</c> sums them (<c>studio.h:683</c>).
    /// </summary>
    private static int RawPositionAt(int flags) =>
        ((flags & RawRotation) != 0 ? 6 : 0) + ((flags & RawRotation64) != 0 ? 8 : 0);

    /// <summary><c>v1 * (1.0 - s) + v2 * s</c> as the engine evaluates it: the <c>1.0</c> is a double (<c>bone_setup.cpp:531</c>).</summary>
    private static float Between((float First, float Second) values, float s) =>
        (float)((values.First * (1.0 - s)) + (values.Second * s));

    /// <summary>A frame read alone, expanded over the group's seeds; an empty one as it is.</summary>
    /// <remarks>
    /// <c>CalcVirtualAnimation</c> seeds every bone before the tracks land (<c>bone_setup.cpp:927-954</c>), so a bone the
    /// frame does not name holds its seed even when the frame is read alone. A sparse pose would leave that bone to be
    /// expanded downstream over the BASE skeleton's rest, and a layer would skip it (B621). The frame is read into
    /// <c>Frame</c>, so <c>Between</c> is free to hold it.
    /// </remarks>
    private static List<StudioBonePose> Seeded(
        ReadOnlyMemory<byte> file,
        StudioRemap group,
        int animation,
        List<StudioBonePose> pose,
        StudioPoseWorkspace workspace,
        int slot) =>
        pose.Count == 0 ? pose : group.ExpandInto(IsDelta(file, animation), pose, workspace.Between[slot]);

    /// <summary>One frame of an animation, renumbered onto the base skeleton when <paramref name="remap"/> is given.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="bones">The skeleton the animation is numbered against.</param>
    /// <param name="remap">Its numbering onto the base model's, or null for the base model's own.</param>
    /// <param name="animation">Which local animation.</param>
    /// <param name="frame">Which frame.</param>
    /// <param name="workspace">The skeleton's pose storage.</param>
    /// <param name="into">Receives the pose, sparse; cleared first.</param>
    /// <returns><paramref name="into"/>.</returns>
    /// <remarks>
    /// **The animation's bones are ITS model's, and must be renumbered.** An animation model has its own bone list and
    /// ordering; applying those indices to the base skeleton moves the wrong joints by the right amounts. Valve remap
    /// every animation through <c>masterBone</c> for exactly this, <c>bone_setup.cpp:966</c>.
    /// </remarks>
    internal static List<StudioBonePose> PoseOfInto(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        StudioPoseWorkspace workspace,
        List<StudioBonePose> into)
    {
        if (remap is null)
        {
            return PoseInto(file, bones, animation, frame, into, workspace.Chain);
        }

        List<StudioBonePose> pose = PoseInto(file, bones, animation, frame, workspace.Raw, workspace.Chain);

        into.Clear();

        foreach (StudioBonePose moved in pose)
        {
            int bone = Renumbered(moved.Bone, remap);

            if (bone >= 0)
            {
                into.Add(moved with { Bone = bone });
            }
        }

        return into;
    }

    /// <summary>A track's bone in the base model's numbering: itself without a remap, -1 when the remap drops it.</summary>
    private static int Renumbered(int bone, int[]? remap)
    {
        if (remap is null)
        {
            return bone;
        }

        return bone >= 0 && bone < remap.Length ? remap[bone] : -1;
    }

    private static StudioBonePose ReadBone(
        ReadOnlySpan<byte> payload,
        StudioBone rest,
        int bone,
        int flags,
        int frame)
    {
        // An absent channel's seed follows the TRACK's delta flag (`bone_setup.cpp:395`, `:511`), not the animation's.
        bool delta = (flags & Delta) != 0;

        (float X, float Y, float Z, float W) rotation =
            delta ? (0f, 0f, 0f, 1f) : rest.Rotation;

        if ((flags & RawRotation) != 0 && payload.Length >= 6)
        {
            rotation = Quaternion48(payload);
        }
        else if ((flags & RawRotation64) != 0 && payload.Length >= 8)
        {
            rotation = Quaternion64(payload);
        }
        else if ((flags & AnimatedRotation) != 0)
        {
            (float x, float y, float z) = Angles(
                Value(payload, 0, frame), Value(payload, 1, frame), Value(payload, 2, frame), rest, flags);

            rotation = Aligned(FromEulerRadians(x, y, z), rest, flags);
        }

        (float X, float Y, float Z) position = delta ? (0f, 0f, 0f) : rest.Position;

        if ((flags & RawPosition) != 0 && payload.Length >= RawPositionAt(flags) + 6)
        {
            position = Vector48(payload[RawPositionAt(flags)..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
            ReadOnlySpan<byte> from = PositionChannels(payload, flags);

            position = Based(
                (Value(from, 0, frame) * rest.PositionScale.X,
                 Value(from, 1, frame) * rest.PositionScale.Y,
                 Value(from, 2, frame) * rest.PositionScale.Z),
                rest,
                flags);
        }

        return new StudioBonePose(bone, position, rotation);
    }

    /// <summary>
    /// **The position channels sit after the rotation channels when both are animated**, which is what pPosV encodes
    /// by advancing one mstudioanim_valueptr_t.
    /// </summary>
    private static ReadOnlySpan<byte> PositionChannels(ReadOnlySpan<byte> payload, int flags) =>
        (flags & AnimatedRotation) != 0 && payload.Length >= 6 ? payload[6..] : payload;

    /// <summary>Three raw run-length values as the Euler angle they encode, in radians.</summary>
    /// <remarks>
    /// Scaled, then added to the bone's rest rotation - unless the track is a delta, where the rest value is not a base.
    /// </remarks>
    private static (float X, float Y, float Z) Angles(float x, float y, float z, in StudioBone rest, int flags)
    {
        x *= rest.RotationScale.X;
        y *= rest.RotationScale.Y;
        z *= rest.RotationScale.Z;

        if ((flags & Delta) == 0)
        {
            x += rest.Euler.X;
            y += rest.Euler.Y;
            z += rest.Euler.Z;
        }

        return (x, y, z);
    }

    /// <summary>A scaled position channel triple with the bone's base added, unless the track is a delta (<c>:542-547</c>).</summary>
    private static (float X, float Y, float Z) Based((float X, float Y, float Z) scaled, in StudioBone rest, int flags) =>
        (flags & Delta) == 0
            ? (scaled.X + rest.Position.X, scaled.Y + rest.Position.Y, scaled.Z + rest.Position.Z)
            : scaled;

    /// <summary>An animated rotation as <c>CalcBoneQuaternion</c> ends with it: aligned for a fixed-alignment bone.</summary>
    private static (float X, float Y, float Z, float W) Aligned(
        (float X, float Y, float Z, float W) rotation, in StudioBone rest, int flags)
    {
        // **`CalcBoneQuaternion`'s last line** (`bone_setup.cpp:470`), and it belongs to this
        // branch alone — `STUDIO_ANIM_RAWROT`, `RAWROT2` and the no-`ANIMROT` case all `return`
        // before reaching it:
        //
        //   // align to unified bone
        //   if (!(panim->flags & STUDIO_ANIM_DELTA) && (iBaseFlags & BONE_FIXED_ALIGNMENT))
        //       QuaternionAlign( baseAlignment, q, q );
        //
        // **This is the half that makes the other two halves safe** (B308). A quaternion and
        // its negation are the same rotation, so a blend has to pick one; settling that HERE,
        // once, against the bone's own `qAlignment`, is what lets `SlerpBones` and `BlendBones`
        // then use their `NoAlign` variants. We had the `NoAlign` slerp without this, which is
        // the worse half of the mechanism to have alone: neither end aligning means an
        // antipodal pair blends the long way round.
        return (flags & Delta) == 0 && (rest.Flags & StudioBoneFlags.FixedAlignment) != 0
            ? StudioBones.Align(rest.Alignment, rotation)
            : rotation;
    }

    /// <summary>How an animation's data is laid out: its frames, and whether it is sectioned.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <returns>Frame count, frames per section (zero when unsectioned), and the section table
    /// offset, and <c>animblock</c>/<c>animindex</c> as the descriptor states them.</returns>
    /// <remarks>
    /// Introspection, like <see cref="Unimplemented"/> and <see cref="Tracks"/>: which layout a
    /// given animation uses decides where a wrong value could have come from, and inferring it from
    /// frame counts is guessing.
    /// </remarks>
    public static (int Frames, int SectionFrames, int Sections, int Block, int Data) Sectioning(
        ReadOnlyMemory<byte> file, int animation)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file))
        {
            return (0, 0, 0, 0, 0);
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return (0, 0, 0, 0, 0);
        }

        ReadOnlySpan<byte> description = bytes[at..];

        return (
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationFrameCountOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationSectionFramesOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationSectionIndexOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationBlockOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationDataOffset..]));
    }

    /// <summary>The bone entries of one animation, with where each one's payload starts.</summary>
    /// <remarks>
    /// **Extracted so <see cref="Pose"/> and <see cref="Tracks"/> walk the chain once, in one
    /// place.** The offsets are returned rather than the payload spans because a <c>ReadOnlySpan</c>
    /// cannot live in a list; the caller slices from them.
    /// </remarks>
    private static List<(int Bone, int Flags, int At)> Chain(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<StudioBone> bones,
        int animation,
        int frame,
        List<(int Bone, int Flags, int At)> chain,
        out int wanted)
    {
        wanted = 0;

        chain.Clear();

        // Stryker disable once all : a mutant turning an && into || reaches Walk without Locate, leaves 'cursor' unassigned (CS0165), and Safe Mode drops the method - B410
        if (animation >= 0 && bones.Count > 0 && Locate(bytes, animation, frame, out int cursor, out wanted))
        {
            Walk(bytes, bones.Count, cursor, chain);
        }

        return chain;
    }

    /// <summary>Where one frame's chain starts, and the frame renumbered within its section.</summary>
    /// <param name="bytes">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation; not negative.</param>
    /// <param name="frame">The frame wanted, in whole-animation numbering; clamped to the animation.</param>
    /// <param name="cursor">The first <c>mstudioanim_t</c>'s offset in the file.</param>
    /// <param name="wanted">The frame to read within that chain's data.</param>
    /// <returns>False when the animation has no chain to read: no frames, a bad offset, or data in a block.</returns>
    /// <remarks>
    /// <c>mstudioanimdesc_t::pAnim</c> without the walk: the section is resolved once, from the frame, and a pair reads
    /// both its keys from that chain, as <c>CalcAnimation</c> does (<c>bone_setup.cpp:1063</c>, <c>:1095-1109</c>).
    /// </remarks>
    private static bool Locate(ReadOnlySpan<byte> bytes, int animation, int frame, out int cursor, out int wanted)
    {
        cursor = 0;
        wanted = 0;

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> description = bytes[at..];

        int frames = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationFrameCountOffset..]);
        int block = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationBlockOffset..]);
        int data = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationDataOffset..]);

        if (frames <= 0)
        {
            return false;
        }

        int sectionFrames =
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationSectionFramesOffset..]);

        int sections =
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationSectionIndexOffset..]);

        wanted = Math.Clamp(frame, 0, frames - 1);

        if (sectionFrames > 0 && sections > 0)
        {
            (int section, int local) = Section(frames, sectionFrames, wanted);

            int entry = at + sections + (section * AnimationSectionStride);

            if (entry < 0 || entry + AnimationSectionStride > bytes.Length)
            {
                return false;
            }

            block = BinaryPrimitives.ReadInt32LittleEndian(bytes[entry..]);
            data = BinaryPrimitives.ReadInt32LittleEndian(
                bytes[(entry + AnimationSectionDataOffset)..]);

            wanted = local;
        }

        if (block != 0 || data == 0)
        {
            return false;
        }

        cursor = at + data;

        return true;
    }

    /// <summary>Follows a chain of <c>mstudioanim_t</c> from <paramref name="cursor"/>, the <c>pNext</c> walk.</summary>
    /// <param name="bytes">The <c>.mdl</c>'s bytes.</param>
    /// <param name="boneCount">The skeleton's size; an entry naming a bone past it ends the walk.</param>
    /// <param name="cursor">Where the first entry is, from <see cref="Locate"/>.</param>
    /// <param name="chain">Receives each entry's bone, flags and payload offset, in chain order.</param>
    private static void Walk(
        ReadOnlySpan<byte> bytes, int boneCount, int cursor, List<(int Bone, int Flags, int At)> chain)
    {
        for (int step = 0; step <= boneCount && cursor > 0 && cursor + 4 <= bytes.Length; step++)
        {
            ReadOnlySpan<byte> entry = bytes[cursor..];

            int bone = entry[0];
            int flags = entry[1];
            int next = BinaryPrimitives.ReadInt16LittleEndian(entry[2..]);

            if (bone >= boneCount)
            {
                break;
            }

            chain.Add((bone, flags, cursor + AnimationEntryHeaderBytes));

            if (next == 0)
            {
                break;
            }

            cursor += next;
        }
    }

    /// <summary>Bytes of <c>mstudioanim_t</c> before its payload: bone, flags, nextoffset.</summary>
    private const int AnimationEntryHeaderBytes = 4;

    /// <summary>How each bone's track in an animation is encoded.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="bones">The skeleton the animation is numbered against.</param>
    /// <param name="animation">Which local animation.</param>
    /// <param name="frame">Which frame, since a sectioned animation has a chain per section.</param>
    /// <returns>The bone and its <c>STUDIO_ANIM_*</c> flags, in chain order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    /// <remarks>
    /// **Format introspection, for the same reason <see cref="Unimplemented"/> exists**: the question
    /// "which path does this data actually take" has to be answerable before anyone argues about
    /// whether that path is right. Five of the six mechanisms here are unverified by any test, so
    /// knowing that a file never exercises one is worth as much as implementing it.
    /// </remarks>
    public static IReadOnlyList<(int Bone, int Flags, int Payload)> Tracks(
        ReadOnlyMemory<byte> file, IReadOnlyList<StudioBone> bones, int animation, int frame)
    {
        ArgumentNullException.ThrowIfNull(bones);

        List<(int Bone, int Flags, int Payload)> tracks = [];

        foreach ((int bone, int flags, int at) in Chain(file.Span, bones, animation, frame, [], out _))
        {
            tracks.Add((bone, flags, at));
        }

        return tracks;
    }

    /// <summary>Which section of a long animation holds a frame, and its index within it.</summary>
    /// <param name="frames">The animation's <c>numframes</c>.</param>
    /// <param name="sectionFrames">Its <c>sectionframes</c>, or zero when it is not sectioned.</param>
    /// <param name="frame">The frame wanted, in whole-animation numbering.</param>
    /// <returns>The section to read, and the frame renumbered within that section.</returns>
    /// <remarks>
    /// **Valve's <c>mstudioanimdesc_t::pAnim</c>, <c>public/studio.cpp</c>**, reduced to the
    /// arithmetic — the engine's version also preloads the next block and blends a stall time for
    /// data still streaming in, neither of which applies when the whole file is in memory.
    ///
    /// <code>
    ///   if (sectionframes != 0)
    ///   {
    ///       if (numframes > sectionframes &amp;&amp; *piFrame == numframes - 1)
    ///       {
    ///           // last frame on long anims is stored separately
    ///           *piFrame = 0;
    ///           section = (numframes / sectionframes) + 1;
    ///       }
    ///       else
    ///       {
    ///           section = *piFrame / sectionframes;
    ///           *piFrame -= section * sectionframes;
    ///       }
    ///   }
    /// </code>
    ///
    /// **The trailing-section case is not an optimisation and skipping it misplaces the last frame.**
    /// A long animation stores its final frame on its own, one past the last regular section, which
    /// is why the guard is <c>numframes &gt; sectionframes</c>: an animation shorter than one
    /// section has no separate last frame and must take the ordinary path.
    /// </remarks>
    public static (int Section, int Frame) Section(int frames, int sectionFrames, int frame)
    {
        if (sectionFrames <= 0)
        {
            return (0, frame);
        }

        if (frames > sectionFrames && frame == frames - 1)
        {
            return ((frames / sectionFrames) + 1, 0);
        }

        int section = frame / sectionFrames;

        return (section, frame - (section * sectionFrames));
    }

    /// <summary>Valve's <c>ExtractAnimValue</c>, for one channel at one frame.</summary>
    /// <remarks>
    /// **A run-length scheme over frames, and the walk is the whole of it.** Each block is two
    /// bytes — <c>valid</c>, how many real values follow, and <c>total</c>, how many frames the
    /// block covers. Frames past <c>valid</c> repeat the last real value, which is how a bone that
    /// holds still for two hundred frames costs four bytes.
    ///
    /// A <c>total</c> of zero would loop for ever; Valve assert on it and return zero, and so does
    /// this.
    /// </remarks>
    private static float Value(ReadOnlySpan<byte> payload, int channel, int frame) =>
        Value(payload, tableAt: 0, channel, frame);

    /// <summary>The same walk, for a structure whose offset table does not start at byte zero.</summary>
    /// <param name="payload">The structure, from its own start — offsets are relative to it.</param>
    /// <param name="tableAt">Where the <c>short</c> offset table begins within it.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="frame">Which frame.</param>
    /// <returns>The raw value, before scaling.</returns>
    /// <remarks>
    /// **Shared with <c>mstudiocompressedikerror_t</c>, whose table sits at byte 24** behind six
    /// floats of scale (<c>studio.h:544</c>). The run-length walk is character for character the
    /// same as an animation track's, because it IS the same: `CalcDecompressedAnimation` calls the
    /// same `ExtractAnimValue` the bone tracks use. A second copy of it here would be a second
    /// place for the block arithmetic to drift.
    /// </remarks>
    internal static float Value(ReadOnlySpan<byte> payload, int tableAt, int channel, int frame)
    {
        if (!Values(payload, tableAt, channel, out ReadOnlySpan<byte> values))
        {
            return 0f;
        }

        int at = 0;
        int remaining = frame;
        int valid = Land(values, ref at, ref remaining);

        return valid < 0 ? 0f : Cell(values, at, valid, remaining);
    }

    /// <summary>One channel's run-length stream, through the <c>short</c> offset table.</summary>
    /// <returns>False when the table is cut short or the offset points nowhere, which reads as zero.</returns>
    private static bool Values(ReadOnlySpan<byte> payload, int tableAt, int channel, out ReadOnlySpan<byte> values)
    {
        values = default;

        if (payload.Length < tableAt + 6)
        {
            return false;
        }

        int offset = BinaryPrimitives.ReadInt16LittleEndian(payload[(tableAt + (channel * 2))..]);

        if (offset <= 0 || offset >= payload.Length)
        {
            return false;
        }

        values = payload[offset..];

        return true;
    }

    /// <summary>
    /// Walks the blocks from <paramref name="at"/> to the one <paramref name="remaining"/> frames land in, the loop of
    /// <c>ExtractAnimValue</c>.
    /// </summary>
    /// <param name="values">The channel's stream.</param>
    /// <param name="at">The block to start from; left at the block landed in.</param>
    /// <param name="remaining">Frames still to go from that block; left as the frame within the landed block.</param>
    /// <returns>The landed block's <c>valid</c>, or -1 for an ending block (<c>total</c> zero) or a stream run out.</returns>
    /// <remarks>
    /// The one-key walk (<c>bone_setup.cpp:349-359</c>). <see cref="ValuePair"/> walks in slots for the two-key form,
    /// whose cases after the landing differ (<c>:297-335</c>).
    /// </remarks>
    private static int Land(ReadOnlySpan<byte> values, ref int at, ref int remaining)
    {
        // `ExtractAnimValue`'s loop (`bone_setup.cpp:349-359`): an ending block (`total` zero) is tested only after a step.
        while (at + 2 <= values.Length)
        {
            int valid = values[at];
            int total = values[at + 1];

            if (total > remaining)
            {
                return valid;
            }

            remaining -= total;
            at += (valid + 1) * 2;

            if (at + 2 > values.Length || values[at + 1] == 0)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>The value a frame reads inside the block it landed in: its own, or the last valid one repeated.</summary>
    private static float Cell(ReadOnlySpan<byte> values, int at, int valid, int remaining)
    {
        int index = remaining < valid ? remaining + 1 : valid;
        int cell = at + (index * 2);

        return cell + 2 <= values.Length
            ? BinaryPrimitives.ReadInt16LittleEndian(values[cell..])
            : 0f;
    }

    /// <summary>One channel at a frame and the next, scaled: Valve's <c>ExtractAnimValue( frame, …, scale, v1, v2 )</c>.</summary>
    /// <param name="payload">The structure, from its own start: offsets are relative to it.</param>
    /// <param name="tableAt">Where its <c>short</c> offset table begins: zero for a bone track, 24 for an IK error.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="frame">The frame within this stream; the second value is the next one, read from the same stream.</param>
    /// <param name="scale">The channel's scale.</param>
    /// <returns><c>v1</c> and <c>v2</c>.</returns>
    /// <remarks>
    /// **Transcribed, branch for branch** (<c>bone_setup.cpp:267-336</c>), indexing <c>mstudioanimvalue_t</c> slots of
    /// two bytes each: the crash guard for a first block of one valid frame (<c>:277-281</c>), the walk to the block
    /// holding the frame (<c>:286-296</c>), and the four cases for <c>v2</c>. Where the next frame is past the block,
    /// <c>v2</c> is "pulled from the first data block in next list": the raw slot <c>[valid + 2]</c>, whatever it holds,
    /// as the engine reads it (B620). This was landed by the one-key rule instead, which differs at a block's end and a
    /// stream's.
    /// </remarks>
    internal static (float First, float Second) ValuePair(
        ReadOnlySpan<byte> payload, int tableAt, int channel, int frame, float scale)
    {
        if (!Values(payload, tableAt, channel, out ReadOnlySpan<byte> values) || values.Length < 2)
        {
            return (0f, 0f);
        }

        if (values[1] == 1 && values[0] == 1)
        {
            float only = Slot(values, 1) * scale;

            return (only, only);
        }

        int at = 0;
        int k = frame;

        while (values[(at * 2) + 1] <= k)
        {
            k -= values[(at * 2) + 1];
            at += values[at * 2] + 1;

            if ((at * 2) + 2 > values.Length || values[(at * 2) + 1] == 0)
            {
                return (0f, 0f);
            }
        }

        int valid = values[at * 2];
        int total = values[(at * 2) + 1];

        if (valid > k)
        {
            float v1 = Slot(values, at + k + 1) * scale;

            if (valid > k + 1)
            {
                return (v1, Slot(values, at + k + 2) * scale);
            }

            return (v1, total > k + 1 ? v1 : Slot(values, at + valid + 2) * scale);
        }

        float last = Slot(values, at + valid) * scale;

        return (last, total > k + 1 ? last : Slot(values, at + valid + 2) * scale);
    }

    /// <summary>One <c>mstudioanimvalue_t</c> slot's <c>value</c>; a slot past the file reads zero.</summary>
    private static short Slot(ReadOnlySpan<byte> values, int slot) =>
        (slot * 2) + 2 <= values.Length ? BinaryPrimitives.ReadInt16LittleEndian(values[(slot * 2)..]) : (short)0;

    /// <summary>Valve's <c>Quaternion48</c>: x:16, y:16, z:15, wneg:1.</summary>
    /// <remarks>
    /// **The radicand is clamped, and that is not tidiness.** <c>w</c> is derived as
    /// <c>sqrt(1 − x² − y² − z²)</c>, and rounding in the sixteen-bit mantissas can push the sum
    /// just past one, which yields NaN — a value that propagates silently through a matrix and
    /// makes an entire model vanish rather than raising anything.
    /// </remarks>
    private static (float X, float Y, float Z, float W) Quaternion48(ReadOnlySpan<byte> from)
    {
        ushort rawX = BinaryPrimitives.ReadUInt16LittleEndian(from);
        ushort rawY = BinaryPrimitives.ReadUInt16LittleEndian(from[2..]);
        ushort rawZ = BinaryPrimitives.ReadUInt16LittleEndian(from[4..]);

        float x = (rawX - 32768) * (1f / 32768f);
        float y = (rawY - 32768) * (1f / 32768f);
        float z = ((rawZ & 0x7FFF) - 16384) * (1f / 16384f);

        float w = MathF.Sqrt(Math.Max(0f, 1f - (x * x) - (y * y) - (z * z)));

        return (x, y, z, (rawZ & 0x8000) != 0 ? -w : w);
    }

    /// <summary>Valve's <c>Quaternion64</c>: x:21, y:21, z:21, wneg:1.</summary>
    private static (float X, float Y, float Z, float W) Quaternion64(ReadOnlySpan<byte> from)
    {
        ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(from);

        float x = ((long)(packed & 0x1FFFFF) - 1048576) * (1f / 1048576f);
        float y = ((long)((packed >> 21) & 0x1FFFFF) - 1048576) * (1f / 1048576f);
        float z = ((long)((packed >> 42) & 0x1FFFFF) - 1048576) * (1f / 1048576f);

        float w = MathF.Sqrt(Math.Max(0f, 1f - (x * x) - (y * y) - (z * z)));

        return (x, y, z, (packed >> 63) != 0 ? -w : w);
    }

    /// <summary>Valve's <c>Vector48</c>: three IEEE half-precision floats.</summary>
    private static (float X, float Y, float Z) Vector48(ReadOnlySpan<byte> from) =>
        (
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from)),
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from[2..])),
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from[4..])));

    /// <summary>Valve's <c>AngleQuaternion</c> for a <c>RadianEuler</c>.</summary>
    /// <param name="x">Roll, in radians.</param>
    /// <param name="y">Pitch, in radians.</param>
    /// <param name="z">Yaw, in radians.</param>
    /// <returns>The rotation.</returns>
    /// <remarks>
    /// **The axis order is the trap.** A <c>RadianEuler</c> is roll, pitch, yaw in x, y, z — not
    /// the pitch-yaw-roll of a <c>QAngle</c> — and Valve's own comment beside the X360 path notes
    /// the ordering differs between the two for exactly that reason. Swapping them produces a
    /// skeleton that is plausible and wrong.
    ///
    /// **Public because `CalcBoneAdj` calls the same function** (<c>bone_setup.cpp:2490</c>):
    /// <c>a0.Init( value * (M_PI / 180.0), 0, 0 ); AngleQuaternion( a0, q0 );</c>. A second copy
    /// there could disagree with this one about the axis order, which is precisely the mistake the
    /// remark above is warning about.
    /// </remarks>
    public static (float X, float Y, float Z, float W) FromEulerRadians(float x, float y, float z)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(z * 0.5f);
        (float sinPitch, float cosPitch) = MathF.SinCos(y * 0.5f);
        (float sinRoll, float cosRoll) = MathF.SinCos(x * 0.5f);

        float rollByPitch = sinRoll * cosPitch;
        float pitchByRoll = cosRoll * sinPitch;
        float bothCosine = cosRoll * cosPitch;
        float bothSine = sinRoll * sinPitch;

        return (
            (rollByPitch * cosYaw) - (pitchByRoll * sinYaw),
            (pitchByRoll * cosYaw) + (rollByPitch * sinYaw),
            (bothCosine * sinYaw) - (bothSine * cosYaw),
            (bothCosine * cosYaw) + (bothSine * sinYaw));
    }
}
