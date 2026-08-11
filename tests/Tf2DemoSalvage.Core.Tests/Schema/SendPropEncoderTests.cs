using System;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Round-trip tests for the coordinate encoder.
/// </summary>
/// <remarks>
/// Stated as a round trip against the decoder, because the encoding is lossy in a specific way
/// and an expected byte array would have to encode that lossiness by hand. A coordinate keeps
/// five fraction bits — 32 steps to the unit — so the value that comes back is the input snapped
/// to the nearest 32nd, and the property has to compare against that rather than against the
/// input.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified, so the property " +
                    "delegate is the assertion. Sonar does not know the library.")]
public sealed class SendPropEncoderTests
{
    private const int FractionSteps = 32;

    /// <summary>SPROP_COORD, the flag the decoder selects the plain form with.</summary>
    private const int CoordFlag = 1 << 1;

    private static readonly SendProperty Coord =
        new(SendPropType.Float, "coord", CoordFlag, string.Empty, 0f, 0f, 0, 0);

    [Fact]
    public void AnyCoordinate_ComesBackSnappedToTheNearestThirtySecond()
    {
        Gen.Float[-8192f, 8192f].Sample(value =>
        {
            // Snapped to the grid, and negative zero folded to positive. The wire cannot express
            // -0.0: zero is two clear bits with no sign bit after them, so a value that rounds to
            // zero from below comes back as +0.0. CsCheck found that in a hundred samples, with
            // -1.832e-12. Adding zero is the IEEE-defined way to normalise it.
            float expected = (MathF.Round(value * FractionSteps) / FractionSteps) + 0f;

            BitWriter writer = new();
            SendPropEncoder.WriteCoord(writer, expected);

            BitReader reader = new(writer.Build());
            float actual = SendPropDecoder.ReadFloat(ref reader, Coord);

            // Compared as bits: the prediction is exact, so any tolerance would only widen what
            // counts as passing.
            return BitConverter.SingleToInt32Bits(actual)
                == BitConverter.SingleToInt32Bits(expected);
        });
    }

    [Theory]
    [InlineData(0f, 2)]
    [InlineData(1f, 2 + 1 + 14)]
    [InlineData(0.5f, 2 + 1 + 5)]
    [InlineData(-1.5f, 2 + 1 + 14 + 5)]
    public void APresentPartCostsItsOwnField(float value, int expectedBits)
    {
        // The widths, pinned as literals. Zero is the case worth stating: two clear bits and no
        // sign, because a sign bit nobody reads would shift every message after this one.
        BitWriter writer = new();
        SendPropEncoder.WriteCoord(writer, value);

        writer.BitCount.ShouldBe(expectedBits);
    }

    [Fact]
    public void AFractionThatRoundsToAWholeUnit_CarriesIntoTheIntegerPart()
    {
        // 0.9999 is 31.997 thirty-seconds. Rounding gives 32, which is not a fraction at all -
        // left as one it would be written into a five-bit field as 0 and read back as 0.0.
        BitWriter writer = new();
        SendPropEncoder.WriteCoord(writer, 0.9999f);

        BitReader reader = new(writer.Build());
        SendPropDecoder.ReadFloat(ref reader, Coord).ShouldBe(1f);
    }
}
