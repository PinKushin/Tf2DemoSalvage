using System;
using System.Diagnostics;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The once-a-second account of where a second of frames went.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.CountFrame</c>** (B188, D90) — an accumulator, a threshold and a format
/// string, none of which needs a window, and none of which had a test.
///
/// **It is not <see cref="FpsMeter"/> and the two must not be merged.** That one is TF2's
/// `cl_showfps` HUD panel: a smoothed average with colour thresholds, drawn on screen. This is a
/// diagnostic ledger written to the log, and its value is the BREAKDOWN — B191 was found by reading
/// which column stayed fat while the others were measured away.
/// </remarks>
public sealed class FrameLedgerTests
{
    [Test]
    public void Report_BeforeASecondHasPassed_IsNothing()
    {
        // **The control for every case below, and the reason this returns a nullable.** A ledger
        // that reported per frame would write sixty lines a second — which is B191's defect, not a
        // diagnostic — so "not yet" has to be expressible.
        FrameLedger ledger = new();

        ledger.Drew(0.016d);

        ledger.Report(Context(), elapsedSeconds: 0.5d).ShouldBeNull();
    }

    [Test]
    public void Report_AfterASecond_NamesTheRateAndTheLongestFrame()
    {
        // Predicted exactly: sixty frames in one second is 60 a second, and the longest of them is
        // the one that matters — a mean hides the stall that a person actually sees.
        FrameLedger ledger = new();

        for (int frame = 0; frame < 59; frame++)
        {
            ledger.Drew(0.016d);
        }

        ledger.Drew(0.120d);

        string report = ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull();

        report.ShouldContain("60 frames a second");
        report.ShouldContain("longest 120");
    }

    [Test]
    public void Report_TheLongestFrame_IsNotClamped()
    {
        // **A saturating instrument is worse than a missing one**, because the ceiling looks like a
        // number somebody measured. The flight clamp used to be applied to this reading, so the
        // worst frame could never exceed 100 ms — and the owner's report of "half a second to maybe
        // a second" met a log that said `longest 100 ms` every time.
        FrameLedger ledger = new();

        ledger.Drew(0.75d);

        ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull().ShouldContain("longest 750");
    }

    [Test]
    public void Report_AfterReporting_StartsTheNextSecondEmpty()
    {
        // Every counter is per-second, so one that survived a report would make the next second read
        // as worse than it was — and a ledger that accumulates for ever eventually reports the whole
        // session as one bad second.
        FrameLedger ledger = new();

        ledger.Drew(0.500d);
        ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull();

        ledger.Drew(0.016d);

        string second = ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull();

        second.ShouldContain("1 frames a second");
        second.ShouldNotContain("longest 500");
    }

    [Test]
    public void Report_ThePhaseTotals_AreCarriedAsMilliseconds()
    {
        // The breakdown is the whole point of the line. Each phase is accumulated in stopwatch ticks
        // and printed in milliseconds, so a caller adding ticks and a reader seeing milliseconds
        // must agree — which is exactly the conversion a hand-rolled format string gets wrong.
        FrameLedger ledger = new();

        ledger.Drew(0.016d);
        ledger.Sampled(Ticks(12d));
        ledger.Posed(Ticks(34d));
        ledger.Drawing(Ticks(56d));

        string report = ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull();

        report.ShouldContain("sampling 12");
        report.ShouldContain("posing 34");
        report.ShouldContain("drawing 56");
    }

    [Test]
    public void Report_TheContext_IsRepeatedVerbatim()
    {
        // The window contributes what only it knows — whether playback is running, whether keys are
        // held, and which Windows message ended the idle burst. Those arrive as data; this must not
        // reinterpret them.
        FrameLedger ledger = new();

        ledger.Drew(0.016d);
        ledger.Yielded();
        ledger.Yielded();

        string report = ledger
            .Report(
                new FrameContext(
                    Playing: true, Flying: true, YieldedTo: "WM_TIMER", LightingTicks: 0, Garbage: " gc 1/0/0"),
                elapsedSeconds: 1d)
            .ShouldNotBeNull();

        report.ShouldContain("playing");
        report.ShouldContain("flying");
        report.ShouldContain("yielded 2 times to WM_TIMER");
        report.ShouldContain("gc 1/0/0");
    }

    [Test]
    public void Report_WhenPausedAndStill_SaysSoRatherThanNothing()
    {
        // The bystander for the case above: the same call with both flags off must produce the
        // OTHER words, not silence. A report that only ever said "playing" would pass the test
        // above and be wrong half the time.
        FrameLedger ledger = new();

        ledger.Drew(0.016d);

        string report = ledger.Report(Context(), elapsedSeconds: 1d).ShouldNotBeNull();

        report.ShouldContain("paused");
        report.ShouldNotContain("flying");
    }

    private static FrameContext Context() =>
        new(Playing: false, Flying: false, YieldedTo: "nothing", LightingTicks: 0, Garbage: "");

    private static long Ticks(double milliseconds) =>
        (long)(milliseconds / 1000d * Stopwatch.Frequency);
}
