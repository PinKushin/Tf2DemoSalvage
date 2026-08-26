namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Clamping, the dead band around zero, and the slider mapping.</summary>
/// <remarks>
/// **`TimeScaleConformanceTests` is the parity half and was written first**; this is what a
/// continuous speed has to get right that no comparison with Valve settles — chiefly that zero is
/// the pause button rather than a speed, and that a slider position survives a round trip.
/// </remarks>
public sealed class TimeScaleTests
{
    [Test]
    public void From_ASpeedInsideTheRange_KeepsItExactly()
    {
        // The whole point of the change: 0.37 is not one of eleven steps and must not be rounded to
        // one. The ladder it replaced would have answered 0.5.
        TimeScale.From(0.37d).Speed.ShouldBe(0.37d);
    }

    [Test]
    public void From_AboveTheCeiling_ClampsToTheFastest()
    {
        TimeScale.From(100d).Speed.ShouldBe(TimeScale.Fastest);
    }

    [Test]
    public void From_BelowTheFloor_ClampsToTheSlowest()
    {
        // **Not to zero**, which is what a naive clamp against a range starting at zero would do.
        // Zero is a stop, and a stop nobody asked for reads as a freeze.
        TimeScale.From(0.0001d).Speed.ShouldBe(TimeScale.Slowest);
    }

    [Test]
    public void From_Zero_IsTheSlowestForwardRatherThanAStop()
    {
        // **Zero is the pause button, not a speed** (D97's range excludes it from both sides). A
        // transport that answered 0 here would look identical to paused while reporting that it was
        // playing — the two states this viewer already keeps carefully apart.
        TimeScale.From(0d).Speed.ShouldBe(TimeScale.Slowest);
    }

    [Test]
    public void From_ASmallNegative_StaysNegative()
    {
        // **The direction survives the clamp**, which is the half a magnitude-only clamp loses. A
        // reverse speed inside the dead band must resolve to the slowest REVERSE, not to forward.
        TimeScale.From(-0.0001d).Speed.ShouldBe(-TimeScale.Slowest);
    }

    [Test]
    public void From_NotANumber_IsNormalSpeed()
    {
        // A NaN reaching a clamp propagates silently through `Math.Clamp` and poisons every later
        // multiplication — a demo that neither plays nor errors. `docs/memory/numeric-decoding-traps.md`
        // records the same shape on the wire.
        TimeScale.From(double.NaN).Speed.ShouldBe(1d);
    }

    [TestCase(-30_000)]
    [TestCase(-29_999)]
    [TestCase(-12_345)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(15_000)]
    [TestCase(29_999)]
    [TestCase(30_000)]
    public void At_ThenPosition_RoundTrips(int position)
    {
        // **The mapping has to be reversible or the slider fights the label.** Both ends, both
        // signs, and either side of the centre — a signed linear map is easiest to get wrong exactly
        // where it crosses.
        TimeScale.At(position).Position().ShouldBe(position);
    }

    [Test]
    public void At_TheCentreOfTheSlider_IsTheSlowestNotAStop()
    {
        // **The one surprising thing about a slider that spans both directions.** Zero is not in the
        // range — stopping is the play button's job — so the middle of the travel is the slowest
        // speed, and either side accelerates away from it. A centre that meant "stopped" would be a
        // second pause control disagreeing with the first.
        TimeScale.At(0).Speed.ShouldBe(TimeScale.Slowest);
    }

    [Test]
    public void At_TheRightEnd_IsTheFastestForwards()
    {
        TimeScale.At(TimeScale.Positions).Speed.ShouldBe(TimeScale.Fastest);
    }

    [Test]
    public void At_TheLeftEnd_IsTheFastestBackwards()
    {
        TimeScale.At(-TimeScale.Positions).Speed.ShouldBe(-TimeScale.Fastest);
    }

    [Test]
    public void At_EitherSideOfTheCentre_MirrorsTheSameSpeed()
    {
        // The two halves are the same ramp, so a person who knows where 2x is going forwards knows
        // where it is going backwards.
        TimeScale.At(-12_345).Speed.ShouldBe(-TimeScale.At(12_345).Speed);
    }

    [Test]
    public void Position_ForAReverseSpeed_IsTheMirrorOfItsForwardTwin()
    {
        TimeScale.From(-2d).Position().ShouldBe(-TimeScale.From(2d).Position());
    }

    [Test]
    public void Position_ForAReverseSpeed_IsNegative()
    {
        // **The control for the mirror test above**, which two magnitudes would also satisfy if the
        // sign were dropped on both sides.
        TimeScale.From(-2d).Position().ShouldBeLessThan(0);
    }

    [Test]
    public void IsReverse_TellsTheDirectionsApart()
    {
        TimeScale.From(-1d).IsReverse.ShouldBeTrue();
        TimeScale.From(1d).IsReverse.ShouldBeFalse();
    }

    [Test]
    public void Label_AtTheSlowest_ShowsTwoDecimalsRatherThanRoundingToNothing()
    {
        // **One decimal would render the whole band this change exists to reach as `0.0x`** — the
        // quantising the ladder used to do, moved into the label where it would be just as wrong and
        // much harder to notice.
        TimeScale.From(0.01d).Label().ShouldBe("0.01x");
    }

    [Test]
    public void Label_AtNormalSpeed_DropsTheDecimalsEntirely()
    {
        TimeScale.Normal.Label().ShouldBe("1x");
    }

    [Test]
    public void Label_InReverse_KeepsTheSign()
    {
        TimeScale.From(-2d).Label().ShouldBe("-2x");
    }
}
