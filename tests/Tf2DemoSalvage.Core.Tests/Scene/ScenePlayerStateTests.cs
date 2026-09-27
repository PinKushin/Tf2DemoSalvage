using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`m_Shared.m_nPlayerState` and `m_bIsMiniBoss`, which the target ID reads (tf_hud_target_id.cpp:431, :811).</summary>
public sealed class ScenePlayerStateTests
{
    [Test]
    public void PlayersAt_DyingMiniBoss_ReadsStateAndMiniBoss()
    {
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithPlayerState(playerState: 3, miniBoss: true)).PlayersAt(100).ShouldHaveSingleItem();

        (player.PlayerState, player.IsMiniBoss).ShouldBe(((int?)3, true));
    }
}
