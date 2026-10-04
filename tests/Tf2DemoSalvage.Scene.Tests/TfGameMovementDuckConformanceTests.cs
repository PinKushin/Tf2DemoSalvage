using System.Numerics;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CTFGameMovement::Duck</c> — <c>DuckOverrides</c>, <c>OnDuck</c>, <c>OnUnDuck</c> — with the base <c>FinishDuck</c>,
/// <c>FinishUnDuck</c>, <c>CanUnduck</c> and <c>FixPlayerCrouchStuck</c>, each value computed by hand from
/// <c>tf_gamemovement.cpp:3170-3370</c> and <c>gamemovement.cpp</c>.
/// </summary>
public sealed class TfGameMovementDuckConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InDuck = 1 << 2;

    [Test]
    public void ProcessMovement_PressingDuckInTheAirAfterTwoAirDucks_IsRefused()
    {
        // DuckOverrides (tf_gamemovement.cpp:3196-3199): tf_clamp_airducks and m_nAirDucked ≥ TF_MAX_AIR_DUCKS 2 off the ground.
        PredictedPlayer player = Airborne() with { AirDucked = 2 };

        Run(ref player, Move(InDuck));

        player.FlDucking.ShouldBeFalse();
        player.Ducking.ShouldBeFalse();
    }

    [Test]
    public void ProcessMovement_PressingDuckOnTheGroundAfterTwoAirDucks_StartsTheDuck()
    {
        // The control: the air-duck count binds only off the ground; on it a press starts the 1000 ms transition.
        PredictedPlayer player = Standing() with { AirDucked = 2 };

        Run(ref player, Move(InDuck));

        player.Ducking.ShouldBeTrue();
        player.DuckTime.ShouldBe(1000f);
    }

    [Test]
    public void ProcessMovement_PressingDuckOnTheGroundEyesDeep_IsRefused()
    {
        // DuckOverrides (:3184-3188): WL_Eyes clears IN_DUCK even on the ground.
        PredictedPlayer player = Standing() with { WaterLevel = 3 };

        Run(ref player, Move(InDuck));

        player.Ducking.ShouldBeFalse();
    }

    [Test]
    public void ProcessMovement_ReleasingDuckOnTheGround_StartsTheDuckTimer()
    {
        // OnUnDuck (:3293-3301): a release sets m_flDuckTimer to curtime + TF_TIME_TO_DUCK 0.3 and, on the ground, counts no
        // air duck.
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true };

        Run(ref player, Move(), oldButtons: InDuck);

        player.DuckTimer.ShouldBe(0.3f, 1e-6f);
        player.AirDucked.ShouldBe(0);
    }

    [Test]
    public void ProcessMovement_ReleasingDuckInTheAir_CountsAnAirDuckAndDropsTheOriginByTheHullDifference()
    {
        // OnUnDuck in the air (:3299, :3348): m_nAirDucked counts it, and FinishUnDuck moves the origin down by the 20 the
        // hulls differ by (gamemovement.cpp FinishUnDuck); a tick of falling at 6 then takes 0.09.
        PredictedPlayer player = Airborne() with { Ducked = true, FlDucking = true };

        Run(ref player, Move(), oldButtons: InDuck);

        player.AirDucked.ShouldBe(1);
        player.DuckTimer.ShouldBe(0.3f, 1e-6f);
        player.Ducked.ShouldBeFalse();
        player.Origin.Z.ShouldBe(480f - 0.09f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_PressingDuckInTheAir_RaisesTheOriginByTheHullDifference()
    {
        // FinishDuck in the air (gamemovement.cpp FinishDuck): the origin goes up 20 so the head stays put; then −0.09.
        PredictedPlayer player = Airborne();

        Run(ref player, Move(InDuck));

        player.FlDucking.ShouldBeTrue();
        player.Origin.Z.ShouldBe(520f - 0.09f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_ReleasingDuckHalfwayDown_UnducksFromTheMatchingPoint()
    {
        // OnUnDuck (:3316-3327): 1000 − 885 = 115 ms into a duck maps to 115 / 200 · 200 of the unduck, so m_flDucktime is
        // 1000 − 200 + 115 = 915; 85 ms of it is under TIME_TO_UNDUCK, so he is still in transition.
        PredictedPlayer player = Standing() with { Ducking = true, DuckTime = 900f };

        Run(ref player, Move(), oldButtons: InDuck);

        player.DuckTime.ShouldBe(915f, 1e-3f);
        player.Ducking.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_DuckedUnderALowCeilingWithDuckLetGo_StaysFullyDucked()
    {
        // OnUnDuck (:3353-3363): CanUnduck fails under the ceiling at 70, and m_flDucktime 485 ≠ 1000, so he is set fully
        // ducked: ducktime 1000, FL_DUCKING and m_bDucked, not in transition.
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true, Ducking = true, DuckTime = 500f };

        Run(ref player, Move(), trace: TfGameMovementWalkConformanceTests.LowCeiling());

        player.DuckTime.ShouldBe(1000f);
        player.Ducked.ShouldBeTrue();
        player.FlDucking.ShouldBeTrue();
        player.Ducking.ShouldBeFalse();
    }

    [Test]
    public void ProcessMovement_DuckedInTheOpenWithDuckLetGo_FinishesTheUnduck()
    {
        // The control: with room, 515 ms past TIME_TO_UNDUCK finishes it (FinishUnDuck clears every duck flag and the time).
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true, InDuckJump = true, DuckTime = 500f };

        Run(ref player, Move());

        player.Ducked.ShouldBeFalse();
        player.FlDucking.ShouldBeFalse();
        player.Ducking.ShouldBeFalse();
        player.InDuckJump.ShouldBeFalse();
        player.DuckTime.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_DuckingInTheAirIntoALedge_IsLiftedClearOfIt()
    {
        // FixPlayerCrouchStuck( true ) (gamemovement.cpp): raised to 520 the ducked hull still overlaps a ledge up to 530,
        // so it is lifted a unit at a time to 530, where it lands on the ledge.
        PredictedPlayer player = Airborne();
        PlayerTraceRay ledge = BoxWorld.Of((new Vector3(-1e4f, -1e4f, 515f), new Vector3(1e4f, 1e4f, 530f)));

        Run(ref player, Move(InDuck), trace: ledge);

        player.Origin.Z.ShouldBe(530f, 1e-4f);
        player.OnGround.ShouldBeTrue();
    }

    private static PredictedPlayer Airborne() => Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

    private static UserCommand Move(uint buttons = 0) => Command(buttons: buttons);

    private static void Run(ref PredictedPlayer player, UserCommand command, uint oldButtons = 0, PlayerTraceRay? trace = null)
    {
        player.OldButtons = oldButtons;

        new TfGameMovement(trace ?? Floor(), MovementConVars.Defaults, maxClients: 24)
            .ProcessMovement(ref player, command, Tick, first: true, commandNumber: 1).ShouldBeTrue();
    }
}
