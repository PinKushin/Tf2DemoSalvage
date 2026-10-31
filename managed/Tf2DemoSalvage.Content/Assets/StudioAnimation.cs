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

        // **A delta animation is seeded from NOTHING, not from the rest pose** (B284).
        // `CalcVirtualAnimation` (`bone_setup.cpp:933`) branches on the ANIMATION's own flags
        // before it reads a single bone:
        //
        //     if (animdesc.flags & STUDIO_DELTA) { q[i].Init( 0,0,0,1 ); pos[i].Init( 0,0,0 ); }
        //     else                               { q[i] = pSeqbone[j].quat; pos[i] = pSeqbone[j].pos; }
        //
        // A delta holds a DIFFERENCE, so a channel it does not carry means "no difference" — an
        // identity rotation and a zero offset. Seeding from the rest pose instead makes every
        // unanimated channel a full bone transform, and adding THAT to a base stretches every limb
        // by its own rest offset. Measured on a scout mid-reload: a standing but elongated,
        // twisted body, where the absolute reading had given a flat one.
        bool additive = IsDelta(file, animation);

        Chain(bytes, bones, animation, frame, chain, out int wanted);

        poses.EnsureCapacity(chain.Count);

        foreach ((int bone, int flags, int payload) in chain)
        {
            poses.Add(ReadBone(bytes[payload..], bones[bone], bone, flags, wanted, additive));
        }

        return poses;
    }

    /// <summary>One animation sampled BETWEEN two of its frames, as the engine samples it, in storage the caller owns.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="bones">
    /// The skeleton the animation is numbered against: each track's rest values and scales come from it, and the
    /// blend is sized and seeded from it. **With a <paramref name="remap"/> that is a mismatch, carried over bit for bit
    /// from the two-pass read and not a design**: the tracks land at the BASE model's indices, but the blend is
    /// seeded and sized from the included model's own bones, so it is right only while the included skeleton matches
    /// the base in order and count. Valve seeds base bone <c>i</c> from the sequence group's bone through
    /// <c>boneMap</c> (<c>bone_setup.cpp:931-947</c>). Filed as B621.
    /// </param>
    /// <param name="remap">
    /// The animation model's bone numbering onto the base model's, or null when the animation is the base model's own.
    /// A bone mapping to a negative index is dropped.
    /// </param>
    /// <param name="animation">Which local animation.</param>
    /// <param name="frame">The frame the cycle landed on.</param>
    /// <param name="fraction">How far from it toward the next, the engine's <c>s</c>.</param>
    /// <param name="workspace">The skeleton's pose storage the result is built in.</param>
    /// <param name="slot">Which grid corner this is, so two corners never share storage.</param>
    /// <returns>
    /// The frame alone, sparse, when <paramref name="fraction"/> is not above zero or the frame names no bone;
    /// otherwise the blend of the frame toward the next, one entry per bone. Valid until the next call with
    /// <paramref name="workspace"/> and <paramref name="slot"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<c>CalcPoseSingle</c> never samples one frame** (<c>bone_setup.cpp:915</c>): every bone is
    /// <c>CalcBoneQuaternion( iFrame, s, … )</c>, a blend of that frame with the next, so an animation plays between
    /// its authored frames rather than stepping (B279).
    ///
    /// **One walk of the chain for both keys, as Valve's.** <c>CalcAnimation</c> resolves the section once
    /// (<c>bone_setup.cpp:1063</c>) and walks the <c>mstudioanim_t</c> chain once (<c>:1095-1109</c>, and
    /// <c>:964-981</c> for a virtual model), and each track yields both keys (<see cref="ReadBonePair"/>). This read the
    /// whole chain twice, once per frame, then expanded both sparse poses to the skeleton before blending: twice the
    /// walks, twice the run-length reads, twice the Euler conversions. Both keys now land straight in the two dense
    /// blend inputs. **The result is the same to the bit** (<c>FramePairDifferentialTests</c>): the blend itself, its
    /// threshold (B614) and its seeding are unchanged.
    ///
    /// **When the two frames resolve to different chains the two-pass read stays.** A sectioned animation stores
    /// each section's data separately, and its last frame alone in a trailing section, so frame and frame + 1 can sit
    /// in different chains. Valve reads frame + 1 from frame's section; this reader reads it from its own, and a cost
    /// change does not move that (filed as B620).
    /// </remarks>
    internal static List<StudioBonePose> PoseBetweenInto(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        float fraction,
        StudioPoseWorkspace workspace,
        int slot)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(workspace);

        List<StudioBonePose> pose = workspace.Frame[slot];

        if (fraction <= 0f)
        {
            return PoseOfInto(file, bones, remap, animation, frame, workspace, pose);
        }

        ReadOnlySpan<byte> bytes = file.Span;

        // Stryker disable once all : a mutant turning an || into && skips Locate, leaves 'cursor' and 'first' unassigned (CS0165), and Safe Mode drops the method - B410
        if (animation < 0 || animation >= Count(file) || bones.Count == 0 ||
            !Locate(bytes, animation, frame, out int cursor, out int first))
        {
            pose.Clear();
            return pose;
        }

        bool additive = IsDelta(file, animation);

        // Stryker disable once all : a cost branch the differential cannot tell apart - sending a same-chain pair to TwoPass gives the same bits, only slower, so mutants that widen it survive by equivalence
        if (!Locate(bytes, animation, frame + 1, out int nextCursor, out int second) || nextCursor != cursor)
        {
            return TwoPass(file, bones, remap, animation, frame, fraction, additive, workspace, slot);
        }

        List<(int Bone, int Flags, int At)> chain = workspace.Chain;

        chain.Clear();
        Walk(bytes, bones.Count, cursor, chain);

        List<StudioBonePose> between = workspace.Between[slot];

        CollectionsMarshal.SetCount(between, bones.Count);
        CollectionsMarshal.SetCount(workspace.Scratch, bones.Count);

        Span<StudioBonePose> left = CollectionsMarshal.AsSpan(between);
        Span<StudioBonePose> right = CollectionsMarshal.AsSpan(workspace.Scratch);

        StudioPoseBlend.SeedInto(bones, additive, left);
        StudioPoseBlend.SeedInto(bones, additive, right);

        int named = 0;

        foreach ((int bone, int flags, int payload) in CollectionsMarshal.AsSpan(chain))
        {
            int target = Renumbered(bone, remap);

            if (target < 0)
            {
                continue;
            }

            // Counted whether or not it lands, as the sparse frame the two-pass read built counted it.
            named++;

            if (target < left.Length)
            {
                StudioBone rest = bones[bone];

                (left[target], right[target]) =
                    ReadBonePair(bytes[payload..], rest, target, flags, first, second, additive);
            }
        }

        if (named == 0)
        {
            pose.Clear();
            return pose;
        }

        StudioPoseBlend.BlendExpanded(bones, fraction, left, right);

        return between;
    }

    /// <summary>The frame and the frame after it read as two whole passes, then blended: for two different chains.</summary>
    private static List<StudioBonePose> TwoPass(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        float fraction,
        bool additive,
        StudioPoseWorkspace workspace,
        int slot)
    {
        List<StudioBonePose> pose = PoseOfInto(file, bones, remap, animation, frame, workspace, workspace.Frame[slot]);

        if (pose.Count == 0)
        {
            return pose;
        }

        List<StudioBonePose> next = PoseOfInto(file, bones, remap, animation, frame + 1, workspace, workspace.Next[slot]);

        if (next.Count == 0)
        {
            return pose;
        }

        StudioPoseBlend.BlendInto(bones, pose, next, fraction, additive, workspace.Between[slot], workspace.Scratch);

        return workspace.Between[slot];
    }

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
        int frame,
        bool additive)
    {
        int rotationBytes = 0;

        (float X, float Y, float Z, float W) rotation =
            additive ? (0f, 0f, 0f, 1f) : rest.Rotation;

        if ((flags & RawRotation) != 0 && payload.Length >= 6)
        {
            rotation = Quaternion48(payload);
            rotationBytes = 6;
        }
        else if ((flags & RawRotation64) != 0 && payload.Length >= 8)
        {
            rotation = Quaternion64(payload);
            rotationBytes = 8;
        }
        else if ((flags & AnimatedRotation) != 0)
        {
            rotation = Turned(
                Angles(Value(payload, 0, frame), Value(payload, 1, frame), Value(payload, 2, frame), rest, flags),
                rest,
                flags);
        }

        (float X, float Y, float Z) position = additive ? (0f, 0f, 0f) : rest.Position;

        if ((flags & RawPosition) != 0 && payload.Length >= rotationBytes + 6)
        {
            position = Vector48(payload[rotationBytes..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
            ReadOnlySpan<byte> from = PositionChannels(payload, flags);

            position = Moved(Value(from, 0, frame), Value(from, 1, frame), Value(from, 2, frame), rest, flags);
        }

        return new StudioBonePose(bone, position, rotation);
    }

    /// <summary>
    /// One track at two frames, decoded once: the pair <c>CalcBoneQuaternion</c> and <c>CalcBonePosition</c> read for a
    /// sub-frame sample.
    /// </summary>
    /// <param name="payload">The track's payload.</param>
    /// <param name="rest">The bone the track is numbered against, in its own model.</param>
    /// <param name="bone">The index to stamp on both poses.</param>
    /// <param name="flags">The track's <c>STUDIO_ANIM_*</c> flags.</param>
    /// <param name="first">The first key's frame within the chain's data.</param>
    /// <param name="second">The second key's frame.</param>
    /// <param name="additive">Whether the animation is a delta.</param>
    /// <returns>
    /// Exactly what <see cref="ReadBone"/> answers at <paramref name="first"/> and at <paramref name="second"/>.
    /// </returns>
    /// <remarks>
    /// **The work Valve does once per pair, done once here** (<c>bone_setup.cpp:374-474</c>, <c>:498-551</c>): a raw
    /// rotation or position is one read for both keys (<c>:379-391</c>, <c>:502-507</c>), an absent channel is one seed
    /// (<c>:393-404</c>, <c>:509-520</c>), each run-length channel is one walk for both keys (<c>:413-415</c>,
    /// <c>:530</c>), and equal angles convert once (<c>:428-446</c>). The angles are compared by their BITS, not by
    /// <c>==</c>: a zero scale turns one short into <c>+0</c> and another into <c>-0</c>, which a delta never washes out
    /// with a base angle, and the two convert to different quaternions. **The blend between the keys is not here** and
    /// is not skipped for equal keys, because <c>a * (1 - s) + a * s</c> is not <c>a</c> to the bit.
    ///
    /// **Why the flag ladder is written twice, here and in <see cref="ReadBone"/>.** Everything the two do with a
    /// value is shared: <see cref="Angles"/>, <see cref="Turned"/>, <see cref="Moved"/>, <see cref="PositionChannels"/>,
    /// the packed decoders and the run-length walk. Only the order of the tests on <c>flags</c> repeats. Joining them
    /// costs work on one side: <see cref="ReadBone"/> as this called with one frame twice does a second
    /// <see cref="Angles"/>, <see cref="Moved"/>, <see cref="SameBits"/> and second-key landing per animated track on
    /// every single-frame read; this as a ladder that asks "one key or two" pays that branch on every channel of the
    /// pair path. Both ladders are pinned to the same frozen reference by <c>FramePairDifferentialTests</c> (the single
    /// read through fractions at or below zero), so a change to either alone fails it.
    /// </remarks>
    private static (StudioBonePose First, StudioBonePose Second) ReadBonePair(
        ReadOnlySpan<byte> payload,
        in StudioBone rest,
        int bone,
        int flags,
        int first,
        int second,
        bool additive)
    {
        int rotationBytes = 0;

        (float X, float Y, float Z, float W) rotation =
            additive ? (0f, 0f, 0f, 1f) : rest.Rotation;

        (float X, float Y, float Z, float W) rotationNext = rotation;

        if ((flags & RawRotation) != 0 && payload.Length >= 6)
        {
            rotation = rotationNext = Quaternion48(payload);
            rotationBytes = 6;
        }
        else if ((flags & RawRotation64) != 0 && payload.Length >= 8)
        {
            rotation = rotationNext = Quaternion64(payload);
            rotationBytes = 8;
        }
        else if ((flags & AnimatedRotation) != 0)
        {
            (float x, float xNext) = ValuePair(payload, 0, first, second);
            (float y, float yNext) = ValuePair(payload, 1, first, second);
            (float z, float zNext) = ValuePair(payload, 2, first, second);

            (float X, float Y, float Z) angles = Angles(x, y, z, rest, flags);
            (float X, float Y, float Z) anglesNext = Angles(xNext, yNext, zNext, rest, flags);

            rotation = Turned(angles, rest, flags);
            // Stryker disable once all : a cost branch the differential cannot tell apart - converting bit-equal angles a second time gives the same quaternion, so a mutant that never takes the shortcut survives by equivalence
            rotationNext = SameBits(angles, anglesNext) ? rotation : Turned(anglesNext, rest, flags);
        }

        (float X, float Y, float Z) position = additive ? (0f, 0f, 0f) : rest.Position;
        (float X, float Y, float Z) positionNext = position;

        if ((flags & RawPosition) != 0 && payload.Length >= rotationBytes + 6)
        {
            position = positionNext = Vector48(payload[rotationBytes..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
            ReadOnlySpan<byte> from = PositionChannels(payload, flags);

            (float x, float xNext) = ValuePair(from, 0, first, second);
            (float y, float yNext) = ValuePair(from, 1, first, second);
            (float z, float zNext) = ValuePair(from, 2, first, second);

            position = Moved(x, y, z, rest, flags);
            positionNext = Moved(xNext, yNext, zNext, rest, flags);
        }

        return (new StudioBonePose(bone, position, rotation), new StudioBonePose(bone, positionNext, rotationNext));
    }

    private static bool SameBits((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) &&
        BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y) &&
        BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

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

    /// <summary>Three raw run-length values as the position they encode.</summary>
    private static (float X, float Y, float Z) Moved(float x, float y, float z, in StudioBone rest, int flags)
    {
        x *= rest.PositionScale.X;
        y *= rest.PositionScale.Y;
        z *= rest.PositionScale.Z;

        return (flags & Delta) == 0
            ? (rest.Position.X + x, rest.Position.Y + y, rest.Position.Z + z)
            : (x, y, z);
    }

    /// <summary>An animated Euler angle as the rotation <c>CalcBoneQuaternion</c> ends with.</summary>
    private static (float X, float Y, float Z, float W) Turned(
        (float X, float Y, float Z) angles, in StudioBone rest, int flags)
    {
        (float X, float Y, float Z, float W) rotation = FromEulerRadians(angles.X, angles.Y, angles.Z);

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
    /// <c>mstudioanimdesc_t::pAnim</c> without the walk, so a frame pair can ask whether both of its frames resolve to the
    /// same chain and walk it once, as <c>CalcAnimation</c> does (<c>bone_setup.cpp:1063</c>, <c>:1095-1109</c>).
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
    /// **Resumable, so a frame pair walks the shared prefix once**: the later frame passes every block the earlier one
    /// does, so <see cref="ValuePair"/> lands the first, adds the gap to <paramref name="remaining"/> and carries on from
    /// the same block, which is the same walk an independent read of the later frame takes.
    /// </remarks>
    private static int Land(ReadOnlySpan<byte> values, ref int at, ref int remaining)
    {
        while (at + 2 <= values.Length)
        {
            int valid = values[at];
            int total = values[at + 1];

            if (total == 0)
            {
                return -1;
            }

            if (total > remaining)
            {
                return valid;
            }

            remaining -= total;
            at += (valid + 1) * 2;
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

    /// <summary>One channel at two frames, in one walk: <c>ExtractAnimValue( frame, …, v1, v2 )</c>.</summary>
    /// <param name="payload">The track's payload; the offset table starts at byte zero.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="first">The frame for the first value.</param>
    /// <param name="second">The frame for the second, normally <paramref name="first"/> + 1.</param>
    /// <returns>Each frame's raw value, exactly what <see cref="Value(ReadOnlySpan{byte}, int, int)"/> answers for it.</returns>
    /// <remarks>
    /// **The shape is Valve's, the arithmetic is this reader's.** <c>bone_setup.cpp:413-415</c> and <c>:530</c> read both
    /// keys of a pair from one walk of the run-length blocks. The second key here is landed by the same rule as
    /// <see cref="Value(ReadOnlySpan{byte}, int, int)"/>, continuing from the first key's block, rather than by Valve's
    /// "repeats, no blend" and "pull from the next list" branches. Those can disagree with an independent walk at a block
    /// edge, and this is a cost change that must give the same bits as two separate walks. Both keys go through
    /// <see cref="Land"/> and <see cref="Cell"/>, the same two steps <see cref="Value(ReadOnlySpan{byte}, int, int, int)"/>
    /// takes, so there is one copy of the block arithmetic.
    /// </remarks>
    private static (float First, float Second) ValuePair(ReadOnlySpan<byte> payload, int channel, int first, int second)
    {
        if (second < first)
        {
            return (Value(payload, channel, first), Value(payload, channel, second));
        }

        if (!Values(payload, tableAt: 0, channel, out ReadOnlySpan<byte> values))
        {
            return (0f, 0f);
        }

        int at = 0;
        int remaining = first;
        int valid = Land(values, ref at, ref remaining);

        if (valid < 0)
        {
            return (0f, 0f);
        }

        float found = Cell(values, at, valid, remaining);

        remaining += second - first;
        valid = Land(values, ref at, ref remaining);

        return (found, valid < 0 ? 0f : Cell(values, at, valid, remaining));
    }

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
