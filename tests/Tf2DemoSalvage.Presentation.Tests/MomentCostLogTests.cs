using System;
using System.Diagnostics;

using Tf2DemoSalvage.Animation.Animating;
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

    /// <remarks>B578: the four parts of drawlist, each distinct, averaged over two rebuilds and printed in order.</remarks>
    [Test]
    public void Report_WithDrawListParts_PrintsEachPartsMean()
    {
        MomentCostLog log = new(every: 2);
        MomentPhases phases = Posing(0) with
        {
            DrawList = 10 * Millisecond,
            DrawListParts = new DrawListParts(1 * Millisecond, 2 * Millisecond, 3 * Millisecond, 4 * Millisecond, 1 * Millisecond),
        };

        _ = log.Report(phases, sampleTicks: 0);
        string? line = log.Report(
            phases with { DrawListParts = phases.DrawListParts with { Filters = 6 * Millisecond, FirstPerson = 2 * Millisecond } }, 0);

        Assert.That(line, Does.Contain("drawlist 10 (prepare 1, players 2, place 3, filters 5 (first person 1.5))"));
    }

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

    /// <remarks>
    /// **Ragdoll stepping runs inside `pose` with no column of its own**, so on `cp_process_f12` it
    /// read as `rest 44` of a 50 ms pose. Named, a 50 ms pose with 40 ms of stepping must read
    /// `corpses 40` and leave `rest 10`.
    /// </remarks>
    [Test]
    public void Report_WithCorpseStepping_NamesItAndTakesItOutOfRest()
    {
        MomentCostLog log = new(every: 1);

        MomentPhases phases = Posing(50) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Corpses: 40 * Millisecond, CorpseSteps: 8, Advance: 40 * Millisecond),
        };

        string? line = log.Report(phases, sampleTicks: 0);

        // 40 ms over 8 steps is 5 ms a step, the number that separates many corpses from a slow one.
        Assert.That(line, Does.Contain("corpses 40 over 8 steps"));
        Assert.That(line, Does.Contain("rest 10"));
    }

    /// <remarks>
    /// **The columns that split `rest` on `cp_process_f12`, where it was 6–20 ms untimed.** Each is
    /// given a distinct width so a column printing another's value cannot pass: 60 ms of pose less
    /// collate 1, baked 2, place 3, advance 9 and undrawn 6 leaves rest 39. The corpse step (5) sits
    /// INSIDE advance, so taking it out as well would read 34.
    /// </remarks>
    [Test]
    public void Report_WithEveryPoseColumn_NamesEachAndLeavesTheResidual()
    {
        MomentCostLog log = new(every: 1);

        MomentPhases phases = Posing(60) with
        {
            Undrawn = 6 * Millisecond,
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                Corpses: 5 * Millisecond,
                Collate: 1 * Millisecond,
                Baked: 2 * Millisecond,
                Place: 3 * Millisecond,
                Advance: 9 * Millisecond),
        };

        string? line = log.Report(phases, sampleTicks: 0);

        Assert.That(line, Does.Contain("collate 1,"));
        Assert.That(line, Does.Contain("baked 2,"));
        Assert.That(line, Does.Contain("place 3 ("));
        Assert.That(line, Does.Contain("advance 9"));
        Assert.That(line, Does.Contain("undrawn 6 ("));
        Assert.That(line, Does.Contain("rest 39"));
    }

    /// <remarks>
    /// **Where the skeletons were built**: 24 a rebuild against 5 posed on f12 says most are not the draw's. Distinct widths
    /// (9 total, 6 simulating, 2 drawing) so a column printing another's count cannot pass.
    /// </remarks>
    [Test]
    public void Report_WithBuildsBySite_NamesEach()
    {
        MomentCostLog log = new(every: 1);

        MomentPhases phases = Posing(10) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15, BuildsSimulating: 6, BuildsDrawing: 2, BuildsCorpses: 3, BuildsPlacing: 4),
        };

        // Each counted where it was built (B243); 15 against 6+2+3+4 also shows nothing else built one.
        Assert.That(log.Report(phases, sampleTicks: 0), Does.Contain("built 15 (simulate 6, corpses 3, draw 2, place 4)"));
    }

    /// <remarks>
    /// **Finding 105 A6's split of setup**, over two rebuilds: roots and children are sums, the slowest root a level whose
    /// mean (2 and 6 → 4) and peak (6) differ, so printing the mean as the peak, or a sum as the level, cannot pass.
    /// </remarks>
    [Test]
    public void Report_WithSetupSplitByRoot_MeansTheSumsAndPeaksTheSlowest()
    {
        MomentCostLog log = new(every: 2);

        MomentPhases Rebuild(long slowest) => Posing(10) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 10 * Millisecond, 0, 0, 0, 0, 0,
                SetupRoot: 7 * Millisecond, SetupChild: 3 * Millisecond, RootBuilds: 5, ChildBuilds: 9, MaxRoot: slowest * Millisecond,
                SetupEligible: 8 * Millisecond / 10, EligibleBuilds: 2, PrePose: 6 * Millisecond / 10, PrePosed: 3, PrePoseBlend: 2 * Millisecond / 10),
        };

        _ = log.Report(Rebuild(2), sampleTicks: 0);
        string? line = log.Report(Rebuild(6), sampleTicks: 0);

        Assert.That(
            line,
            Does.Contain("setup 10 (roots 7 over 5 built, eligible 0.8 over 2, slowest 4 peak 6; children 3 over 9 built; prepose 0.6 over 3, blend 0.2)"));
    }

    /// <remarks>
    /// **The parts of the roots' setup, and the residual no timer covers**, over two identical rebuilds so a sum printed as
    /// the mean cannot pass. Every width distinct. 70 ms of roots less blend 2, base 3, accumulate 13, adjust 1, loop 11,
    /// ik 4 and finish 8 leaves unsplit 28; layer sampling (5) sits INSIDE accumulate, so subtracting it as well would
    /// read 23.
    /// </remarks>
    [Test]
    public void Report_WithRootSetupParts_NamesEachAndLeavesTheUnsplitResidual()
    {
        MomentCostLog log = new(every: 2);

        MomentPhases phases = Posing(100) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 80 * Millisecond, 0, 0, 0, 0, 0,
                SetupRoot: 70 * Millisecond,
                RootParts: new RootSetupParts(
                    Blend: 2 * Millisecond,
                    Base: 3 * Millisecond,
                    Accumulate: 13 * Millisecond,
                    Sampling: 5 * Millisecond,
                    Samples: 6,
                    Adjust: 1 * Millisecond,
                    Loop: 11 * Millisecond,
                    Ik: 4 * Millisecond,
                    Finish: 8 * Millisecond)),
        };

        _ = log.Report(phases, sampleTicks: 0);

        Assert.That(
            log.Report(phases, sampleTicks: 0),
            Does.Contain(
                "roots split (blend 2, base 3, accumulate 13 of which layer sampling 5 over 6"
                + " (seed 0, locks 0, compose 0, other 8; enumerators 0), adjust 1, loop 11, ik 4, finish 8; unsplit 28)"));
    }

    /// <remarks>
    /// **The parts of `accumulate`, and its own residual**, over two identical rebuilds. 13 ms of accumulate less layer
    /// sampling 5, seed 0.5, locks 0.75 and compose 5.5 leaves other 1.25; the enumerators are a count, 18 over two
    /// rebuilds. The roots' `unsplit` stays 28: the sub-parts are inside accumulate and must not be subtracted again.
    /// </remarks>
    [Test]
    public void Report_WithAccumulateSubParts_NamesEachAndLeavesTheOtherResidual()
    {
        MomentCostLog log = new(every: 2);

        MomentPhases phases = Posing(100) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 80 * Millisecond, 0, 0, 0, 0, 0,
                SetupRoot: 70 * Millisecond,
                RootParts: new RootSetupParts(
                    Blend: 2 * Millisecond,
                    Base: 3 * Millisecond,
                    Accumulate: 13 * Millisecond,
                    Sampling: 5 * Millisecond,
                    Samples: 6,
                    Adjust: 1 * Millisecond,
                    Loop: 11 * Millisecond,
                    Ik: 4 * Millisecond,
                    Finish: 8 * Millisecond,
                    Seed: Millisecond / 2,
                    Locks: 3 * Millisecond / 4,
                    Compose: 11 * Millisecond / 2,
                    Enumerators: 9)),
        };

        _ = log.Report(phases, sampleTicks: 0);

        Assert.That(
            log.Report(phases, sampleTicks: 0),
            Does.Contain(
                "roots split (blend 2, base 3, accumulate 13 of which layer sampling 5 over 6"
                + " (seed 0.5, locks 0.75, compose 5.5, other 1.25; enumerators 9), adjust 1, loop 11, ik 4"
                + ", finish 8; unsplit 28)"));
    }

    /// <remarks>
    /// **The parts of `place`, each a distinct width**, so a column printing its neighbour's value cannot pass.
    /// </remarks>
    [Test]
    public void Report_WithPlaceParts_NamesEach()
    {
        MomentCostLog log = new(every: 1);

        MomentPhases phases = Posing(30) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                Place: 20 * Millisecond,
                PlaceCompose: 1 * Millisecond,
                PlaceFade: 2 * Millisecond,
                PlaceBuild: 17 * Millisecond,
                PlaceBinds: 3 * Millisecond,
                PlaceLookups: 14 * Millisecond,
                PlaceMatrix: 4 * Millisecond,
                PlaceSwap: 5 * Millisecond,
                PlaceFlex: 6 * Millisecond),
        };

        Assert.That(
            log.Report(phases, sampleTicks: 0),
            Does.Contain("place 20 (compose 1, fade 2, build 17 of which binds 3, lookups 14 = matrix 4, swap 5, flex 6)"));
    }

    /// <remarks>
    /// **The parts of `flex`, each a distinct width, over two identical rebuilds**: a mean prints each value once, a sum
    /// would print it doubled, and a column printing its neighbour's value cannot pass.
    /// </remarks>
    [Test]
    public void Report_WithFlexParts_NamesEachAsAMeanPerRebuild()
    {
        MomentCostLog log = new(every: 2);

        MomentPhases phases = Posing(100) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                PlaceFlex: 90 * Millisecond,
                Flex: new FlexCounters(
                    Driving: 11 * Millisecond,
                    Scenes: 2 * Millisecond,
                    Voices: 3 * Millisecond,
                    Step: 4 * Millisecond,
                    Local: 5 * Millisecond,
                    Rules: 6 * Millisecond,
                    Delayed: 7 * Millisecond,
                    Probe: 8 * Millisecond,
                    Corners: 9 * Millisecond,
                    Clear: 10 * Millisecond,
                    Deltas: 12 * Millisecond,
                    Slots: 13 * Millisecond,
                    Resting: 14 * Millisecond,
                    Calls: 40,
                    Faces: 9,
                    Own: 5,
                    Worn: 2,
                    Corpse: 1,
                    RestingFaces: 1,
                    Hits: 3,
                    Misses: 5,
                    Vertices: 1234)),
        };

        _ = log.Report(phases, sampleTicks: 0);

        Assert.That(
            log.Report(phases, sampleTicks: 0),
            Does.Contain(
                "flex 90), flex parts (driving 11 = scenes 2, voices 3, step 4; local 5, rules 6, delayed 7, probe 8, corners 9"
                + ", clear 10, deltas 12, slots 13, resting 14; calls 40, faces 9 = own 5, worn 2, corpse 1, resting 1"
                + "; hits 3, misses 5, vertices 1234)"));
    }

    /// <remarks>
    /// **The step is inside `advance`, so it is taken out once.** 50 ms of pose, 30 of advance holding 20 of stepping: the
    /// formula reads 20. Subtracting both would read 0, `corpses` alone 30, and neither 50 — four candidates, four answers.
    /// </remarks>
    [Test]
    public void PoseRest_WithTheStepInsideAdvance_TakesItOutOnce()
    {
        MomentPhases phases = Posing(50) with
        {
            Counters = new EntityModelSet.PoseCounters(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Corpses: 20 * Millisecond, Advance: 30 * Millisecond),
        };

        Assert.That(phases.PoseRest, Is.EqualTo(20 * Millisecond));
    }

    /// <remarks>
    /// **A rebuild replays every tick since the earliest death, so it is the number that says whether `corpses` is the
    /// engine's per-tick step or our catch-up** — a count summed over the window (3 across two rebuilds), not averaged.
    /// </remarks>
    [Test]
    public void Report_WithCorpseRebuilds_SumsTheCountAndMeansTheTime()
    {
        MomentCostLog log = new(every: 2);

        _ = log.Report(Posing(10) with { Counters = new EntityModelSet.PoseCounters(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, CorpseRebuilds: 1, CorpseBuilding: 2 * Millisecond) }, sampleTicks: 0);

        string? line = log.Report(Posing(10) with { Counters = new EntityModelSet.PoseCounters(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, CorpseRebuilds: 2, CorpseBuilding: 6 * Millisecond) }, sampleTicks: 0);

        Assert.That(line, Does.Contain("3 rebuilt in 4"));
    }
}
