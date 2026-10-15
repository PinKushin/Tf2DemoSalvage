using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The timeline builder snapshotted between any two commands and resumed must produce the timeline a straight build
/// produces (D217) — the property a checkpointed seek stands on.
/// </summary>
/// <remarks>
/// **Every cut, not one.** The snapshot is taken after each command in turn, from before the first to after the last,
/// and the live builder runs on past it before the resume, so the resumed build meets output lists that have grown and
/// had their last element replaced since — the case length marks exist for.
/// </remarks>
public sealed class TimelineBuilderSnapshotTests
{
    private static IEnumerable<TestCaseData> Demos()
    {
        yield return new TestCaseData(SyntheticPlayer.DemoOverTicks(0.015f, (10, 0f, 0f), (11, 4f, 0f), (12, 8f, 2f), (14, 16f, 4f)))
            .SetArgDisplayNames("a player moving");
        yield return new TestCaseData(SyntheticPlayer.DemoOfALateTeam(0.015f, (10, null), (12, SceneTeams.Blu), (14, SceneTeams.Red)))
            .SetArgDisplayNames("a team stated late");
        yield return new TestCaseData(SyntheticRocket.DemoOverTicks(0.015f, ownerModel: SyntheticRocket.Model, [(20, 0f, 0f, 0f), (21, 10f, 0f, 0f), (22, 20f, 5f, 0f), (24, 40f, 5f, 2f)]))
            .SetArgDisplayNames("a rocket and its owner's track");
    }

    /// <remarks>**The equivalence**: identical fingerprints at every cut, and the fingerprint must have read something.</remarks>
    [TestCaseSource(nameof(Demos))]
    public void BuildThroughSnapshot_AtEveryCommand_EqualsAStraightBuild(byte[] demo)
    {
        TimelineFingerprint straight = TimelineFingerprint.Of(DemoTimeline.Build(demo));
        straight.Values.ShouldBeGreaterThan(10);

        int commands = DemoTimeline.CommandCount(demo);
        commands.ShouldBeGreaterThan(3);

        for (int at = 0; at <= commands; at++)
        {
            TimelineFingerprint.Of(DemoTimeline.BuildThroughSnapshot(demo, at)).ShouldBe(straight, $"snapshot after {at} of {commands} commands");
        }
    }
}
