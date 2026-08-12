using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Property-based tests for varint decoding.
/// </summary>
/// <remarks>
/// Every encoding in <c>VarIntTests</c> was worked out by hand — 300 as <c>AC 02</c>,
/// <c>uint.MaxValue</c> as <c>FF FF FF FF 0F</c>, and so on. That verifies the cases I thought
/// of, and quietly assumes I did the arithmetic right. A round trip assumes nothing: the
/// encoder here is written from the format description, and any value must survive it.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified, so the property " +
                    "delegate is the assertion. Sonar does not know the library.")]
public sealed class VarIntPropertyTests
{
    /// <summary>
    /// Encodes a varint: seven payload bits per byte, least-significant group first, high bit
    /// set on every byte but the last.
    /// </summary>
    private static void WriteVarInt(BitWriter writer, ulong value)
    {
        while (value >= 0x80)
        {
            writer.Write((uint)((value & 0x7F) | 0x80), 8);
            value >>= 7;
        }

        writer.Write((uint)value, 8);
    }

    private static void WriteZigZag(BitWriter writer, long value) =>
        WriteVarInt(writer, (ulong)((value << 1) ^ (value >> 63)));

    [Test]
    public void AnyUInt32_SurvivesARoundTrip()
    {
        Gen.UInt.Sample(value =>
        {
            BitWriter writer = new();
            WriteVarInt(writer, value);
            BitReader reader = new(writer.Build());

            return VarInt.ReadUInt32(ref reader) == value;
        });
    }

    [Test]
    public void AnyUInt64_SurvivesARoundTrip()
    {
        Gen.ULong.Sample(value =>
        {
            BitWriter writer = new();
            WriteVarInt(writer, value);
            BitReader reader = new(writer.Build());

            return VarInt.ReadUInt64(ref reader) == value;
        });
    }

    [Test]
    public void AnyInt32_SurvivesAZigZagRoundTrip()
    {
        Gen.Int.Sample(value =>
        {
            BitWriter writer = new();
            WriteZigZag(writer, value);
            BitReader reader = new(writer.Build());

            return VarInt.ReadInt32(ref reader) == value;
        });
    }

    [Test]
    public void AnyInt64_SurvivesAZigZagRoundTrip()
    {
        Gen.Long.Sample(value =>
        {
            BitWriter writer = new();
            WriteZigZag(writer, value);
            BitReader reader = new(writer.Build());

            return VarInt.ReadInt64(ref reader) == value;
        });
    }

    [Test]
    public void ASequenceOfVarInts_ReadsBackInOrder()
    {
        // Varints are self-delimiting, so a wrong continuation-bit test would misread the
        // *next* value rather than this one. A single value in isolation cannot catch that.
        Gen.UInt.Array[1, 30].Sample(values =>
        {
            BitWriter writer = new();
            foreach (uint value in values)
            {
                WriteVarInt(writer, value);
            }

            BitReader reader = new(writer.Build());
            foreach (uint value in values)
            {
                if (VarInt.ReadUInt32(ref reader) != value)
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Test]
    public void VarIntsStartingAtAnyBitOffset_StillDecode()
    {
        // Varints are byte-oriented but not byte-aligned: Source reads them through the same
        // bit reader as everything else, so one can begin mid-byte.
        Gen.Select(Gen.UInt, Gen.Int[0, 7]).Sample(t =>
        {
            (uint value, int offset) = t;

            BitWriter writer = new();
            if (offset > 0)
            {
                writer.Write(0, offset);
            }

            WriteVarInt(writer, value);
            BitReader reader = new(writer.Build());

            if (offset > 0)
            {
                _ = reader.ReadUInt32(offset);
            }

            return VarInt.ReadUInt32(ref reader) == value;
        });
    }

    [Test]
    public void ZigZagKeepsSmallMagnitudesShort()
    {
        // The reason zig-zag exists: -1 must cost one byte, not five. Without it every
        // negative number sets the high bit and takes the maximum width.
        Gen.Int[-63, 63].Sample(value =>
        {
            BitWriter writer = new();
            WriteZigZag(writer, value);

            return writer.BitCount == 8;
        });
    }
}
