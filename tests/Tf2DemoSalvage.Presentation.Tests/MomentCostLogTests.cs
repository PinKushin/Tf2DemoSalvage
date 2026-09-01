using System;
using System.Diagnostics;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The periodic breakdown of `advance`, which is where the frame time actually goes.
/// </summary>
/// <remarks>
/// **Measured before this existed:** at 96 fps on `tf2-2026-pub-pov-clean`, an 11 ms frame is
/// `advance 7.7, draw 1.7, sound 0.4` — so seventy per cent of the frame is the scene rebuild and
/// the GPU is nearly idle. `StallReport.Moment` names the parts of `advance` and fires only past
/// 30 ms, so at this rate it never fires and nothing says which part.
///
/// **Counted in rebuilds rather than seconds**, because this runs inside the rebuild and has no
/// clock. One line per hundred is about one a second at the rate above, and the line says how many
/// it averaged so a reader never has to assume.
/// </remarks>
public sealed class MomentCostLogTests
{
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    private static MomentPhases Posing(int milliseconds) =>
        new(
            Total: milliseconds * Millisecond,
            DrawList: 0,
            Models: 0,
            Pose: milliseconds * Millisecond,
            Weapons: 0,
            Viewmodel: 0,
            Counters: default,
            Drawn: 0);

    [Test]
    public void Report_BeforeTheCountIsReached_SaysNothing()
    {
        MomentCostLog log = new(every: 3);

        Assert.That(log.Report(Posing(5), sampleTicks: 0), Is.Null);
        Assert.That(log.Report(Posing(5), sampleTicks: 0), Is.Null);
    }

    /// <remarks>
    /// **The three rebuilds differ and the mean differs from each**, so a report of the last one
    /// cannot pass by accident: pose of 3, 6 and 12 means 7.
    /// </remarks>
    [Test]
    public void Report_OverTheInterval_AveragesEveryRebuildRatherThanTheLast()
    {
        MomentCostLog log = new(every: 3);

        _ = log.Report(Posing(3), sampleTicks: 0);
        _ = log.Report(Posing(6), sampleTicks: 0);

        string? line = log.Report(Posing(12), sampleTicks: 0);

        Assert.That(line, Does.Contain("over 3 rebuilds"));
        Assert.That(line, Does.Contain("pose 7"), "mean of 3, 6 and 12 — not the last rebuild's 12");
    }

    [Test]
    public void Report_AfterReporting_ForgetsTheRebuildsItAlreadyReported()
    {
        MomentCostLog log = new(every: 2);

        _ = log.Report(Posing(30), sampleTicks: 0);
        _ = log.Report(Posing(30), sampleTicks: 0);

        _ = log.Report(Posing(2), sampleTicks: 0);

        Assert.That(
            log.Report(Posing(2), sampleTicks: 0),
            Does.Contain("pose 2"),
            "the 30 ms rebuilds belonged to the interval already reported");
    }

    /// <remarks>
    /// Sampling the timeline is measured outside <see cref="MomentPhases"/> and is a real column —
    /// 2 to 6.5 ms in the only breakdowns seen so far — so it has to be averaged alongside rather
    /// than dropped.
    /// </remarks>
    [Test]
    public void Report_WithSampling_AveragesItAsItsOwnColumn()
    {
        MomentCostLog log = new(every: 2);

        _ = log.Report(Posing(0), sampleTicks: 2 * Millisecond);

        Assert.That(
            log.Report(Posing(0), sampleTicks: 8 * Millisecond),
            Does.Contain("sample 5"),
            "mean of 2 and 8");
    }
}
