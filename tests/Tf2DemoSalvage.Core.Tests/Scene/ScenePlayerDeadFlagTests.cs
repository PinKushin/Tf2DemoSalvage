using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`pl.deadflag`, which `C_BasePlayer::IsPlayerDead` returns (c_baseplayer.cpp:765) and the head label reads (B549).</summary>
public sealed class ScenePlayerDeadFlagTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void PlayersAt_ADeadFlagSent_ReachesTheScenePlayer(bool deadFlag) =>
        DemoTimeline.Build(SyntheticPlayer.DemoWithDeadFlag(deadFlag)).PlayersAt(100).ShouldHaveSingleItem().DeadFlag.ShouldBe(deadFlag);
}
