using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The networked fields <c>CTFGameMovement</c>'s other modes read, off an authored demo (B450): <c>m_vecViewOffset[2]</c>
/// (<c>c_baseplayer.cpp:229</c>), the legacy movement stun (<c>tf_player_shared.cpp:368-370</c>) and the taunt, kart and hook
/// fields (<c>c_tf_player.cpp:3793, 3800-3801, 3827</c>).
/// </summary>
public sealed class SceneMovementModeFieldsTests
{
    [Test]
    public void PlayersAt_TheMovementModeFields_ReachTheScenePlayer()
    {
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithMovementModeFields()).PlayersAt(100).ShouldHaveSingleItem();

        player.ViewOffsetZ.ShouldBe(68f);
        player.MovementStunTime.ShouldBe(2.5f);
        player.MovementStunAmount.ShouldBe(153);
        player.MovementStunParity.ShouldBe(3);
        player.AllowMoveDuringTaunt.ShouldBeTrue();
        player.CurrentTauntMoveSpeed.ShouldBe(212.5f);
        player.VehicleReverseTime.ShouldBe(104.25f);
        player.GrapplingHookTarget.ShouldBe(2);
    }
}
