using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The landing sound's volume, as prediction's <c>CheckFalling</c> picks it (B172).
/// </summary>
/// <remarks>
/// <c>CTFGameMovement::FullWalkMove</c> calls <c>CheckFalling</c> (<c>tf_gamemovement.cpp:2702</c>);
/// <c>CGameMovement::CheckFalling</c> (<c>gamemovement.cpp:3919-3985</c>) acts only on the ground with
/// <c>m_flFallVelocity</c> ≥ <c>PLAYER_FALL_PUNCH_THRESHOLD</c>, 350 outside HL2 (<c>shareddefs.h:419</c>): <c>fvol</c>
/// 0.5 in water, else 1.0 past <c>PLAYER_MAX_SAFE_FALL_SPEED</c> 580, 0.85 past half of it, and 0 below
/// <c>PLAYER_MIN_BOUNCE_SPEED</c> 200 (which 350 never is). <c>CTFGameMovement::PlayerRoughLandingEffects</c>
/// (<c>:2975-3005</c>) silences a grappling player and drops a scout's below 1 to 0; the base plays
/// <c>PlayStepSound</c> only for <c>fvol &gt; 0</c> (<c>gamemovement.cpp:3989</c>). <c>PlayerMove</c> sets
/// <c>m_flFallVelocity = -z</c> in the air before the move, so the fall speed here is the command's starting one.
/// </remarks>
public sealed class TfGameMovementLandingConformanceTests
{
    private const float Tick = 0.015f;

    [TestCase(400f, 0.85f)]
    [TestCase(580f, 0.85f)]
    [TestCase(600f, 1f)]
    public void ProcessMovement_LandingAtAFallSpeed_PlaysTheVolumeCheckFallingPicks(float fall, float volume)
    {
        Land(Falling(fall)).ShouldBe(volume);
    }

    [Test]
    public void ProcessMovement_LandingBelowThePunchThreshold_IsSilent()
    {
        Land(Falling(340f)).ShouldBeNull();
    }

    [Test]
    public void ProcessMovement_AScoutLandingSafely_IsSilent()
    {
        Land(Falling(400f) with { PlayerClass = 1 }).ShouldBeNull();
    }

    [Test]
    public void ProcessMovement_AScoutLandingHard_PlaysFullVolume()
    {
        Land(Falling(600f) with { PlayerClass = 1 }).ShouldBe(1f);
    }

    [Test]
    public void ProcessMovement_LandingWhileGrappling_IsSilent()
    {
        // TF_COND_GRAPPLINGHOOK, 98 (tf_shareddefs.h:788).
        Land(Falling(600f) with { Conditions = new PlayerConditions(0, 0, 0, 1 << (98 - 96), 0) }).ShouldBeNull();
    }

    [Test]
    public void ProcessMovement_LandingFeetInWater_IsHalfVolumeAtAnySpeed()
    {
        Land(Falling(600f) with { WaterLevel = 1 }).ShouldBe(0.5f);
    }

    [Test]
    public void ProcessMovement_StillInTheAir_PlaysNothing()
    {
        Land(Falling(600f) with { Origin = new Vector3(0f, 0f, 500f) }).ShouldBeNull();
    }

    private static PredictedPlayer Falling(float speed) => Standing() with
    {
        Origin = new Vector3(0f, 0f, 2f),
        OnGround = false,
        Velocity = new Vector3(0f, 0f, -speed),
    };

    private static float? Land(PredictedPlayer player)
    {
        TfGameMovement movement = new(Floor(), MovementConVars.Defaults, maxClients: 24);

        movement.ProcessMovement(ref player, Command(), Tick, first: true, commandNumber: 1).ShouldBeTrue();

        return movement.LandingVolume;
    }
}
