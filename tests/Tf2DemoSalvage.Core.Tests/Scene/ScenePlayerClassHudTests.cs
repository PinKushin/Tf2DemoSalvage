using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>What `CTFHudPlayerClass::OnThink` (tf_hud_playerstatus.cpp:184) reads of the local player, off an authored demo.</summary>
public sealed class ScenePlayerClassHudTests
{
    [Test]
    public void PlayersAt_TheSpyAndWeaponFields_ReachTheScenePlayer()
    {
        // Distinct values everywhere, and an account ID above 2^31, so a signed read or a swapped field shows.
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithPlayerClassHud(
                invisChangeCompleteTime: 12.5f, cloakMeter: 37.25f, disguiseWeapon: 44, velocity: (100f, -50f, 25f), accountId: 0x80000123u, quality: 11))
            .PlayersAt(100).ShouldHaveSingleItem();

        player.InvisChangeCompleteTime.ShouldBe(12.5f);
        player.CloakMeter.ShouldBe(37.25f);
        player.DisguiseWeapon.ShouldBe(44);
        player.Velocity.ShouldBe((100f, -50f, 25f));
        player.WeaponAccountId.ShouldBe(0x80000123u);
        player.WeaponQuality.ShouldBe(11);
    }
}
