using System;
using System.Buffers.Binary;
using System.Collections.Generic;

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

        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file) || bones.Count == 0)
        {
            return [];
        }

        int table = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]);
        int at = table + (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return [];
        }

        ReadOnlySpan<byte> description = bytes[at..];

        int frames = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationFrameCountOffset..]);
        int block = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationBlockOffset..]);
        int data = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationDataOffset..]);

        int sectionFrames =
            BinaryPrimitives.ReadInt32LittleEndian(description[AnimationSectionFramesOffset..]);

        int sections = BinaryPrimitives.ReadInt32LittleEndian(
            description[AnimationSectionIndexOffset..]);

        int wanted = Math.Clamp(frame, 0, frames - 1);

        // **A sectioned animation renumbers its frames per section and keeps its data per section**
        // (`mstudioanimdesc_t::pAnim`, `public/studio.cpp`). `animindex` is Valve-documented as
        // "non-zero when anim data isn't in sections", so it does not locate the data on its own.
        //
        // Without this the run-length walk reads every frame out of section zero, runs off the end
        // of it and keeps going — which repeats a stale value for most frames and lands on stray
        // bytes for a few. That was B222: `vm_weapon_bone_1` 219 units from rest, and the sticky
        // launcher merged onto it torn across the view.
        if (sectionFrames > 0 && sections > 0)
        {
            (int section, int local) = Section(frames, sectionFrames, wanted);

            int entry = at + sections + (section * AnimationSectionStride);

            if (entry < 0 || entry + AnimationSectionStride > bytes.Length)
            {
                return [];
            }

            block = BinaryPrimitives.ReadInt32LittleEndian(bytes[entry..]);
            data = BinaryPrimitives.ReadInt32LittleEndian(
                bytes[(entry + AnimationSectionDataOffset)..]);

            wanted = local;
        }

        if (block != 0 || data == 0 || frames <= 0)
        {
            return [];
        }

        int cursor = at + data;

        List<StudioBonePose> poses = [];

        // Bounded by the bone count: the chain is terminated by a zero nextoffset, and a malformed
        // one would otherwise walk the file for ever.
        for (int step = 0; step <= bones.Count && cursor > 0 && cursor + 4 <= bytes.Length; step++)
        {
            ReadOnlySpan<byte> entry = bytes[cursor..];

            int bone = entry[0];
            int flags = entry[1];
            int next = BinaryPrimitives.ReadInt16LittleEndian(entry[2..]);

            if (bone >= bones.Count)
            {
                break;
            }

            poses.Add(ReadBone(entry[4..], bones[bone], bone, flags, wanted));

            if (next == 0)
            {
                break;
            }

            cursor += next;
        }

        return poses;
    }

    private static StudioBonePose ReadBone(
        ReadOnlySpan<byte> payload, StudioBone rest, int bone, int flags, int frame)
    {
        int rotationBytes = 0;

        (float X, float Y, float Z, float W) rotation = rest.Rotation;

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
            // Three run-length channels of Euler angle, scaled and added to the bone's rest
            // rotation - unless the animation is a delta, where the rest value is not a base.
            float x = Value(payload, 0, frame) * rest.RotationScale.X;
            float y = Value(payload, 1, frame) * rest.RotationScale.Y;
            float z = Value(payload, 2, frame) * rest.RotationScale.Z;

            if ((flags & Delta) == 0)
            {
                x += rest.Euler.X;
                y += rest.Euler.Y;
                z += rest.Euler.Z;
            }

            rotation = FromEuler(x, y, z);
        }

        (float X, float Y, float Z) position = rest.Position;

        if ((flags & RawPosition) != 0 && payload.Length >= rotationBytes + 6)
        {
            position = Vector48(payload[rotationBytes..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
            // **The position channels sit after the rotation channels when both are animated**,
            // which is what pPosV encodes by advancing one mstudioanim_valueptr_t.
            ReadOnlySpan<byte> from = (flags & AnimatedRotation) != 0 && payload.Length >= 6
                ? payload[6..]
                : payload;

            float x = Value(from, 0, frame) * rest.PositionScale.X;
            float y = Value(from, 1, frame) * rest.PositionScale.Y;
            float z = Value(from, 2, frame) * rest.PositionScale.Z;

            position = (flags & Delta) == 0
                ? (rest.Position.X + x, rest.Position.Y + y, rest.Position.Z + z)
                : (x, y, z);
        }

        return new StudioBonePose(bone, position, rotation);
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
    private static float Value(ReadOnlySpan<byte> payload, int channel, int frame)
    {
        if (payload.Length < 6)
        {
            return 0f;
        }

        int offset = BinaryPrimitives.ReadInt16LittleEndian(payload[(channel * 2)..]);

        if (offset <= 0 || offset >= payload.Length)
        {
            return 0f;
        }

        ReadOnlySpan<byte> values = payload[offset..];

        int remaining = frame;
        int at = 0;

        while (at + 2 <= values.Length)
        {
            int valid = values[at];
            int total = values[at + 1];

            if (total == 0)
            {
                return 0f;
            }

            if (total > remaining)
            {
                int index = remaining < valid ? remaining + 1 : valid;
                int cell = at + (index * 2);

                return cell + 2 <= values.Length
                    ? BinaryPrimitives.ReadInt16LittleEndian(values[cell..])
                    : 0f;
            }

            remaining -= total;
            at += (valid + 1) * 2;
        }

        return 0f;
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
    /// <remarks>
    /// **The axis order is the trap.** A <c>RadianEuler</c> is roll, pitch, yaw in x, y, z — not
    /// the pitch-yaw-roll of a <c>QAngle</c> — and Valve's own comment beside the X360 path notes
    /// the ordering differs between the two for exactly that reason. Swapping them produces a
    /// skeleton that is plausible and wrong.
    /// </remarks>
    private static (float X, float Y, float Z, float W) FromEuler(float x, float y, float z)
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
