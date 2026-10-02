using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The last of B450's movement defaults, each now read: <c>GetMovementForwardPull</c>, the Atomizer's deploy test,
/// <c>hype_resets_on_jump</c>, <c>IsLoser</c>'s duck crop and <c>tf_clamp_airducks</c>. Every expectation computed by hand from
/// the SDK, on <see cref="TfGameMovementConformanceTests"/>'s world: gravity 800 and a 0.015 s tick, so a command in the air
/// loses two half-ticks of gravity, 12.
/// </summary>
public sealed class TfGameMovementRemainderConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;

    // ---- GetMovementForwardPull (tf_player_shared.cpp:10767-10779) ----

    [Test]
    public void ProcessMovement_WalkingWhileTheWeaponFiresWithAForwardPull_AddsThePullAlongTheView()
    {
        // WalkMove (tf_gamemovement.cpp:1817-1828): from rest nothing accelerates, then vecForward · 100.
        PredictedPlayer player = Standing() with { ActiveWeaponFiring = true };

        Run(ref player, Command(), Pull(100f));

        player.Velocity.X.ShouldBe(100f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_WalkingWithAForwardPullButNotFiring_AddsNothing()
    {
        // :10770 pWpn->IsFiring(): only CTFFlameThrower overrides it (tf_weapon_flamethrower.h:92), FT_STATE_FIRING.
        PredictedPlayer player = Standing();

        Run(ref player, Command(), Pull(100f));

        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_WalkingPulledPastTheMaxSpeed_IsCutBackToIt()
    {
        // :1823-1827: 290 less friction's 290 · 4 · 0.015 = 272.6, plus 100 is 372.6 > 300, normalised to 300.
        PredictedPlayer player = Standing() with { ActiveWeaponFiring = true, Velocity = new Vector3(290f, 0f, 0f) };

        Run(ref player, Command(), Pull(100f));

        player.Velocity.X.ShouldBe(300f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_InTheAirPulledPastTheMaxSpeed_KeepsItsFallSpeed()
    {
        // AirMove (:2169-2183): 290 + 100 cut to 300 in the plane, z put back; gravity −6 either side.
        PredictedPlayer player = Airborne() with { ActiveWeaponFiring = true, Velocity = new Vector3(290f, 0f, 0f) };

        Run(ref player, Command(), Pull(100f));

        player.Velocity.X.ShouldBe(300f, 1e-3f);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    // ---- CanAirDash's Atomizer test (tf_player_shared.cpp:12867-12873) ----

    [Test]
    public void ProcessMovement_AThirdJumpWithin07SecondsOfDeploying_IsRefused()
    {
        // iDashCount 2, GetAirDash() == 1, curtime 0 − deploy −0.5 = 0.5 < 0.7: no dash, so only gravity.
        PredictedPlayer player = AirborneScout() with { AirDash = 1, LastDeployTime = -0.5f };

        Run(ref player, Command(forward: 450f, buttons: InJump), DashCount(2f));

        player.AirDash.ShouldBe(1);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AThirdJump07SecondsAfterDeploying_Dashes()
    {
        PredictedPlayer player = AirborneScout() with { AirDash = 1, LastDeployTime = -0.8f };

        Run(ref player, Command(forward: 450f, buttons: InJump), DashCount(2f));

        player.AirDash.ShouldBe(2);
    }

    [Test]
    public void ProcessMovement_ASecondJumpRightAfterDeploying_Dashes()
    {
        // The test is the third jump's alone: GetAirDash() == 1.
        PredictedPlayer player = AirborneScout() with { LastDeployTime = 0f };

        Run(ref player, Command(forward: 450f, buttons: InJump), DashCount(2f));

        player.AirDash.ShouldBe(1);
    }

    // ---- hype_resets_on_jump (tf_gamemovement.cpp:1007-1016) ----

    [Test]
    public void ProcessMovement_AnAirDashWithHypeResetsOnJump_LosesHypeAndTheBabyFacesSpeed()
    {
        // SetScoutHypeMeter( 60 − 25 ) (tf_player_shared.cpp:14124-14129), then TeamFortress_SetSpeed: the hype term is
        // RemapValClamped( hype, 0, 100, 1, 1.45 ) (:11085), so 508 / 1.27 · 1.1575 = 463.
        PredictedPlayer player = AirborneScout() with { MaxSpeed = 508f, HypeMeter = 60f, OwnsPepBrawlerBlaster = true };

        Run(ref player, Command(forward: 450f, buttons: InJump), HypeResets(25f));

        player.AirDash.ShouldBe(1);
        player.HypeMeter.ShouldBe(35f, 1e-4f);
        player.MaxSpeed.ShouldBe(463f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AnAirDashWithHypeResetsOnJumpAndNoBabyFace_LosesHypeOnly()
    {
        PredictedPlayer player = AirborneScout() with { HypeMeter = 10f };

        Run(ref player, Command(forward: 450f, buttons: InJump), HypeResets(25f));

        player.HypeMeter.ShouldBe(0f, "Clamp( val, 0, 100 )");
        player.MaxSpeed.ShouldBe(400f);
    }

    [Test]
    public void ProcessMovement_AnAirDashWhileHypeBuffed_KeepsTheHype()
    {
        // :14126 IsHypeBuffed(): TF_COND_SODAPOPPER_HYPE (36).
        PredictedPlayer player = AirborneScout() with
        {
            HypeMeter = 60f, OwnsPepBrawlerBlaster = true, Conditions = new PlayerConditions(0, 1 << (36 - 32), 0, 0, 0),
        };

        Run(ref player, Command(forward: 450f, buttons: InJump), HypeResets(25f));

        player.HypeMeter.ShouldBe(60f);
    }

    [Test]
    public void ProcessMovement_AnAirDashResettingHypeUnderASpeedBoost_IsDeclined()
    {
        // TF_COND_SPEED_BOOST (32, tf_shareddefs.h:722) is added to the max speed under GAME_DLL only (:10918-10928), so the
        // client's TeamFortress_SetSpeed recomputes a speed the networked one does not divide back out of.
        PredictedPlayer player = AirborneScout() with { HypeMeter = 60f, Conditions = new PlayerConditions(0, 1, 0, 0, 0) };

        new TfGameMovement(Floor(), MovementConVars.Defaults, maxClients: 24) { Items = HypeResets(25f) }
            .ProcessMovement(ref player, Command(forward: 450f, buttons: InJump), Tick, first: true, commandNumber: 1)
            .ShouldBeFalse();
    }

    // ---- IsLoser's duck crop (tf_gamemovement.cpp:3371-3384) ----

    [Test]
    public void ProcessMovement_ALoserDucking_CannotMove()
    {
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true, IsLoser = true };

        Run(ref player, Command(forward: 450f), MovementItems.None);

        player.Velocity.X.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_NotALoserDucking_MovesAtAThird()
    {
        // The control: 450 clamped to 300, cropped to 100, Accelerate 10 · 0.015 · 100.
        PredictedPlayer player = Standing() with { Ducked = true, FlDucking = true };

        Run(ref player, Command(forward: 450f), MovementItems.None);

        player.Velocity.X.ShouldBe(15f, 1e-3f);
    }

    // ---- tf_clamp_airducks (tf_gamemovement.cpp:49, :3190-3191) ----

    [Test]
    public void ProcessMovement_TheDuckTimerRunningWithClampAirDucksOff_Ducks()
    {
        PredictedPlayer player = Standing() with { DuckTimer = 1f };

        new TfGameMovement(Floor(), MovementConVars.Defaults with { ClampAirDucks = false }, maxClients: 24)
            .ProcessMovement(ref player, Command(buttons: InDuck), Tick, first: true, commandNumber: 1).ShouldBeTrue();

        player.Ducking.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_TheDuckTimerRunningWithClampAirDucksOn_DoesNotDuck()
    {
        PredictedPlayer player = Standing() with { DuckTimer = 1f };

        Run(ref player, Command(buttons: InDuck), MovementItems.None);

        player.Ducking.ShouldBeFalse();
        MovementConVars.Defaults.ClampAirDucks.ShouldBeTrue("its declared default, \"1\"");
    }

    private static PredictedPlayer Airborne() => Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

    private static PredictedPlayer AirborneScout() => Airborne() with { PlayerClass = 1, MaxSpeed = 400f };

    private static void Run(ref PredictedPlayer player, Core.Container.UserCommand command, MovementItems items) =>
        new TfGameMovement(Floor(), MovementConVars.Defaults, maxClients: 24) { Items = items }
            .ProcessMovement(ref player, command, Tick, first: true, commandNumber: 1).ShouldBeTrue();

    /// <summary>An active weapon whose <c>firing_forward_pull</c> is <paramref name="pull"/>.</summary>
    private static MovementItems Pull(float pull) =>
        MovementItems.None with { OnActiveWeapon = (name, value) => name == "firing_forward_pull" ? value + pull : value };

    /// <summary>An active weapon whose <c>air_dash_count</c> is multiplied — the Atomizer's 2.</summary>
    private static MovementItems DashCount(float multiply) =>
        MovementItems.None with { OnActiveWeapon = (name, value) => name == "air_dash_count" ? value * multiply : value };

    private static MovementItems HypeResets(float amount) =>
        MovementItems.None with { OnPlayer = (name, value) => name == "hype_resets_on_jump" ? value + amount : value };
}
