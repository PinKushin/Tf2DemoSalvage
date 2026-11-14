namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which of a decode's progress reports reach the log (B613).</summary>
/// <remarks>
/// The f12 decode reports 200 fractions over about four minutes, and the log said nothing between
/// "protocol 24" and the timeline's total, so a reader of the log could not tell a slow decode from
/// a hung one. One line per tenth is nine lines a load, whatever its length.
/// </remarks>
public sealed class DecodeProgressLogTests
{
    [Test]
    public void Report_BelowTheFirstTenth_SaysNothing()
    {
        DecodeProgressLog log = new("a.dem");

        log.Report(0d, 0d).ShouldBeNull();
        log.Report(0.099, 4d).ShouldBeNull();
    }

    [Test]
    public void Report_CrossingATenth_NamesTheDemoTheFractionAsReportedAndTheSeconds()
    {
        DecodeProgressLog log = new("f12.dem");

        log.Report(0.314, 12.34).ShouldBe("Decoding the demo f12.dem: 31% after 12.3 s");
    }

    [Test]
    public void Report_TwiceInOneTenth_SaysItOnceThenTheNextTenth()
    {
        DecodeProgressLog log = new("a.dem");

        log.Report(0.31, 1d).ShouldNotBeNull();
        log.Report(0.39, 2d).ShouldBeNull();
        log.Report(0.40, 3d).ShouldBe("Decoding the demo a.dem: 40% after 3.0 s");
    }

    [Test]
    public void Report_SkippingSeveralTenths_SaysOnceForWhereItStands()
    {
        DecodeProgressLog log = new("a.dem");

        log.Report(0.75, 9d).ShouldBe("Decoding the demo a.dem: 75% after 9.0 s");
        log.Report(0.79, 10d).ShouldBeNull();
    }

    [Test]
    public void Report_TheEnd_SaysNothingBecauseTheTimingLineSaysIt()
    {
        DecodeProgressLog log = new("a.dem");

        log.Report(0.95, 190d).ShouldNotBeNull();
        log.Report(1d, 200d).ShouldBeNull();
    }

    [Test]
    public void Report_OutsideZeroToOne_SaysNothing()
    {
        DecodeProgressLog log = new("a.dem");

        log.Report(-0.5, 1d).ShouldBeNull();
        log.Report(1.5, 2d).ShouldBeNull();
        log.Report(0.2, 3d).ShouldBe("Decoding the demo a.dem: 20% after 3.0 s");
    }
}
