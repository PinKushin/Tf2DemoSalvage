using System;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The multiplayer coordinate reader against a transcription of Valve's, over random bits.
/// </summary>
/// <remarks>
/// **This is the encoding a player's position actually arrives in**, which makes it the most
/// load-bearing decode in the project for anyone documenting a surf or jump run. It is also the one
/// most worth differentialling, because Valve does not implement it with branches: the integer width
/// and the fraction scale are selected by arithmetic on the flag bits, with two lookup tables and a
/// mask built from <c>(flags &amp; INBOUNDS) - 1</c>.
///
/// **The reference below keeps that arithmetic verbatim rather than simplifying it to the branches
/// it is equivalent to.** Simplifying would be a second chance to be wrong in the same place, and
/// the equivalence is exactly what a differential is supposed to establish rather than assume. It
/// reads oddly on purpose.
///
/// **Three variants, and they are not the same shape.** The integral one reads two flag bits rather
/// than three and folds the sign into the low bit of the value; the other two read three flags and
/// then one combined field with the integer in the LOW bits and the fraction above it. A reader
/// treating them as one function with modifiers gets the widths wrong — which is a
/// desynchronisation, not a wrong number.
///
/// Value and bit position are both compared, for the reason given in
/// <see cref="BitCoordDifferentialTests"/>: these properties carry no length prefix.
/// </remarks>
public sealed class BitCoordMpDifferentialTests
{
    /// <summary><c>COORD_INTEGER_BITS</c>, used when the value is out of the world bounds.</summary>
    private const int IntegerBits = 14;

    /// <summary><c>COORD_INTEGER_BITS_MP</c>, used when it is inside them.</summary>
    private const int IntegerBitsMp = 11;

    /// <summary><c>COORD_FRACTIONAL_BITS</c>.</summary>
    private const int FractionBits = 5;

    /// <summary><c>COORD_FRACTIONAL_BITS_MP_LOWPRECISION</c>.</summary>
    private const int FractionBitsLowPrecision = 3;

    [Test]
    public void TheMultiplayerCoordinateAgreesWithValve()
    {
        AgreeOverRandomBits(SendPropDecoder.CoordMpFlag, integral: false, lowPrecision: false);
    }

    [Test]
    public void TheLowPrecisionCoordinateAgreesWithValve()
    {
        AgreeOverRandomBits(
            SendPropDecoder.CoordMpLowPrecisionFlag, integral: false, lowPrecision: true);
    }

    [Test]
    public void TheIntegralCoordinateAgreesWithValve()
    {
        // The odd one out: two flag bits instead of three, no fraction at all, and the sign carried
        // in the low bit of the integer field rather than as its own bit.
        AgreeOverRandomBits(
            SendPropDecoder.CoordMpIntegralFlag, integral: true, lowPrecision: false);
    }

    [Test]
    public void AnIntegralCoordinateWithNoIntegerConsumesTwoBits()
    {
        // Valve returns 0 without reading anything further, so only the two flag bits are consumed.
        // A reader that always read three would be one bit ahead for the rest of the entity.
        BitReader reader = new(new byte[] { 0b0000_0000 });

        SendPropDecoder.ReadFloat(ref reader, Property(SendPropDecoder.CoordMpIntegralFlag))
            .ShouldBe(0f);

        reader.BitsRead.ShouldBe(2);
    }

    [Test]
    public void TheInBoundsBitSelectsTheNarrowerIntegerField()
    {
        // **The bit that decides a width, which is the dangerous kind.** In bounds reads an 11-bit
        // integer, out of bounds reads 14. Inverting it misreads the integer AND leaves the reader
        // three bits out for everything after it, so the damage is not confined to this property.
        int inBounds = Consumed(SendPropDecoder.CoordMpFlag, flags: 0b011);
        int outOfBounds = Consumed(SendPropDecoder.CoordMpFlag, flags: 0b010);

        (outOfBounds - inBounds).ShouldBe(
            IntegerBits - IntegerBitsMp,
            "the two integer widths differ by three bits and the in-bounds flag chooses between them");
    }

    /// <summary>Runs both readers over random bit patterns and requires agreement.</summary>
    private static void AgreeOverRandomBits(int flag, bool integral, bool lowPrecision)
    {
        Random random = new(20260816);

        byte[] bits = new byte[8];

        for (int trial = 0; trial < 20_000; trial++)
        {
            random.NextBytes(bits);

            BitReader ours = new(bits);
            BitReader theirs = new(bits);

            float mine = SendPropDecoder.ReadFloat(ref ours, Property(flag));
            float valve = ValveReadBitCoordMP(ref theirs, integral, lowPrecision);

            mine.ShouldBe(
                valve,
                $"trial {trial}: this decoder and a transcription of bf_read::ReadBitCoordMP " +
                $"disagree on {Convert.ToHexString(bits)} (integral {integral}, " +
                $"low precision {lowPrecision})");

            ours.BitsRead.ShouldBe(
                theirs.BitsRead,
                $"trial {trial}: same value, different width — everything after this property " +
                $"would decode from the wrong offset");
        }
    }

    /// <summary>How many bits this decoder consumes for a given set of leading flag bits.</summary>
    private static int Consumed(int flag, int flags)
    {
        BitWriter writer = new();

        // The flags go out low bit first, which is the order ReadUBitLong reassembles them in.
        writer.WriteBit((flags & 1) != 0);
        writer.WriteBit((flags & 2) != 0);
        writer.WriteBit((flags & 4) != 0);
        writer.Write(0, 32);

        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Property(flag));

        return reader.BitsRead;
    }

    /// <summary>A property carrying one of the multiplayer coordinate flags.</summary>
    private static SendProperty Property(int flag) =>
        new(SendPropType.Float, "coord", flag, string.Empty, 0f, 1f, 0, 0);

    /// <summary>
    /// <c>bf_read::ReadBitCoordMP</c>, transcribed from <c>src/tier1/bitbuf.cpp:1126</c>.
    /// </summary>
    /// <remarks>
    /// **Valve's arithmetic is kept as arithmetic.** <c>selectNotMP</c> and <c>selectNotLow</c> are
    /// masks built by subtracting one from a flag, used to choose between two values without
    /// branching, and they are reproduced rather than replaced by the <c>if</c> they are equivalent
    /// to. That equivalence is a thing to establish, not to assume while writing the reference that
    /// is supposed to establish it.
    /// </remarks>
    private static float ValveReadBitCoordMP(ref BitReader buffer, bool integral, bool lowPrecision)
    {
        const int InBounds = 1;
        const int IntVal = 2;
        const int Sign = 4;

        int flags = (int)buffer.ReadUInt32(3 - (integral ? 1 : 0));

        if (integral)
        {
            if ((flags & IntVal) != 0)
            {
                // "Read the third bit and the integer portion together at once"
                uint read = buffer.ReadUInt32(
                    (flags & InBounds) != 0 ? IntegerBitsMp + 1 : IntegerBits + 1);

                // "Remap from [0,N] to [1,N+1]"
                int whole = (int)(read >> 1) + 1;

                return (read & 1) != 0 ? -whole : whole;
            }

            return 0f;
        }

        float[] multiplyTable =
        [
            1f / (1 << FractionBits),
            -1f / (1 << FractionBits),
            1f / (1 << FractionBitsLowPrecision),
            -1f / (1 << FractionBitsLowPrecision),
        ];

        float multiply = multiplyTable[((flags & Sign) != 0 ? 1 : 0) + (lowPrecision ? 2 : 0)];

        byte[] widths =
        [
            FractionBits,
            FractionBits,
            FractionBits + IntegerBits,
            FractionBits + IntegerBitsMp,
            FractionBitsLowPrecision,
            FractionBitsLowPrecision,
            FractionBitsLowPrecision + IntegerBits,
            FractionBitsLowPrecision + IntegerBitsMp,
        ];

        uint bits = buffer.ReadUInt32(
            widths[(flags & (InBounds | IntVal)) + (lowPrecision ? 4 : 0)]);

        if ((flags & IntVal) != 0)
        {
            // "Shuffle the bits to remap the integer portion from [0,N] to [1,N+1] and then paste
            // in front of the fractional parts so we only need one int-to-float conversion."
            uint fractionBitsMp = bits >> IntegerBitsMp;
            uint fractionBits = bits >> IntegerBits;

            uint maskMp = (1u << IntegerBitsMp) - 1;
            uint mask = (1u << IntegerBits) - 1;

            uint selectNotMp = (uint)(flags & InBounds) - 1;

            fractionBits -= fractionBitsMp;
            fractionBits &= selectNotMp;
            fractionBits += fractionBitsMp;

            mask -= maskMp;
            mask &= selectNotMp;
            mask += maskMp;

            uint whole = (bits & mask) + 1;
            uint wholeLow = whole << FractionBitsLowPrecision;
            uint wholeNormal = whole << FractionBits;
            uint selectNotLow = (uint)(lowPrecision ? 1 : 0) - 1;

            wholeNormal -= wholeLow;
            wholeNormal &= selectNotLow;
            wholeNormal += wholeLow;

            bits = fractionBits | wholeNormal;
        }

        return (int)bits * multiply;
    }
}
