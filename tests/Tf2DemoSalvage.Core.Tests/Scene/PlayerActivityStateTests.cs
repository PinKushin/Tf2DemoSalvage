using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Choosing a player's body activity the way <c>CMultiPlayerAnimState</c> chooses it.
/// </summary>
/// <remarks>
/// **B100's first step.** Every player was playing one of two animations picked by speed alone —
/// <c>run_PRIMARY</c> when moving, <c>Stand_PRIMARY</c> when not — so crouching, jumping, swimming
/// and dying all looked like running or standing.
///
/// A demo never networks a player's sequence, so this is recomputed rather than decoded, and the
/// engine's own order is the specification:
///
/// <code>
/// if ( HandleJumping( idealActivity ) || HandleDucking( idealActivity ) ||
///      HandleSwimming( idealActivity ) || HandleDying( idealActivity ) )
/// { }
/// else { HandleMoving( idealActivity ); }
/// </code>
///
/// The tests below are that order, because the order is the part a reimplementation gets wrong: a
/// crouching player who is also moving must crouch-walk rather than run, and each case has to be
/// asked WITH the others true to prove the precedence rather than merely the mapping.
/// </remarks>
public sealed class PlayerActivityStateTests
{
    private const int OnGround = 1 << 0;

    private const int Ducking = 1 << 1;

    private const float Running = 300f;

    private const float Still = 0f;

    [Test]
    public void StandingStillOnTheGround_Idles()
    {
        PlayerActivityState.For(OnGround, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.StandIdle);
    }

    [Test]
    public void MovingOnTheGround_Runs()
    {
        // **There is no walk.** HandleMoving carries the comment "In TF we run all the time now"
        // and sets ACT_MP_RUN for any speed over the threshold, so a slow player runs slowly rather
        // than playing a different animation.
        PlayerActivityState.For(OnGround, Running, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Run);

        PlayerActivityState.For(OnGround, 1f, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Run);
    }

    [Test]
    public void TheMovingThreshold_IsHalfAUnitASecond()
    {
        // MOVING_MINIMUM_SPEED, and strictly greater — the engine's test is `>`, so exactly the
        // threshold is still standing. Interpolated positions jitter by tiny amounts and this is
        // what stops that reading as walking.
        PlayerActivityState.For(OnGround, 0.5f, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.StandIdle);

        PlayerActivityState.For(OnGround, 0.51f, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Run);
    }

    [Test]
    public void Crouched_IdlesOrWalks()
    {
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.CrouchIdle);

        PlayerActivityState.For(OnGround | Ducking, Running, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.CrouchWalk);
    }

    [Test]
    public void CrouchingBeatsRunning()
    {
        // **The precedence, not the mapping.** HandleDucking runs before HandleMoving and returns
        // true, so a moving crouched player never reaches the running case. An implementation that
        // checked speed first would pass every test above and lay a crouching scout out flat.
        PlayerActivityState.For(OnGround | Ducking, Running, waistDeep: false, alive: true)
            .ShouldNotBe(PlayerActivity.Run);
    }

    [Test]
    public void AirborneJumps_WhateverElseIsTrue()
    {
        // HandleJumping is asked first and returns true, so nothing below it can win. Tested with
        // crouch and movement both set, because that is the combination that would expose an
        // implementation ordering the checks by convenience.
        PlayerActivityState.For(0, Running, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Jump);

        PlayerActivityState.For(Ducking, Running, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Jump);

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Jump);
    }

    [Test]
    public void WaterStopsTheJump()
    {
        // HandleJumping clears the jump the moment the water reaches the waist, before it can
        // return true. So a player who leaps into water swims rather than falling with their legs
        // tucked, which is what a naive "not on the ground means jumping" would draw.
        PlayerActivityState.For(0, Still, waistDeep: true, alive: true)
            .ShouldBe(PlayerActivity.SwimIdle);

        PlayerActivityState.For(0, Running, waistDeep: true, alive: true)
            .ShouldBe(PlayerActivity.Swim);
    }

    [Test]
    public void CrouchingBeatsSwimming()
    {
        // HandleDucking is asked before HandleSwimming. Ordering these the other way is the kind of
        // thing that looks right in shallow water and wrong in deep.
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: true, alive: true)
            .ShouldBe(PlayerActivity.CrouchIdle);
    }

    [Test]
    public void TheDeadDie_WhateverTheyWereDoing()
    {
        // A corpse is not running, and its position keeps changing as the ragdoll settles — so the
        // speed test would otherwise have it sprinting along the floor.
        PlayerActivityState.For(OnGround, Running, waistDeep: false, alive: false)
            .ShouldBe(PlayerActivity.Die);

        PlayerActivityState.For(OnGround | Ducking, Running, waistDeep: false, alive: false)
            .ShouldBe(PlayerActivity.Die);

        PlayerActivityState.For(0, Running, waistDeep: false, alive: false)
            .ShouldBe(PlayerActivity.Die);
    }

    [Test]
    public void EveryActivityHasTheEnginesName()
    {
        // **The name is the lookup.** studio.h says mstudioseqdesc_t.activity is "initialized at
        // loadtime to game DLL values", so a model file stores the activity's NAME rather than its
        // number — matching on the name is how a sequence is found, and a typo here resolves to
        // nothing and freezes the model in its reference pose.
        PlayerActivityState.NameOf(PlayerActivity.StandIdle).ShouldBe("ACT_MP_STAND_IDLE");
        PlayerActivityState.NameOf(PlayerActivity.Run).ShouldBe("ACT_MP_RUN");
        PlayerActivityState.NameOf(PlayerActivity.CrouchIdle).ShouldBe("ACT_MP_CROUCH_IDLE");
        PlayerActivityState.NameOf(PlayerActivity.CrouchWalk).ShouldBe("ACT_MP_CROUCHWALK");
        PlayerActivityState.NameOf(PlayerActivity.Jump).ShouldBe("ACT_MP_JUMP");
        PlayerActivityState.NameOf(PlayerActivity.Die).ShouldBe("ACT_MP_DIE");
    }

    [Test]
    public void AnUnknownActivityThrows()
    {
        // Rather than defaulting, because a wrong name resolves to no sequence and a model frozen
        // in its reference pose reads as a model fault rather than a lookup one.
        Should.Throw<ArgumentOutOfRangeException>(
            () => PlayerActivityState.NameOf((PlayerActivity)999));
    }
}
