using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A frame pair sampled exactly as it was at <c>f19b0d83</c>, before the single-pass rewrite
/// (docs/measurements/2026-10-31-setup-bones-read.md M1): <c>SkinnedModel.PoseBetween</c> and <c>PoseOf</c>, which read
/// the frame and the frame after it as two whole <c>StudioAnimation.PoseInto</c> passes, then expanded both and blended
/// them through <c>StudioPoseBlend.BlendInto</c>. Every function on that path is copied verbatim, the leaves too
/// (<c>Value</c>, <c>Chain</c>, the packed decoders, <c>FromEulerRadians</c>, both aligns, <c>Normalize</c>).
/// **Kept only here, never in production**: it is the reference the rewrite is held bit-identical to, and a copy that
/// called production would be broken the same way production is.
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

    /// <summary>The old <c>PoseBetween</c>; <paramref name="remap"/> null stands for <c>group == 0</c> or no remap.</summary>
    public static IReadOnlyList<StudioBonePose> PoseBetween(
        ReadOnlyMemory<byte> file,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        float fraction,
        Lists lists)
    {
        List<StudioBonePose> pose = PoseOf(file, bones, remap, animation, frame, lists, lists.Frame);

        if (fraction <= 0f || pose.Count == 0)
        {
            return pose;
        }

        List<StudioBonePose> next = PoseOf(file, bones, remap, animation, frame + 1, lists, lists.Next);

        return next.Count == 0
            ? pose
            : BlendInto(bones, pose, next, fraction, IsDelta(file, animation), lists.Between, lists.Scratch);
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

        bool additive = IsDelta(file, animation);

        Chain(bytes, bones, animation, frame, chain, out int wanted);

        poses.EnsureCapacity(chain.Count);

        foreach ((int bone, int flags, int payload) in chain)
        {
            poses.Add(ReadBone(bytes[payload..], bones[bone], bone, flags, wanted, additive));
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

    private static List<StudioBonePose> BlendInto(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> first,
        IReadOnlyList<StudioBonePose> second,
        float s,
        bool additive,
        List<StudioBonePose> into,
        List<StudioBonePose> scratch)
    {
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(into, bones.Count);
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(scratch, bones.Count);

        Span<StudioBonePose> left = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(into);
        Span<StudioBonePose> right = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(scratch);

        if (additive)
        {
            ExpandIdentityInto(first, left);
            ExpandIdentityInto(second, right);
        }
        else
        {
            ExpandInto(bones, first, left);
            ExpandInto(bones, second, right);
        }

        float weightFirst = 1f - s;

        for (int bone = 0; bone < left.Length; bone++)
        {
            (float X, float Y, float Z, float W) p = left[bone].Rotation;

            (float X, float Y, float Z, float W) q =
                (bones[bone].Flags & StudioBoneFlags.FixedAlignment) != 0
                    ? right[bone].Rotation
                    : BlendAlign(p, right[bone].Rotation);

            (float X, float Y, float Z) a = left[bone].Position;
            (float X, float Y, float Z) b = right[bone].Position;

            left[bone] = new StudioBonePose(
                bone,
                ((a.X * weightFirst) + (b.X * s),
                 (a.Y * weightFirst) + (b.Y * s),
                 (a.Z * weightFirst) + (b.Z * s)),
                Normalize((
                    (p.X * weightFirst) + (q.X * s),
                    (p.Y * weightFirst) + (q.Y * s),
                    (p.Z * weightFirst) + (q.Z * s),
                    (p.W * weightFirst) + (q.W * s))));
        }

        return into;
    }

    private static void ExpandIdentityInto(IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            full[bone] = new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }

        Overlay(pose, full);
    }

    private static void ExpandInto(
        IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            StudioBone rest = bones[bone];

            full[bone] = new StudioBonePose(bone, rest.Position, rest.Rotation);
        }

        Overlay(pose, full);
    }

    private static void Overlay(IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int entry = 0; entry < pose.Count; entry++)
        {
            StudioBonePose moved = pose[entry];

            if (moved.Bone >= 0 && moved.Bone < full.Length)
            {
                full[moved.Bone] = moved;
            }
        }
    }

    /// <summary><c>StudioPoseBlend.Align</c>, as it was.</summary>
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

    private static (float X, float Y, float Z, float W) Normalize(
        (float X, float Y, float Z, float W) q)
    {
        float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));

        return length > 0f ? (q.X / length, q.Y / length, q.Z / length, q.W / length) : q;
    }
}
