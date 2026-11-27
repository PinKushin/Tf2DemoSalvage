using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The moment source carries a view's bone-setup reports to the timeline's cycle resets (B878).</summary>
/// <remarks>
/// Fixture as `BoneSetupConformanceTests`: updates at 10, 20, 30, 40, cycles 0.2 to 0.8, animation times 3 behind, parity
/// 0, 0, 1, 1. Reported posed through 25 and not after, the update at 40 resets (its parity differs from the setup's), so
/// 40 holds 0.8. Never reported, the source makes no record and samples as before — a blend from the reset at 30.
/// </remarks>
public sealed class TimelineMomentsBoneSetupTests
{
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    private static float CycleAt40(bool report)
    {
        TimelineMoments moments = new(DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"],
            animationLag: 3,
            (10, 1, [], 0.2f, 0),
            (20, 1, [], 0.4f, 0),
            (30, 1, [], 0.6f, 1),
            (40, 1, [], 0.8f, 1))));
        List<SceneProp> props = [];
        HashSet<int> prop = [SyntheticAnimatingProp.EntityIndex];
        HashSet<int> none = [];

        for (int tick = 10; tick < 40; tick++)
        {
            moments.PropsAt(tick, props);

            if (report)
            {
                moments.BonesSetUp(tick <= 25 ? prop : none, tick);
            }
        }

        moments.PropsAt(40, props);

        return props.Single(sampled => sampled.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle;
    }

    [Test]
    public void PropsAt_AfterReportsThatStopPosingAProp_HoldsTheCycleItsUnseenResetLeft()
    {
        CycleAt40(report: true).ShouldBe(0.8f);
        CycleAt40(report: false).ShouldBe(
            0.6f + (0.2f * (40 - Delay - 30) / 7f), 1e-5f, "the control: never reported, nothing changes");
    }
}
