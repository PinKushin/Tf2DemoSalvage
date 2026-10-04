using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>Valve's <c>interval_t</c>: <c>ReadInterval</c> and <c>RandomInterval</c> (B462).</summary>
/// <remarks>
/// **Every soundscape value that may vary goes through these two** — `volume`, `pitch`, `attenuation` and a numeric
/// `soundlevel` in <c>ProcessPlayLooping</c> (<c>c_soundscape.cpp:735,739,751,761</c>). Read from published source,
/// <c>game/shared/interval.cpp:21-59</c>:
///
/// <code>
/// char *token = strtok( tempString, "," );
/// if ( token ) { tmp.start = atof( token );
///     token = strtok( NULL, "," );
///     if ( token ) tmp.range = atof( token ) - tmp.start; }
/// ...
/// float out = interval.start;  if ( interval.range != 0 ) out += RandomFloat( 0, interval.range );
/// </code>
///
/// **`atof` and `strtok`, not a number parser**: a value that does not parse is a zero rather than a fallback, a
/// trailing word is ignored, and an empty token between commas does not exist.
/// </remarks>
public sealed class IntervalConformanceTests
{
    [Test]
    public void Read_TwoNumbers_AreTheStartAndTheDifference()
    {
        // `Halloween.Outside`'s wind, as shipped: `"volume" ".2, .3"`.
        Interval read = Interval.Read(".2, .3");

        read.Start.ShouldBe(0.2f);

        // **`tmp.range = atof( token ) - tmp.start` subtracts in DOUBLE** (`interval.cpp:38`): `atof` is a double and the
        // float start is promoted, and only the difference is narrowed. 0.3 − 0.2f is 0.0999999970…, which narrows to
        // 0.099999994f; two floats subtracted give 0.10000001f — one float apart.
        read.Range.ShouldBe((float)(0.3d - 0.2f));
        BitConverter.SingleToInt32Bits(read.Range).ShouldNotBe(
            BitConverter.SingleToInt32Bits(0.3f - 0.2f), "the control: the float subtraction gives the other neighbour");
    }

    [Test]
    public void Read_OneNumber_HasNoRange()
    {
        Interval.Read("50").ShouldBe(new Interval(50f, 0f));
    }

    [Test]
    public void Read_ADescendingPair_HasANegativeRange()
    {
        // `range = atof( end ) - start`, with no ordering: a reversed pair is a negative range, and RandomFloat over it
        // still lands between the two.
        Interval.Read("110,90").ShouldBe(new Interval(110f, -20f));
    }

    [Test]
    public void Read_ALeadingComma_IsSkippedAsStrtokSkipsIt()
    {
        // strtok never returns an empty token, so ",5" is the one token "5" — a start, not a range from zero.
        Interval.Read(",5").ShouldBe(new Interval(5f, 0f));
        Interval.Read("1,,3").ShouldBe(new Interval(1f, 2f));
    }

    [Test]
    public void Read_TrailingTextAfterANumber_IsItsPrefixAsAtofReadsIt()
    {
        Interval.Read("0.5abc, 2x").ShouldBe(new Interval(0.5f, 1.5f));
    }

    [Test]
    public void Read_NoNumberAtAll_IsZeroRatherThanAFallback()
    {
        // `tmp.start = 0` before anything is read, and `atof` of a word is 0 — so a soundscape `volume` with no number
        // in it is silence, never a default volume.
        Interval.Read("loud").ShouldBe(new Interval(0f, 0f));
        Interval.Read(string.Empty).ShouldBe(new Interval(0f, 0f));
    }

    [Test]
    public void Random_NoRange_IsTheStartAndDrawsNothing()
    {
        UniformRandomStream used = Seeded();
        UniformRandomStream control = Seeded();

        new Interval(100f, 0f).Random(used).ShouldBe(100f);

        // `if ( interval.range != 0 )` guards the draw, so the stream must be exactly where a fresh one is.
        used.RandomFloat(0f, 1f).ShouldBe(control.RandomFloat(0f, 1f), "a fixed value must not consume a draw");
    }

    [Test]
    public void Random_ARange_IsTheStartPlusOneDrawOverTheRange()
    {
        UniformRandomStream used = Seeded();
        UniformRandomStream control = Seeded();

        float expected = 90f + control.RandomFloat(0f, 20f);

        new Interval(90f, 20f).Random(used).ShouldBe(expected);
        expected.ShouldNotBe(90f, "the control has to have moved, or this cannot tell a draw from none");
    }

    private static UniformRandomStream Seeded()
    {
        UniformRandomStream stream = new();
        stream.SetSeed(7);
        return stream;
    }
}
