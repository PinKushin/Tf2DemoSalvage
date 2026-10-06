using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFPlayerShared::InvisibilityThink` (tf_player_shared.cpp:7977-8040), which `GetPercentInvisible` (:8047) returns.</summary>
public sealed class PlayerInvisibilityConformanceTests
{
    private const int Spy = 8;
    private const int Stealthed = 1 << 4;
    private const int StealthedBlink = 1 << 9;

    [Test]
    public void Percent_Decloaking_IsHalfTheTimeLeft()
    {
        // Not stealthed, 1 s left: ( 11 − 10 ) × 0.5 (:8007).
        PlayerInvisibility.Percent(Player(conditions: 0, complete: 11f), curTime: 10f, motionCloak: false).ShouldBe(0.5f);
    }

    [Test]
    public void Percent_Cloaking_IsOneLessTheTimeLeft()
    {
        // Stealthed, 0.25 s left: 1 − 0.25 (:8003).
        PlayerInvisibility.Percent(Player(Stealthed, complete: 10.25f), curTime: 10f, motionCloak: false).ShouldBe(0.75f);
    }

    [Test]
    public void Percent_CloakedAndBumped_IsTheBlinkScale()
    {
        // Fully stealthed × TF_SPY_STEALTH_BLINKSCALE (:215, :7995).
        PlayerInvisibility.Percent(Player(Stealthed | StealthedBlink, complete: 0f), curTime: 10f, motionCloak: false).ShouldBe(0.85f);
    }

    [Test]
    public void Percent_StealthedButNotASpy_IsZero()
    {
        PlayerInvisibility.Percent(Player(Stealthed, complete: 0f) with { PlayerClass = 3 }, curTime: 10f, motionCloak: false).ShouldBe(0f);
    }

    [Test]
    public void Percent_MotionCloakWithAnEmptyMeter_FadesBySpeed()
    {
        // RemapVal( 150², 0, 300², 1, 0.5 ) = 1 − 0.5 × 0.25 = 0.875 (:8023).
        ScenePlayer moving = Player(Stealthed, complete: 0f) with { MaxSpeed = 300f, CloakMeter = 0f, Velocity = (150f, 0f, 0f) };

        PlayerInvisibility.Percent(moving, curTime: 10f, motionCloak: true).ShouldBe(0.875f);
        PlayerInvisibility.Percent(moving with { CloakMeter = 5f }, curTime: 10f, motionCloak: true).ShouldBe(1f);
        PlayerInvisibility.Percent(moving, curTime: 10f, motionCloak: false).ShouldBe(1f);
    }

    [Test]
    public void Effective_NotAnEnemy_IsCappedAtTeammateMaxInvis()
    {
        // GetEffectiveInvisibilityLevel (c_tf_player.cpp:6844): !IsEnemyPlayer() → min( percent, tf_teammate_max_invis 0.95 ).
        PlayerInvisibility.Effective(1f, isEnemy: false, entityIndex: 4, recorderObserverMode: null, recorderObserverTarget: null)
            .ShouldBe(0.95f);
        PlayerInvisibility.Effective(0.5f, isEnemy: false, 4, null, null).ShouldBe(0.5f);
    }

    [Test]
    public void Effective_AnEnemy_IsThePercent()
    {
        PlayerInvisibility.Effective(1f, isEnemy: true, 4, recorderObserverMode: 4, recorderObserverTarget: 4).ShouldBe(1f);
    }

    [Test]
    public void Effective_TheEnemyWhoKilledTheRecorder_IsCappedInDeathcamAndFreezecam()
    {
        // OBS_MODE_DEATHCAM 1 / OBS_MODE_FREEZECAM 2 with GetObserverTarget() == this (:6867-6877).
        PlayerInvisibility.Effective(1f, isEnemy: true, 4, recorderObserverMode: 1, recorderObserverTarget: 4).ShouldBe(0.95f);
        PlayerInvisibility.Effective(1f, isEnemy: true, 4, recorderObserverMode: 2, recorderObserverTarget: 4).ShouldBe(0.95f);
        PlayerInvisibility.Effective(1f, isEnemy: true, 4, recorderObserverMode: 2, recorderObserverTarget: 5).ShouldBe(1f);
    }

    [Test]
    public void LocalWeapon_HalfCloaked_RemapsIntoTheViewmodelRange()
    {
        // CInvisProxy::OnBind for the local player (tf_viewmodel.cpp:575-594): RemapVal( p, 0, 1, 0.22, 0.5 ).
        PlayerInvisibility.LocalWeapon(0.5f, blink: false, motionCloakDry: false).ShouldBe(0.36f, 1e-6f);
        PlayerInvisibility.LocalWeapon(1f, blink: false, motionCloakDry: false).ShouldBe(0.5f);
    }

    [Test]
    public void LocalWeapon_BarelyCloaked_IsZero()
    {
        // ( flPercentInvisible < 0.01 ) ? 0.0
        PlayerInvisibility.LocalWeapon(0.009f, blink: false, motionCloakDry: false).ShouldBe(0f);
        PlayerInvisibility.LocalWeapon(0.01f, blink: false, motionCloakDry: false).ShouldBe(0.2228f, 1e-6f);
    }

    [Test]
    public void LocalWeapon_BumpedOrDry_IsPointThree()
    {
        PlayerInvisibility.LocalWeapon(1f, blink: true, motionCloakDry: false).ShouldBe(0.3f);
        PlayerInvisibility.LocalWeapon(0f, blink: false, motionCloakDry: true).ShouldBe(0.3f);
    }

    private static ScenePlayer Player(int conditions, float complete) =>
        new(1, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: Spy, Conditions: new PlayerConditions(conditions, 0, 0, 0, 0))
        {
            InvisChangeCompleteTime = complete,
        };
}
