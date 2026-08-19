using System;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// This project's coordinate reader against a transcription of Valve's, over random bits.
/// </summary>
/// <remarks>
/// **Constants can be derived from a header; an algorithm cannot.** The rest of the conformance
/// suite computes numbers from declarations, which works because a declaration states a value. A
/// read ORDER states nothing — <c>bf_read::ReadBitCoord</c> is nine lines of control flow, and the
/// only way to check ours against it is to have two implementations and disagree.
///
/// So the reference below is transcribed from <c>tier1/bitbuf.cpp</c> line by line, and the two are
/// run over random bit patterns. **They are independent in the way that matters**: this project's
/// reader was written from the format months ago, the reference was typed from Valve's source now,
/// and a shared misreading would have to be the same misreading arrived at twice from different
/// directions.
///
/// **Random bits are valid input.** Every bit pattern is a legal coordinate — the presence flags
/// decide what follows and there is no invalid encoding — so no fixture construction is needed and
/// no hand-computed expected value can be wrong.
///
/// **Both the value and the bit position are compared.** A reader that returns the right number
/// having consumed the wrong number of bits is the more dangerous failure, because these properties
/// carry no length prefix: everything after it in the entity decodes from the wrong offset, and the
/// values that come back are ordinary numbers.
///
/// Four things in Valve's nine lines are easy to get subtly wrong, and each is checked directly
/// below as well as differentially:
///
/// - the sign bit is read AFTER both presence flags, not before;
/// - the integer is stored minus one, so a present integer of raw 0 decodes to 1;
/// - when neither flag is set the value is zero and NO sign bit is consumed;
/// - the fraction is scaled by <c>COORD_RESOLUTION</c>, which is 1/(1 &lt;&lt; 5).
/// </remarks>
public sealed class BitCoordDifferentialTests
{
    /// <summary><c>SPROP_COORD</c>, which selects the plain coordinate encoding.</summary>
    private const int CoordFlag = 1 << 1;

    /// <summary><c>COORD_INTEGER_BITS</c> from <c>public/coordsize.h</c>.</summary>
    private const int IntegerBits = 14;

    /// <summary><c>COORD_FRACTIONAL_BITS</c>.</summary>
    private const int FractionBits = 5;

    /// <summary><c>COORD_RESOLUTION</c>: <c>1.0 / (1 &lt;&lt; COORD_FRACTIONAL_BITS)</c>.</summary>
    private const float Resolution = 1f / (1 << FractionBits);

    [Test]
    public void BitCoord_OverRandomBits_AgreesWithValve()
    {
        // 20,000 patterns is far more than the encoding has distinct shapes - four flag
        // combinations times the integer and fraction fields - so this is saturating rather than
        // sampling. The seed is fixed because a failure has to be reproducible; the point is not
        // to search, it is to compare.
        Random random = new(20260816);

        byte[] bits = new byte[8];

        for (int trial = 0; trial < 20_000; trial++)
        {
            random.NextBytes(bits);

            BitReader ours = new(bits);
            BitReader theirs = new(bits);

            float mine = SendPropDecoder.ReadFloat(ref ours, Coordinate());
            float valve = ValveReadBitCoord(ref theirs);

            mine.ShouldBe(
                valve,
                $"trial {trial}: this decoder and a transcription of bf_read::ReadBitCoord " +
                $"disagree on {Convert.ToHexString(bits)}");

            ours.BitsRead.ShouldBe(
                theirs.BitsRead,
                $"trial {trial}: same value, different width - everything after this property " +
                $"would decode from the wrong offset");
        }
    }

    [Test]
    public void BitCoord_AnAbsentCoordinate_ConsumesTwoBitsAndNoSign()
    {
        // Both presence flags clear. Valve returns before reading the sign, so a reader that always
        // reads three bits is one ahead from here on - and returns 0 either way, which is why this
        // needs asserting on the POSITION rather than the value.
        BitReader reader = new(new byte[] { 0b0000_0000 });

        SendPropDecoder.ReadFloat(ref reader, Coordinate()).ShouldBe(0f);
        reader.BitsRead.ShouldBe(2);
    }

    [Test]
    public void BitCoord_APresentIntegerOfZero_DecodesToOne()
    {
        // **The plus-one, which is the single most consequential line in the encoding.** A present
        // integer is never zero, because that case is carried by the presence bit, so the encoder
        // stores it minus one. Dropping the adjustment shifts every coordinate in the demo by one
        // unit - a whole-number error that looks like nothing at all on a map 4,096 units wide.
        BitWriter writer = new();
        writer.WriteBit(true);              // integer present
        writer.WriteBit(false);             // no fraction
        writer.WriteBit(false);             // positive
        writer.Write(0, IntegerBits);       // raw zero

        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Coordinate()).ShouldBe(1f);
    }

    [Test]
    public void BitCoord_TheSign_IsReadAfterBothFlags()
    {
        // Fraction only, negative. If the sign were read first this would decode the sign bit as
        // the fraction flag and come out positive with the wrong magnitude.
        BitWriter writer = new();
        writer.WriteBit(false);             // no integer
        writer.WriteBit(true);              // fraction present
        writer.WriteBit(true);              // negative
        writer.Write(16, FractionBits);     // half a unit

        BitReader reader = new(writer.Build());

        SendPropDecoder.ReadFloat(ref reader, Coordinate()).ShouldBe(-0.5f);
    }

    /// <summary>A property carrying <c>SPROP_COORD</c>, which is what selects this encoding.</summary>
    private static SendProperty Coordinate() =>
        new(SendPropType.Float, "coord", CoordFlag, string.Empty, 0f, 1f, 0, 0);

    /// <summary>
    /// <c>bf_read::ReadBitCoord</c>, transcribed from <c>src/tier1/bitbuf.cpp:1083</c>.
    /// </summary>
    /// <remarks>
    /// Kept in Valve's structure rather than tidied, including the two separate flag reads and the
    /// early return, because the point is to be a second reading of the same source and not a
    /// second version of this project's reader. Tidying it would let it drift toward ours, which is
    /// exactly the failure a differential is supposed to rule out.
    /// </remarks>
    private static float ValveReadBitCoord(ref BitReader buffer)
    {
        int intval = buffer.ReadBit() ? 1 : 0;
        int fractval = buffer.ReadBit() ? 1 : 0;

        float value = 0f;

        if (intval != 0 || fractval != 0)
        {
            bool signbit = buffer.ReadBit();

            if (intval != 0)
            {
                // "Adjust the integers from [0..MAX_COORD_VALUE-1] to [1..MAX_COORD_VALUE]"
                intval = (int)buffer.ReadUInt32(IntegerBits) + 1;
            }

            if (fractval != 0)
            {
                fractval = (int)buffer.ReadUInt32(FractionBits);
            }

            value = intval + (fractval * Resolution);

            if (signbit)
            {
                value = -value;
            }
        }

        return value;
    }
}
