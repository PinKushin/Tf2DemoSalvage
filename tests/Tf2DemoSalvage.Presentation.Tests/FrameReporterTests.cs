using System.Diagnostics;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The once-a-second frame line, and the counters it resets on the way out.</summary>
/// <remarks>
/// **This was `MainForm.CountFrame`** (B188, D90): a one-second clock, two counter resets and a log
/// call. Exactly one part of it needed a window — the name of the Windows message that ended the
/// idle wait — and that is now passed in, the same arrangement `MomentView` uses.
///
/// **The clock is `IElapsedTime` so a second can pass without one passing.** `FrameLedger` already
/// took its elapsed seconds as an argument for this reason; the reporter is what owns the clock that
/// produces them, and it would otherwise be the one piece that could only be tested by sleeping.
/// </remarks>
public sealed class FrameReporterTests
{
    [Test]
    public void Drew_BeforeASecondHasPassed_ReportsNothing()
    {
        // Once a second, so a log covering a whole session stays readable — at 300 frames a second
        // the unguarded version would write 300 lines, each taking a lock and a flush, which is B191
        // exactly.
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        clock.Seconds = 0.9d;
        reporter.Drew(0.004d, View());

        log.Lines.Count.ShouldBe(0);
    }

    [Test]
    public void Drew_AfterASecondHasPassed_ReportsTheFrameRate()
    {
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        clock.Seconds = 1d;
        reporter.Drew(0.004d, View());

        log.Count("frames a second").ShouldBe(1);
    }

    [Test]
    public void Drew_WithTheWindowsMessage_PutsItInTheLine()
    {
        // **The one value a second frontend could not produce**, so it is the one value the reporter
        // is given rather than finding. If it stopped arriving the line would still print, with a
        // blank where the cause of the wake-up should be — silent, and only visible to someone who
        // knew what the column meant.
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        clock.Seconds = 1d;
        reporter.Drew(0.004d, new FrameView(Playing: true, Flying: false, YieldedTo: "WM_TIMER"));

        log.Count("WM_TIMER").ShouldBe(1);
    }

    [Test]
    public void Drew_AfterReporting_ClearsTheLightingTicks()
    {
        // **A per-second total that is never reset is a running total**, which reads as lighting
        // getting steadily slower for the life of the session. The reset is one line, it is the kind
        // that goes missing in a move, and nothing else in the program would notice.
        (FrameReporter reporter, _, FakeElapsedTime clock, EntityModelSet models) = Reporter();

        models.LightingTicks = 5_000;
        clock.Seconds = 1d;

        reporter.Drew(0.004d, View());

        models.LightingTicks.ShouldBe(0);
    }

    [Test]
    public void Drew_WithoutReporting_LeavesTheLightingTicksAlone()
    {
        // **The control for the case above.** Clearing on every frame rather than every report would
        // pass that test identically and throw away 299 frames of lighting cost out of every 300 —
        // an instrument reading near zero for work that is actually happening.
        (FrameReporter reporter, _, FakeElapsedTime clock, EntityModelSet models) = Reporter();

        models.LightingTicks = 5_000;
        clock.Seconds = 0.5d;

        reporter.Drew(0.004d, View());

        models.LightingTicks.ShouldBe(5_000);
    }

    [Test]
    public void Drew_AfterReporting_StartsTheNextSecondFromZero()
    {
        // Without the restart the clock keeps growing, so every frame after the first second is over
        // a second and every frame gets its own line — the flood the threshold exists to prevent,
        // arriving one second late.
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        clock.Seconds = 1d;
        reporter.Drew(0.004d, View());
        reporter.Drew(0.004d, View());
        reporter.Drew(0.004d, View());

        log.Count("frames a second").ShouldBe(1, "the second must restart, not keep accumulating");
    }

    [Test]
    public void Drew_OverASecond_CountsEveryFrameNotJustTheReportingOne()
    {
        // **The rate is frames per second, so the frames have to be counted where they happen.** A
        // reporter that only counted on the reporting call would report 1 for ever, which is a
        // plausible-looking number and the reason this asserts an exact rate.
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        for (int frame = 0; frame < 59; frame++)
        {
            reporter.Drew(0.004d, View());
        }

        clock.Seconds = 1d;
        reporter.Drew(0.004d, View());

        log.Count("60 frames a second").ShouldBe(1);
    }

    [Test]
    public void Drawing_AndYielded_ReachTheSameSecondAsTheFrames()
    {
        // **The reporter is the only face of frame accounting the window has**, so the two events it
        // reports from elsewhere — the draw call's cost and an idle yield — go through here too. If
        // they went to a ledger the window also held, that would be a second holder to keep in step.
        (FrameReporter reporter, RecordingLogger log, FakeElapsedTime clock, _) = Reporter();

        reporter.Yielded();
        reporter.Yielded();
        reporter.Drawing(Stopwatch.Frequency / 100);

        clock.Seconds = 1d;
        reporter.Drew(0.004d, View());

        log.Count("yielded 2 times").ShouldBe(1);
        log.Count("drawing 10 ms").ShouldBe(1);
    }

    private static (FrameReporter Reporter, RecordingLogger Log, FakeElapsedTime Clock, EntityModelSet Models)
        Reporter()
    {
        RecordingLogger log = new();
        FakeElapsedTime clock = new();
        EntityModelSet models = new();

        FrameReporter reporter = new(new FrameLedger(), models, clock, log);

        return (reporter, log, clock, models);
    }

    private static FrameView View() => new(Playing: false, Flying: false, YieldedTo: "WM_PAINT");
}
