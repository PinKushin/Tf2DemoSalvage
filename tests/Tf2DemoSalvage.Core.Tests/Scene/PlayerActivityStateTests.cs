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
    public void For_StandingStillOnTheGround_Idles()
    {
        PlayerActivityState.For(OnGround, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.StandIdle);
    }

    [Test]
    public void For_MovingOnTheGround_Runs()
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
    public void For_AtHalfAUnitASecond_StillStands()
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
    public void For_Crouched_IdlesOrWalksBySpeed()
    {
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.CrouchIdle);

        PlayerActivityState.For(OnGround | Ducking, Running, waistDeep: false, alive: true)
            .ShouldBe(PlayerActivity.CrouchWalk);
    }

    [Test]
    public void For_CrouchingWhileMoving_IsNotARun()
    {
        // **The precedence, not the mapping.** HandleDucking runs before HandleMoving and returns
        // true, so a moving crouched player never reaches the running case. An implementation that
        // checked speed first would pass every test above and lay a crouching scout out flat.
        PlayerActivityState.For(OnGround | Ducking, Running, waistDeep: false, alive: true)
            .ShouldNotBe(PlayerActivity.Run);
    }

    [Test]
    public void For_JumpingWhateverElseIsTrue_Jumps()
    {
        // HandleJumping is asked first and returns true while `m_bJumping`, so nothing below it can win. Tested with
        // crouch and movement both set, because that is the combination that would expose an implementation ordering
        // the checks by convenience.
        PlayerActivityState.For(0, Running, waistDeep: false, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.Jump);

        PlayerActivityState.For(Ducking, Running, waistDeep: false, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.Jump);
    }

    [Test]
    public void For_AirborneWithoutAJumpEvent_CrouchesOrRuns()
    {
        // **Off the ground is not a jump.** `m_bJumping` is set only by PLAYERANIMEVENT_JUMP
        // (`multiplayer_animstate.cpp:288`), so a rocket jump or a step off a ledge falls through HandleJumping to
        // HandleDucking — the tucked crouch — or to HandleMoving. Drawing the jump there put a crouched rocket-jumper's
        // pelvis 30-45 units from the server's hitbox (f12 ticks 14252, 16756).
        PlayerActivityState.For(Ducking, Running, waistDeep: false, alive: true, airborneSeconds: null)
            .ShouldBe(PlayerActivity.CrouchWalk);

        PlayerActivityState.For(Ducking, Still, waistDeep: false, alive: true, airborneSeconds: null)
            .ShouldBe(PlayerActivity.CrouchIdle);

        PlayerActivityState.For(0, Running, waistDeep: false, alive: true, airborneSeconds: null)
            .ShouldBe(PlayerActivity.Run);

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: null)
            .ShouldBe(PlayerActivity.StandIdle);
    }

    [Test]
    public void For_AJumpUpToHalfASecondOld_IsThePushOff()
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
    public void For_AirWalkingAtEitherJumpPhase_AirWalks()
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
    public void For_AnAirWalkWhileDucking_IsThePushOff()
    {
        // `( bValidAirWalkClass && ( vecVelocity.z > 300.0f || m_bInAirWalk ) && !bInDuck )` — a
        // crouched rocket jump tucks rather than running in the air, which is what a crouch-jump
        // looks like in the game.
        PlayerActivityState.For(
            Ducking, Running, waistDeep: false, alive: true, airborneSeconds: 0.1f, airwalking: true)
            .ShouldBe(PlayerActivity.JumpStart);
    }

    /// <remarks>
    /// **A latched player who ducks stands** (B437). The duck keeps the air-walk block from running, but
    /// `HandleJumping` still ends `if ( m_bJumping || m_bInAirWalk ) return true;` (`tf_playeranimstate.cpp:1534`),
    /// so `CalcMainActivity` stops with `idealActivity` at its `ACT_MP_STAND_IDLE` start — in the air, after a
    /// crouched landing, and in water alike. Not the crouch.
    /// </remarks>
    [Test]
    public void For_LatchedAndDuckingWithNoJump_StandsIdle()
    {
        PlayerActivityState.For(Ducking, Running, waistDeep: false, alive: true, airborneSeconds: null, airwalking: true)
            .ShouldBe(PlayerActivity.StandIdle);
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: false, alive: true, airborneSeconds: null, airwalking: true)
            .ShouldBe(PlayerActivity.StandIdle);
        PlayerActivityState.For(Ducking, Still, waistDeep: true, alive: true, airborneSeconds: null, airwalking: true)
            .ShouldBe(PlayerActivity.StandIdle);

        // The control: the same duck without the latch crouches.
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: false, alive: true, airborneSeconds: null, airwalking: false)
            .ShouldBe(PlayerActivity.CrouchIdle);
    }

    [Test]
    public void For_AnAirborneJumpWithoutTheAirWalk_IsThePushOff()
    {
        // The control for the two above: the air-walk must not swallow every airborne case. This
        // is the same input with the flag cleared, and it has to answer differently.
        PlayerActivityState
            .For(0, Running, waistDeep: false, alive: true, airborneSeconds: 0.1f, airwalking: false)
            .ShouldBe(PlayerActivity.JumpStart);
    }

    [Test]
    public void IdealName_TheAirWalk_IsItsOwnActivity()
    {
        PlayerActivityState.IdealName(PlayerActivity.Airwalk).ShouldBe("ACT_MP_AIRWALK");
    }

    [Test]
    public void IdealName_TheJumpPhases_AreTwoActivities()
    {
        // The land is deliberately not here: ACT_MP_JUMP_LAND is started with
        // RestartGesture( GESTURE_SLOT_JUMP, ... ), so it is a layered gesture over whatever the
        // body is doing rather than a body activity. Returning it as one would replace the run a
        // player lands into.
        PlayerActivityState.IdealName(PlayerActivity.JumpStart).ShouldBe("ACT_MP_JUMP_START");
        PlayerActivityState.IdealName(PlayerActivity.Jump).ShouldBe("ACT_MP_JUMP_FLOAT");
    }

    [Test]
    public void For_JumpingIntoWaistDeepWater_Swims()
    {
        // HandleJumping clears the jump the moment the water reaches the waist, before it can
        // return true. So a player who leaps into water swims rather than falling with their legs
        // tucked, which is what a naive "not on the ground means jumping" would draw.
        PlayerActivityState.For(0, Still, waistDeep: true, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.SwimIdle);

        PlayerActivityState.For(0, Running, waistDeep: true, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.Swim);
    }

    [Test]
    public void For_WaterBelowTheWaist_IsStillAJump()
    {
        // **WL_Waist is 2**, from Valve's own comment at player.cpp:1961 — 0 dry, 1 feet, 2 waist,
        // 3 eyes — and both HandleJumping and HandleSwimming test `>= WL_Waist`. Feet-deep water is
        // therefore NOT swimming, which is the boundary worth pinning: a player wading through a
        // shallow puddle keeps running.
        PlayerActivityState.WaistDeepWaterLevel.ShouldBe(2);

        PlayerActivityState.For(0, Still, waistDeep: false, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.Jump, "feet in water is not swimming; this is still a jump");

        PlayerActivityState.For(0, Still, waistDeep: true, alive: true, airborneSeconds: 1f)
            .ShouldBe(PlayerActivity.SwimIdle);
    }

    [Test]
    public void For_CrouchingInWaistDeepWater_CrouchIdles()
    {
        // HandleDucking is asked before HandleSwimming. Ordering these the other way is the kind of
        // thing that looks right in shallow water and wrong in deep.
        PlayerActivityState.For(OnGround | Ducking, Still, waistDeep: true, alive: true)
            .ShouldBe(PlayerActivity.CrouchIdle);
    }

    [Test]
    public void For_TheDeadWhateverTheyWereDoing_Die()
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
    public void IdealName_EveryActivity_IsCalcMainActivitysOwnName()
    {
        // **The engine's own answer, before any weapon has touched it** — `idealActivity` in
        // `CMultiPlayerAnimState::ComputeMainSequence` (`multiplayer_animstate.cpp:1168`), which
        // `TranslateActivity` then hands to the held weapon's table. No model ships these names:
        // the table turns ACT_MP_STAND_IDLE into ACT_MP_STAND_PRIMARY, ACT_MP_CROUCH_IDLE into
        // ACT_MP_CROUCH_PRIMARY, and ACT_MP_RUN into whatever the weapon's role runs with (B105) —
        // which is why the name is the table's key and not a string to paste a suffix onto.
        //
        // Both swims are ACT_MP_SWIM: HandleSwimming sets the one activity whether or not the
        // player is moving, and the difference is the move_x pose parameter.
        PlayerActivityState.IdealName(PlayerActivity.StandIdle).ShouldBe("ACT_MP_STAND_IDLE");
        PlayerActivityState.IdealName(PlayerActivity.Run).ShouldBe("ACT_MP_RUN");
        PlayerActivityState.IdealName(PlayerActivity.CrouchIdle).ShouldBe("ACT_MP_CROUCH_IDLE");
        PlayerActivityState.IdealName(PlayerActivity.CrouchWalk).ShouldBe("ACT_MP_CROUCHWALK");
        PlayerActivityState.IdealName(PlayerActivity.SwimIdle).ShouldBe("ACT_MP_SWIM");
        PlayerActivityState.IdealName(PlayerActivity.Swim).ShouldBe("ACT_MP_SWIM");
        PlayerActivityState.IdealName(PlayerActivity.Die).ShouldBe("ACT_DIESIMPLE");
    }

    [Test]
    public void IdealName_AnUnknownActivity_Throws()
    {
        // Rather than defaulting, because a wrong name resolves to no sequence and a model frozen
        // in its reference pose reads as a model fault rather than a lookup one.
        Should.Throw<ArgumentOutOfRangeException>(
            () => PlayerActivityState.IdealName((PlayerActivity)999));
    }
}
