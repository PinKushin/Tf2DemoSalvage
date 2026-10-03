using System;
using System.Numerics;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CTFGameMovement</c>'s ground and air moves pinned branch by branch, each expectation computed by hand from
/// <c>src/game/shared/tf/tf_gamemovement.cpp</c> and <c>src/game/shared/gamemovement.cpp</c> at the default ConVars.
/// </summary>
/// <remarks>Written against the surviving mutants of a Stryker run: each test names the branch it holds.</remarks>
public sealed class TfGameMovementWalkConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;

    [Test]
    public void ProcessMovement_HoldingForwardWithAMaxSpeedAbove450_RunsAtTheMaxSpeed()
    {
        // HighMaxSpeedMove (tf_gamemovement.cpp:884-911): forwardmove AlmostEqual to cl_forwardspeed 450 and under the max
        // speed 500 becomes 500; Accelerate adds 10 · 0.015 · 500 = 75.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(75f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_HoldingBackWithAMaxSpeedAbove450_RunsBackAtTheMaxSpeed()
    {
        // :892: −cl_backspeed becomes −500; 75 back is under tf_clamp_back_speed_min 100, so it is not clamped.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(forward: -450f));

        player.Velocity.X.ShouldBe(-75f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_StrafingRightWithAMaxSpeedAbove450_StrafesAtTheMaxSpeed()
    {
        // :900-903: sidemove 450 becomes 500. At yaw 0 AngleVectors' right is (0, −1, 0), so y is −75.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(side: 450f));

        player.Velocity.Y.ShouldBe(-75f, 1e-3f);
        player.Origin.Y.ShouldBe(-75f * Tick, 1e-4f);
    }

    [Test]
    public void ProcessMovement_StrafingLeftWithAMaxSpeedAbove450_StrafesAtTheMaxSpeed()
    {
        // :904-907: −cl_sidespeed becomes −500, and y is +75.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(side: -450f));

        player.Velocity.Y.ShouldBe(75f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_ADeadPlayerHoldingForward_GainsNoSpeed()
    {
        // CheckParameters (gamemovement.cpp:1017-1022): a dead player's moves are zeroed before WalkMove reads them.
        PredictedPlayer player = Standing() with { IsDead = true };

        Run(ref player, Move(forward: 450f));

        player.Velocity.ShouldBe(Vector3.Zero);
    }

    [Test]
    public void ProcessMovement_SlidingUnderTheStopSpeed_LosesTheStopSpeedsFriction()
    {
        // Friction (gamemovement.cpp:1630): control is sv_stopspeed 100 under it, so drop = 100 · 4 · 0.015 = 6.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(50f, 0f, 0f) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(44f, 1e-3f);
        player.Origin.X.ShouldBe(44f * Tick, 1e-4f);
    }

    [Test]
    public void ProcessMovement_AWishSpeedUnderTheThreshold_AcceleratesHardEnoughToBeatFriction()
    {
        // tf_gamemovement.cpp:1779-1795: wish 30 is under 100 · 4 / 10 = 40, so accelerate = 100 · 4 / 30 + 1, and
        // Accelerate adds (40 / 3 + 1) · 0.015 · 30 = 6.45 rather than 10 · 0.015 · 30 = 4.5.
        PredictedPlayer player = Standing();

        Run(ref player, Move(forward: 30f));

        player.Velocity.X.ShouldBe(6.45f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AWishSpeedUnderTheThresholdWhileLosingFooting_GetsNoBonusTraction()
    {
        // :1785: sliding at 60 (past the restick speed 50) keeps TF_COND_LOST_FOOTING and a surface friction of 0.1, so
        // friction takes 100 · 0.4 · 0.015 = 0.6 and the threshold is 100 · 0.4 / 10 = 4. Wishing 3 backward is under it,
        // but lost footing skips the bonus: Accelerate takes 10 · 0.015 · 3 · 0.1 = 0.045, not (40 / 3 + 1)'s 0.0645.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(60f, 0f, 0f), Conditions = Cond(126) };

        Run(ref player, Move(forward: -3f));

        player.Velocity.X.ShouldBe(59.355f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_MovingBackwardFast_IsClampedToNinetyPercentOfTheMaxSpeed()
    {
        // :1832-1850: 300 back loses a tick of friction to 282, past 0.9 · 300 = 270, so the back move is cut to 270; the
        // sideways part, 100 less 6 % friction, is kept.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(-300f, 100f, 0f) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(-270f, 1e-2f);
        player.Velocity.Y.ShouldBe(94f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_BackwardWithARolledViewWhoseRightIsBackward_IsRescaledToTheMaxSpeed()
    {
        // :1852-1858, the re-run against "viewangles hacking": pitch 45 and roll 90 flatten right to −forward, so the
        // back move 270 and the right move 282 add to 552 along −x, cut back to the max speed 300.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(-300f, 0f, 0f) };

        Run(ref player, new UserCommand(1, 1, 45f, 0f, 90f, 0f, 0f, 0f, 0, 0, 0, 0, 0, 0, 0));

        player.Velocity.X.ShouldBe(-300f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_NotInTheActiveState_CannotAccelerate()
    {
        // CTFGameMovement::CanAccelerate: only TF_STATE_ACTIVE (0) accelerates.
        PredictedPlayer player = Standing() with { PlayerState = 1 };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_JumpingMidDuckTransition_SetsTheImpulseRatherThanAddingIt()
    {
        // CheckJumpButton (tf_gamemovement.cpp:1305-1312): ducking, z = 289 outright instead of −6 + 289; then two
        // half-ticks of gravity leave 277.
        PredictedPlayer player = Standing() with { Ducking = true, DuckTime = 900f };

        Run(ref player, Move(buttons: InDuck | InJump), oldButtons: InDuck);

        player.Velocity.Z.ShouldBe(277f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingFasterThanBunnyHopAllows_IsSlowedTo12TimesTheMaxSpeed()
    {
        // PreventBunnyJumping (tf_gamemovement.cpp:1094): |(500, 0, −6)| is cut to 1.2 · 300 = 360, so x = 359.974 and
        // z = −4.3197; then +289 and −12.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(500f, 0f, 0f) };

        Run(ref player, Move(buttons: InJump));

        player.Velocity.X.ShouldBe(359.974f, 1e-2f);
        player.Velocity.Z.ShouldBe(272.680f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_WalkingIntoAStepUnderTheStepSize_StepsUpOntoIt()
    {
        // StepMove (tf_gamemovement.cpp:2829-2926), the high road: up 18 + 1/32, across 282 · 0.015 = 4.23, and down onto the
        // step's top at 10, a trace's DIST_EPSILON above it.
        PredictedPlayer player = Standing() with { Origin = new Vector3(15f, 0f, 0.03125f), Velocity = new Vector3(300f, 0f, 0f) };

        Run(ref player, Move(), trace: Step());

        player.Origin.X.ShouldBe(19.23f, 1e-3f);
        player.Origin.Z.ShouldBe(10.03125f, 1e-4f);
        player.Velocity.X.ShouldBe(282f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WalkingIntoAStepWithoutAutoMovement_TakesTheLowRoadAndStops()
    {
        // :2845, :2875-2878: without m_bAllowAutoMovement there is no high road; the low road reuses the trace that met the
        // step 1 unit away, stops a DIST_EPSILON short of it, and the clip against its face leaves nothing.
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(15f, 0f, 0.03125f),
            Velocity = new Vector3(300f, 0f, 0f),
            AllowAutoMovement = false,
        };

        Run(ref player, Move(), trace: Step());

        player.Origin.X.ShouldBe(15.96875f, 1e-4f);
        player.Origin.Z.ShouldBe(0.03125f, 1e-4f);
        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_AForwardMoveTwoUlpsFrom450_IsStillTakenAsTheKey()
    {
        // AlmostEqual's few-ULP tolerance, taken as 2: 450 two floats up still scales to the max speed 500, so 75.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(forward: MathF.BitIncrement(MathF.BitIncrement(450f))));

        player.Velocity.X.ShouldBe(75f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AForwardMoveThreeUlpsFrom450_IsAnAnalogMove()
    {
        // The control: three floats up is not the key, so the wish stays ~450 and Accelerate adds 67.5.
        PredictedPlayer player = Standing() with { MaxSpeed = 500f };

        Run(ref player, Move(forward: MathF.BitIncrement(MathF.BitIncrement(MathF.BitIncrement(450f)))));

        player.Velocity.X.ShouldBe(67.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_ShieldChargingWithBothAttacksAndJump_KeepsOnlyTheAttacks()
    {
        // ChargeMove (tf_gamemovement.cpp:484-520): the buttons become IN_ATTACK2 if held, plus IN_ATTACK if held, and
        // FinishMove stores them as m_nOldButtons.
        PredictedPlayer player = Standing() with { MaxSpeed = 750f, Conditions = Cond(17) };

        Run(ref player, Move(buttons: InJump | (1 << 0) | (1 << 11)));

        player.OldButtons.ShouldBe((1u << 0) | (1u << 11));
    }

    [Test]
    public void ProcessMovement_ShieldChargingWithJumpOnly_DropsEveryButton()
    {
        PredictedPlayer player = Standing() with { MaxSpeed = 750f, Conditions = Cond(17) };

        Run(ref player, Move(buttons: InJump));

        player.OldButtons.ShouldBe(0u);
    }

    [Test]
    public void ProcessMovement_ALaterCommandRisingFasterThan250_LeavesTheGround()
    {
        // PlayerMove (gamemovement.cpp:4611-4614): not the first command, so no CategorizePosition, but z > 250 still drops
        // the ground; then he flies, 300 less two half-ticks of gravity.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(0f, 0f, 300f) };
        player.OldButtons = 0;

        new TfGameMovement(Floor(), MovementConVars.Defaults, maxClients: 24)
            .ProcessMovement(ref player, Move(), Tick, first: false, commandNumber: 1).ShouldBeTrue();

        player.OnGround.ShouldBeFalse();
        player.Velocity.Z.ShouldBe(288f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_FallingInTheAir_RecordsTheFallSpeedBeforeGravity()
    {
        // PlayerMove (gamemovement.cpp:4617-4620): off the ground, m_flFallVelocity = −velocity.z before the move.
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(0f, 0f, 500f),
            OnGround = false,
            Velocity = new Vector3(0f, 0f, -100f),
        };

        Run(ref player, Move());

        player.FallVelocity.ShouldBe(100f);
    }

    [Test]
    public void ProcessMovement_DuckedHoldingForwardAndStrafing_CropsTheClampedMoveToAThird()
    {
        // CheckParameters scales (450, 450) to the max speed 300 along the diagonal — 212.13 each — and the duck crop takes a
        // third: a wish of 100 at 45°, so Accelerate adds 15 split as 10.607 forward and 10.607 to the right (−y).
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true };

        Run(ref player, Move(forward: 450f, side: 450f, buttons: InDuck), oldButtons: InDuck);

        player.Velocity.X.ShouldBe(10.607f, 1e-2f);
        player.Velocity.Y.ShouldBe(-10.607f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_FacingNorthMovingForwardAndRight_AcceleratesNorthAndEast()
    {
        // AngleVectors (mathlib_base.cpp): at yaw 90 forward is +y and right is +x. Wish (30, 30) is 42.43, over the
        // threshold 40, so Accelerate adds 10 · 0.015 · 42.43 = 6.364 split evenly: 4.5 each.
        PredictedPlayer player = Standing();

        Run(ref player, Look(30f, 90f, 0f, forward: 30f, side: 30f));

        player.Velocity.X.ShouldBe(4.5f, 1e-3f);
        player.Velocity.Y.ShouldBe(4.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutAirDashingSideways_DashesAlongHisRight()
    {
        // AirDash (tf_gamemovement.cpp:992-1031): CheckParameters cuts sidemove 450 to the max 400, right is (0, −1, 0), so
        // the velocity is (0, −400, 268.33), less the end-of-move half-tick of gravity.
        PredictedPlayer player = AirborneScout();

        Run(ref player, Move(side: 450f, buttons: InJump));

        player.Velocity.Y.ShouldBe(-400f, 1e-3f);
        player.Velocity.Z.ShouldBe(262.3281573f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutAirDashingWithTheAgilityRune_DashesOnePointEightTimesHigher()
    {
        // AirDash's z is 268.33 · flJumpMod, and RUNE_AGILITY makes it 1.8 (tf_gamemovement.cpp:1019): 482.99 − 6.
        PredictedPlayer player = AirborneScout() with { Conditions = Cond(97) };

        Run(ref player, Move(buttons: InJump));

        player.Velocity.Z.ShouldBe((268.3281573f * 1.8f) - 6f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_FallingFasterThanMaxVelocity_IsBoundedBySvMaxVelocity()
    {
        // CheckVelocity (gamemovement.cpp:1094-1128): each axis is clamped to sv_maxvelocity 3500.
        PredictedPlayer player = Airborne() with { Velocity = new Vector3(5000f, 0f, 0f) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(3500f);
    }

    [Test]
    public void ProcessMovement_AVelocityThatIsNaN_IsZeroed()
    {
        // CheckVelocity (gamemovement.cpp:1100-1106): an IS_NAN component is set to 0.
        PredictedPlayer player = Airborne() with { Velocity = new Vector3(float.NaN, 0f, 0f) };

        Run(ref player, Move());

        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_StandingOnAConveyor_MovesByItsBaseVelocityAndKeepsNoneOfIt()
    {
        // WalkMove (tf_gamemovement.cpp:1861, :1882-1886): the base velocity is added for the trace and removed after.
        PredictedPlayer player = Standing() with { BaseVelocity = new Vector3(100f, 0f, 0f) };

        Run(ref player, Move());

        player.Origin.X.ShouldBe(1.5f, 1e-4f);
        player.Velocity.X.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_OnAConveyorSlowerThanOneUnit_IsNotMoved()
    {
        // :1864-1869: under 1 unit/s the velocity is zeroed and he stays put; the base velocity is not removed.
        PredictedPlayer player = Standing() with { BaseVelocity = new Vector3(0.5f, 0f, 0f) };

        Run(ref player, Move());

        player.Origin.X.ShouldBe(0f);
        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_InTheAirWithAnUpwardBaseVelocity_TakesATickOfItIntoTheFall()
    {
        // StartGravity (gamemovement.cpp:1240-1262): z += basevelocity.z · dt, then basevelocity.z = 0: −6 + 1.5 − 6.
        PredictedPlayer player = Airborne() with { BaseVelocity = new Vector3(0f, 0f, 100f) };

        Run(ref player, Move());

        player.Velocity.Z.ShouldBe(-10.5f, 1e-4f);
        player.BaseVelocity.Z.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_RunningNearTheMaxSpeedHoldingForward_AddsOnlyTheShortfall()
    {
        // Accelerate (gamemovement.cpp:1450-1485): (280, 100) loses 6 % to friction, (263.2, 94); addspeed 300 − 263.2 =
        // 36.8 is under 45, so x is 300; |(300, 94)| = 314.38 is cut back to 300.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(280f, 100f, 0f) };

        Run(ref player, Move(forward: 450f));

        player.Velocity.X.ShouldBe(286.276f, 1e-2f);
        player.Velocity.Y.ShouldBe(89.700f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_InTheAirWithARolledViewAndHighAirControl_CapsTheWishAtTheMaxSpeed()
    {
        // AirMove (tf_gamemovement.cpp:2133-2137): pitch 45 and roll 90 flatten right to −forward, so forward 212 and side
        // −212 wish 424 along x, capped at 300; mod_air_control 20 lifts the air cap to 600, so AirAccelerate adds
        // 10 · 300 · 0.015 = 45 rather than 10 · 424 · 0.015.
        PredictedPlayer player = Airborne();
        MovementItems items = MovementItems.None with { OnPlayer = (name, value) => name == "mod_air_control" ? value * 20f : value };

        Run(ref player, Look(45f, 0f, 90f, forward: 450f, side: -450f), items: items);

        player.Velocity.X.ShouldBe(45f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_InTheAirAgainstTwoWalls_SlidesOffBothAndKeepsOnlyTheFall()
    {
        // TryPlayerMove (gamemovement.cpp:2558-2760): touching x = 100 and y = 100, the first plane clips x away; the
        // second, met without moving, clips y away against both planes; the fall alone goes on: −6, then −6 more.
        const float Touching = 100f - 24f - 0.03125f;
        PredictedPlayer player = Airborne() with { Origin = new Vector3(Touching, Touching, 500f), Velocity = new Vector3(100f, 50f, 0f) };

        Run(ref player, Move(), trace: Corner());

        player.Velocity.ShouldBe(new Vector3(0f, 0f, -12f));
        player.Origin.X.ShouldBe(Touching);
        player.Origin.Y.ShouldBe(Touching);
        player.Origin.Z.ShouldBe(500f - 0.09f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_RunningIntoACornerOnTheGround_Stops()
    {
        // TryPlayerMove on the ground (:2700-2752): each of two planes' clips runs into the other, so the crease of x = 100
        // and y = 100 is vertical and leaves nothing of a level run.
        const float Touching = 100f - 24f - 0.03125f;
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(Touching, Touching, 0.03125f),
            Velocity = new Vector3(300f, 300f, 0f),
        };

        Run(ref player, Move(), trace: Corner());

        player.Velocity.ShouldBe(Vector3.Zero);
        player.Origin.ShouldBe(new Vector3(Touching, Touching, 0.03125f));
    }

    [Test]
    public void ProcessMovement_DuckedUnderALowCeiling_WalksWithTheDuckedHull()
    {
        // GetPlayerMins/Maxs ducked (gamemovement.cpp:3500-3520): the 62-unit hull fits under a ceiling at 70 where the
        // 82-unit one would start solid; 100 less a tick of friction moves him 94 · 0.015.
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true, Velocity = new Vector3(100f, 0f, 0f) };

        Run(ref player, Move(buttons: InDuck), oldButtons: InDuck, trace: LowCeiling());

        player.Origin.X.ShouldBe(1.41f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_JumpingWhileTheDuckJumpTimerRuns_DoesNotJump()
    {
        // CheckJumpButton (tf_gamemovement.cpp:1213): m_flDuckJumpTime 100, less 15, is still above 0.
        PredictedPlayer player = Standing() with { DuckJumpTime = 100f };

        Run(ref player, Move(buttons: InJump));

        player.Velocity.Z.ShouldBe(0f);
        player.DuckJumpTime.ShouldBe(85f);
    }

    /// <summary>Walls at x ≥ 100 and y ≥ 100, over a floor below z = 0.</summary>
    private static PlayerTraceRay Corner() => BoxWorld.Of(
        (new Vector3(-1e4f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, 0f)),
        (new Vector3(100f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, 1e4f)),
        (new Vector3(-1e4f, 100f, -1000f), new Vector3(1e4f, 1e4f, 1e4f)));

    /// <summary>A floor below z = 0 and a ceiling from z = 70.</summary>
    internal static PlayerTraceRay LowCeiling() => BoxWorld.Of(
        (new Vector3(-1e4f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, 0f)),
        (new Vector3(-1e4f, -1e4f, 70f), new Vector3(1e4f, 1e4f, 1000f)));

    private static PredictedPlayer Airborne() => Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

    private static PredictedPlayer AirborneScout() => Airborne() with { PlayerClass = 1, MaxSpeed = 400f };

    /// <summary>A floor below z = 0 and a step 10 high from x = 40 on.</summary>
    private static PlayerTraceRay Step() => BoxWorld.Of(
        (new Vector3(-1e4f, -1e4f, -1000f), new Vector3(1e4f, 1e4f, 0f)),
        (new Vector3(40f, -1e4f, 0f), new Vector3(1e4f, 1e4f, 10f)));

    private static PlayerConditions Cond(int condition) => (condition / 32) switch
    {
        0 => new PlayerConditions(1 << condition, 0, 0, 0, 0),
        1 => new PlayerConditions(0, 1 << (condition - 32), 0, 0, 0),
        2 => new PlayerConditions(0, 0, 1 << (condition - 64), 0, 0),
        3 => new PlayerConditions(0, 0, 0, 1 << (condition - 96), 0),
        _ => new PlayerConditions(0, 0, 0, 0, 1 << (condition - 128)),
    };

    private static UserCommand Move(float forward = 0f, float side = 0f, uint buttons = 0) =>
        new(1, 1, 0f, 0f, 0f, forward, side, 0f, buttons, 0, 0, 0, 0, 0, 0);

    private static void Run(
        ref PredictedPlayer player, UserCommand command, uint oldButtons = 0, PlayerTraceRay? trace = null, MovementItems? items = null)
    {
        player.OldButtons = oldButtons;

        new TfGameMovement(trace ?? Floor(), MovementConVars.Defaults, maxClients: 24) { Items = items ?? MovementItems.None }
            .ProcessMovement(ref player, command, Tick, first: true, commandNumber: 1).ShouldBeTrue();
    }

    internal static UserCommand Look(float pitch, float yaw, float roll, float forward = 0f, float side = 0f, uint buttons = 0) =>
        new(1, 1, pitch, yaw, roll, forward, side, 0f, buttons, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// A world of axis-aligned solid boxes, traced as the engine's box sweep is: the hull is the box grown by the player's,
/// and a hit stops <c>DIST_EPSILON</c> short along the entered face's normal (<c>CM_ClipBoxToBrush</c>).
/// </summary>
internal static class BoxWorld
{
    private const float DistEpsilon = 0.03125f;

    internal static PlayerTraceRay Of(params (Vector3 Min, Vector3 Max)[] boxes) => (start, end, mins, maxs, _) =>
    {
        Vector3 delta = end - start;
        float best = 1f;
        Vector3 normal = Vector3.Zero;

        foreach ((Vector3 min, Vector3 max) in boxes)
        {
            Vector3 low = min - maxs;
            Vector3 high = max - mins;

            if (Inside(start, low, high))
            {
                return new Content.Bsp.BspTrace(0f, -1, default, Inside(end, low, high), StartSolid: true);
            }

            float enter = float.NegativeInfinity;
            float exit = float.PositiveInfinity;
            int axis = -1;
            bool miss = false;

            for (int i = 0; i < 3; i++)
            {
                float s = start[i];
                float d = delta[i];

                if (d == 0f)
                {
                    miss |= s <= low[i] || s >= high[i];
                    continue;
                }

                float t1 = (low[i] - s) / d;
                float t2 = (high[i] - s) / d;

                if (MathF.Min(t1, t2) > enter)
                {
                    enter = MathF.Min(t1, t2);
                    axis = i;
                }

                exit = MathF.Min(exit, MathF.Max(t1, t2));
            }

            if (miss || axis < 0 || enter > exit || enter < 0f || enter > 1f)
            {
                continue;
            }

            float fraction = MathF.Max(0f, enter - (DistEpsilon / MathF.Abs(delta[axis])));

            if (fraction < best)
            {
                best = fraction;
                normal = Vector3.Zero;
                normal[axis] = delta[axis] > 0f ? -1f : 1f;
            }
        }

        return new Content.Bsp.BspTrace(best, -1, (normal.X, normal.Y, normal.Z), false);
    };

    private static bool Inside(Vector3 point, Vector3 low, Vector3 high) =>
        point.X > low.X && point.X < high.X && point.Y > low.Y && point.Y < high.Y && point.Z > low.Z && point.Z < high.Z;
}
