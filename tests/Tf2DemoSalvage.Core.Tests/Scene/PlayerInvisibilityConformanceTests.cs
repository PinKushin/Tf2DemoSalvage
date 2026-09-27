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

    private static ScenePlayer Player(int conditions, float complete) =>
        new(1, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: Spy, Conditions: new PlayerConditions(conditions, 0, 0, 0, 0))
        {
            InvisChangeCompleteTime = complete,
        };
}
