using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What prediction restores for B450's last movement inputs: the weapon's fire state, the hype meter, the Baby Face's
/// Blaster, and the active weapon's last deploy time — each as the client holds it.
/// </summary>
public sealed class RecorderPredictionWeaponStateTests
{
    private const float Interval = 0.015f;

    [Test]
    public void Restore_AFlameThrowerInFtStateFiring_IsFiring()
    {
        // IsFiring() is m_iWeaponState == FT_STATE_FIRING (tf_weapon_flamethrower.h:36, :92), networked and predicted
        // (tf_weapon_flamethrower.cpp:200, :216).
        PredictedPlayer player = RecorderPrediction.Restore(Holding(Flamethrower(state: 2)), Interval, packetTick: 100);

        player.ActiveWeaponFiring.ShouldBeTrue();
    }

    [Test]
    public void Restore_AFlameThrowerStartingToFire_IsNotFiring()
    {
        // FT_STATE_STARTFIRING (1) is not FT_STATE_FIRING (2).
        RecorderPrediction.Restore(Holding(Flamethrower(state: 1)), Interval, packetTick: 100).ActiveWeaponFiring.ShouldBeFalse();
    }

    [Test]
    public void Restore_AFlameThrowerNotActive_IsNotFiring()
    {
        // GetActiveTFWeapon()->IsFiring(): an owned but holstered flamethrower is not asked.
        ScenePlayer recorder = Holding(Flamethrower(state: 2)) with { ActiveWeapon = 31 };

        RecorderPrediction.Restore(recorder, Interval, packetTick: 100).ActiveWeaponFiring.ShouldBeFalse();
    }

    [Test]
    public void Restore_TheHypeMeterAndAnOwnedBabyFace_AreCarried()
    {
        // m_flHypeMeter, and Weapon_OwnsThisID( TF_WEAPON_PEP_BRAWLER_BLASTER ) (tf_player_shared.cpp:11082) — owned,
        // not necessarily active.
        SceneItem babyFace = new(31, "CTFPEPBrawlerBlaster", 772, new EconAttributeWire([], [], false), IsWeapon: true);
        ScenePlayer recorder = new(1, 0f, 0f, 0f, 2, 125, 1, ActiveWeapon: 30) { HypeMeter = 41.5f, Items = [Flamethrower(0), babyFace] };

        PredictedPlayer player = RecorderPrediction.Restore(recorder, Interval, packetTick: 100);

        player.HypeMeter.ShouldBe(41.5f);
        player.OwnsPepBrawlerBlaster.ShouldBeTrue();
        RecorderPrediction.Restore(recorder with { Items = [Flamethrower(0)] }, Interval, 100).OwnsPepBrawlerBlaster.ShouldBeFalse();
    }

    [Test]
    public void LastDeployTime_ASwitchToTheActiveWeaponInTheWindow_IsThatCommandsCurtime()
    {
        // CPrediction::RunCommand selects the weapon before the movement (prediction.cpp:903-910), and Deploy sets
        // m_flLastDeployTime = gpGlobals->curtime (tf_weaponbase.cpp:1319), which no table sends or predicts (:169-248). Command 11 runs at 1.5, so command 8 ran at
        // 1.5 − 3 · 0.015.
        float? deployed = RecorderPrediction.LastDeployTime(
            Commands(switchAt: 8, to: 30), acknowledged: 10, activeWeapon: 30, activeBefore: _ => 31, curTime: 1.5f, Interval);

        deployed.ShouldNotBeNull().ShouldBe(1.455f, 1e-4f);
    }

    [Test]
    public void LastDeployTime_ASelectOfTheWeaponAlreadyHeld_IsNoDeploy()
    {
        // SelectItem returns when the item is already active, so nothing deploys.
        RecorderPrediction.LastDeployTime(
            Commands(switchAt: 8, to: 30), acknowledged: 10, activeWeapon: 30, activeBefore: _ => 30, curTime: 1.5f, Interval)
            .ShouldBeNull();
    }

    [Test]
    public void LastDeployTime_ASwitchOlderThanTheAtomizersWindow_IsNotLookedFor()
    {
        // 0.7 s is 47 ticks: command 8 ran 53 commands before command 61, outside anything CanAirDash asks.
        RecorderPrediction.LastDeployTime(
            Commands(switchAt: 8, to: 30, count: 70), acknowledged: 60, activeWeapon: 30, activeBefore: _ => 31, curTime: 1.5f, Interval)
            .ShouldBeNull();
    }

    private static SceneItem Flamethrower(int state) =>
        new(30, "CTFFlameThrower", 21, new EconAttributeWire([], [], false), IsWeapon: true) { FlameThrowerState = state };

    private static ScenePlayer Holding(SceneItem weapon) => new(1, 0f, 0f, 0f, 2, 175, 7, ActiveWeapon: 30) { Items = [weapon] };

    private static List<RecordedUserCommand> Commands(int switchAt, int to, int count = 12) =>
        [.. Enumerable.Range(1, count).Select(sequence => new RecordedUserCommand(
            sequence, sequence, new UserCommand(sequence, sequence, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0, sequence == switchAt ? to : 0, 0, 0, 0, 0)))];
}
