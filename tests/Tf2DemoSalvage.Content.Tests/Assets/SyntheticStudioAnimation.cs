using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A hand-built <c>.mdl</c> whose animations carry real bone data, every reader branch reachable (B620).</summary>
/// <remarks>
/// Built for <c>FramePairDifferentialTests</c>, whose remarks list the branches each animation reaches. Its own
/// file so the Scene tests can link it (B742): a blend grid whose corners have different frame counts needs animations
/// that move a bone, which the Scene fixtures do not write.
/// </remarks>
internal static class SyntheticStudioAnimation
{
    private const int Descriptions = 256;

    private sealed record Track(int Bone, int Flags, byte[] Payload);

    private sealed record Animation(
        int Frames,
        Track[][] Chains,
        int Flags = 0,
        int SectionFrames = 0,
        int Block = 0,
        int[]? SectionMap = null,
        int[]? SectionBlocks = null);

    /// <summary>Eight bones, each with its own rest, Euler, scales; bone 5 fixed-aligned, bone 7 with a zero roll scale.</summary>
    internal static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, 8).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, (2f * bone) + 0.5f, -bone * 0.25f),
            Rotation: (0.1f * bone, 0.2f, 0.3f, 0.9f),
            PoseToBone: default,
            Euler: (0.1f * bone, -0.2f, 0.05f * bone),
            PositionScale: (0.01f, 0.02f, 0.03f * (bone + 1)),
            RotationScale: (bone == 7 ? 0f : 0.001f, 0.0005f * (bone + 1), 0.002f),
            Flags: bone == 5 ? StudioBoneFlags.FixedAlignment : 0,
            Alignment: bone == 5 ? (0f, 0f, -0.7071f, -0.7071f) : default)),
    ];

    /// <summary>
    /// The model: animation 0 has six frames, 1 is a four-frame delta, 2 and 3 are sectioned ten-frame animations,
    /// 4 has one frame, 5 is blocked, 6 names an absent bone, 7 is empty, 8 has three frames, 9 holds its trailing
    /// section in an external block.
    /// </summary>
    internal static byte[] Synthetic()
    {
        const int rawRot = StudioFlags.AnimationRawRotation;
        const int rawRot64 = StudioFlags.AnimationRawRotation64;
        const int rawPos = StudioFlags.AnimationRawPosition;
        const int animRot = StudioFlags.AnimationAnimatedRotation;
        const int animPos = StudioFlags.AnimationAnimatedPosition;
        const int delta = StudioFlags.AnimationDelta;

        Track[] mixed =
        [
            new(0, rawRot64 | rawPos, [.. Q64(0x8123_4567_89AB_CDEFUL), .. V48(1.5f, -2.25f, 3f)]),
            new(1, rawRot | animPos, [.. Q48(40000, 20000, 0x8000 | 9000), 0, 0, 0, 0, 0, 0]),
            new(2, animRot | animPos, Animated(
                [Rle((2, 3, [100, -200]), (1, 3, [300])), Rle((6, 6, [1, 2, 3, 4, 5, 6])), Rle((1, 6, [50]))],
                [Rle((3, 2, [7, 8, 9]), (1, 4, [10])), null, Rle((2, 2, [5, 6]), (0, 0, []))])),
            new(3, animRot | delta, Animated(
                [Rle((3, 6, [1000, -1000, 1000])), null, Rle((1, 1, [5]), (1, 5, [6]))], null)),
            new(4, 0, []),
            new(5, animRot, Animated(
                [Rle((6, 6, [0, 500, 1000, 1500, 2000, 2500])), Rle((1, 6, [-300])), Rle((2, 6, [30000, -30000]))],
                null)),
            new(7, animRot | delta, Animated(
                [Rle((6, 6, [5, -3, 0, 7, -7, 0])), Rle((1, 6, [100])), Rle((1, 6, [0]))], null)),
            new(2, rawPos, [.. V48(-4f, 0.125f, 9f)]),
        ];

        Track[] Section(short a, short b) =>
        [
            new(2, animRot | animPos, Animated(
                [Rle((5, 5, [a, (short)(a + 10), (short)(a + 20), (short)(a + 30), (short)(a + 40)])), Rle((1, 5, [b])), null],
                [Rle((2, 5, [b, a])), null, null])),
            new(6, rawRot, [.. Q48((ushort)(32768 + a), (ushort)(32768 - b), 16384)]),
        ];

        Animation[] animations =
        [
            new(6, [mixed]),
            new(4, [[
                new(1, animRot, Animated([Rle((4, 4, [10, 20, 30, 40])), Rle((1, 4, [7])), null], null)),
                new(4, rawPos, [.. V48(0.5f, 0.25f, 0.125f)]),
                new(6, 0, []),
            ]], Flags: StudioFlags.SequenceDelta),
            new(10, [Section(100, 1), Section(200, 2), Section(300, 3), Section(400, 4)], SectionFrames: 4),
            new(10, [Section(500, 5)], SectionFrames: 4, SectionMap: [0, 0, 0, 0]),
            new(1, [mixed]),
            new(6, [mixed], Block: 1),
            new(6, [[new(200, rawPos, [.. V48(1f, 1f, 1f)])]]),
            new(0, [mixed]),
            new(3, [Section(600, 6)], SectionFrames: 4),

            // The trailing last-frame section lives in an external block, so frame 8 reads and frame 9 does not.
            new(10, [Section(700, 7), Section(800, 8), Section(900, 9)], SectionFrames: 4, SectionBlocks: [0, 0, 0, 1]),
        ];

        byte[] file = new byte[16384];
        int data = Descriptions + (animations.Length * StudioLayout.AnimationStride);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderAnimationCountOffset), animations.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderAnimationIndexOffset), Descriptions);

        for (int index = 0; index < animations.Length; index++)
        {
            Animation animation = animations[index];
            int at = Descriptions + (index * StudioLayout.AnimationStride);

            Write32(file, at + StudioLayout.AnimationFlagsOffset, animation.Flags);
            Write32(file, at + StudioLayout.AnimationFrameCountOffset, animation.Frames);
            Write32(file, at + StudioLayout.AnimationBlockOffset, animation.Block);

            int[] chains = new int[animation.Chains.Length];

            for (int chain = 0; chain < chains.Length; chain++)
            {
                chains[chain] = data;
                data = WriteChain(file, data, animation.Chains[chain]);
            }

            if (animation.SectionFrames == 0)
            {
                Write32(file, at + StudioLayout.AnimationDataOffset, chains[0] - at);
                continue;
            }

            int table = data;
            int entries = (animation.Frames / animation.SectionFrames) + 2;

            Write32(file, at + StudioLayout.AnimationSectionFramesOffset, animation.SectionFrames);
            Write32(file, at + StudioLayout.AnimationSectionIndexOffset, table - at);
            Write32(file, at + StudioLayout.AnimationDataOffset, chains[0] - at);

            for (int section = 0; section < entries; section++)
            {
                int chain = animation.SectionMap?[Math.Min(section, animation.SectionMap.Length - 1)] ??
                    Math.Min(section, chains.Length - 1);

                Write32(
                    file,
                    table + (section * StudioLayout.AnimationSectionStride),
                    animation.SectionBlocks is { } blocks && section < blocks.Length ? blocks[section] : 0);
                Write32(
                    file,
                    table + (section * StudioLayout.AnimationSectionStride) + StudioLayout.AnimationSectionDataOffset,
                    chains[chain] - at);
            }

            data = table + (entries * StudioLayout.AnimationSectionStride) + 8;
        }

        return file;
    }

    private static int WriteChain(byte[] file, int cursor, Track[] tracks)
    {
        for (int entry = 0; entry < tracks.Length; entry++)
        {
            Track track = tracks[entry];
            int size = 4 + track.Payload.Length;
            bool last = entry == tracks.Length - 1;

            file[cursor] = (byte)track.Bone;
            file[cursor + 1] = (byte)track.Flags;
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(cursor + 2), last ? (short)0 : (short)size);
            track.Payload.CopyTo(file, cursor + 4);

            cursor += size;
        }

        return cursor + 16;
    }

    private static void Write32(byte[] file, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), value);

    /// <summary>A run-length channel: each block is <c>valid</c>, <c>total</c>, then the valid values.</summary>
    private static byte[] Rle(params (int Valid, int Total, short[] Values)[] blocks)
    {
        List<byte> bytes = [];

        foreach ((int valid, int total, short[] values) in blocks)
        {
            bytes.Add((byte)valid);
            bytes.Add((byte)total);

            foreach (short value in values)
            {
                bytes.Add((byte)(value & 0xFF));
                bytes.Add((byte)((value >> 8) & 0xFF));
            }
        }

        return [.. bytes];
    }

    /// <summary>
    /// Animated channels as the reader lays them out: the rotation table, the position table after it when both are
    /// animated, then the streams. A null channel has offset zero, which reads as zero.
    /// </summary>
    private static byte[] Animated(byte[]?[]? rotation, byte[]?[]? position)
    {
        int tables = (rotation is null ? 0 : 6) + (position is null ? 0 : 6);
        List<byte> bytes = [.. new byte[tables]];

        void Table(byte[]?[] channels, int tableAt)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (channels[channel] is not { } stream)
                {
                    continue;
                }

                BinaryPrimitives.WriteInt16LittleEndian(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes)[(tableAt + (channel * 2))..],
                    (short)(bytes.Count - tableAt));

                bytes.AddRange(stream);
            }
        }

        if (rotation is not null)
        {
            Table(rotation, 0);
        }

        if (position is not null)
        {
            Table(position, rotation is null ? 0 : 6);
        }

        return [.. bytes];
    }

    private static byte[] Q48(ushort x, ushort y, ushort z)
    {
        byte[] bytes = new byte[6];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, x);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), y);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), z);

        return bytes;
    }

    private static byte[] Q64(ulong packed)
    {
        byte[] bytes = new byte[8];

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, packed);

        return bytes;
    }

    private static byte[] V48(float x, float y, float z)
    {
        byte[] bytes = new byte[6];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, BitConverter.HalfToUInt16Bits((Half)x));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), BitConverter.HalfToUInt16Bits((Half)y));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), BitConverter.HalfToUInt16Bits((Half)z));

        return bytes;
    }
}
