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

    private static SendProperty Property(
        SendPropType type = SendPropType.Int,
        int flags = 0,
        int bits = 8,
        float low = 0f,
        float high = 1f) =>
        new(type, "p", flags, string.Empty, low, high, bits, 0);

    [Fact]
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

    [Fact]
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

    [Theory]
    [InlineData(-1, 11)]
    [InlineData(-2048, 12)]
    [InlineData(2047, 12)]
    [InlineData(-1, 32)]
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
    [Fact]
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

    [Fact]
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

    [Theory]
    [InlineData(0u, 0f)]
    [InlineData(255u, 100f)]
    public void RangeEncodedFloat_HitsItsEndpointsExactly(uint raw, float expected)
    {
        BitWriter writer = new();
        writer.Write(raw, 8);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(bits: 8, low: 0f, high: 100f))
            .ShouldBe(expected, 0.001f);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void EmptyString_ReadsAsEmptyAndConsumesOnlyItsLength()
    {
        BitWriter writer = new();
        writer.Write(0, 9);
        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadString(ref reader).ShouldBeEmpty();
        reader.BitsRead.ShouldBe(9);
    }

    [Theory]
    [InlineData(0.6f, 0.8f, false)]
    [InlineData(0.6f, 0.8f, true)]
    [InlineData(1f, 0f, false)]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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

    [Fact]
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

    [Fact]
    public void NormalFloat_UsesTheFullElevenBitRange()
    {
        BitWriter writer = new();
        writer.Write(0, 1).Write(2047, 11);
        BitReader reader = new(writer.Build());

        // 2047 of 2047 is exactly 1.0; a wrong divisor shows up here rather than in the middle.
        SendPropDecoder.ReadFloat(ref reader, Property(flags: Normal)).ShouldBe(1f, 0.0001f);
    }

    [Theory]
    [InlineData(0u, -50f)]
    [InlineData(255u, 50f)]
    [InlineData(128u, 0.2f)]
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

    [Theory]
    [InlineData(Coord)]
    [InlineData(CoordMp)]
    [InlineData(1 << 14)]
    [InlineData(1 << 15)]
    public void CoordinateEncodings_ThrowRatherThanGuess(int flags)
    {
        // Checked through every entry point, not just ReadFloat - a vector reaching the
        // component decoder one flag at a time would otherwise slip past the guard.
        Should.Throw<NotSupportedException>(() =>
        {
            BitReader vectorReader = new(new byte[16]);
            SendPropDecoder.ReadVector(ref vectorReader, Property(SendPropType.Vector, flags));
        });

        Should.Throw<NotSupportedException>(() =>
        {
            BitReader xyReader = new(new byte[16]);
            SendPropDecoder.ReadVectorXY(ref xyReader, Property(SendPropType.VectorXY, flags));
        });
        // The most important behaviour in this file. A wrong coordinate is a plausible
        // position, and TF2 almost certainly uses SPROP_COORD_MP for player origins - a flag
        // VDC does not document at all. Refusing is the only safe answer until it is
        // implemented and verified against the reference parser.
        SendPropDecoder.IsSupported(Property(flags: flags)).ShouldBeFalse();

        Should.Throw<NotSupportedException>(() =>
        {
            BitReader reader = new(new byte[16]);
            SendPropDecoder.ReadFloat(ref reader, Property(flags: flags));
        });
    }

    [Fact]
    public void SupportedEncodings_AreReportedAsSuch()
    {
        SendPropDecoder.IsSupported(Property(flags: NoScale)).ShouldBeTrue();
        SendPropDecoder.IsSupported(Property(flags: Normal)).ShouldBeTrue();
        SendPropDecoder.IsSupported(Property(flags: Unsigned)).ShouldBeTrue();
        SendPropDecoder.IsSupported(Property()).ShouldBeTrue();
    }
}
