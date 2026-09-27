using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>What `CHudItemEffectMeter` and its weapon meters (tf_hud_itemeffectmeter.cpp) read, off an authored demo.</summary>
public sealed class SceneItemEffectMeterFieldsTests
{
    [Test]
    public void PlayersAt_TheMeterFields_ReachTheScenePlayer()
    {
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem();

        player.RageMeter.ShouldBe(88.25f);
        player.RageDraining.ShouldBeTrue();
        player.HypeMeter.ShouldBe(41.5f);
        player.RevengeCrits.ShouldBe(6);
        player.RuneCharge.ShouldBe(62.5f);
        player.KartNextAvailableBoost.ShouldBe(203.5f);
        player.KartHealth.ShouldBe(137);
        player.SpawnCounter.ShouldBe(1);
        player.ItemChargeMeter.ShouldBe([0f, 12.75f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 99.5f]);
    }

    [Test]
    public void PlayersAt_TheWeaponMeterFields_ReachTheSceneItem()
    {
        SceneItem item = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem()
            .Items.ShouldNotBeNull().ShouldHaveSingleItem();

        item.EffectBarRegenTime.ShouldBe(311.25f);
        item.Energy.ShouldBe(15f);
        item.KillComboClass.ShouldBe(7);
        item.KillComboCount.ShouldBe(2);
        item.KnifeExists.ShouldBeTrue();
        item.KnifeRegenerateDuration.ShouldBe(15.5f);
        item.KnifeMeltTimestamp.ShouldBe(290.75f);
        item.MinicritCharge.ShouldBe(64.5f);
        item.RocketPackEnabled.ShouldBeTrue();
        item.ChargeBeginTime.ShouldBe(301.5f, "DT_ParticleCannon.m_flChargeBeginTime (tf_weapon_particle_cannon.cpp:37)");
        item.NumCharges.ShouldBe(3);
        item.PrimaryAmmoType.ShouldBe(4);
    }
}
