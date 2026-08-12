using System;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Property-based tests for the bit reader, stated as round trips rather than fixtures.
/// </summary>
/// <remarks>
/// These exist because hand-written fixtures have been the least reliable part of this test
/// suite. Several bugs found during layer 2 were in the *fixtures* — a byte-aligned message
/// appended to another, padding forgotten, an expected value computed wrongly by hand. A round
/// trip has no expected value to get wrong: whatever went in must come out.
///
/// The other gain is shrinking. The seeded-random layer in <c>BitReaderFuzzPropertyTests</c>
/// can only report that one of two thousand buffers failed; CsCheck reduces a failure to its
/// minimal case and prints a seed that reproduces it exactly.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified, so the property " +
                    "delegate is the assertion. Sonar does not know the library. Suppressed " +
                    "at class scope rather than project-wide, because S2699 is worth keeping " +
                    "for ordinary tests.")]
public sealed class BitReaderPropertyTests
{
    [Test]
    public void AnyValueAtAnyWidth_SurvivesARoundTrip()
    {
        Gen.Select(Gen.UInt, Gen.Int[1, 32]).Sample(t =>
        {
            (uint value, int bits) = t;
            uint masked = Mask(value, bits);

            BitWriter writer = new();
            writer.Write(masked, bits);
            BitReader reader = new(writer.Build());

            return reader.ReadUInt32(bits) == masked;
        });
    }

    [Test]
    public void ASequenceOfWidths_ReadsBackInOrder()
    {
        // The case fixtures kept getting wrong: values packed at arbitrary, unaligned offsets
        // rather than one value in isolation.
        Gen.Select(Gen.UInt, Gen.Int[1, 32]).Array[1, 20].Sample(fields =>
        {
            BitWriter writer = new();
            foreach ((uint value, int bits) in fields)
            {
                writer.Write(Mask(value, bits), bits);
            }

            BitReader reader = new(writer.Build());
            foreach ((uint value, int bits) in fields)
            {
                if (reader.ReadUInt32(bits) != Mask(value, bits))
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Test]
    public void ReadingAdvancesByExactlyTheWidthRequested()
    {
        Gen.Select(Gen.UInt, Gen.Int[1, 32]).Sample(t =>
        {
            (uint value, int bits) = t;

            BitWriter writer = new();
            writer.Write(Mask(value, bits), bits);
            BitReader reader = new(writer.Build());

            int before = reader.BitsRead;
            _ = reader.ReadUInt32(bits);

            return reader.BitsRead - before == bits;
        });
    }

    [Test]
    public void ValuesAreAlwaysZeroExtended()
    {
        // No read may return bits above its requested width, whatever surrounds it.
        Gen.Select(Gen.UInt, Gen.Int[1, 31]).Sample(t =>
        {
            (uint value, int bits) = t;

            BitWriter writer = new();
            writer.Write(uint.MaxValue, 32);   // noise before
            writer.Write(Mask(value, bits), bits);
            writer.Write(uint.MaxValue, 32);   // noise after

            BitReader reader = new(writer.Build());
            _ = reader.ReadUInt32(32);

            return reader.ReadUInt32(bits) >> bits == 0;
        });
    }

    [Test]
    public void BitsReadAndBitsRemainingAlwaysSumToTheBufferSize()
    {
        Gen.Byte.Array[1, 64].Select(Gen.Int[0, 200]).Sample(t =>
        {
            (byte[] bytes, int reads) = t;
            BitReader reader = new(bytes);
            int total = bytes.Length * 8;

            for (int i = 0; i < reads && reader.BitsRemaining > 0; i++)
            {
                _ = reader.ReadBit();
                if (reader.BitsRead + reader.BitsRemaining != total)
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static uint Mask(uint value, int bits) =>
        bits == 32 ? value : value & ((1u << bits) - 1);
}
