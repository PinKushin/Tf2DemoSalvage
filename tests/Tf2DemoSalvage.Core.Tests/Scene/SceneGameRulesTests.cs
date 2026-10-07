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
