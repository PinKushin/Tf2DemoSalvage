using System;
using System.Diagnostics;
using System.Linq;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Naming where a slow frame, a slow scene rebuild or a slow sound step went.
/// </summary>
/// <remarks>
/// **These were <c>MainForm.ReportSlowFrame</c>, <c>ReportSlowMoment</c> and
/// <c>ReportSlowSounds</c>** (B188, D90) — around 190 lines of arithmetic and formatting with no
/// window in any of it, and no test, because reaching them meant constructing a form.
///
/// **That absence is why they matter.** B191 was found by reading these lines: a stall the owner
/// described as "every handful of seconds" turned out to be one log line taking a machine-wide
/// mutex, and it was located by watching which column stayed fat as more were measured. A report
/// that got its own arithmetic wrong would have sent that hunt somewhere else entirely.
///
/// **Every test predicts an exact number.** A report is a measurement, and an assertion that a
/// measurement is merely present tells you nothing about whether it is right.
/// </remarks>
public sealed class StallReportTests
{
    /// <summary>Comfortably over the threshold, so a report is expected.</summary>
    private const double SlowMs = 100d;

    /// <summary>Comfortably under it.</summary>
    private const double FastMs = 5d;

    [Test]
    public void Frame_UnderTheThreshold_IsNotReported()
    {
        // **The control for every report below.** A reporter that logged unconditionally would pass
        // any test that only checks a slow frame produces a line — and it would also flood the log
        // at sixty lines a second, which is B191's defect exactly.
        RecordingLogger log = new();

        StallReport.Frame(Phases(FastMs / 7d), log);

        log.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Frame_OverTheThreshold_NamesEveryPhase()
    {
        RecordingLogger log = new();

        StallReport.Frame(Phases(SlowMs / 7d), log);

        log.Lines.Count.ShouldBe(1);

        string line = log.Lines[0].Message;

        // Seven phases, in the order the frame runs them. Named individually rather than by a
        // count, because a report that lost one column would still have "seven commas".
        foreach (string phase in
            new[] { "sound", "camera", "project", "advance", "capture", "hud", "draw" })
        {
            line.ShouldContain(phase);
        }

        line.ShouldStartWith("SLOW FRAME");
    }

    [Test]
    public void Frame_WithTimeInNoNamedPhase_ReportsItAsUnaccounted()
    {
        // **`unaccounted` is Valve's own name for this column** — `VPROF_BUDGETGROUP_OTHER_UNACCOUNTED`
        // is `_T("Unaccounted")` in `public/tier0/vprof.h`. It is also the single most useful number
        // in these lines: B191 was found because every measured column read about 1 ms while the
        // remainder held 126.
        //
        // So the input here has a total LARGER than its parts, which is the only condition under
        // which a correct report and one that prints zero can be told apart.
        RecordingLogger log = new();

        FramePhases phases = Phases(1d) with { Total = Ticks(100d) };

        StallReport.Frame(phases, log);

        // 100 ms total, seven phases of 1 ms each: 93 left over.
        log.Lines[0].Message.ShouldContain("unaccounted 93");
    }

    // **`Frame_BuiltFromTimestamps_MeasuresTheGapsBetweenThem` was here until 2026-08-26** (B203).
    // It guarded `FramePhases.Between` against off-by-one PAIRING — charging `flownAt - soundedAt`
    // to the camera column when it belonged to the project one — and both the method and that
    // failure mode are now gone: `FrameSequence` names each phase at the call that produces it, so
    // there is no pairing step left to get wrong.
    //
    // Its replacement is `FrameSequenceTests.Run_WithOneSlowStage_ChargesTheTimeToThatStagesColumn`,
    // which is strictly stronger — it walks every stage rather than checking one arithmetic chain.

    [Test]
    public void Frame_WithNoLogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => StallReport.Frame(Phases(SlowMs), log: null!));
    }

    [Test]
    public void Moment_UnderTheThreshold_IsNotReported()
    {
        RecordingLogger log = new();

        StallReport.Moment(default, Ticks(1d), Ticks(1d), log);

        log.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Moment_SlowOnlyBecauseOfSampling_IsStillReported()
    {
        // **The threshold covers the whole moment, not just the scene rebuild.** Sampling the
        // timeline and building the markers are measured here rather than inside `MomentScene`, so
        // a threshold on the rebuild's own total would miss a moment made slow by either — and
        // report nothing at all for it.
        RecordingLogger log = new();

        StallReport.Moment(default, Ticks(SlowMs), playerTicks: 0, log);

        log.Lines.Count.ShouldBe(1);
        log.Lines[0].Message.ShouldContain("sample 100");
    }

    [Test]
    public void Moment_WithPoseTimeInNoSubPhase_ReportsItAsRest()
    {
        // **`rest` is a residual and the rest are measured**, which is the thing to know when
        // reading one of these lines: every direct column small while `rest` is large means the
        // cost is in something still unmeasured. That pattern is what found B191, and it took
        // several rounds to notice because it was read as noise.
        RecordingLogger log = new();

        MomentPhases phases = new(
            Total: Ticks(SlowMs),
            DrawList: 0,
            Models: 0,
            Pose: Ticks(SlowMs),
            Weapons: 0,
            Viewmodel: 0,
            Counters: default,
            Drawn: 0);

        StallReport.Moment(phases, sampleTicks: 0, playerTicks: 0, log);

        // The whole 100 ms pose is in no measured sub-phase.
        log.Lines[0].Message.ShouldContain("rest 100");
    }

    [Test]
    public void Moment_WithNoLogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(() =>
            StallReport.Moment(default, Ticks(SlowMs), 0, log: null!));
    }

    [Test]
    public void Sounds_UnderTheThreshold_AreNotReported()
    {
        RecordingLogger log = new();

        StallReport.Sounds(default, Ticks(FastMs), log);

        log.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Sounds_OverTheThreshold_NameEveryPhase()
    {
        RecordingLogger log = new();

        SoundPhases phases = new(
            Advance: Ticks(10d),
            Reclaim: Ticks(20d),
            Loops: Ticks(30d),
            Soundscape: Ticks(40d),
            Starting: Ticks(50d));

        StallReport.Sounds(phases, Ticks(SlowMs), log);

        string line = log.Lines[0].Message;

        line.ShouldStartWith("SLOW SOUND");
        line.ShouldContain("advance 10");
        line.ShouldContain("reclaim 20");
        line.ShouldContain("loops 30");
        line.ShouldContain("soundscape 40");
        line.ShouldContain("starting 50");
    }

    [Test]
    public void Sounds_WithNoLogger_Refuse()
    {
        Should.Throw<ArgumentNullException>(() =>
            StallReport.Sounds(default, Ticks(SlowMs), log: null!));
    }

    [Test]
    public void Sounds_AtExactlyTheThreshold_AreNotReported()
    {
        // The boundary, stated: the comparison is strictly greater, so a step that takes exactly
        // the budget is not a stall. Worth pinning because the three copies of this number in the
        // repository could otherwise drift in their comparison as well as in their value.
        RecordingLogger log = new();

        StallReport.Sounds(default, Ticks(StallReport.StallSeconds * 1000d), log);

        log.Lines.ShouldBeEmpty();
    }

    /// <summary>Seven equal phases, each of the given duration.</summary>
    private static FramePhases Phases(double eachMs)
    {
        long each = Ticks(eachMs);

        return new FramePhases(each, each, each, each, each, each, each, each * 7);
    }

    /// <summary>A duration in milliseconds, as stopwatch ticks.</summary>
    private static long Ticks(double milliseconds) =>
        (long)(milliseconds / 1000d * Stopwatch.Frequency);

    // `Ms`, the inverse of `Ticks`, went with the `Between` test it existed for (B203).
}
