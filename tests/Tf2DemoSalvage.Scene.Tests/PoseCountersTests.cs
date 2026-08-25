namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Differencing the pose-phase counters across one call.
/// </summary>
/// <remarks>
/// **Ten fields subtracted by hand, which is the whole risk.** Every counter is a running total and
/// <c>Since</c> pairs each with its own earlier value — pairing one with a NEIGHBOUR compiles, runs,
/// and produces entirely plausible milliseconds. The ledger these feed is read to decide where a
/// stall is, so a crossed pair would send the next investigation at the wrong subsystem, which is
/// exactly what B191 cost a night to.
/// </remarks>
public sealed class PoseCountersTests
{
    [Test]
    public void Since_AnEarlierSnapshot_SubtractsEveryFieldFromItsOwnPair()
    {
        // **Every field gets a DISTINCT value, and that is the experiment.** With repeated values a
        // crossed pair produces the same answer as a correct one, so the test would pass against
        // the bug it exists to catch. The two snapshots differ by a different amount per field for
        // the same reason.
        EntityModelSet.PoseCounters before = new(
            Lighting: 100,
            Simulate: 200,
            WornLight: 300,
            Report: 400,
            ReportLog: 500,
            Setup: 600,
            Skin: 700,
            Animation: 800,
            AnimationCalls: 900,
            Built: 1000);

        EntityModelSet.PoseCounters now = new(
            Lighting: 101,
            Simulate: 202,
            WornLight: 303,
            Report: 404,
            ReportLog: 505,
            Setup: 606,
            Skin: 707,
            Animation: 808,
            AnimationCalls: 909,
            Built: 1010);

        EntityModelSet.PoseCounters moment = now.Since(before);

        moment.Lighting.ShouldBe(1);
        moment.Simulate.ShouldBe(2);
        moment.WornLight.ShouldBe(3);
        moment.Report.ShouldBe(4);
        moment.ReportLog.ShouldBe(5);
        moment.Setup.ShouldBe(6);
        moment.Skin.ShouldBe(7);
        moment.Animation.ShouldBe(8);
        moment.AnimationCalls.ShouldBe(9);
        moment.Built.ShouldBe(10);
    }

    [Test]
    public void Since_TheSameSnapshot_IsAllZeroes()
    {
        // A moment in which nothing happened — a paused viewer reprojecting the same tick — must
        // read as zero rather than as the totals since the demo opened.
        EntityModelSet.PoseCounters counters = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        counters.Since(counters).ShouldBe(default(EntityModelSet.PoseCounters));
    }
}
