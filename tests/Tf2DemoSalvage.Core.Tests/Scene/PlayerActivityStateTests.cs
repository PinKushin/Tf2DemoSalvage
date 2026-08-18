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
    public void AJumpStartsBeforeItFloats()
    {
        // **Half a second, strictly** — `gpGlobals->curtime - m_flJumpStartTime > 0.5` in
        // CTFPlayerAnimState::HandleJumping, so exactly the threshold is still the push-off. Both
        // sides are asserted because a comparison with the wrong direction passes either one alone.
        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: 0f)
            .ShouldBe(PlayerActivity.JumpStart);

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: 0.5f)
            .ShouldBe(PlayerActivity.JumpStart, "the engine's test is strictly greater than");

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: 0.51f)
            .ShouldBe(PlayerActivity.Jump);
    }

    [Test]
    public void AirWalkingBeatsBothJumpPhases()
    {
        // **HandleJumping checks the air-walk BEFORE the jump and it supersedes it**, so a
        // fast-rising player runs in the air rather than tucking — whatever the jump clock says.
        // Asserted at both phases, because a check placed after the split would pass at one.
        PlayerActivityState
            .For(0, Running, waistDeep: false, alive: true, airborneSeconds: 0.1f, airwalking: true)
            .ShouldBe(PlayerActivity.Airwalk);

        PlayerActivityState
            .For(0, Running, waistDeep: false, alive: true, airborneSeconds: 2f, airwalking: true)
            .ShouldBe(PlayerActivity.Airwalk);
    }

    [Test]
    public void DuckingCancelsTheAirWalk()
    {
        // `( bValidAirWalkClass && ( vecVelocity.z > 300.0f || m_bInAirWalk ) && !bInDuck )` — a
        // crouched rocket jump tucks rather than running in the air, which is what a crouch-jump
        // looks like in the game.
        PlayerActivityState.For(
            Ducking, Running, waistDeep: false, alive: true, airborneSeconds: 0.1f, airwalking: true)
            .ShouldBe(PlayerActivity.JumpStart);
    }

    [Test]
    public void WithoutTheAirWalkTheJumpPhasesStillApply()
    {
        // The control for the two above: the air-walk must not swallow every airborne case. This
        // is the same input with the flag cleared, and it has to answer differently.
        PlayerActivityState
            .For(0, Running, waistDeep: false, alive: true, airborneSeconds: 0.1f, airwalking: false)
            .ShouldBe(PlayerActivity.JumpStart);
    }

    [Test]
    public void TheAirWalkHasItsOwnName()
    {
        PlayerActivityState.NameOf(PlayerActivity.Airwalk).ShouldBe("ACT_MP_AIRWALK_PRIMARY");
    }

    [Test]
    public void AnUnknownAirborneTimeFloats()
    {
        // Null is "cannot tell", not "just left the ground". The float is what a jump spends most
        // of its time in and is what this drew before the phases existed, so an absent clock keeps
        // the previous behaviour rather than making every airborne player launch repeatedly.
        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: null)
            .ShouldBe(PlayerActivity.Jump);
    }

    [Test]
    public void TheJumpPhasesHaveTheirOwnNames()
    {
        // The land is deliberately not here: ACT_MP_JUMP_LAND is started with
        // RestartGesture( GESTURE_SLOT_JUMP, ... ), so it is a layered gesture over whatever the
        // body is doing rather than a body activity. Returning it as one would replace the run a
        // player lands into.
        PlayerActivityState.NameOf(PlayerActivity.JumpStart).ShouldBe("ACT_MP_JUMP_START_PRIMARY");
        PlayerActivityState.NameOf(PlayerActivity.Jump).ShouldBe("ACT_MP_JUMP_FLOAT_PRIMARY");

        PlayerActivityState.NameOf(PlayerActivity.JumpStart, "MELEE")
            .ShouldBe("ACT_MP_JUMP_START_MELEE");
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
    public void TheWaistIsWhereSwimmingStarts()
    {
        // **WL_Waist is 2**, from Valve's own comment at player.cpp:1961 — 0 dry, 1 feet, 2 waist,
        // 3 eyes — and both HandleJumping and HandleSwimming test `>= WL_Waist`. Feet-deep water is
        // therefore NOT swimming, which is the boundary worth pinning: a player wading through a
        // shallow puddle keeps running.
        PlayerActivityState.WaistDeepWaterLevel.ShouldBe(2);

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.Jump, "feet in water is not swimming; this is still a jump");

        PlayerActivityState.For(0, Still, waistDeep: true, alive: true)
            .ShouldBe(PlayerActivity.SwimIdle);
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
        //
        // **This branch is unreachable in TF2, and is kept because HandleDying is real code.**
        // `m_bDying` can only be set by PLAYERANIMEVENT_DIE, which is raised nowhere in the game
        // tree — its handler is `Assert( 0 ); // Should be here - not supporting this yet!`. TF2
        // hides the dead player with EF_NODRAW and spawns a CTFRagdoll instead, so no player model
        // ever plays a death animation and no viewer path can select this. Asserting it anyway
        // keeps the reimplementation of CalcMainActivity complete and honest about what the engine
        // contains; B102 records why nothing reaches it.
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
        // **Weapon-suffixed, because that is what a model actually ships.** The bare ACT_MP_RUN that
        // CalcMainActivity returns appears in no model at all; TranslateActivity adds the slot. That
        // is measured against the scout in SequenceActivityTests rather than assumed here.
        //
        // The naming is also irregular, so every one of these is taken from the model rather than
        // composed from the enum: standing is STAND and not STAND_IDLE, crouching idle is CROUCH
        // with no IDLE, and a jump has no single name — start, float and land are three activities.
        PlayerActivityState.NameOf(PlayerActivity.StandIdle).ShouldBe("ACT_MP_STAND_PRIMARY");
        PlayerActivityState.NameOf(PlayerActivity.Run).ShouldBe("ACT_MP_RUN_PRIMARY");
        PlayerActivityState.NameOf(PlayerActivity.CrouchIdle).ShouldBe("ACT_MP_CROUCH_PRIMARY");
        PlayerActivityState.NameOf(PlayerActivity.CrouchWalk).ShouldBe("ACT_MP_CROUCHWALK_PRIMARY");
        PlayerActivityState.NameOf(PlayerActivity.Jump).ShouldBe("ACT_MP_JUMP_FLOAT_PRIMARY");

        // Another slot, to show the suffix is a parameter rather than baked in.
        PlayerActivityState.NameOf(PlayerActivity.Run, "MELEE").ShouldBe("ACT_MP_RUN_MELEE");
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
