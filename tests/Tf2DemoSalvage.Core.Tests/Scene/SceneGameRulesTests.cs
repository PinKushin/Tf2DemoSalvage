using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The game rules the kill feed tests, read per frame: `IsMannVsMachineMode`, `IsHalloweenScenario`, player destruction.</summary>
public sealed class SceneGameRulesTests
{
    [Test]
    public void RulesAt_AMannVsMachineHightowerRound_ReadsBoth() =>
        DemoTimeline.Build(SyntheticPlayer.DemoWithGameRules(mannVsMachine: true, halloweenScenario: 4, playerDestruction: false))
            .RulesAt(100)
            .ShouldBe(new SceneGameRules(MannVsMachine: true, HalloweenScenario: 4, PlayerDestruction: false) { Present = true });

    [TestCase(true)]
    [TestCase(false)]
    public void RulesAt_ATrainingFlagSent_IsInTrainingAsSent(bool inTraining) =>
        // `IsInTraining()` is `m_bIsInTraining` (tf_gamerules.h:678), which the spectator bar's hint asks first (B733).
        DemoTimeline.Build(SyntheticPlayer.DemoWithGameRules(mannVsMachine: false, halloweenScenario: 0, playerDestruction: false, inTraining))
            .RulesAt(100).InTraining.ShouldBe(inTraining);

    /// <remarks>
    /// **Before the first frame the rules are the client's unsent ones, not a zeroed struct** (B856): `default` skipped the
    /// initializers, reading match group 0 (MvM practice) for -1, respawn wave times 0 for -1 and the ropes' holiday
    /// lights off for the constructor's on (`tf_gamerules.cpp:1644`).
    /// </remarks>
    [Test]
    public void RulesAt_BeforeTheFirstFrame_IsTheUnsentDefaults()
    {
        SceneGameRules rules = DemoTimeline.Build(SyntheticPlayer.DemoAtServerTick(tick: 66, serverTick: 13_557)).RulesAt(-1_000);

        (rules.MatchGroup, rules.TeamRespawnWaveTimes, rules.RopesHolidayLightsAllowed).ShouldBe((-1, (-1f, -1f), true));
    }

    [Test]
    public void ServerTickAt_AfterANetTick_IsTheServersTickNotTheDemos() =>
        DemoTimeline.Build(SyntheticPlayer.DemoAtServerTick(tick: 66, serverTick: 13_557)).ServerTickAt(66).ShouldBe(13_557);

    [Test]
    public void RulesAt_WithAPlayerDestructionLogic_SaysSo() =>
        DemoTimeline.Build(SyntheticPlayer.DemoWithGameRules(mannVsMachine: false, halloweenScenario: 0, playerDestruction: true))
            .RulesAt(100)
            .ShouldBe(new SceneGameRules(MannVsMachine: false, HalloweenScenario: 0, PlayerDestruction: true)
            {
                // Player destruction's logic derives from robot destruction's, so it is `GetRobotDestructionLogic()`; its
                // respawn scales are unsent here.
                RobotDestructionRespawnScale = (0f, 0f),
                Present = true,
            });
}
