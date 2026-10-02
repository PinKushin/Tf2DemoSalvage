using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The movement modes <c>CTFGameMovement</c> runs besides walking — water, ghosts, stuns, taunts, karts, parachutes and the
/// grappling hook — one usercmd each, every expectation computed from the SDK by hand (D205, B450).
/// </summary>
/// <remarks>
/// <c>src/game/shared/tf/tf_gamemovement.cpp</c> and <c>src/game/shared/gamemovement.cpp</c>, default <c>sv_*</c> values
/// (gravity 800, friction 4, accelerate 10), a 0.015 s tick, so half a tick of gravity is 6. "In the open" is z = 500 over
/// <see cref="TfGameMovementConformanceTests.Floor"/>, where no trace meets anything.
/// </remarks>
public sealed class TfGameMovementModeConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InAttack = 1 << 0;
    private const uint InJump = 1 << 1;

    private const int CondTaunting = 7;
    private const int CondStunned = 15;
    private const int CondGhost = 77;
    private const int CondParachute = 80;
    private const int CondKart = 82;
    private const int CondSwimmingCurse = 86;

    private const int StunMovement = 1 << 0;
    private const int StunControls = 1 << 1;
    private const int StunForwardOnly = 1 << 2;

    private const int ContentsWater = 0x20;
    private const int ContentsMonster = 0x2000000;

    // ---- Water: CheckWater, FullWalkMoveUnderwater, WaterMove, CheckWaterJumpButton ----

    [Test]
    public void ProcessMovement_UnderwaterWithNoInput_SinksAtSixtyTimesPointEightAccelerated()
    {
        // CheckWater (tf_gamemovement.cpp:1452): feet and eyes wet, WL_Eyes. FullWalkMove skips StartGravity (:2624);
        // WaterMove (:1537): wishvel z −60, wishspeed 60 · 0.8 = 48, accelspeed 10 · 48 · 0.015 = 7.2.
        PredictedPlayer player = InTheOpen();

        Run(ref player, Command(), Water(1000f)).ShouldBeTrue();

        player.WaterLevel.ShouldBe(3);
        player.Velocity.Z.ShouldBe(-7.2f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_UnderwaterHoldingJump_SwimsUpByTheWaterJumpThenTheSwimStroke()
    {
        // CheckWaterJumpButton (:938): WL ≥ 2 in CONTENTS_WATER sets z to 100. WaterMove: wishvel z += clientmaxspeed 300,
        // wishspeed 240; friction 100 − 0.015 · 100 · 4 = 94; accelspeed 10 · 240 · 0.015 = 36 ≤ 240 − 94: 130.
        PredictedPlayer player = InTheOpen();

        Run(ref player, Command(buttons: InJump), Water(1000f));

        player.Velocity.Z.ShouldBe(130f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WaterAtTheFeetOnly_WalksAsOnDryGround()
    {
        // WL_Feet is not InWater() (gamemovement.cpp:3479): the walk runs, 10 · 0.015 · 300 = 45 as on dry ground.
        PredictedPlayer player = TfGameMovementConformanceTests.Standing();

        Run(ref player, Command(forward: 450f), Water(10f));

        player.WaterLevel.ShouldBe(1);
        player.Velocity.X.ShouldBe(45f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_UnderASwimmingCurseOnDryLand_Swims()
    {
        // CheckWater forces WL_Eyes under TF_COND_SWIMMING_CURSE (:1504), so the dry player sinks as in water.
        PredictedPlayer player = InTheOpen() with { Conditions = Cond(CondSwimmingCurse) };

        Run(ref player, Command(), Water(-1000f));

        player.WaterLevel.ShouldBe(3);
        player.Velocity.Z.ShouldBe(-7.2f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_WaterJumpingWhenTheClockIsNotCarried_DeclinesToPredict()
    {
        // m_flWaterJumpTime is a DEFINE_FIELD, neither sent nor restored (c_baseplayer.cpp:383): FL_WATERJUMP with no time.
        PredictedPlayer player = InTheOpen() with { WaterJumpUnknown = true };

        Run(ref player, Command(), Water(1000f)).ShouldBeFalse();
    }

    // ---- Ghosts: the swim code out of water, the fly jump, a brush-only mask ----

    [Test]
    public void ProcessMovement_AGhostWithNoInput_FallsByGravityThenTheSwimSink()
    {
        // StartGravity −6 (not in water), friction 6 → 5.64, ghost acceleration (:1623) adds 7.2 down: −12.84.
        PredictedPlayer player = InTheOpen() with { Conditions = Cond(CondGhost) };

        Run(ref player, Command());

        player.Velocity.Z.ShouldBe(-12.84f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AGhostHoldingJump_FliesUpAtTheGhostSpeed()
    {
        // CheckJumpButton (:1195): z = tf_ghost_up_speed 300, FinishGravity −6 = 294; WaterMove's friction · 0.94 = 276.36.
        PredictedPlayer player = InTheOpen() with { Conditions = Cond(CondGhost) };

        Run(ref player, Command(buttons: InJump));

        player.Velocity.Z.ShouldBe(276.36f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_AGhost_TracesTheWorldOnly()
    {
        // PlayerSolidMask (:264): MASK_PLAYERSOLID_BRUSHONLY for a ghost; anyone else adds CONTENTS_MONSTER (players).
        PredictedPlayer ghost = InTheOpen() with { Conditions = Cond(CondGhost) };
        PredictedPlayer walker = InTheOpen();
        List<int> ghostMasks = [];
        List<int> walkerMasks = [];

        Run(ref ghost, Command(), trace: Recording(ghostMasks));
        Run(ref walker, Command(), trace: Recording(walkerMasks));

        ghostMasks.ShouldAllBe(mask => mask == BspLeafTree.MaskPlayerSolid);
        walkerMasks.ShouldAllBe(mask => mask == (BspLeafTree.MaskPlayerSolid | ContentsMonster));
    }

    // ---- Stuns: StunMove (:537) ----

    [Test]
    public void ProcessMovement_MovementStunnedByAFifth_WalksAtFourFifthsOfTheMove()
    {
        // GetAmountStunned: 51 / 255 = 0.2; forwardmove 450 · 0.8 = 360 under a 400 max; Accelerate 10 · 0.015 · 360 = 54.
        PredictedPlayer player = Stunned(51, StunMovement);

        Run(ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(54f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_StunnedForwardOnly_CannotMoveForward()
    {
        PredictedPlayer player = Stunned(51, StunMovement | StunForwardOnly);

        Run(ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_ControlStunned_NeitherMovesNorJumps()
    {
        // IsControlStunned: buttons and moves zeroed.
        PredictedPlayer player = Stunned(0, StunControls);

        Run(ref player, Command(forward: 450f, buttons: InJump | InAttack));

        player.Velocity.ShouldBe(Vector3.Zero);
        player.OnGround.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_HalfwayThroughTheStunFadeOut_ScalesByHalfTheLastAmount()
    {
        // The lerp out (:585): RemapValClamped(0.1, 0.2, 0, 0, 1) = 0.5 of a 0.2 target: 450 · 0.9 = 405; 10 · 0.015 · 405.
        PredictedPlayer player = TfGameMovementConformanceTests.Standing() with
        {
            MaxSpeed = 450f,
            CurTime = 10.1f,
            LastMovementStunChange = 10f,
            StunLerpTarget = 0.2f,
        };

        Run(ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(60.75f, 1e-2f);
    }

    // ---- Taunts: TauntMove (:633) ----

    [Test]
    public void ProcessMovement_InATauntThatCannotMove_WalksWithTheTauntSpeedZeroed()
    {
        // CanMoveDuringTaunt false: SetCurrentTauntMoveSpeed( 0 ), then the walk — friction 300 → 282.
        PredictedPlayer player = TfGameMovementConformanceTests.Standing() with
        {
            Conditions = Cond(CondTaunting),
            Velocity = new Vector3(300f, 0f, 0f),
            CurrentTauntMoveSpeed = 50f,
        };

        Run(ref player, Command()).ShouldBeTrue();

        player.Velocity.X.ShouldBe(282f, 1e-3f);
        player.CurrentTauntMoveSpeed.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_InAMovingTauntWithNoAcceleration_DrivesAtTheTauntSpeed()
    {
        // flMoveDir 450 / 450 = 1, speed 200 at once, SimpleSpline(1) = 1: forwardmove 200, maxspeed 200; 10 · 0.015 · 200.
        PredictedPlayer player = Taunting(new TauntMovement(false, 200f, 0f));

        Run(ref player, Command(forward: 450f));

        player.CurrentTauntMoveSpeed.ShouldBe(200f);
        player.Velocity.X.ShouldBe(30f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InAMovingTauntThatAccelerates_GainsFrametimeOverAccelerationOfTheSpeed()
    {
        // 0 + 0.015 / 0.5 · 200 = 6.
        PredictedPlayer player = Taunting(new TauntMovement(false, 200f, 0.5f));

        Run(ref player, Command(forward: 450f));

        player.CurrentTauntMoveSpeed.ShouldBe(6f, 1e-4f);
    }

    [Test]
    public void ProcessMovement_InAMovingTauntWhoseDataIsNotKnown_DeclinesToPredict()
    {
        PredictedPlayer player = Taunting(null);

        Run(ref player, Command(forward: 450f)).ShouldBeFalse();
    }

    // ---- Karts: VehicleMove (:738) ----

    [Test]
    public void ProcessMovement_AKartFromRestOnTheGas_ApproachesBySlowMovingAcceleration()
    {
        // Under the 300 slow-moving threshold: accel 500 · 1, Approach( 650, 0, 7.5 ) = 7.5; Bias( 0 ) drives nothing yet.
        PredictedPlayer player = Kart(0f);

        Run(ref player, Command(forward: 450f));

        player.CurrentTauntMoveSpeed.ShouldBe(7.5f, 1e-4f);
        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_AKartAtHalfSpeed_DrivesAtTheBiasedSpeed()
    {
        // At 325: accel 300, Approach( 650, 325, 4.5 ) = 329.5; Bias( 0.5, 0.7 ) · 650 = 455 forward; 10 · 0.015 · 455 = 68.25.
        PredictedPlayer player = Kart(325f);

        Run(ref player, Command(forward: 450f));

        player.CurrentTauntMoveSpeed.ShouldBe(329.5f, 1e-3f);
        player.Velocity.X.ShouldBe(68.25f, 2e-2f);
    }

    // ---- Parachutes: FullWalkMove's clamp (:2626) and GetAirSpeedCap (:2067) ----

    [Test]
    public void ProcessMovement_FallingFastUnderAParachute_ClampsTheFallAndDampsTheDrift()
    {
        // z = max( −400, −100 ); x 500 over 300 dampens to 300 + 200 / 3 − 10; then StartGravity and FinishGravity, −12.
        PredictedPlayer player = InTheOpen() with
        {
            Conditions = Cond(CondParachute),
            Velocity = new Vector3(500f, 0f, -400f),
        };

        Run(ref player, Command());

        player.Velocity.X.ShouldBe(300f + (200f / 3f) - 10f, 1e-3f);
        player.Velocity.Z.ShouldBe(-112f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_UnderAParachuteHoldingForward_HasTwoAndAHalfTimesTheAirControl()
    {
        // GetAirSpeedCap 30 · 2.5 = 75: AirAccelerate adds min( 10 · 300 · 0.015, 75 ) = 45, against 30 without.
        PredictedPlayer player = InTheOpen() with { Conditions = Cond(CondParachute) };

        Run(ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(45f, 1e-3f);
    }

    // ---- The grappling hook: GrapplingHookMove (:342) ----

    [Test]
    public void ProcessMovement_HookedFarAway_FliesAtTheGrappleSpeedTowardTheTarget()
    {
        // tf_grapplinghook_use_acceleration 0: velocity = desired · 750 / 1000; no input; StartGravity and FinishGravity −12.
        PredictedPlayer player = InTheOpen() with
        {
            GrapplingHook = new GrapplingTarget(new Vector3(1000f, 0f, 541f), new Vector3(1000f, 0f, 500f), IsPlayer: false),
        };

        Run(ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(750f, 1e-2f);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    private static PredictedPlayer InTheOpen() => TfGameMovementConformanceTests.Standing() with
    {
        Origin = new Vector3(0f, 0f, 500f),
        OnGround = false,
        ViewOffsetZ = 68f,
    };

    private static PredictedPlayer Stunned(int amount, int flags) => TfGameMovementConformanceTests.Standing() with
    {
        MaxSpeed = 400f,
        Conditions = Cond(CondStunned),
        StunActive = true,
        StunAmount = amount,
        StunFlags = flags,
        StunExpireTime = 100f,
    };

    private static PredictedPlayer Taunting(TauntMovement? movement) => TfGameMovementConformanceTests.Standing() with
    {
        Conditions = Cond(CondTaunting),
        AllowMoveDuringTaunt = true,
        TauntMovement = movement,
    };

    private static PredictedPlayer Kart(float speed) => TfGameMovementConformanceTests.Standing() with
    {
        Conditions = Cond(CondKart),
        CurrentTauntMoveSpeed = speed,
    };

    private static PlayerConditions Cond(int condition) => (condition / 32) switch
    {
        0 => new PlayerConditions(1 << condition, 0, 0, 0, 0),
        1 => new PlayerConditions(0, 1 << (condition - 32), 0, 0, 0),
        2 => new PlayerConditions(0, 0, 1 << (condition - 64), 0, 0),
        3 => new PlayerConditions(0, 0, 0, 1 << (condition - 96), 0),
        _ => new PlayerConditions(0, 0, 0, 0, 1 << (condition - 128)),
    };

    private static Func<Vector3, int> Water(float surface) => point => point.Z < surface ? ContentsWater : 0;

    private static PlayerTraceRay Recording(List<int> masks)
    {
        PlayerTraceRay floor = TfGameMovementConformanceTests.Floor();

        return (start, end, mins, maxs, mask) =>
        {
            masks.Add(mask);
            return floor(start, end, mins, maxs, mask);
        };
    }

    private static bool Run(
        ref PredictedPlayer player, UserCommand command, Func<Vector3, int>? contents = null, PlayerTraceRay? trace = null) =>
        new TfGameMovement(trace ?? TfGameMovementConformanceTests.Floor(), MovementConVars.Defaults)
        {
            PointContents = contents ?? Water(-1000f),
        }.ProcessMovement(ref player, command, Tick, first: true);

    private static UserCommand Command(float forward = 0f, uint buttons = 0) =>
        TfGameMovementConformanceTests.Command(forward, buttons);
}
