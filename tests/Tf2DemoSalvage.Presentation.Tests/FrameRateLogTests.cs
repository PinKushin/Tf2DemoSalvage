using System;
using System.Diagnostics;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The periodic frame-rate line, which exists because nothing else in the viewer can see a steady
/// state.
/// </summary>
/// <remarks>
/// **The instrument this supplements is blind above 33 fps.** `StallReport.StallSeconds` is 0.03, so
/// a run reports a breakdown only for frames slower than 33 per second and says nothing whatever
/// about the difference between 40 and 600. A twenty-second autoplay run of
/// `tf2-2026-pub-pov-clean` logged zero lines, which reads as "no slow frames" and means only
/// "nothing exceeded 30 ms" — `docs/memory/a-threshold-instrument-cannot-see-a-sum.md` applied to
/// the frame loop itself.
///
/// **Every phase reported is a MEAN over the interval, never the frame that happened to cross it.**
/// The owner, on the first design, which logged one frame's phases once a second: *"a probe that
/// only polls per second is way too slow so that better be a fucking average"*. He is right, and it
/// is `docs/memory/log-the-event-not-a-sample-of-it.md`: at 90 fps a per-second sample publishes one
/// frame in ninety and calls it the cost. The frame RATE was already an average — `FpsMeter` smooths
/// it and carries watermarks — which is exactly what made the phases beside it look safe.
/// </remarks>
public sealed class FrameRateLogTests
{
    /// <summary>One millisecond, in the stopwatch ticks <c>FramePhases</c> is measured in.</summary>
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    private static FpsReading Reading(int fps, int low, int high, double milliseconds) =>
        new(fps, low, high, milliseconds, Smoothed: true);

    /// <summary>A frame whose whole cost is drawing, so the mean of one phase is unambiguous.</summary>
    private static FramePhases Drawing(int milliseconds, int totalMilliseconds) =>
        new(
            Sound: 0,
            Camera: 0,
            Project: 0,
            Advance: 0,
            Capture: 0,
            Hud: 0,
            Draw: milliseconds * Millisecond,
            Total: totalMilliseconds * Millisecond);

    [Test]
    public void Report_BeforeTheIntervalElapses_SaysNothing()
    {
        FrameRateLog log = new();

        Assert.That(log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 0.0), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 0.5), Is.Null);
    }

    [Test]
    public void Report_AfterTheIntervalElapses_NamesTheRateAndBothWatermarks()
    {
        FrameRateLog log = new();

        _ = log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 0.0);

        Assert.That(
            log.Report(Reading(120, 44, 310, 8.3), Drawing(3, 3), 1.0),
            Does.StartWith("frame rate 120 fps (44 worst, 310 best), 8.3 ms"));
    }

    /// <remarks>
    /// **The three frames differ, and the mean differs from every one of them.** Draw of 3, 6 and 12
    /// means 7 — not 12, which a report of the crossing frame would give, and not 3, which the first
    /// would. An input where a correct implementation and a sampling one predict the same number
    /// would not test this at all.
    /// </remarks>
    [Test]
    public void Report_OverAnInterval_AveragesEveryFrameRatherThanTheCrossingOne()
    {
        FrameRateLog log = new();

        _ = log.Report(Reading(120, 44, 310, 8.3), Drawing(3, 4), 0.0);
        _ = log.Report(Reading(120, 44, 310, 8.3), Drawing(6, 8), 0.4);

        string? line = log.Report(Reading(120, 44, 310, 8.3), Drawing(12, 15), 1.0);

        Assert.That(line, Does.Contain("over 3 frames"));
        Assert.That(line, Does.Contain("draw 7"), "mean of 3, 6 and 12 — not the crossing frame's 12");
    }

    /// <remarks>
    /// A second interval must not carry the first one's frames, or every line after the first is an
    /// average of the whole run and a stall never washes out.
    /// </remarks>
    [Test]
    public void Report_AfterReporting_ForgetsTheFramesItAlreadyReported()
    {
        FrameRateLog log = new();

        _ = log.Report(Reading(120, 44, 310, 8.3), Drawing(30, 30), 0.0);
        _ = log.Report(Reading(120, 44, 310, 8.3), Drawing(30, 30), 1.0);

        Assert.That(log.Report(Reading(120, 44, 310, 8.3), Drawing(2, 2), 1.9), Is.Null);

        Assert.That(
            log.Report(Reading(120, 44, 310, 8.3), Drawing(2, 2), 2.0),
            Does.Contain("draw 2"),
            "the 30 ms frames belonged to the interval already reported");
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

        Assert.That(log.Report(reading: null, Drawing(3, 3), 0.0), Is.Null);
        Assert.That(log.Report(reading: null, Drawing(3, 3), 5.0), Is.Null);

        // The interval is measured from the first REPORTABLE frame, so a run that spent five
        // seconds loading does not fire immediately and then again a second later.
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 5.0), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 5.5), Is.Null);
        Assert.That(log.Report(Reading(300, 280, 320, 3.3), Drawing(3, 3), 6.0), Is.Not.Null);
    }
}
