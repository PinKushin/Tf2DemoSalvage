using System;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The periodic frame-rate line, which exists because nothing else in the viewer can see a steady
/// state.
/// </summary>
/// <remarks>
/// **The instrument this replaces is blind above 33 fps.** `StallReport.StallSeconds` is 0.03, so a
/// run reports a breakdown only for frames slower than 33 per second and says nothing whatever
/// about the difference between 40 and 600. A twenty-second autoplay run of
/// `tf2-2026-pub-pov-clean` logged zero lines, which was read as "no slow frames" and means only
/// "no frame took longer than 30 ms" — `docs/memory/a-threshold-instrument-cannot-see-a-sum.md`
/// applied to the frame loop itself.
///
/// **A summary rather than a sample, for the same reason.** `FpsReading` already carries the
/// smoothed rate and the two watermarks every frame; the gap is that nothing writes them down.
/// Reporting the worst frame alongside the average is what separates "we are slow" from "we are
/// fast and something stalls", and those have different causes.
/// </remarks>
public sealed class FrameRateLogTests
{
    private static FpsReading Reading(int fps, int low, int high, double milliseconds) =>
        new(fps, low, high, milliseconds, Smoothed: true);

    [Test]
    public void Report_BeforeTheIntervalElapses_SaysNothing()
    {
        FrameRateLog log = new();

        Assert.That(log.Report(Reading(300, 280, 320, 3.3), atSeconds: 0.0), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), atSeconds: 0.5), Is.Null);
    }

    [Test]
    public void Report_AfterTheIntervalElapses_NamesTheRateAndBothWatermarks()
    {
        FrameRateLog log = new();

        _ = log.Report(Reading(300, 280, 320, 3.3), atSeconds: 0.0);

        Assert.That(
            log.Report(Reading(120, 44, 310, 8.3), atSeconds: 1.0),
            Is.EqualTo("frame rate 120 fps (44 worst, 310 best), 8.3 ms"));
    }

    [Test]
    public void Report_AfterReporting_WaitsAnotherWholeInterval()
    {
        FrameRateLog log = new();

        _ = log.Report(Reading(300, 280, 320, 3.3), atSeconds: 0.0);
        _ = log.Report(Reading(120, 44, 310, 8.3), atSeconds: 1.0);

        Assert.That(log.Report(Reading(120, 44, 310, 8.3), atSeconds: 1.9), Is.Null);
        Assert.That(log.Report(Reading(90, 40, 200, 11.1), atSeconds: 2.0), Is.Not.Null);
    }

    /// <remarks>
    /// **The first frame after a mode change has no duration to report**, which is `FpsMeter`'s own
    /// rule rather than this type's — it answers null and this must pass that through rather than
    /// inventing a zero. A zero here would print `0 fps` and be read as a stall.
    /// </remarks>
    [Test]
    public void Report_WithNoReading_SaysNothingAndDoesNotStartTheClock()
    {
        FrameRateLog log = new();

        Assert.That(log.Report(reading: null, atSeconds: 0.0), Is.Null);
        Assert.That(log.Report(reading: null, atSeconds: 5.0), Is.Null);

        // The interval is measured from the first REPORTABLE frame, so a run that spent five
        // seconds loading does not fire immediately and then again a second later.
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), atSeconds: 5.0), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), atSeconds: 5.5), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), atSeconds: 6.0), Is.Not.Null);
    }
}
