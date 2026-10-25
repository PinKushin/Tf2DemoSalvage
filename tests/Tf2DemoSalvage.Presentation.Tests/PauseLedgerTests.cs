namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// <see cref="PauseLedger"/>: which frame stalls a heartbeat gap overlaps. A frequency of 1,000 makes a tick a
/// millisecond, so every value below is read as milliseconds.
/// </summary>
[TestFixture]
public sealed class PauseLedgerTests
{
    [Test]
    public void Report_AStallAGapOverlapsAndOneItDoesNot_PairsOnlyTheOverlap()
    {
        PauseLedger ledger = new(1000);

        ledger.Frame(end: 1500, length: 400, phase: "draw", pausedMs: 0);   // 1100..1500
        ledger.Frame(end: 3000, length: 150, phase: "hud", pausedMs: 12);   // 2850..3000
        ledger.Frame(end: 4000, length: 20, phase: "camera", pausedMs: 0);  // not a stall
        ledger.Beat(previous: 1050, now: 1450);                             // 400 ms gap inside the first
        ledger.Beat(previous: 3500, now: 3600);                             // 100 ms gap beside nothing
        ledger.Beat(previous: 3600, now: 3610);                             // not a gap

        ledger.Report(start: 1000).ShouldBe(
            "pauses: 2 frames over 100 ms, 2 heartbeat gaps over 50 ms; 2 frames over 50 ms, 550 ms in them\n" +
            "  stall at 0.500 s: 400 ms, mostly draw, gc paused 0 ms; heartbeat gap 400 ms ending 0.450 s\n" +
            "  stall at 2.000 s: 150 ms, mostly hud, gc paused 12 ms; no heartbeat gap\n" +
            "  gap without a stall at 2.600 s: 100 ms");
    }

    [Test]
    public void Report_AGapThatEndsWhereTheStallBegins_DoesNotOverlap()
    {
        PauseLedger ledger = new(1000);

        ledger.Frame(end: 1500, length: 200, phase: "draw", pausedMs: 0);   // 1300..1500
        ledger.Beat(previous: 1200, now: 1300);                             // ends at its start

        ledger.Report(start: 1000).ShouldContain("no heartbeat gap");
    }

    [Test]
    public void Report_FramesOverFiftyMilliseconds_AreCountedSummedAndCarryTheirDetail()
    {
        PauseLedger ledger = new(1000);

        ledger.Frame(end: 1500, length: 120, phase: "camera", pausedMs: 0, detail: "jit 40 ms");  // a stall, and slow
        ledger.Frame(end: 2000, length: 70, phase: "between", pausedMs: 0, detail: "jit 3 ms");   // slow only
        ledger.Frame(end: 2500, length: 50, phase: "draw", pausedMs: 0, detail: "never printed"); // not over 50

        ledger.Report(start: 1000).ShouldBe(
            "pauses: 1 frames over 100 ms, 0 heartbeat gaps over 50 ms; 2 frames over 50 ms, 190 ms in them\n" +
            "  stall at 0.500 s: 120 ms, mostly camera, gc paused 0 ms; no heartbeat gap; jit 40 ms\n" +
            "  slow at 1.000 s: 70 ms, mostly between, gc paused 0 ms; jit 3 ms");
    }

    [Test]
    public void IsSlow_AtAndPastFiftyMilliseconds_IsFalseThenTrue()
    {
        PauseLedger ledger = new(1000);

        ledger.IsSlow(50).ShouldBeFalse();
        ledger.IsSlow(51).ShouldBeTrue();
    }

    [Test]
    public void LargestPhase_AFrameMostlyOutsideItsSequence_IsBetween()
    {
        FramePhases phases = new(Sound: 1, Camera: 30, Project: 0, Advance: 2, Capture: 0, Hud: 5, Draw: 20, Total: 60);

        PauseLedger.LargestPhase(phases, frameTicks: 60).ShouldBe("camera");
        PauseLedger.LargestPhase(phases, frameTicks: 100).ShouldBe("between");
    }
}
