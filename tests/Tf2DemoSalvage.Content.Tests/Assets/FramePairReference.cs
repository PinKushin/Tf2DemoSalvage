using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A frame pair sampled as the engine samples it, written apart from production so the two can disagree. The one-frame
/// read is the reader as it stood at <c>f19b0d83</c> (docs/measurements/2026-10-31-setup-bones-read.md M1), copied with
/// its leaves (<c>Value</c>, <c>Chain</c>, the packed decoders, <c>FromEulerRadians</c>, the align). The pair is
/// transcribed from <c>bone_setup.cpp</c> and <c>mathlib_base.cpp</c> (B620); see <see cref="PoseBetween"/>.
/// **Kept only here, never in production**: a copy that called production would be broken the same way production is.
/// </summary>
internal static class FramePairReference
{
    private const int RawPosition = StudioFlags.AnimationRawPosition;
    private const int RawRotation = StudioFlags.AnimationRawRotation;
    private const int AnimatedPosition = StudioFlags.AnimationAnimatedPosition;
    private const int AnimatedRotation = StudioFlags.AnimationAnimatedRotation;
    private const int Delta = StudioFlags.AnimationDelta;
    private const int RawRotation64 = StudioFlags.AnimationRawRotation64;
    private const int AnimationEntryHeaderBytes = 4;

    /// <summary>The scratch lists the old path used, standing in for <c>StudioPoseWorkspace</c>'s.</summary>
    internal sealed class Lists
    {
        public List<StudioBonePose> Frame { get; } = [];

        public List<StudioBonePose> Next { get; } = [];

        public List<StudioBonePose> Between { get; } = [];

        public List<StudioBonePose> Raw { get; } = [];

        public List<StudioBonePose> Scratch { get; } = [];

        public List<(int Bone, int Flags, int At)> Entries { get; } = [];
    }

    /// <summary>
    /// One animation between a frame and the next as the engine samples it: the frame alone at or below <c>s = 0.001</c>,
    /// else <c>CalcAnimation</c>'s one walk of the frame's own section with each track blending its two keys (B620).
    /// </summary>
    /// <remarks>
    /// **Two later changes of meaning, marked.** The frame alone comes back expanded over the seeds (B621). The pair is
    /// no longer the old two-pass read: it is transcribed from <c>bone_setup.cpp</c> here — <c>CalcBoneQuaternion</c>
    /// (<c>:374-474</c>), <c>CalcBonePosition</c> (<c>:498-550</c>) and the two-key <c>ExtractAnimValue</c>
    /// (<c>:267-336</c>) — with <c>QuaternionBlend</c> and <c>QuaternionNormalize</c> from <c>mathlib_base.cpp</c>
    /// (<c>:1545-1576</c>, <c>:1735-1754</c>), sharing no code with production. <paramref name="bones"/> are also the
    /// seeds, as they are for every remap this test builds.
    /// </remarks>
    public static IReadOnlyList<StudioBonePose> PoseBetween(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        float fraction,
        Lists lists)
    {
        bool additive = IsDelta(file, animation);

        // The engine's `s > 0.001f` (B614), as its own literal so a drift in production's constant is caught.
        if (!(fraction > 0.001f))
        {
            List<StudioBonePose> pose = PoseOf(file, bones, remap, animation, frame, lists, lists.Frame);

            return pose.Count == 0 ? pose : Seeded(bones, additive, pose, lists.Between);
        }

        List<StudioBonePose> dense = lists.Between;
        dense.Clear();

        if (animation < 0 || animation >= Count(file) || bones.Count == 0)
        {
            return dense;
        }

        ReadOnlySpan<byte> bytes = file.Span;

        Chain(bytes, bones, animation, frame, lists.Entries, out int local);

        Seeded(bones, additive, [], dense);

        int named = 0;

        foreach ((int bone, int flags, int payload) in lists.Entries)
        {
            int target = bone;

            if (remap is not null)
            {
                target = bone < remap.Length ? remap[bone] : -1;
            }

            if (target < 0)
            {
                continue;
            }

            named++;

            if (target < dense.Count)
            {
                dense[target] = new StudioBonePose(
                    target,
                    CalcBonePosition(local, fraction, bones[bone], flags, bytes[payload..]),
                    CalcBoneQuaternion(local, fraction, bones[bone], flags, bytes[payload..]));
            }
        }

        if (named == 0)
        {
            dense.Clear();
        }

        return dense;
    }

    /// <summary>A pose over its skeleton's rest, or identity for a delta, each named bone written over its seed.</summary>
    private static List<StudioBonePose> Seeded(
        IReadOnlyList<StudioBone> bones, bool additive, IReadOnlyList<StudioBonePose> pose, List<StudioBonePose> into)
    {
        into.Clear();

        for (int bone = 0; bone < bones.Count; bone++)
        {
            into.Add(additive
                ? new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f))
                : new StudioBonePose(bone, bones[bone].Position, bones[bone].Rotation));
        }

        foreach (StudioBonePose named in pose)
        {
            if (named.Bone >= 0 && named.Bone < into.Count)
            {
                into[named.Bone] = named;
            }
        }

        return into;
    }

    /// <summary><c>CalcBoneQuaternion</c> with <c>s &gt; 0.001</c> (<c>bone_setup.cpp:374-474</c>).</summary>
    private static (float X, float Y, float Z, float W) CalcBoneQuaternion(
        int frame, float s, StudioBone bone, int flags, ReadOnlySpan<byte> panim)
    {
        if ((flags & RawRotation) != 0)
        {
            return Quaternion48(panim);
        }

        if ((flags & RawRotation64) != 0)
        {
            return Quaternion64(panim);
        }

        if ((flags & AnimatedRotation) == 0)
        {
            return (flags & Delta) != 0 ? (0f, 0f, 0f, 1f) : bone.Rotation;
        }

        (float ax1, float ax2) = ExtractAnimValue(frame, panim, 0, bone.RotationScale.X);
        (float ay1, float ay2) = ExtractAnimValue(frame, panim, 1, bone.RotationScale.Y);
        (float az1, float az2) = ExtractAnimValue(frame, panim, 2, bone.RotationScale.Z);

        if ((flags & Delta) == 0)
        {
            ax1 += bone.Euler.X;
            ay1 += bone.Euler.Y;
            az1 += bone.Euler.Z;
            ax2 += bone.Euler.X;
            ay2 += bone.Euler.Y;
            az2 += bone.Euler.Z;
        }

        // `angle1.x != angle2.x || …`: C++'s float compare, under which -0 equals +0 and a NaN equals nothing.
        static bool Same(float a, float b) => a.CompareTo(b) == 0 && !float.IsNaN(a);

        bool equal = Same(ax1, ax2) && Same(ay1, ay2) && Same(az1, az2);

        (float X, float Y, float Z, float W) q = equal
            ? FromEulerRadians(ax1, ay1, az1)
            : QuaternionBlend(FromEulerRadians(ax1, ay1, az1), FromEulerRadians(ax2, ay2, az2), s);

        return (flags & Delta) == 0 && (bone.Flags & StudioBoneFlags.FixedAlignment) != 0
            ? BoneAlign(bone.Alignment, q)
            : q;
    }

    /// <summary><c>CalcBonePosition</c> with <c>s &gt; 0.001</c> (<c>bone_setup.cpp:498-550</c>).</summary>
    private static (float X, float Y, float Z) CalcBonePosition(
        int frame, float s, StudioBone bone, int flags, ReadOnlySpan<byte> panim)
    {
        int rotationBytes = 0;

        if ((flags & RawRotation) != 0)
        {
            rotationBytes = 6;
        }
        else if ((flags & RawRotation64) != 0)
        {
            rotationBytes = 8;
        }

        if ((flags & RawPosition) != 0)
        {
            return Vector48(panim[rotationBytes..]);
        }

        if ((flags & AnimatedPosition) == 0)
        {
            return (flags & Delta) != 0 ? (0f, 0f, 0f) : bone.Position;
        }

        // `pPosV`: one `mstudioanim_valueptr_t` past the rotation's when that is animated too.
        ReadOnlySpan<byte> positions = (flags & AnimatedRotation) != 0 ? panim[6..] : panim;

        float[] pos = new float[3];
        float[] scale = [bone.PositionScale.X, bone.PositionScale.Y, bone.PositionScale.Z];

        for (int j = 0; j < 3; j++)
        {
            (float v1, float v2) = ExtractAnimValue(frame, positions, j, scale[j]);

            pos[j] = (float)((v1 * (1.0 - s)) + (v2 * s));
        }

        return (flags & Delta) != 0
            ? (pos[0], pos[1], pos[2])
            : (pos[0] + bone.Position.X, pos[1] + bone.Position.Y, pos[2] + bone.Position.Z);
    }

    /// <summary>The two-key <c>ExtractAnimValue</c> (<c>bone_setup.cpp:267-336</c>), over slots of two bytes each.</summary>
    private static (float V1, float V2) ExtractAnimValue(int frame, ReadOnlySpan<byte> valuePointer, int channel, float scale)
    {
        int offset = BinaryPrimitives.ReadInt16LittleEndian(valuePointer[(channel * 2)..]);

        // `pAnimvalue` is null for a zero offset. One pointing before the track or past the file reads memory no file
        // holds; it reads zero here, as in production, and is not a comparison of the engine.
        if (offset <= 0 || offset >= valuePointer.Length)
        {
            return (0f, 0f);
        }

        ReadOnlySpan<byte> panimvalue = valuePointer[offset..];

        if (Total(panimvalue, 0) == 1 && Valid(panimvalue, 0) == 1)
        {
            return (Slot(panimvalue, 1) * scale, Slot(panimvalue, 1) * scale);
        }

        int p = 0;
        int k = frame;

        while (Total(panimvalue, p) <= k)
        {
            k -= Total(panimvalue, p);
            p += Valid(panimvalue, p) + 1;

            if (Total(panimvalue, p) == 0)
            {
                return (0f, 0f);
            }
        }

        int valid = Valid(panimvalue, p);
        int total = Total(panimvalue, p);

        if (valid > k)
        {
            float v1 = Slot(panimvalue, p + k + 1) * scale;

            if (valid > k + 1)
            {
                return (v1, Slot(panimvalue, p + k + 2) * scale);
            }

            return total > k + 1 ? (v1, v1) : (v1, Slot(panimvalue, p + valid + 2) * scale);
        }

        float last = Slot(panimvalue, p + valid) * scale;

        return total > k + 1 ? (last, last) : (last, Slot(panimvalue, p + valid + 2) * scale);
    }

    /// <summary><c>panimvalue[at].num.valid</c>.</summary>
    private static byte Valid(ReadOnlySpan<byte> slots, int at) => (at * 2) < slots.Length ? slots[at * 2] : (byte)0;

    /// <summary><c>panimvalue[at].num.total</c>; zero past the file, which ends the walk.</summary>
    private static byte Total(ReadOnlySpan<byte> slots, int at) => (at * 2) + 1 < slots.Length ? slots[(at * 2) + 1] : (byte)0;

    /// <summary><c>panimvalue[at].value</c>; zero past the file, which the engine would read from memory beyond.</summary>
    private static short Slot(ReadOnlySpan<byte> slots, int at) =>
        (at * 2) + 2 <= slots.Length ? BinaryPrimitives.ReadInt16LittleEndian(slots[(at * 2)..]) : (short)0;

    /// <summary><c>QuaternionBlend</c>'s scalar path: align, then <c>QuaternionBlendNoAlign</c> and its normalize.</summary>
    private static (float X, float Y, float Z, float W) QuaternionBlend(
        (float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q, float t)
    {
        (float X, float Y, float Z, float W) q2 = BlendAlign(p, q);
        float sclp = 1.0f - t;
        float sclq = t;

        (float X, float Y, float Z, float W) qt = (
            (sclp * p.X) + (sclq * q2.X),
            (sclp * p.Y) + (sclq * q2.Y),
            (sclp * p.Z) + (sclq * q2.Z),
            (sclp * p.W) + (sclq * q2.W));

        float radius = (qt.X * qt.X) + (qt.Y * qt.Y) + (qt.Z * qt.Z) + (qt.W * qt.W);

        // `if ( radius )`: true for anything but zero, a NaN included.
        if (radius > 0f || float.IsNaN(radius))
        {
            radius = MathF.Sqrt(radius);
            float iradius = 1.0f / radius;

            qt = (qt.X * iradius, qt.Y * iradius, qt.Z * iradius, qt.W * iradius);
        }

        return qt;
    }

    private static List<StudioBonePose> PoseOf(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> owner,
        int[]? remap,
        int animation,
        int frame,
        Lists lists,
        List<StudioBonePose> into)
    {
        if (remap is null)
        {
            return PoseInto(file, owner, animation, frame, into, lists.Entries);
        }

        List<StudioBonePose> pose = PoseInto(file, owner, animation, frame, lists.Raw, lists.Entries);

        List<StudioBonePose> renumbered = into;

        renumbered.Clear();

        foreach (StudioBonePose moved in pose)
        {
            int bone = moved.Bone >= 0 && moved.Bone < remap.Length ? remap[moved.Bone] : -1;

            if (bone >= 0)
            {
                renumbered.Add(moved with { Bone = bone });
            }
        }

        return renumbered;
    }

    private static int Count(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < HeaderAnimationIndexOffset + 4
            ? 0
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationCountOffset..]));
    }

    private static int Flags(ReadOnlyMemory<byte> file, int animation)
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

    private static bool IsDelta(ReadOnlyMemory<byte> file, int animation) =>
        (Flags(file, animation) & StudioFlags.SequenceDelta) != 0;

    private static List<StudioBonePose> PoseInto(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int animation,
        int frame,
        List<StudioBonePose> poses,
        List<(int Bone, int Flags, int At)> chain)
    {
        poses.Clear();
        chain.Clear();

        ReadOnlySpan<byte> bytes = file.Span;

        if (animation < 0 || animation >= Count(file) || bones.Count == 0)
        {
            return poses;
        }

        Chain(bytes, bones, animation, frame, chain, out int wanted);

        poses.EnsureCapacity(chain.Count);

        // A later change of meaning (B620): an absent channel's seed follows the TRACK's delta flag
        // (`bone_setup.cpp:395`, `:511`), where this read the animation's.
        foreach ((int bone, int flags, int payload) in chain)
        {
            poses.Add(ReadBone(bytes[payload..], bones[bone], bone, flags, wanted, (flags & Delta) != 0));
        }

        return poses;
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
            float x = Value(payload, 0, frame) * rest.RotationScale.X;
            float y = Value(payload, 1, frame) * rest.RotationScale.Y;
            float z = Value(payload, 2, frame) * rest.RotationScale.Z;

            if ((flags & Delta) == 0)
            {
                x += rest.Euler.X;
                y += rest.Euler.Y;
                z += rest.Euler.Z;
            }

            rotation = FromEulerRadians(x, y, z);

            if ((flags & Delta) == 0 &&
                (rest.Flags & StudioBoneFlags.FixedAlignment) != 0)
            {
                rotation = BoneAlign(rest.Alignment, rotation);
            }
        }

        (float X, float Y, float Z) position = additive ? (0f, 0f, 0f) : rest.Position;

        if ((flags & RawPosition) != 0 && payload.Length >= rotationBytes + 6)
        {
            position = Vector48(payload[rotationBytes..]);
        }
        else if ((flags & AnimatedPosition) != 0)
        {
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

    private static void Chain(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<StudioBone> bones,
        int animation,
        int frame,
        List<(int Bone, int Flags, int At)> chain,
        out int wanted)
    {
        wanted = 0;

        chain.Clear();

        if (animation < 0 || bones.Count == 0)
        {
            return;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        if (at < 0 || at + AnimationStride > bytes.Length)
        {
            return;
        }

        ReadOnlySpan<byte> description = bytes[at..];

        int frames = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationFrameCountOffset..]);
        int block = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationBlockOffset..]);
        int data = BinaryPrimitives.ReadInt32LittleEndian(description[AnimationDataOffset..]);

        if (frames <= 0)
        {
            return;
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
                return;
            }

            block = BinaryPrimitives.ReadInt32LittleEndian(bytes[entry..]);
            data = BinaryPrimitives.ReadInt32LittleEndian(
                bytes[(entry + AnimationSectionDataOffset)..]);

            wanted = local;
        }

        if (block != 0 || data == 0)
        {
            return;
        }

        int cursor = at + data;

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

            chain.Add((bone, flags, cursor + AnimationEntryHeaderBytes));

            if (next == 0)
            {
                break;
            }

            cursor += next;
        }
    }

    private static (int Section, int Frame) Section(int frames, int sectionFrames, int frame)
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

    private static float Value(ReadOnlySpan<byte> payload, int channel, int frame)
    {
        const int tableAt = 0;

        if (payload.Length < tableAt + 6)
        {
            return 0f;
        }

        int offset = BinaryPrimitives.ReadInt16LittleEndian(payload[(tableAt + (channel * 2))..]);

        if (offset <= 0 || offset >= payload.Length)
        {
            return 0f;
        }

        ReadOnlySpan<byte> values = payload[offset..];

        int remaining = frame;
        int at = 0;

        // A later change of meaning (B620): an ending block is tested after a step, as the one-key `ExtractAnimValue`
        // tests it (`bone_setup.cpp:349-359`), where this tested it first.
        while (at + 2 <= values.Length)
        {
            int valid = values[at];
            int total = values[at + 1];

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

            if (at + 2 > values.Length || values[at + 1] == 0)
            {
                return 0f;
            }
        }

        return 0f;
    }

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

    private static (float X, float Y, float Z, float W) Quaternion64(ReadOnlySpan<byte> from)
    {
        ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(from);

        float x = ((long)(packed & 0x1FFFFF) - 1048576) * (1f / 1048576f);
        float y = ((long)((packed >> 21) & 0x1FFFFF) - 1048576) * (1f / 1048576f);
        float z = ((long)((packed >> 42) & 0x1FFFFF) - 1048576) * (1f / 1048576f);

        float w = MathF.Sqrt(Math.Max(0f, 1f - (x * x) - (y * y) - (z * z)));

        return (x, y, z, (packed >> 63) != 0 ? -w : w);
    }

    private static (float X, float Y, float Z) Vector48(ReadOnlySpan<byte> from) =>
        (
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from)),
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from[2..])),
            (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(from[4..])));

    private static (float X, float Y, float Z, float W) FromEulerRadians(float x, float y, float z)
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

    /// <summary><c>StudioBones.Align</c>, as it was.</summary>
    private static (float X, float Y, float Z, float W) BoneAlign(
        (float X, float Y, float Z, float W) to,
        (float X, float Y, float Z, float W) rotation)
    {
        float apart =
            ((to.X - rotation.X) * (to.X - rotation.X)) +
            ((to.Y - rotation.Y) * (to.Y - rotation.Y)) +
            ((to.Z - rotation.Z) * (to.Z - rotation.Z)) +
            ((to.W - rotation.W) * (to.W - rotation.W));

        float together =
            ((to.X + rotation.X) * (to.X + rotation.X)) +
            ((to.Y + rotation.Y) * (to.Y + rotation.Y)) +
            ((to.Z + rotation.Z) * (to.Z + rotation.Z)) +
            ((to.W + rotation.W) * (to.W + rotation.W));

        return apart > together
            ? (-rotation.X, -rotation.Y, -rotation.Z, -rotation.W)
            : rotation;
    }

    /// <summary><c>QuaternionAlign</c>, which <c>QuaternionBlend</c> calls first.</summary>
    private static (float X, float Y, float Z, float W) BlendAlign(
        (float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        float apart =
            ((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)) +
            ((p.Z - q.Z) * (p.Z - q.Z)) + ((p.W - q.W) * (p.W - q.W));

        float together =
            ((p.X + q.X) * (p.X + q.X)) + ((p.Y + q.Y) * (p.Y + q.Y)) +
            ((p.Z + q.Z) * (p.Z + q.Z)) + ((p.W + q.W) * (p.W + q.W));

        return apart > together ? (-q.X, -q.Y, -q.Z, -q.W) : q;
    }
}
