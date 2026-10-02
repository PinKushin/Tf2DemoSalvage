using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// One usercmd through <c>CTFGameMovement::ProcessMovement</c>, each expectation computed from the SDK by hand.
/// </summary>
/// <remarks>
/// <c>src/game/shared/gamemovement.cpp</c> and <c>src/game/shared/tf/tf_gamemovement.cpp</c>, with the default
/// <c>sv_*</c> values of <c>movevars_shared.cpp:37-100</c>: gravity 800, friction 4, stopspeed 100, accelerate and
/// airaccelerate 10. A tick is 0.015 s, so half a tick of gravity (<c>StartGravity</c>, <c>FinishGravity</c>) is 6.
/// The world is one half-space; a player resting on a floor sits <c>DIST_EPSILON</c> above it, where a trace leaves him.
/// </remarks>
public sealed class TfGameMovementConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;

    [Test]
    public void ProcessMovement_OnTheGroundWithNoInput_LosesOneTickOfFriction()
    {
        // Friction (gamemovement.cpp:1610): drop = max(speed, stopspeed) · friction · dt = 300 · 4 · 0.015 = 18.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(300f, 0f, 0f) };

        Run(Floor(), ref player, Command()).ShouldBeTrue();

        player.Velocity.X.ShouldBe(282f, 1e-3f);
        player.Velocity.Z.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_FromRestHoldingForward_AcceleratesByAccelTimesWishSpeed()
    {
        // CheckParameters clamps forwardmove 450 to the 300 max; Accelerate adds 10 · 0.015 · 300 = 45.
        PredictedPlayer player = Standing();

        Run(Floor(), ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(45f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingFromTheGround_Leaves289LessTwoHalfTicksOfGravity()
    {
        // StartGravity −6, CheckJumpButton +289 and FinishGravity −6, then the end-of-move FinishGravity −6: 271.
        PredictedPlayer player = Standing();

        Run(Floor(), ref player, Command(buttons: InJump));

        player.Velocity.Z.ShouldBe(271f, 1e-3f);
        player.OnGround.ShouldBeFalse();
    }

    [Test]
    public void ProcessMovement_FallingInTheOpen_GainsOneTickOfGravity()
    {
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

        Run(Floor(), ref player, Command());

        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InTheAirHoldingForward_IsCappedAtThirtyUnitsOfAirSpeed()
    {
        // GetAirSpeedCap is 30 (gamemovement.h:104): AirAccelerate adds min(45, 30 − 0).
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

        Run(Floor(), ref player, Command(forward: 450f));

        player.Velocity.X.ShouldBe(30f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_FlyingIntoAWall_LosesTheComponentIntoIt()
    {
        // TryPlayerMove's first plane in the air clips with an overbounce of 1 + sv_bounce · (1 − friction) = 1.
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(50f, 0f, 500f),
            Velocity = new Vector3(2000f, 0f, 0f),
            OnGround = false,
        };

        Run(Wall(), ref player, Command());

        player.Velocity.X.ShouldBe(0f, 1e-3f);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
        player.Origin.X.ShouldBeLessThan(100f - 24f);
    }

    [Test]
    public void ProcessMovement_AScoutJumpingInTheAir_AirDashesAlongHisWish()
    {
        // AirDash (tf_gamemovement.cpp:992): velocity = wish (forward · 400) plus 268.33 up, then FinishGravity −6.
        PredictedPlayer player = Standing() with
        {
            Origin = new Vector3(0f, 0f, 500f),
            OnGround = false,
            PlayerClass = 1,
            MaxSpeed = 400f,
        };

        Run(Floor(), ref player, Command(forward: 450f, buttons: InJump));

        player.Velocity.X.ShouldBe(400f, 1e-3f);
        player.Velocity.Z.ShouldBe(268.3281572999747f - 6f, 1e-3f);
        player.AirDash.ShouldBe(1);
    }

    [Test]
    public void ProcessMovement_ASecondAirJumpForANonScout_IsIgnored()
    {
        PredictedPlayer player = Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

        Run(Floor(), ref player, Command(buttons: InJump));

        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_DuckedOnTheGround_CropsTheMoveToAThird()
    {
        // HandleDuckingSpeedCrop (gamemovement.cpp:4306): 300 · 0.333 = 100; Accelerate adds 10 · 0.015 · 100.
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true };

        Run(Floor(), ref player, Command(forward: 450f, buttons: InDuck));

        player.Velocity.X.ShouldBe(15f, 1e-2f);
    }

    [Test]
    public void ProcessMovement_ShieldCharging_DrivesForwardAtTheChargeSpeed()
    {
        // ChargeMove (tf_gamemovement.cpp:484): forwardmove 750 whatever was pressed; Accelerate adds 10 · 0.015 · 750.
        PredictedPlayer player = Standing() with { MaxSpeed = 750f, Conditions = new Core.Scene.PlayerConditions(1 << 17, 0, 0, 0, 0) };

        Run(Floor(), ref player, Command());

        player.Velocity.X.ShouldBe(112.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WithNoWorld_DeclinesToPredict()
    {
        PredictedPlayer player = Standing();

        Run((_, _, _, _, _) => null, ref player, Command()).ShouldBeFalse();
    }

    [Test]
    public void ProcessMovement_Taunting_DeclinesToPredict()
    {
        // TauntMove and its kin are not ported; the networked velocity stands.
        PredictedPlayer player = Standing() with { Conditions = new Core.Scene.PlayerConditions(1 << 7, 0, 0, 0, 0) };

        Run(Floor(), ref player, Command()).ShouldBeFalse();
    }

    private static bool Run(PlayerTraceRay trace, ref PredictedPlayer player, UserCommand command) =>
        new TfGameMovement(trace, MovementConVars.Defaults).ProcessMovement(ref player, command, Tick, first: true);

    internal static PredictedPlayer Standing() => new()
    {
        Origin = new Vector3(0f, 0f, 0.03125f),
        OnGround = true,
        MaxSpeed = 300f,
        PlayerClass = 3,
    };

    internal static UserCommand Command(float forward = 0f, uint buttons = 0) =>
        new(1, 1, 0f, 0f, 0f, forward, 0f, 0f, buttons, 0, 0, 0, 0, 0, 0);

    /// <summary>A floor: everything below z = 0 is solid.</summary>
    internal static PlayerTraceRay Floor() => Through(HalfSpace(0f, 0f, 1f, 0f));

    /// <summary>A wall with no floor: everything at x ≥ 100 is solid.</summary>
    private static PlayerTraceRay Wall() => Through(HalfSpace(-1f, 0f, 0f, -100f));

    private static PlayerTraceRay Through(BspLeafTree tree) => (start, end, mins, maxs, mask) =>
    {
        Vector3 centre = (mins + maxs) * 0.5f;
        Vector3 extents = (maxs - mins) * 0.5f;
        Vector3 from = start + centre;
        Vector3 to = end + centre;

        return tree.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (extents.X, extents.Y, extents.Z), 0, mask);
    };

    private static BspLeafTree HalfSpace(float nx, float ny, float nz, float distance)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), nx);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(4), ny);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), nz);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(12), distance);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -2);

        byte[] leaves = new byte[64];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), 1);

        byte[] brushes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(8), 1);

        return BspLeafTree.FromCollisionLumps(node, plane, leaves, new byte[2], brushes, new byte[8]);
    }
}

/// <summary>Which recorded usercmds prediction re-runs at a moment of playback (<c>CPrediction::PerformPrediction</c>).</summary>
public sealed class RecorderPredictionPendingTests
{
    [Test]
    public void Pending_AfterAPacket_IsEveryCommandPastItsAcknowledgementReadByNow()
    {
        // prediction.cpp:1611-1679: from incoming_acknowledged + 1 to the last command made, in order.
        List<RecordedUserCommand> commands =
        [
            Recorded(100, 10), Recorded(101, 11), Recorded(102, 12), Recorded(103, 13),
        ];

        RecorderPrediction.Pending(commands, acknowledged: 10, tick: 102).ShouldBe([commands[1], commands[2]]);
    }

    [Test]
    public void Pending_WhenThePacketAcknowledgesEverything_IsEmpty() =>
        RecorderPrediction.Pending([Recorded(100, 10)], acknowledged: 10, tick: 100).ShouldBeEmpty();

    private static RecordedUserCommand Recorded(int tick, int sequence) =>
        new(tick, sequence, new UserCommand(sequence, tick, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0, 0, 0, 0, 0, 0));
}
