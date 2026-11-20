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
    public void PlayersAt_TheRecordersDominatingMeFlags_ReachTheScenePlayer()
    {
        // `m_bPlayerDominatingMe` (tf_player_shared.cpp:523, `DT_TFPlayerSharedLocal`): who dominates the recorder. On the
        // client a killer's `IsPlayerDominated( local )` is this array's entry for him (:7737-7751), which the freeze
        // panel's nemesis line reads (B748).
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem();

        player.DominatingMe.ShouldBe((System.UInt128.One << 3) | (System.UInt128.One << 64));
        player.IsDominatedBy(3).ShouldBeTrue();
        player.IsDominatedBy(4).ShouldBeFalse("the control: a slot whose flag is clear");
    }

    [Test]
    public void PlayersAt_ADesiredClassSent_ReachesTheScenePlayer()
    {
        // `DT_TFPlayerShared.m_iDesiredPlayerClass` (tf_player_shared.cpp:367, sent to everyone): what
        // `GetDesiredPlayerClassIndex()` answers and the spectator bar's tip is chosen by (tf_spectatorgui.cpp:466, B733).
        DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem()
            .DesiredPlayerClass.ShouldBe(4);
    }

    [Test]
    public void PlayersAt_AGravitySent_ReachesTheScenePlayer()
    {
        // `CBasePlayer::GetGravity` is `m_flGravity`, which StartGravity and FinishGravity scale gravity by when nonzero
        // (gamemovement.cpp:1250, :1689) — D205's prediction reads it off the recorder.
        DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem().Gravity.ShouldBe(0.5f);
    }

    [Test]
    public void PlayersAt_TheWeaponMeterFields_ReachTheSceneItem()
    {
        SceneItem item = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem()
            .Items.ShouldNotBeNull().ShouldHaveSingleItem();

        item.EffectBarRegenTime.ShouldBe(311.25f);
        item.LastFireTime.ShouldBe(297.5f, "DT_LocalTFWeaponData.m_flLastFireTime (tf_weaponbase.cpp:179)");
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
        item.FlameThrowerState.ShouldBe(2, "DT_WeaponFlameThrower.m_iWeaponState, FT_STATE_FIRING (tf_weapon_flamethrower.cpp:206)");
        item.ChargeLevel.ShouldBe(0.625f, "DT_LocalTFWeaponMedigunData.m_flChargeLevel (tf_weapon_medigun.cpp:128), what CHudMedicChargeMeter reads");
        item.ChargeRelease.ShouldBeTrue("DT_WeaponMedigun.m_bChargeRelease (:152)");
        item.ChargeResistType.ShouldBe(2, "DT_WeaponMedigun.m_nChargeResistType (:154)");
    }

    [Test]
    public void PlayersAt_TheItemsLevelAndAccount_ReachTheSceneItem()
    {
        // `m_iEntityLevel` and `m_iAccountID` (econ_item_view.cpp:183, :187): the item panel's "Level 37" line and whose
        // backpack the item came from (B751).
        SceneItem item = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem()
            .Items.ShouldNotBeNull().ShouldHaveSingleItem();

        item.Level.ShouldBe(37);
        item.AccountId.ShouldBe(123456789u);
    }
}
