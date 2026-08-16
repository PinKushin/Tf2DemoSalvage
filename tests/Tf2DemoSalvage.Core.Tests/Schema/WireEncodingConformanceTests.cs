using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The widths that decide how a coordinate, a string and a property flag are read.
/// </summary>
/// <remarks>
/// **These four coordinate widths decide every position in every demo**, which makes them the most
/// load-bearing numbers in the project for anyone documenting a run. A position is sent as an
/// integer part and a fraction; get the fraction width wrong and every coordinate is off by a factor
/// of four, get the integer width wrong and the reader desynchronises from that property onward.
/// Neither throws. A run's path would simply be somewhere else.
///
/// **They are also the numbers a reader is most likely to get subtly right.** There are two integer
/// widths and two fraction widths, because multiplayer origins use a narrower range and a lower
/// precision than the general coordinate encoding — so a decoder that used one pair everywhere works
/// on most values and drifts on the rest.
///
/// <c>SPROP_NUMFLAGBITS_NETWORKED</c> is the one with a documented trap beside it: the flags field
/// is 16 bits on the wire, not the 17 of <c>SPROP_NUMFLAGBITS</c>, and the header says so in a
/// comment. Reading 17 consumes a bit belonging to the next field of the send table.
/// </remarks>
public sealed class WireEncodingConformanceTests
{
    /// <summary>Where the engine declares the coordinate encoding.</summary>
    private const string CoordSize = "src/public/coordsize.h";

    /// <summary>Where the engine declares the send table encoding.</summary>
    private const string DataTable = "src/public/dt_common.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheCoordinateWidthsAreTheEngines()
    {
        IReadOnlyDictionary<string, int> coords = SourceSdk.Constants(CoordSize);

        SendPropDecoder.CoordIntegerBits.ShouldBe(coords["COORD_INTEGER_BITS"]);
        SendPropDecoder.CoordFractionBits.ShouldBe(coords["COORD_FRACTIONAL_BITS"]);
        SendPropDecoder.CoordIntegerBitsInBounds.ShouldBe(coords["COORD_INTEGER_BITS_MP"]);
        SendPropDecoder.CoordFractionBitsLowPrecision
            .ShouldBe(coords["COORD_FRACTIONAL_BITS_MP_LOWPRECISION"]);
    }

    [Test]
    public void TheTwoPrecisionsAreGenuinelyDifferent()
    {
        // **Stated separately because a decoder using one pair everywhere mostly works.** The
        // multiplayer origin encoding is three bits narrower in range and two coarser in precision;
        // a reader that collapsed them would be right for values inside the smaller bound and wrong
        // beyond it, which on a large map means correct near the middle and drifting at the edges.
        SendPropDecoder.CoordIntegerBits
            .ShouldBeGreaterThan(SendPropDecoder.CoordIntegerBitsInBounds);

        SendPropDecoder.CoordFractionBits
            .ShouldBeGreaterThan(SendPropDecoder.CoordFractionBitsLowPrecision);

        // A fraction of five bits is a resolution of 1/32 of a unit, and three bits is 1/8. Stated
        // as the arithmetic the encoding means rather than as two more numbers.
        (1 << SendPropDecoder.CoordFractionBits).ShouldBe(32);
        (1 << SendPropDecoder.CoordFractionBitsLowPrecision).ShouldBe(8);
    }

    [Test]
    public void TheStringLengthAndFlagWidthsAreTheEngines()
    {
        IReadOnlyDictionary<string, int> tables = SourceSdk.Constants(DataTable);

        SendPropDecoder.StringLengthBits.ShouldBe(tables["DT_MAX_STRING_BITS"]);
        SendTableParser.FlagBits.ShouldBe(tables["SPROP_NUMFLAGBITS_NETWORKED"]);
    }

    [Test]
    public void TheFlagsFieldIsTheNetworkedWidthNotTheFullOne()
    {
        // **The trap the header itself warns about.** SPROP_NUMFLAGBITS is 17 and only 16 are sent;
        // dt_common.h says so in a comment next to the constant. Reading 17 eats a bit belonging to
        // whatever follows in the send table, and a send table is the thing every entity decode
        // depends on — so this one bit would take the whole schema with it.
        IReadOnlyDictionary<string, int> tables = SourceSdk.Constants(DataTable);

        tables["SPROP_NUMFLAGBITS_NETWORKED"].ShouldBe(16);
        SendTableParser.FlagBits.ShouldNotBe(17);
    }

    [Test]
    public void TheStopFlagIsTheEnginesSoundFlag()
    {
        // SND_STOP is declared as (1 << 2), which is the form most of Valve's flag sets use. A
        // sound message carrying it has none of the fields that describe playback, so reading the
        // wrong bit means reading fields that are not there.
        SoundDecoder.StopFlag.ShouldBe(SourceSdk.Constants("src/public/soundflags.h")["SND_STOP"]);
    }
}
