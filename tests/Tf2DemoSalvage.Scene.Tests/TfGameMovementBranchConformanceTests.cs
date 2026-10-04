using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CTFGameMovement</c>'s remaining branches — ground categorisation, stuck checks, water, water jumps, the kart, stuns,
/// the parachute, the grappling hook and the air-speed cap — each value computed by hand from
/// <c>src/game/shared/tf/tf_gamemovement.cpp</c> and <c>src/game/shared/gamemovement.cpp</c> at the default ConVars.
/// </summary>
public sealed class TfGameMovementBranchConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InAttack = 1 << 0;
    private const uint InJump = 1 << 1;
    private const uint InAttack2 = 1 << 11;
    private const int ContentsSlime = 0x10;
    private const int ContentsWater = 0x20;

    // ---- CategorizePosition --------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_LosingFootingWhileRisingOffTheGround_IsInTheAir()
    {
        // CategorizePosition (tf_gamemovement.cpp:2388-2394): with TF_COND_LOST_FOOTING any velocity away from the ground's
        // plane makes him airborne, so z = 10 falls by a full tick of gravity rather than being zeroed on the ground.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(0f, 0f, 10f), Conditions = Cond(126) };

        Run(ref player, Move());

        player.OnGround.ShouldBeFalse();
        player.Velocity.Z.ShouldBe(-2f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_LosingFootingAtRest_RegainsIt()
    {
        // :2403-2407: under tf_movement_lost_footing_restick 50 and not moving away, RemoveCond( TF_COND_LOST_FOOTING ).
        PredictedPlayer player = Standing() with { Conditions = Cond(126) };

        Run(ref player, Move());

        player.Conditions.Has(126).ShouldBeFalse();
        player.OnGround.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_OnTheGroundFiveUnitsAboveTheFloor_IsSnappedDownToIt()
    {
        // :2425-2440, StayOnGround subsumed: on the ground, the trace reaches down a step and the origin takes its end, a
        // DIST_EPSILON above the floor.
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, 5f) };

        Run(ref player, Move());

        player.Origin.Z.ShouldBe(0.03125f, 1e-4f);
        player.OnGround.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_RisingOffASteepSlope_IsInTheAirWithAQuarterOfTheFriction()
    {
        // :2412-2423: a plane whose normal z is 0.6, under 0.7, is too steep even for the four sub-boxes; rising, his
        // m_surfaceFriction is 0.25.
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(0f, 0f, 32.1f),
            OnGround = false,
            Velocity = new Vector3(0f, 0f, 20f),
        };

        Run(ref player, Move(), trace: Through(HalfSpace(0.8f, 0f, 0.6f, 0f)));

        player.OnGround.ShouldBeFalse();
        player.SurfaceFriction.ShouldBe(0.25f);
    }

    // ---- PlayerSolidMask, CheckInterval, CheckStuck ----------------------------------------------------------------

    [Test]
    public void ProcessMovement_ABluPlayer_TracesAgainstTheRedTeamsContents()
    {
        // PlayerSolidMask (tf_gamemovement.cpp:269-283): BLU collides with CONTENTS_REDTEAM (0x800), not BLUETEAM (0x1000).
        List<int> masks = [];
        PlayerTraceRay floor = Floor();
        PredictedPlayer player = Standing() with { Team = 3, Velocity = new Vector3(100f, 0f, 0f) };

        Run(ref player, Move(), trace: (start, end, mins, maxs, mask) =>
        {
            masks.Add(mask);
            return floor(start, end, mins, maxs, mask);
        });

        masks.ShouldNotBeEmpty();
        masks.ShouldAllBe(mask => (mask & 0x800) != 0 && (mask & 0x1000) == 0);
    }

    [Test]
    public void ProcessMovement_StuckWithOneClientOn13thCommand_IsCheckedEveryFifthOfASecond()
    {
        // CheckInterval (gamemovement.cpp:689-701): with maxClients 1 CHECK_STUCK_INTERVAL_SP is 0.2 s, (int)(0.2 / 0.015)
        // = 13 commands, so command 13 checks and the table's third offset frees him: −0.1 + 0.125.
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, -0.1f) };

        Run(ref player, Move(), commandNumber: 13, maxClients: 1);

        player.Origin.Z.ShouldBe(0.025f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_StillBeingUnstuck_IsCheckedOnEveryCommand()
    {
        // CheckInterval (:693-697): m_StuckLast ≠ 0 makes the interval 1, so command 67 checks too.
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, -0.1f), StuckLast = 1 };

        Run(ref player, Move(), commandNumber: 67);

        player.Origin.Z.ShouldBe(0.025f, 1e-4f);
        player.StuckLast.ShouldBe(0);
    }

    [Test]
    public void ProcessMovement_StuckInABuildingTwiceInOneFrame_TriesOneOffsetOnly()
    {
        // CheckStuck (gamemovement.cpp:3454-3462): m_flStuckCheckTime gates a second try within CHECKSTUCK_MINTIME, so the
        // second command skips the move without walking the table: m_StuckLast stays at the one offset tried.
        TfGameMovement movement = new(EnemyAround(Floor(), entity: 40), MovementConVars.Defaults, maxClients: 24);
        PredictedPlayer player = Standing() with { Team = 2, EntityIndex = 1 };

        movement.ProcessMovement(ref player, Move(), Tick, first: true, commandNumber: 65).ShouldBeTrue();
        player.StuckLast.ShouldBe(1);

        movement.ProcessMovement(ref player, Move(), Tick, first: false, commandNumber: 66).ShouldBeTrue();
        player.StuckLast.ShouldBe(1);
    }

    [TestCase(1)]
    [TestCase(24)]
    public void ProcessMovement_StartingInsidePlayerNumberAtTheEdgesOfTheRange_PassesThroughHim(int entity)
    {
        // CBaseEntity::IsPlayer: entity indices 1 to maxClients are players, both ends included (tf_gamemovement.cpp:1401).
        PredictedPlayer player = Standing() with { Team = 2, EntityIndex = 1 };

        Run(ref player, Move(), trace: EnemyAround(Floor(), entity), commandNumber: 65);

        player.PassingThroughEnemies.ShouldBeTrue();
    }

    // ---- Water ---------------------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_UnderwaterStrafing_DoesNotSink()
    {
        // WaterMove (tf_gamemovement.cpp:1578-1581): a sidemove is input, so there is no −60 sink; wish 100 · 0.8 = 80 and
        // sv_accelerate adds 10 · 80 · 0.015 = 12 along right, (0, −1, 0).
        PredictedPlayer player = Swimming();

        Run(ref player, new UserCommand(1, 1, 0f, 0f, 0f, 0f, 100f, 0f, 0, 0, 0, 0, 0, 0, 0), contents: Water(1000f));

        player.Velocity.Y.ShouldBe(-12f, 1e-3f);
        player.Velocity.Z.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_UnderwaterWithAnUpMove_SwimsUp()
    {
        // :1583-1586: z += upmove; 100 · 0.8 · 10 · 0.015 = 12 up.
        PredictedPlayer player = Swimming();

        Run(ref player, new UserCommand(1, 1, 0f, 0f, 0f, 0f, 0f, 100f, 0, 0, 0, 0, 0, 0, 0), contents: Water(1000f));

        player.Velocity.Z.ShouldBe(12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_UnderwaterGlidingFasterThanTheSink_OnlyLosesWaterFriction()
    {
        // :1606-1620: water friction takes 100 · 4 · 0.015 = 6; the sink's wish 48 is under the 94 left, so no acceleration.
        PredictedPlayer player = Swimming() with { Velocity = new Vector3(100f, 0f, 0f) };

        Run(ref player, Move(), contents: Water(1000f));

        player.Velocity.X.ShouldBe(94f, 1e-3f);
        player.Velocity.Z.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AGhostDriftingFast_IsCappedAtTheGhostXySpeed()
    {
        // :1624-1638: (400, 0, −6) after StartGravity loses 6 % to friction, (376, 0, −5.64); the sink adds 10 · 48 · 0.015 =
        // 7.2 down, and the 376 across is cut to tf_ghost_xy_speed 300.
        PredictedPlayer player = Swimming() with { Velocity = new Vector3(400f, 0f, 0f), Conditions = Cond(77) };

        Run(ref player, Move(), contents: Water(-1000f));

        player.Velocity.X.ShouldBe(300f, 1e-2f);
        player.Velocity.Z.ShouldBe(-12.84f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_JumpingInSlime_RisesAtEightyThenSwims()
    {
        // CheckWaterJumpButton (tf_gamemovement.cpp:938-980): eyes deep in CONTENTS_SLIME, z = 80; then WaterMove's jump at
        // WL_Eyes wishes 300 up, · 0.8 = 240, friction leaves 75.2, and 10 · 240 · 0.015 = 36 more: 111.2.
        PredictedPlayer player = Swimming();

        Run(ref player, Move(InJump), contents: point => point.Z < 1000f ? ContentsSlime : 0);

        player.Velocity.Z.ShouldBe(111.2f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WaistDeepFacingALedge_WaterJumpsOutOfTheWater()
    {
        // CheckWaterJump (tf_gamemovement.cpp:2464-2530): the box from his centre meets the ledge's face within 30, the box
        // from eye height + 8 clears it, and the drop from there finds standable ground: z = 300, m_flWaterJumpTime 2000,
        // and the jump velocity is the face's normal · −50. WaterMove's friction then takes 300 · 4 · 0.015 = 18.
        PredictedPlayer player = WaistDeep();

        Run(ref player, Move(forward: 450f), contents: Water(80f), trace: Ledge());

        player.WaterJumpTime.ShouldBe(2000f);
        player.WaterJumpVelocity.ShouldBe(new Vector3(50f, 0f, 0f));
        player.Velocity.Z.ShouldBe(282f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WaistDeepFacingALedgeHoldingJump_CountsTheWaterJumpDownBySeconds()
    {
        // CheckWaterJumpButton (:944-948): with the water jump just started, jump counts m_flWaterJumpTime down by
        // frametime — seconds, though WaterJump counts it in milliseconds.
        PredictedPlayer player = WaistDeep();

        Run(ref player, Move(forward: 450f, buttons: InJump), contents: Water(80f), trace: Ledge());

        player.WaterJumpTime.ShouldBe(2000f - Tick, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WaterJumpingOutOfTheWater_EndsTheJumpAndKeepsItsVelocity()
    {
        // WaterJump (gamemovement.cpp:1348-1372): out of the water the timer ends at once, and the velocity's x and y are
        // m_vecWaterJumpVel's; StartGravity takes 6 and nothing more is applied.
        PredictedPlayer player = InTheAir() with
        {
            Velocity = new Vector3(0f, 0f, 100f),
            WaterJumpTime = 1000f,
            WaterJumpVelocity = new Vector3(50f, 0f, 0f),
        };

        Run(ref player, Move(), contents: Water(-1000f));

        player.WaterJumpTime.ShouldBe(0f);
        player.Velocity.ShouldBe(new Vector3(50f, 0f, 94f));
        player.Origin.X.ShouldBe(0.75f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_WaterJumpingWithFeetStillWet_CountsTheTimerDownInMilliseconds()
    {
        // WaterJump (:1361-1366): 1000 − 1000 · 0.015 = 985, kept while the feet are in water.
        PredictedPlayer player = InTheAir() with { WaterJumpTime = 1000f, WaterJumpVelocity = new Vector3(50f, 0f, 0f) };

        Run(ref player, Move(), contents: Water(505f));

        player.WaterJumpTime.ShouldBe(985f, 1e-3f);
    }

    // ---- The kart ------------------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_AKartBrakingAtFullBack_SlowsByTheBrakeAcceleration()
    {
        // VehicleMove (tf_gamemovement.cpp:775-786): back at full while moving, the target is the brake speed 0 at
        // tf_halloween_kart_brake_accel 500: 300 − 500 · 0.015 = 292.5.
        PredictedPlayer player = Kart(300f);

        Run(ref player, Move(forward: -450f));

        player.CurrentTauntMoveSpeed.ShouldBe(292.5f, 1e-3f);
        player.VehicleReverseTime.ShouldBe(float.MaxValue);
    }

    [Test]
    public void ProcessMovement_AKartBrakingAtHalfBack_SlowsByHalfTheBrakeAcceleration()
    {
        // :771-772: the back input is normalised by cl_backspeed, 225 / 450, so 500 · 0.5 · 0.015 = 3.75.
        PredictedPlayer player = Kart(300f);

        Run(ref player, Move(forward: -225f));

        player.CurrentTauntMoveSpeed.ShouldBe(296.25f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartAtRestHoldingBackAfterGoingForward_Reverses()
    {
        // :787-795: at rest with last command's forwardmove ≥ 0 the target is the reverse speed −50, and crossing zero uses
        // the brake acceleration: −7.5.
        PredictedPlayer player = Kart(0f);

        Run(ref player, Move(forward: -450f));

        player.CurrentTauntMoveSpeed.ShouldBe(-7.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartAtRestStillHoldingBack_StallsBeforeReversing()
    {
        // :796-800: already holding back at rest with no reverse time set, it stalls: m_flVehicleReverseTime = now + 0.6.
        PredictedPlayer player = Kart(0f) with { OldForwardMove = -450f, CurTime = 2f };

        Run(ref player, Move(forward: -450f));

        player.VehicleReverseTime.ShouldBe(2.6f, 1e-4f);
        player.CurrentTauntMoveSpeed.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_AKartWithABombHead_AcceleratesHalfAgainFaster()
    {
        // :808-812: TF_COND_HALLOWEEN_BOMB_HEAD scales the acceleration by 1.5: 500 · 1.5 · 0.015 = 11.25.
        PredictedPlayer player = Kart(0f) with { Conditions = Both(Cond(82), Cond(53)) };

        Run(ref player, Move(forward: 450f));

        player.CurrentTauntMoveSpeed.ShouldBe(11.25f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartDashing_DrivesAtTheDashSpeed()
    {
        // :817-822: TF_COND_HALLOWEEN_KART_DASH sets every speed to tf_halloween_kart_dash_speed 1000.
        PredictedPlayer player = Kart(0f) with { Conditions = Both(Cond(82), Cond(83)) };

        Run(ref player, Move());

        player.CurrentTauntMoveSpeed.ShouldBe(1000f);
    }

    [Test]
    public void ProcessMovement_AKartInTheAir_HasTheKartsAirControl()
    {
        // GetAirSpeedCap (tf_gamemovement.cpp:2061-2073): 30 · tf_halloween_kart_air_control 1.2 = 36.
        PredictedPlayer player = Kart(300f) with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(36f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartJumpingFast_IsNotSlowedByTheBunnyHopCap()
    {
        // PreventBunnyJumping (tf_gamemovement.cpp:1094-1098) returns for a kart, so 500 stays 500.
        PredictedPlayer player = Kart(0f) with { Velocity = new Vector3(500f, 0f, 0f) };

        Run(ref player, Move(InJump));

        player.Velocity.X.ShouldBe(500f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingFastWithNoMaxSpeed_IsNotSlowedByTheBunnyHopCap()
    {
        // :1100-1102: a max scaled speed of 0 skips the cap rather than stopping him.
        PredictedPlayer player = Standing() with { MaxSpeed = 0f, Velocity = new Vector3(500f, 0f, 0f) };

        Run(ref player, Move(InJump));

        player.Velocity.X.ShouldBe(500f, 1e-3f);
    }

    // ---- The air-speed cap and air dash --------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_InTheAirOnARocketPack_HasHalfTheAirControl()
    {
        // GetAirSpeedCap (:2096-2099): TF_COND_ROCKETPACK halves the cap to 15.
        PredictedPlayer player = InTheAir() with { Conditions = Cond(125) };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(15f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutWhoHasDashedOnce_CannotDashAgain()
    {
        // CTFPlayer::CanAirDash (tf_player_shared.cpp:12860): m_iAirDash 1 ≥ tf_scout_air_dash_count 1.
        PredictedPlayer player = InTheAir() with { PlayerClass = 1, MaxSpeed = 400f, AirDash = 1 };

        Run(ref player, Move(InJump));

        player.AirDash.ShouldBe(1);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AHypedScoutWhoHasDashedFiveTimes_CannotDashAgain()
    {
        // :12852-12855: under TF_COND_SODAPOPPER_HYPE the limit is five dashes.
        PredictedPlayer player = InTheAir() with { PlayerClass = 1, MaxSpeed = 400f, AirDash = 5, Conditions = Cond(36) };

        Run(ref player, Move(InJump));

        player.AirDash.ShouldBe(5);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    // ---- Stuns ---------------------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_AControlStunnedHeavyFiringAMinigun_KeepsOnlySpinning()
    {
        // StunMove (tf_gamemovement.cpp:543-552): a heavy with a minigun keeps IN_ATTACK2 while his controls are stunned.
        PredictedPlayer player = Stunned(0, 2) with { PlayerClass = 6, ActiveWeaponIsMinigun = true };

        Run(ref player, Move(InAttack));

        player.OldButtons.ShouldBe(InAttack2);
    }

    [Test]
    public void ProcessMovement_AControlStunnedSoldierFiring_LosesEveryButton()
    {
        PredictedPlayer player = Stunned(0, 2);

        Run(ref player, Move(InAttack | InAttack2));

        player.OldButtons.ShouldBe(0u);
    }

    [Test]
    public void ProcessMovement_InTheLoserStateHoldingForwardAndJump_WalksButCannotJump()
    {
        // :543-560: TF_STUN_LOSER_STATE clears the buttons but, without TF_STUN_CONTROLS, keeps the move: 10 · 0.015 · 300.
        PredictedPlayer player = Stunned(0, 64) with { MaxSpeed = 300f };

        Run(ref player, Move(forward: 450f, buttons: InJump));

        player.Velocity.X.ShouldBe(45f, 1e-3f);
        player.Velocity.Z.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_AMovementStunThatHasExpired_ScalesNothing()
    {
        // GetAmountStunned (tf_player_shared.cpp:9938-9950): the stun counts only while its expiry is after now.
        PredictedPlayer player = Stunned(51, 1) with { MaxSpeed = 300f, StunExpireTime = 0f };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(45f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AMovementStunStrafing_ScalesTheSideMoveToo()
    {
        // StunMove (:568-575): sidemove 450 · (1 − 0.2) = 360, under the max 400: 10 · 0.015 · 360 = 54 along −y.
        PredictedPlayer player = Stunned(51, 1);

        Run(ref player, new UserCommand(1, 1, 0f, 0f, 0f, 0f, 450f, 0f, 0, 0, 0, 0, 0, 0, 0));

        player.Velocity.Y.ShouldBe(-54f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_ANewMovementStun_StartsTheLerpAtNow()
    {
        // :577-584: a stun amount unlike the lerp target restarts the lerp: m_flLastMovementStunChange = curtime.
        PredictedPlayer player = Stunned(51, 1) with { CurTime = 5f, StunExpireTime = 100f };

        Run(ref player, Move());

        player.LastMovementStunChange.ShouldBe(5f);
        player.StunLerpTarget.ShouldBe(0.2f, 1e-6f);
        player.StunNeedsFadeOut.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_AMovementStunThatJustEnded_StartsTheFadeOutAtFullStrength()
    {
        // :588-605: the stun gone and the fade-out pending, the fade restarts at now, so RemapValClamped(0, 0.2, 0, 0, 1) is
        // 1 and the last target 0.5 still halves forwardmove 300: 10 · 0.015 · 150 = 22.5.
        PredictedPlayer player = Standing() with
        {
            CurTime = 2f,
            LastMovementStunChange = 1f,
            StunNeedsFadeOut = true,
            StunLerpTarget = 0.5f,
        };

        Run(ref player, Move(forward: 300f));

        player.LastMovementStunChange.ShouldBe(2f);
        player.StunNeedsFadeOut.ShouldBeFalse();
        player.Velocity.X.ShouldBe(22.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AMovementStunFadedOutLongAgo_ClearsTheLerp()
    {
        // :606-610: past the 0.2 s fade the remap is 0, so the target and the change time reset.
        PredictedPlayer player = Standing() with { CurTime = 5f, LastMovementStunChange = 1f, StunLerpTarget = 0.5f };

        Run(ref player, Move(forward: 300f));

        player.StunLerpTarget.ShouldBe(0f);
        player.LastMovementStunChange.ShouldBe(0f);
        player.Velocity.X.ShouldBe(45f, 1e-3f);
    }

    // ---- The parachute -------------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_FallingFastUnderAParachuteInBothNegativeAxes_DampsBoth()
    {
        // FullWalkMove's parachute (tf_gamemovement.cpp:2626-2647): 400 over 300 reduces the clamp by 100 / 3 − 10, so
        // each axis is held to −323.33; the fall is held to −100, then two half-ticks of gravity.
        PredictedPlayer player = InTheAir() with { Velocity = new Vector3(-400f, -400f, -500f), Conditions = Cond(80) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(-323.333f, 1e-2f);
        player.Velocity.Y.ShouldBe(-323.333f, 1e-2f);
        player.Velocity.Z.ShouldBe(-112f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_FallingFastWithoutAParachute_IsNotDamped()
    {
        PredictedPlayer player = InTheAir() with { Velocity = new Vector3(400f, 0f, -500f) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(400f, 1e-3f);
    }

    // ---- The grappling hook --------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_JumpingOnAGrapplingHook_ClimbsTheRope()
    {
        // GrapplingHookMove (:342-480): 3 units from the target's centre is under 750 · 0.015, so the velocity is 3 / 0.015
        // = 200 up. CheckJumpButton (:1170-1192) then adds tf_grapplinghook_jump_up_speed 375 to the 194 left after
        // StartGravity, under the cap 750, and two half-ticks of gravity follow: 557.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 544f));

        Run(ref player, Move(InJump));

        player.Velocity.Z.ShouldBe(557f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingOnAGrapplingHookWithTheFlagAndAHeavyRune_ClimbsAtEightyPercent()
    {
        // :1176-1179: carrying a rune other than agility and the flag, the climb is · 0.8: (194 + 375) · 0.8 − 12.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 544f)) with { HasTheFlag = true, Conditions = Cond(90) };

        Run(ref player, Move(InJump));

        player.Velocity.Z.ShouldBe(443.2f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AHeavyJumpingOnAGrapplingHook_DoesNotClimb()
    {
        // :1170: a heavy's jump is not a climb, and in the air he has no other: 200 − 12.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 544f)) with { PlayerClass = 6 };

        Run(ref player, Move(InJump));

        player.Velocity.Z.ShouldBe(188f, 1e-3f);
    }

    [TestCase(3, 838f)]
    [TestCase(7, 938f)]
    public void ProcessMovement_ClimbingAGrapplingHookWithTheAgilityRune_IsCappedByClass(int playerClass, float z)
    {
        // GetAirSpeedCap (tf_gamemovement.cpp:2033-2042): with RUNE_AGILITY the hook pulls at 950 and the cap is 850 for a
        // soldier or heavy, 950 otherwise; 950 − 6 + 375 is cut to the cap, then −6 −6.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 2000f)) with { PlayerClass = playerClass, Conditions = Cond(97) };

        Run(ref player, Move(InJump));

        player.Velocity.Z.ShouldBe(z, 1e-3f);
    }

    [TestCase(false, false, 0f, 0f, 750f - 12f)]
    [TestCase(true, false, 0f, 1f, (750f * 0.8f) - 12f)]
    [TestCase(true, false, 0f, 3f, (750f * 0.65f) - 12f)]
    [TestCase(true, true, 90f, 3f, (750f * 0.5f) - 12f)]
    [TestCase(true, true, 90f, 1f, (750f * 0.65f) - 12f)]
    [TestCase(true, true, 97f, 3f, (750f * 0.65f) - 12f)]
    [TestCase(false, false, 0f, 6f, (750f * 0.7f) - 12f)]
    [TestCase(true, false, 0f, 6f, (750f * 0.65f) - 12f)]
    public void ProcessMovement_PulledByAGrapplingHook_FliesAtTheClassAndFlagSpeed(
        bool hasTheFlag, bool rune, float runeCondition, float playerClass, float z)
    {
        // GrapplingHookMove (tf_gamemovement.cpp:385-420): 750, · 0.7 for a heavy without the flag; with it, a scout · 0.8
        // (0.65 with a heavy rune) and anyone else · 0.65 (0.5 with a heavy rune) — the agility rune counts as light.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 2000f)) with
        {
            HasTheFlag = hasTheFlag,
            PlayerClass = (int)playerClass,
            Conditions = rune ? Cond((int)runeCondition) : new PlayerConditions(0, 0, 0, 0, 0),
        };

        Run(ref player, Move());

        player.Velocity.Z.ShouldBe(z, 1e-2f);
    }

    [Test]
    public void ProcessMovement_APyroHookedToAPlayer_IsPulledAtSeventyPercent()
    {
        // :421-424: a pyro under TF_COND_GRAPPLINGHOOK_LATCHED (grappled to a player) is pulled at 750 · 0.7.
        PredictedPlayer player = Hooked(new Vector3(0f, 0f, 2000f)) with { PlayerClass = 7, Conditions = Cond(120) };

        Run(ref player, Move());

        player.Velocity.Z.ShouldBe((750f * 0.7f) - 12f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_HookedToAPlayer_StopsTheFollowDistanceShort()
    {
        // :366-372: hooked to a player, the target is pulled back 64 along the hook's line: 70 − 64 = 6, under 750 · 0.015,
        // so the velocity is 6 / 0.015 = 400 up.
        PredictedPlayer player = InTheAir() with
        {
            GrapplingHook = new GrapplingTarget(new Vector3(0f, 0f, 611f), new Vector3(0f, 0f, 5000f), IsPlayer: true),
        };

        Run(ref player, Move());

        player.Velocity.Z.ShouldBe(400f - 12f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_HookedToAGrapplingPlayer_StopsShortAlongHisHookDirection()
    {
        // :366-369: a grappling target's own hook direction (1, 0, 0) moves the aim to (−64, 0, 70), 94.85 away, so the pull
        // is 750 along it: x −506.08, z 553.52, less two half-ticks of gravity.
        PredictedPlayer player = InTheAir() with
        {
            GrapplingHook = new GrapplingTarget(new Vector3(0f, 0f, 611f), new Vector3(0f, 0f, 5000f), IsPlayer: true, new Vector3(1f, 0f, 0f)),
        };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(-506.08f, 1e-1f);
        player.Velocity.Z.ShouldBe(553.52f - 12f, 1e-1f);
    }

    [Test]
    public void ProcessMovement_HookedWithinAHundredUnitsOfTheTarget_SlowsByTheRemainingDistance()
    {
        // :447-452: 90 from the hook's origin, RemapValClamped(8100, 6400, 10000, 0, 750) = 354.17.
        PredictedPlayer player = InTheAir() with
        {
            GrapplingHook = new GrapplingTarget(new Vector3(0f, 0f, 2000f), new Vector3(0f, 0f, 590f), IsPlayer: false),
        };

        Run(ref player, Move());

        player.Velocity.Z.ShouldBe(354.1667f - 12f, 1e-2f);
    }

    // ---- CheckWater's points -------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_InAColumnOfWaterOnlyAtHisCentre_IsEyesDeep()
    {
        // CTFGameMovement::CheckWater (tf_gamemovement.cpp:1452-1500): every point is at the hull's centre in x and y.
        PredictedPlayer player = InTheAir() with { Origin = new Vector3(10f, 0f, 500f) };

        Run(ref player, Move(), contents: p => MathF.Abs(p.X - 10f) < 1f && MathF.Abs(p.Y) < 1f && p.Z < 1000f ? ContentsWater : 0);

        player.WaterLevel.ShouldBe(3);
    }

    [Test]
    public void ProcessMovement_WaterHalfAUnitDeep_DoesNotReachTheFeetPoint()
    {
        // :1460: the feet point is the origin's z + mins.z + 1, so water to 0.5 above the feet is not WL_Feet.
        PredictedPlayer player = InTheAir();

        Run(ref player, Move(), contents: Water(500.5f));

        player.WaterLevel.ShouldBe(0);
    }

    [Test]
    public void ProcessMovement_WaterToFiftyUnits_IsBelowTheWaistPoint()
    {
        // :1478-1484: the waist point is the hull's mid-height + 12, 53 above the origin; water to 50 is WL_Feet only.
        PredictedPlayer player = InTheAir();

        Run(ref player, Move(), contents: Water(550f));

        player.WaterLevel.ShouldBe(1);
    }

    [Test]
    public void ProcessMovement_SwimmingForwardAndUpPastTheMaxSpeed_ScalesBothEqually()
    {
        // CheckParameters scales (450, 0, 450) to 300 together, (212.13, 0, 212.13); WaterMove wishes it at 0.8 and adds
        // 10 · 240 · 0.015 = 36 along the diagonal.
        PredictedPlayer player = Swimming();

        Run(ref player, new UserCommand(1, 1, 0f, 0f, 0f, 450f, 0f, 450f, 0, 0, 0, 0, 0, 0, 0), contents: Water(1000f));

        player.Velocity.X.ShouldBe(25.456f, 1e-2f);
        player.Velocity.Z.ShouldBe(25.456f, 1e-2f);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ProcessMovement_SwimmingUnderACeilingWithOrWithoutAutoMovement_ReachesTheSameDestination(bool auto)
    {
        // WaterMove (tf_gamemovement.cpp:1693-1700) raises the press-down start by stepsize + 1 only with
        // m_bAllowAutoMovement. The first trace to the destination was clear, so the raised box either starts solid (here:
        // the ceiling 10 above his head) and TryPlayerMove reaches the same point, or sweeps down onto it unobstructed: the
        // flag cannot change where he ends. 100 less water friction is 94; 94 · 0.015 = 1.41.
        PredictedPlayer player = Swimming() with { Velocity = new Vector3(100f, 0f, 0f), AllowAutoMovement = auto };
        PlayerTraceRay ceiling = BoxWorld.Of((new Vector3(-1e4f, -1e4f, 592f), new Vector3(1e4f, 1e4f, 1000f)));

        Run(ref player, Move(), contents: Water(1000f), trace: ceiling);

        player.Origin.ShouldBe(new Vector3(1.41f, 0f, 500f));
        player.Velocity.X.ShouldBe(94f, 1e-3f);
    }

    // ---- The sub-boxes -------------------------------------------------------------------------------------------

    [Test]
    public void ProcessMovement_StraddlingASteepSlopeAndAFloorUnderOneQuadrant_StandsOnTheFloor()
    {
        // TracePlayerBBoxForGround (gamemovement.cpp:3660-3720): the full hull meets the slope (normal z 0.6) first, but the
        // +x +y quadrant alone reaches the floor beneath it, whose normal is standable, so he is on the ground.
        PlayerTraceRay slope = Through(HalfSpace(0.8f, 0f, 0.6f, -18.9f));
        PlayerTraceRay floor = BoxWorld.Of((new Vector3(0f, 0f, -1000f), new Vector3(1e4f, 1e4f, 0f)));
        PredictedPlayer player = InTheAir() with { Origin = new Vector3(0f, 0f, 0.6f) };

        Run(ref player, Move(), trace: (start, end, mins, maxs, mask) =>
        {
            Content.Bsp.BspTrace? a = slope(start, end, mins, maxs, mask);
            Content.Bsp.BspTrace? b = floor(start, end, mins, maxs, mask);

            if (a is not { } x || b is not { } y)
            {
                return null;
            }

            return x.StartSolid || x.Fraction <= y.Fraction ? x : y;
        });

        player.OnGround.ShouldBeTrue();
        player.Velocity.Z.ShouldBe(0f);
    }

    // ---- Taunts and the kart -------------------------------------------------------------------------------------

    [TestCase(-450f, -30f)]
    [TestCase(225f, 15f)]
    public void ProcessMovement_InAMovingTaunt_DrivesInTheInputsDirectionAndProportion(float forward, float x)
    {
        // TauntMove (tf_gamemovement.cpp:660-676): the direction is forwardmove over cl_forwardspeed or cl_backspeed, so
        // −450 drives back at 200 and 225 at 100: 10 · 0.015 · ±200 or 100.
        PredictedPlayer player = Taunting(new TauntMovement(false, 200f, 0f));

        Run(ref player, Move(forward));

        player.Velocity.X.ShouldBe(x, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InATauntThatForcesForward_DrivesWithoutInput()
    {
        // :657-660: "taunt force move forward" fixes the direction at 1.
        PredictedPlayer player = Taunting(new TauntMovement(true, 200f, 0f));

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(30f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InAnAcceleratingTauntWithNoInput_SlowsDown()
    {
        // :678-686: no direction makes the sign −1: 100 − 0.015 / 0.5 · 200 = 94.
        PredictedPlayer player = Taunting(new TauntMovement(false, 200f, 0.5f)) with { CurrentTauntMoveSpeed = 100f };

        Run(ref player, Move());

        player.CurrentTauntMoveSpeed.ShouldBe(94f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InAnAcceleratingTauntPartWay_DrivesAtTheSplinedSpeed()
    {
        // :687-693: 100 + 6 = 106 of 200, SimpleSpline(0.53) = 0.544946, so forwardmove 108.99 and 10 · 0.015 · 108.99.
        PredictedPlayer player = Taunting(new TauntMovement(false, 200f, 0.5f)) with { CurrentTauntMoveSpeed = 100f };

        Run(ref player, Move(450f));

        player.Velocity.X.ShouldBe(16.348f, 1e-2f);
    }

    [TestCase(100f, 0f, 95.5f)]
    [TestCase(0f, 225f, 3.75f)]
    [TestCase(400f, 450f, 404.5f)]
    [TestCase(300f, 450f, 304.5f)]
    [TestCase(300f, -900f, 292.5f)]
    [TestCase(-20f, -450f, -27.5f)]
    [TestCase(-20f, 225f, -12.5f)]
    public void ProcessMovement_AKart_ApproachesItsTargetSpeed(float current, float forward, float expected)
    {
        // VehicleMove (tf_gamemovement.cpp:738-830): coasting at 300 toward 0; half throttle at 500 · 0.5 from below
        // tf_halloween_kart_slow_moving_threshold 300 and 300 from it up; a back input past full counts as full; reversing
        // further at the brake rate; and crossing zero always at the brake rate 500.
        PredictedPlayer player = Kart(current);

        Run(ref player, Move(forward));

        player.CurrentTauntMoveSpeed.ShouldBe(expected, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartAtRestHoldingBackPastTheStall_Reverses()
    {
        // :787: the stall over (m_flVehicleReverseTime before now), it reverses: −7.5.
        PredictedPlayer player = Kart(0f) with { OldForwardMove = -450f, CurTime = 2f, VehicleReverseTime = 1f };

        Run(ref player, Move(-450f));

        player.CurrentTauntMoveSpeed.ShouldBe(-7.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AKartMovingForward_ClearsTheReverseTime()
    {
        // :802-805: moving forward, m_flVehicleReverseTime = FLT_MAX.
        PredictedPlayer player = Kart(100f) with { VehicleReverseTime = 5f };

        Run(ref player, Move());

        player.VehicleReverseTime.ShouldBe(float.MaxValue);
    }

    // ---- Fixtures ------------------------------------------------------------------------------------------------

    private static PredictedPlayer Taunting(TauntMovement movement) => Standing() with
    {
        Conditions = Cond(7),
        AllowMoveDuringTaunt = true,
        TauntMovement = movement,
    };

    private static PredictedPlayer InTheAir() => Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false, ViewOffsetZ = 68f };

    private static PredictedPlayer Swimming() => InTheAir();

    /// <summary>Waist deep 36 units from a ledge face at x = 100 whose top is at z = 90.</summary>
    private static PredictedPlayer WaistDeep() => InTheAir() with { Origin = new Vector3(60f, 0f, 20f) };

    private static PlayerTraceRay Ledge() => BoxWorld.Of(
        (new Vector3(-1e4f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, -100f)),
        (new Vector3(100f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, 90f)));

    private static PredictedPlayer Kart(float speed) => Standing() with { Conditions = Cond(82), CurrentTauntMoveSpeed = speed };

    private static PredictedPlayer Stunned(int amount, int flags) => Standing() with
    {
        MaxSpeed = 400f,
        Conditions = Cond(15),
        StunActive = true,
        StunAmount = amount,
        StunFlags = flags,
        StunExpireTime = 100f,
    };

    /// <summary>In the air at 500, his centre at 541, hooked to a target centred at <paramref name="center"/> and based far off.</summary>
    private static PredictedPlayer Hooked(Vector3 center) => InTheAir() with
    {
        GrapplingHook = new GrapplingTarget(center, new Vector3(0f, 0f, 5000f), IsPlayer: false),
    };

    private static PlayerConditions Cond(int condition) => (condition / 32) switch
    {
        0 => new PlayerConditions(1 << condition, 0, 0, 0, 0),
        1 => new PlayerConditions(0, 1 << (condition - 32), 0, 0, 0),
        2 => new PlayerConditions(0, 0, 1 << (condition - 64), 0, 0),
        3 => new PlayerConditions(0, 0, 0, 1 << (condition - 96), 0),
        _ => new PlayerConditions(0, 0, 0, 0, 1 << (condition - 128)),
    };

    private static PlayerConditions Both(PlayerConditions a, PlayerConditions b) =>
        new(a.Cond | b.Cond, a.Ex | b.Ex, a.Ex2 | b.Ex2, a.Ex3 | b.Ex3, a.Ex4 | b.Ex4);

    private static Func<Vector3, int> Water(float surface) => point => point.Z < surface ? ContentsWater : 0;

    private static UserCommand Move(uint buttons = 0) => Command(buttons: buttons);

    private static UserCommand Move(float forward, uint buttons = 0) => Command(forward, buttons);

    private static void Run(
        ref PredictedPlayer player,
        UserCommand command,
        Func<Vector3, int>? contents = null,
        PlayerTraceRay? trace = null,
        int commandNumber = 1,
        int maxClients = 24) =>
        new TfGameMovement(trace ?? Floor(), MovementConVars.Defaults, maxClients) { PointContents = contents }
            .ProcessMovement(ref player, command, Tick, first: true, commandNumber).ShouldBeTrue();
}
