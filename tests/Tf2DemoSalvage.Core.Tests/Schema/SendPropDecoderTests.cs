using System;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Tests for decoding individual property values.
/// </summary>
/// <remarks>
/// These decoders are testable in isolation, which is why they were built before entity
/// iteration: a round trip needs no hand-computed expectation and no knowledge of how
/// properties are addressed.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified.")]
public sealed class SendPropDecoderTests
{
    private const int Unsigned = 1 << 0;
    private const int Coord = 1 << 1;
    private const int NoScale = 1 << 2;
    private const int Normal = 1 << 5;
    private const int CoordMp = 1 << 13;
    private const int CoordMpLowPrecision = 1 << 14;
    private const int CoordMpIntegral = 1 << 15;

    /// <summary>The same bit as <c>Normal</c>, meaning varint when the property is an integer.</summary>
    private const int VarInt = 1 << 5;

    private static SendProperty Property(
        SendPropType type = SendPropType.Int,
        int flags = 0,
        int bits = 8,
        float low = 0f,
        float high = 1f) =>
        new(type, "p", flags, string.Empty, low, high, bits, 0);

    [Test]
    public void UnsignedInt_RoundTripsAtAnyWidth()
    {
        Gen.Select(Gen.UInt, Gen.Int[1, 31]).Sample(t =>
        {
            (uint value, int bits) = t;
            uint masked = value & ((1u << bits) - 1);

            BitWriter writer = new();
            writer.Write(masked, bits);
            BitReader reader = new(writer.Build());

            return SendPropDecoder.ReadInt(ref reader, Property(flags: Unsigned, bits: bits))
                   == (int)masked;
        });
    }

    [Test]
    public void SignedInt_IsSignExtendedFromItsOwnWidth()
    {
        // The failure this guards: a negative value read at 11 bits comes back as a large
        // positive one, which is wrong in a way that still looks like a plausible number.
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[12, 31]).Sample(t =>
        {
            (int value, int bits) = t;

            BitWriter writer = new();
            writer.Write((uint)value & ((1u << bits) - 1), bits);
            BitReader reader = new(writer.Build());

            return SendPropDecoder.ReadInt(ref reader, Property(bits: bits)) == value;
        });
    }

    [Test]
    public void UnsignedThirtyTwoBitInteger_DoesNotWrapNegative()
    {
        // The whole range of a uint cannot fit in an int, so a 32-bit unsigned property read
        // into one comes back as -1 for 0xFFFFFFFF. Found by the per-snapshot differential:
        // the oracle reported 4294967295 for properties 618 and 619 where this parser said -1.
        //
        // Same bits either way, so it never desynchronised - it just reported a wrong number,
        // which is the failure mode this codebase keeps meeting.
        BitWriter writer = new();
        writer.Write(0xFFFFFFFF, 32);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(flags: Unsigned, bits: 32))
            .ShouldBe(4294967295L);
    }
    [TestCase(2147483648L)]
    [TestCase(3000000000L)]
    [TestCase(4294967294L)]
    public void UnsignedValuesAboveIntMaxValue_SurviveIntact(long expected)
    {
        BitWriter writer = new();
        writer.Write((uint)expected, 32);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(flags: Unsigned, bits: 32))
            .ShouldBe(expected);
    }

    [Test]
    public void SignedThirtyTwoBitInteger_IsStillNegativeWhereItShouldBe()
    {
        // The control. Widening the return type must not turn signed -1 into 4294967295.
        BitWriter writer = new();
        writer.Write(0xFFFFFFFF, 32);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(bits: 32)).ShouldBe(-1L);
    }
    [TestCase(-1, 11)]
    [TestCase(-2048, 12)]
    [TestCase(2047, 12)]
    [TestCase(-1, 32)]
    public void SignedInt_KnownBoundaries(int value, int bits)
    {
        BitWriter writer = new();
        writer.Write((uint)value & (bits == 32 ? uint.MaxValue : (1u << bits) - 1), bits);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(bits: bits)).ShouldBe(value);
    }

    [SuppressMessage("Major Code Smell", "S1244:Do not check floating point equality",
        Justification = "Exactness is the entire point of SPROP_NOSCALE - the flag exists so " +
                        "the value survives untouched. A tolerance here would pass even if " +
                        "the encoding quietly became lossy.")]
    [Test]
    public void NoScaleFloat_IsExactlyTheOriginal()
    {
        // SPROP_NOSCALE exists precisely so a value survives untouched, so this is the one
        // float encoding where exact equality is the right assertion.
        Gen.Float.Where(f => !float.IsNaN(f)).Sample(value =>
        {
            BitWriter writer = new();
            writer.Write((uint)BitConverter.SingleToInt32Bits(value), 32);
            BitReader reader = new(writer.Build());

            return SendPropDecoder.ReadFloat(ref reader, Property(flags: NoScale)) == value;
        });
    }

    [Test]
    public void RangeEncodedFloat_LandsWithinItsQuantisationStep()
    {
        // Range encoding is lossy by design: the value is a fraction of the way from low to
        // high, so the round trip is only accurate to one step.
        Gen.Select(Gen.Float[0f, 100f], Gen.Int[4, 20]).Sample(t =>
        {
            (float value, int bits) = t;
            SendProperty property = Property(bits: bits, low: 0f, high: 100f);

            uint quantised = (uint)MathF.Round(value / 100f * ((1L << bits) - 1));
            BitWriter writer = new();
            writer.Write(quantised, bits);
            BitReader reader = new(writer.Build());

            float decoded = SendPropDecoder.ReadFloat(ref reader, property);
            float step = 100f / ((1L << bits) - 1);

            return MathF.Abs(decoded - value) <= step;
        });
    }
    [TestCase(0u, 0f)]
    [TestCase(255u, 100f)]
    public void RangeEncodedFloat_HitsItsEndpointsExactly(uint raw, float expected)
    {
        BitWriter writer = new();
        writer.Write(raw, 8);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(bits: 8, low: 0f, high: 100f))
            .ShouldBe(expected, 0.001f);
    }

    [Test]
    public void Normal_DecodesSignAndMagnitudeWithinRange()
    {
        Gen.Select(Gen.Bool, Gen.UInt[0, 2047]).Sample(t =>
        {
            (bool negative, uint magnitude) = t;

            BitWriter writer = new();
            writer.Write(negative ? 1u : 0u, 1).Write(magnitude, 11);
            BitReader reader = new(writer.Build());

            float decoded = SendPropDecoder.ReadFloat(ref reader, Property(flags: Normal));

            return decoded >= -1f && decoded <= 1f && (decoded <= 0f) == (negative || magnitude == 0);
        });
    }

    [Test]
    public void Vector_ReadsThreeComponents()
    {
        BitWriter writer = new();
        foreach (float component in new[] { 1f, 2f, 3f })
        {
            writer.Write((uint)BitConverter.SingleToInt32Bits(component), 32);
        }

        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, NoScale))
            .ShouldBe((1f, 2f, 3f));
    }

    [Test]
    public void NormalVector_DerivesTheThirdComponentFromTheFirstTwo()
    {
        // A unit normal only transmits a sign bit for z. Reading a whole float instead would
        // consume bits belonging to the next property - a desynchronisation, not a wrong value.
        BitWriter writer = new();
        writer.Write(0, 1).Write(0, 11);   // x = 0
        writer.Write(0, 1).Write(0, 11);   // y = 0
        writer.Write(1, 1);                // z is negative
        BitReader reader = new(writer.Build());

        (float x, float y, float z) =
            SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, Normal));

        x.ShouldBe(0f);
        y.ShouldBe(0f);
        z.ShouldBe(-1f, 0.0001f);
    }

    [Test]
    public void VectorXY_ReadsTwoComponentsAndStopsThere()
    {
        BitWriter writer = new();
        writer.Write((uint)BitConverter.SingleToInt32Bits(4f), 32);
        writer.Write((uint)BitConverter.SingleToInt32Bits(5f), 32);
        writer.Write(0xABCD, 16);          // must remain unread
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadVectorXY(ref reader, Property(SendPropType.VectorXY, NoScale))
            .ShouldBe((4f, 5f));
        reader.ReadUInt32(16).ShouldBe(0xABCDu);
    }

    [Test]
    public void String_IsLengthPrefixedNotNulTerminated()
    {
        // The message layer uses NUL-terminated strings; entity properties use a nine-bit
        // length prefix. Confusing the two desynchronises the entity rather than failing.
        BitWriter writer = new();
        writer.Write(5, 9);
        foreach (byte b in "hello"u8)
        {
            writer.Write(b, 8);
        }

        writer.Write(0x7F, 7);             // must remain unread
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadString(ref reader).ShouldBe("hello");
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }

    [Test]
    public void String_WithMultiByteCharacters_IsReadAsUtf8()
    {
        // The nine-bit prefix counts BYTES, not characters, and this is another place player
        // names appear - an entity's name property is a string property. Reading it as ASCII
        // leaves question marks; reading the length as a character count desynchronises the
        // entity outright.
        byte[] utf8 = "Пётр🚀"u8.ToArray();

        BitWriter writer = new();
        writer.Write((uint)utf8.Length, 9);
        foreach (byte b in utf8)
        {
            writer.Write(b, 8);
        }

        writer.Write(0x7F, 7);             // must remain unread
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadString(ref reader).ShouldBe("Пётр🚀");

        // Proves the length was interpreted as bytes. Three different counts disagree here, which
        // is the point: 5 code points, 6 UTF-16 chars (the emoji is a surrogate pair), 12 bytes.
        // Had the decoder consumed either character count the trailing sentinel would not line up.
        utf8.Length.ShouldBe(12);
        "Пётр🚀".Length.ShouldBe(6);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }

    [Test]
    public void EmptyString_ReadsAsEmptyAndConsumesOnlyItsLength()
    {
        BitWriter writer = new();
        writer.Write(0, 9);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadString(ref reader).ShouldBeEmpty();
        reader.BitsRead.ShouldBe(9);
    }
    [TestCase(0.6f, 0.8f, false)]
    [TestCase(0.6f, 0.8f, true)]
    [TestCase(1f, 0f, false)]
    public void NormalVector_ReconstructsAUnitLengthZ(float x, float y, bool negative)
    {
        // z is derived so the vector is unit length. Getting the arithmetic wrong yields a
        // number rather than an error, so the length is asserted rather than the components.
        BitWriter writer = new();
        writer.Write(0, 1).Write((uint)MathF.Round(x * 2047), 11);
        writer.Write(0, 1).Write((uint)MathF.Round(y * 2047), 11);
        writer.Write(negative ? 1u : 0u, 1);
        BitReader reader = new(writer.Build());

        (float dx, float dy, float dz) =
            SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, Normal));

        MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)).ShouldBe(1f, 0.01f);
        (dz < 0).ShouldBe(negative && MathF.Abs(dz) > 0.0001f);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NormalVector_WithASlackComponentSum_HasANonZeroZ(bool negative)
    {
        // Every other normal-vector test here happens to produce z = 0, which makes the sign
        // bit and the square root untestable - mutation testing caught exactly that. Half
        // magnitudes leave real slack: 0.5^2 + 0.5^2 = 0.5, so z is about 0.707.
        BitWriter writer = new();
        writer.Write(0, 1).Write(1024, 11);
        writer.Write(0, 1).Write(1024, 11);
        writer.Write(negative ? 1u : 0u, 1);
        BitReader reader = new(writer.Build());

        (_, _, float z) =
            SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, Normal));

        MathF.Abs(z).ShouldBe(0.707f, 0.01f);
        (z < 0).ShouldBe(negative);
    }

    [Test]
    public void NormalVector_ComponentsBeyondUnitLength_ClampZToZeroRatherThanNaN()
    {
        // x and y can exceed unit length in malformed data. Without the guard the square root
        // is of a negative number, and NaN would propagate silently through every later
        // calculation instead of failing.
        BitWriter writer = new();
        writer.Write(0, 1).Write(2047, 11);
        writer.Write(0, 1).Write(2047, 11);
        writer.Write(0, 1);
        BitReader reader = new(writer.Build());

        (_, _, float z) =
            SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, Normal));

        float.IsNaN(z).ShouldBeFalse();
        z.ShouldBe(0f);
    }

    [Test]
    public void NormalFloat_UsesTheFullElevenBitRange()
    {
        BitWriter writer = new();
        writer.Write(0, 1).Write(2047, 11);
        BitReader reader = new(writer.Build());

        // 2047 of 2047 is exactly 1.0; a wrong divisor shows up here rather than in the middle.
        SendPropDecoder.ReadFloat(ref reader, Property(flags: Normal)).ShouldBe(1f, 0.0001f);
    }
    [TestCase(0u, -50f)]
    [TestCase(255u, 50f)]
    [TestCase(128u, 0.2f)]
    public void RangeEncodedFloat_SpansANegativeToPositiveRange(uint raw, float expected)
    {
        // Both endpoints and the middle. The lower endpoint alone is not enough: at raw 0 the
        // span is multiplied by zero, so a decoder that added the bounds instead of
        // subtracting them would still return the right answer there.
        BitWriter writer = new();
        writer.Write(raw, 8);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(bits: 8, low: -50f, high: 50f))
            .ShouldBe(expected, 0.5f);
    }
    [TestCase(0u, 0)]
    [TestCase(1u, 1)]
    [TestCase(300u, 300)]
    [TestCase(70000u, 70000)]
    public void UnsignedVarIntInteger_IsReadAsAVarIntNotAFixedWidthField(uint value, int expected)
    {
        // Flag 32 is SPROP_NORMAL on a float and SPROP_VARINT on an integer. Reading a varint
        // as BitCount bits consumes the wrong number of bits and desynchronises everything
        // after it, so the sentinel matters as much as the value.
        BitWriter writer = new();
        WriteVarInt(writer, value);
        writer.Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(flags: Unsigned | VarInt, bits: 8))
            .ShouldBe(expected);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(-300)]
    [TestCase(300)]
    public void SignedVarIntInteger_IsZigZagDecoded(int value)
    {
        // Signed varints are zig-zag encoded, so -1 is one byte rather than five. Decoding a
        // signed varint as unsigned turns -1 into 1 - a plausible number, not an error, which
        // is why both polarities and both magnitudes are here.
        BitWriter writer = new();
        WriteVarInt(writer, (uint)((value << 1) ^ (value >> 31)));
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadInt(ref reader, Property(flags: VarInt, bits: 8)).ShouldBe(value);
    }

    [Test]
    public void VarIntFlagOnAFloat_StillMeansNormalNotVarInt()
    {
        // The same bit, the other meaning. If ReadFloat took the varint path the sentinel
        // below would not line up - which is what makes this a test of the overload rather
        // than of the value.
        BitWriter writer = new();
        writer.Write(0, 1).Write(2047, 11).Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: VarInt)).ShouldBe(1f, 0.0001f);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }

    [Test]
    public void DataTableProperties_AreTheOnlyUnsupportedKind()
    {
        // DataTable is structure rather than a value and never reaches a flattened list.
        // Everything else decodes now, coordinates included.
        SendPropDecoder.IsSupported(Property(SendPropType.DataTable)).ShouldBeFalse();

        foreach (SendPropType type in new[]
        {
            SendPropType.Int, SendPropType.Float, SendPropType.Vector,
            SendPropType.VectorXY, SendPropType.String, SendPropType.Array,
        })
        {
            SendPropDecoder.IsSupported(Property(type)).ShouldBeTrue(type.ToString());
        }
    }

    /// <summary>Writes a base-128 varint, seven bits per byte, low group first.</summary>
    private static void WriteVarInt(BitWriter writer, uint value)
    {
        while (value >= 0x80)
        {
            writer.Write((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }

        writer.Write(value, 8);
    }

    // --- Coordinate encodings ---
    //
    // Bit layouts read from the reference parser's decoder and the SDK's bf_read; the fixture
    // writers below mirror the SDK's bf_write, so a layout misread would have to be made
    // twice, in two different shapes, to go unnoticed. The integer part is stored minus one
    // (a present integer is never zero), which is exactly the kind of off-by-one that decodes
    // to a plausible position rather than an error.

    /// <summary>Writes <c>SPROP_COORD</c>: presence bits, sign, 14-bit integer, 5-bit fraction.</summary>
    private static BitWriter WriteCoord(BitWriter writer, int intPart, int frac, bool negative)
    {
        writer.Write(intPart != 0 ? 1u : 0u, 1).Write(frac != 0 ? 1u : 0u, 1);
        if (intPart != 0 || frac != 0)
        {
            writer.Write(negative ? 1u : 0u, 1);
            if (intPart != 0)
            {
                writer.Write((uint)(intPart - 1), 14);
            }

            if (frac != 0)
            {
                writer.Write((uint)frac, 5);
            }
        }

        return writer;
    }

    /// <summary>Writes <c>SPROP_COORD_MP</c> and its integral / low-precision variants.</summary>
    private static BitWriter WriteCoordMp(
        BitWriter writer,
        bool inBounds,
        int intPart,
        int frac,
        bool negative,
        bool integral = false,
        bool lowPrecision = false)
    {
        writer.Write(inBounds ? 1u : 0u, 1).Write(intPart != 0 ? 1u : 0u, 1);
        if (integral)
        {
            if (intPart != 0)
            {
                writer.Write(negative ? 1u : 0u, 1);
                writer.Write((uint)(intPart - 1), inBounds ? 11 : 14);
            }

            return writer;
        }

        writer.Write(negative ? 1u : 0u, 1);
        if (intPart != 0)
        {
            writer.Write((uint)(intPart - 1), inBounds ? 11 : 14);
        }

        return writer.Write((uint)frac, lowPrecision ? 3 : 5);
    }

    [Test]
    public void Coord_WithNeitherPartPresent_IsZeroAfterExactlyTwoBits()
    {
        BitWriter writer = new();
        WriteCoord(writer, 0, 0, negative: false).Write(0x7F, 7); // sentinel must remain unread
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: Coord)).ShouldBe(0f);
        reader.BitsRead.ShouldBe(2);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }
    [TestCase(5, 0, false, 5f)]
    [TestCase(0, 16, false, 0.5f)]
    [TestCase(3, 16, false, 3.5f)]
    [TestCase(3, 16, true, -3.5f)]
    [TestCase(16384, 31, false, 16384.96875f)]
    public void Coord_KnownValues(int intPart, int frac, bool negative, float expected)
    {
        BitWriter writer = new();
        WriteCoord(writer, intPart, frac, negative);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: Coord))
            .ShouldBe(expected, 0.0001f);
    }

    [SuppressMessage("Major Code Smell", "S1244:Do not check floating point equality",
        Justification = "Coordinates quantise to multiples of 1/32, every one of which is " +
                        "exactly representable in a float. A tolerance would accept a decoder " +
                        "that landed on the wrong grid point, which is the failure being tested.")]
    [Test]
    public void Coord_RoundTripsAcrossItsWholeGrid()
    {
        // Every representable coordinate is a multiple of 1/32, which is exact in a float, so
        // this is equality rather than tolerance. The zero-parts case writes a different
        // layout and has its own test.
        Gen.Select(Gen.Int[0, 16384], Gen.Int[0, 31], Gen.Bool)
            .Where(t => t.Item1 != 0 || t.Item2 != 0)
            .Sample(t =>
            {
                (int intPart, int frac, bool negative) = t;

                BitWriter writer = new();
                WriteCoord(writer, intPart, frac, negative);
                BitReader reader = new(writer.Build());

                float expected = (intPart + (frac / 32f)) * (negative ? -1f : 1f);
                return SendPropDecoder.ReadFloat(ref reader, Property(flags: Coord)) == expected;
            });
    }
    [TestCase(true, 0, 16, false, 0.5f)]
    [TestCase(true, 100, 8, false, 100.25f)]
    [TestCase(false, 5000, 0, false, 5000f)]
    [TestCase(true, 0, 16, true, -0.5f)]
    public void CoordMp_KnownValues(bool inBounds, int intPart, int frac, bool negative, float expected)
    {
        // The in-bounds bit narrows the integer to 11 bits; out of bounds keeps the full 14.
        // 5000 does not fit in 11 bits, so the out-of-bounds row fails if the width selection
        // is inverted.
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds, intPart, frac, negative);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: CoordMp))
            .ShouldBe(expected, 0.0001f);
    }

    [SuppressMessage("Major Code Smell", "S1244:Do not check floating point equality",
        Justification = "As above - the 1/32 grid is exact in a float, and a tolerance would " +
                        "hide a decoder landing on an adjacent grid point.")]
    [Test]
    public void CoordMp_RoundTripsAcrossTheGrid()
    {
        Gen.Select(Gen.Bool, Gen.Int[0, 2048], Gen.Int[0, 31], Gen.Bool).Sample(t =>
        {
            (bool inBounds, int intPart, int frac, bool negative) = t;

            BitWriter writer = new();
            WriteCoordMp(writer, inBounds, intPart, frac, negative);
            BitReader reader = new(writer.Build());

            float expected = (intPart + (frac / 32f)) * (negative ? -1f : 1f);
            return SendPropDecoder.ReadFloat(ref reader, Property(flags: CoordMp)) == expected;
        });
    }
    [TestCase(true, 0, false, 0f)]
    [TestCase(true, 7, false, 7f)]
    [TestCase(false, 10000, false, 10000f)]
    [TestCase(true, 7, true, -7f)]
    public void CoordMpIntegral_KnownValues(bool inBounds, int intPart, bool negative, float expected)
    {
        // Integral coords have no fraction bits at all, and read the sign only when an
        // integer is present - a different shape from the non-integral variant, not a subset.
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds, intPart, 0, negative, integral: true);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: CoordMpIntegral))
            .ShouldBe(expected, 0.0001f);
    }

    [Test]
    public void CoordMpIntegral_Zero_ConsumesExactlyTwoBits()
    {
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds: true, 0, 0, negative: false, integral: true)
            .Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: CoordMpIntegral)).ShouldBe(0f);
        reader.BitsRead.ShouldBe(2);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }
    [TestCase(0, 4, 0.5f)]
    [TestCase(3, 2, 3.25f)]
    public void CoordMpLowPrecision_UsesThreeFractionBitsAtOneEighthResolution(
        int intPart, int frac, float expected)
    {
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds: true, intPart, frac, negative: false, lowPrecision: true);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: CoordMpLowPrecision))
            .ShouldBe(expected, 0.0001f);
    }
    [TestCase(CoordMp | CoordMpLowPrecision)]
    [TestCase(CoordMp | CoordMpIntegral)]
    [TestCase(CoordMp | CoordMpLowPrecision | CoordMpIntegral)]
    public void CoordFlags_AreFirstMatchNotIndependentModifiers(int flags)
    {
        // The engine tests these in order and takes the first: COORD, COORD_MP, LOWPRECISION,
        // INTEGRAL. Treating the later ones as modifiers that refine COORD_MP changes how many
        // bits a value occupies - five fraction bits become three, or vanish entirely - which
        // desynchronises every property after it rather than returning a wrong number.
        //
        // Asserted by where the reader lands as much as by the value, since that is what the
        // next property depends on.
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds: true, 100, 8, negative: false);
        writer.Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: flags)).ShouldBe(100.25f, 0.0001f);
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }

    [Test]
    public void CoordVector_ReadsThreeCoordComponents()
    {
        // m_vecOrigin is exactly this: a vector whose components are coordinate-encoded.
        // The wrong failure mode here is desynchronisation, so a sentinel follows the vector.
        BitWriter writer = new();
        WriteCoordMp(writer, inBounds: true, 100, 8, negative: false);
        WriteCoordMp(writer, inBounds: true, 200, 16, negative: true);
        WriteCoordMp(writer, inBounds: true, 0, 0, negative: false);
        writer.Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadVector(ref reader, Property(SendPropType.Vector, CoordMp))
            .ShouldBe((100.25f, -200.5f, 0f));
        reader.ReadUInt32(7).ShouldBe(0x7Fu);
    }

    [Test]
    public void Coord_TakesPrecedenceOverNoScale_MatchingTheEngine()
    {
        // The engine checks coordinate flags before SPROP_NOSCALE, so a property carrying
        // both is a coordinate. Getting precedence backwards reads 32 bits instead of 2 -
        // provable by where the reader lands rather than by the value.
        BitWriter writer = new();
        WriteCoord(writer, 0, 0, negative: false).Write(0x7F, 7);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flags: Coord | NoScale)).ShouldBe(0f);
        reader.BitsRead.ShouldBe(2);
    }
}
