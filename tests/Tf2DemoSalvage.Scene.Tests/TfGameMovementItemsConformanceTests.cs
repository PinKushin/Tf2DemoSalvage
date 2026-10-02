using System;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Scene.Tests.TfGameMovementConformanceTests;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What <c>CTFGameMovement</c> reads off the player beyond his networked state: item attributes, the surface under his feet,
/// <c>m_flGravity</c>, and <c>CanJump</c>/<c>CanDuck</c>/<c>CanAirDash</c> (B450, D205). Each expectation computed by hand from the SDK.
/// </summary>
/// <remarks>
/// The world and the defaults are <see cref="TfGameMovementConformanceTests"/>'s: gravity 800, a 0.015 s tick, so a jump from the
/// ground leaves its impulse less three half-ticks of gravity, 18.
/// </remarks>
public sealed class TfGameMovementItemsConformanceTests
{
    private const float Tick = 0.015f;
    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;

    [Test]
    public void ProcessMovement_JumpingWithModJumpHeight_ScalesTheImpulse()
    {
        // tf_gamemovement.cpp:1294 CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( m_pTFPlayer, flJumpMod, mod_jump_height ); 289 · 1.5 − 18.
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InJump), Player("mod_jump_height", 1.5f));

        player.Velocity.Z.ShouldBe(415.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingWithModJumpHeightFromWeapon_ScalesTheImpulseByTheActiveWeapon()
    {
        // :1296-1300, the weapon-restricted version, hooked on the active weapon: 289 · 0.8 − 18.
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InJump), Weapon("mod_jump_height_from_weapon", 0.8f));

        player.Velocity.Z.ShouldBe(213.2f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingCarryingTheAgilityRune_JumpsOnePointEightTimesHigher()
    {
        // :1310-1313 RUNE_AGILITY, TF_COND_RUNE_AGILITY 97 (tf_shareddefs.h:787): 289 · 1.8 − 18.
        PredictedPlayer player = Standing() with { Conditions = new PlayerConditions(0, 0, 0, 1 << (97 - 96), 0) };

        Run(ref player, Command(buttons: InJump), MovementItems.None);

        player.Velocity.Z.ShouldBe(502.2f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_JumpingOffSlipperySlime_ScalesTheImpulseByItsJumpFactor()
    {
        // :1279-1282 flGroundFactor = m_pSurfaceData->game.jumpFactor, set by CategorizeGroundSurface on landing: 289 · 0.7 − 18.
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InJump), MovementItems.None, Surface(friction: 0.8f, jumpFactor: 0.7f));

        player.Velocity.Z.ShouldBe(184.3f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_OnASurfaceWithMaxSpeedFactor_ScalesTheMaxSpeed()
    {
        // CheckParameters (gamemovement.cpp:1002-1014): m_flMaxSpeed 300 · 0.5; Accelerate adds 10 · 0.015 · 150. The factor is the
        // surface already under him: CheckParameters runs before this command's CategorizePosition.
        PredictedPlayer player = Standing() with { Surface = Surface(friction: 0.8f, maxSpeedFactor: 0.5f)(default) };

        Run(ref player, Command(forward: 450f), MovementItems.None);

        player.Velocity.X.ShouldBe(22.5f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_StandingOnALowFrictionSurface_LosesItsScaledFriction()
    {
        // CategorizeGroundSurface (gamemovement.cpp:919-930): m_surfaceFriction = 0.1 · 1.25; Friction drops
        // 300 · 4 · 0.125 · 0.015 = 2.25.
        PredictedPlayer player = Standing() with { Velocity = new Vector3(300f, 0f, 0f) };

        Run(ref player, Command(), MovementItems.None, Surface(friction: 0.1f));

        player.Velocity.X.ShouldBe(297.75f, 1e-3f);
        player.SurfaceFriction.ShouldBe(0.125f);
    }

    [Test]
    public void ProcessMovement_InTheAirWithModAirControl_RaisesTheAirSpeedCap()
    {
        // GetAirSpeedCap (tf_gamemovement.cpp:2081-2094): 30 · 1.2 = 36, under AirAccelerate's 45.
        PredictedPlayer player = Airborne();

        Run(ref player, Command(forward: 450f), Player("mod_air_control", 1.2f));

        player.Velocity.X.ShouldBe(36f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_BlastJumpingWithModAirControlBlastJump_ScalesTheCapAgain()
    {
        // :2084-2087, only while TF_COND_BLASTJUMPING (81, tf_shareddefs.h:771): 30 · 0.5.
        PredictedPlayer player = Airborne() with { Conditions = new PlayerConditions(0, 0, 1 << (81 - 64), 0, 0) };

        Run(ref player, Command(forward: 450f), Player("mod_air_control_blast_jump", 0.5f));

        player.Velocity.X.ShouldBe(15f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_NotBlastJumping_IgnoresModAirControlBlastJump()
    {
        PredictedPlayer player = Airborne();

        Run(ref player, Command(forward: 450f), Player("mod_air_control_blast_jump", 0.5f));

        player.Velocity.X.ShouldBe(30f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutWithAirDashCountTwo_DashesASecondTime()
    {
        // CanAirDash (tf_player_shared.cpp:12860-12865): tf_scout_air_dash_count 1, hooked by air_dash_count on the active weapon.
        PredictedPlayer player = AirborneScout() with { AirDash = 1 };

        Run(ref player, Command(forward: 450f, buttons: InJump), Weapon("air_dash_count", 2f));

        player.AirDash.ShouldBe(2);
        player.Velocity.Z.ShouldBe(268.3281572999747f - 6f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutWithDoubleJumpDisabled_DoesNotDash()
    {
        // :12875-12878 set_scout_doublejump_disabled, hooked on the player.
        PredictedPlayer player = AirborneScout();

        Run(ref player, Command(forward: 450f, buttons: InJump), Player("set_scout_doublejump_disabled", 0f, add: 1f));

        player.AirDash.ShouldBe(0);
        player.Velocity.Z.ShouldBe(-12f, 1e-3f);
    }

    [Test]
    public void ProcessMovement_AScoutHypedBySodaPopper_DashesUpToFiveTimes()
    {
        // :12852-12858 TF_COND_SODAPOPPER_HYPE (36): GetAirDash() < 5.
        PredictedPlayer player = AirborneScout() with { AirDash = 4, Conditions = new PlayerConditions(0, 1 << (36 - 32), 0, 0, 0) };

        Run(ref player, Command(forward: 450f, buttons: InJump), MovementItems.None);

        player.AirDash.ShouldBe(5);
    }

    [Test]
    public void ProcessMovement_ANonScoutWithTheHalloweenSpeedBoost_AirDashes()
    {
        // :12845-12846 TF_COND_HALLOWEEN_SPEED_BOOST (72) answers true before the class is asked.
        PredictedPlayer player = Airborne() with { Conditions = new PlayerConditions(0, 0, 1 << (72 - 64), 0, 0) };

        Run(ref player, Command(forward: 450f, buttons: InJump), MovementItems.None);

        player.AirDash.ShouldBe(1);
    }

    [Test]
    public void ProcessMovement_WithNoJump_StaysOnTheGround()
    {
        // CTFPlayer::CanJump (tf_player_shared.cpp:12289-12292): CALL_ATTRIB_HOOK_INT( iNoJump, no_jump ).
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InJump), Player("no_jump", 0f, add: 1f));

        player.OnGround.ShouldBeTrue();
        player.Velocity.Z.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_DrawingABow_CannotJump()
    {
        // :12282-12287 OwnerCanJump, false for CTFCompoundBow while charging (tf_weapon_compound_bow.cpp:657-660).
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InJump), MovementItems.None with { OwnerCanJump = false });

        player.OnGround.ShouldBeTrue();
    }

    [Test]
    public void ProcessMovement_WithNoDuck_DoesNotStartADuck()
    {
        // CTFGameMovement::Duck (tf_gamemovement.cpp:3434) asks CanDuck (tf_player_shared.cpp:12298-12304) before OnDuck.
        PredictedPlayer player = Standing();

        Run(ref player, Command(buttons: InDuck), Player("no_duck", 0f, add: 1f));

        player.Ducking.ShouldBeFalse();
        player.DuckTime.ShouldBe(0f);
    }

    [Test]
    public void ProcessMovement_WithHalfGravity_FallsHalfAsFast()
    {
        // StartGravity and FinishGravity (gamemovement.cpp:1250-1257, :1689-1695): ent_gravity = GetGravity() when nonzero.
        PredictedPlayer player = Airborne() with { Gravity = 0.5f };

        Run(ref player, Command(), MovementItems.None);

        player.Velocity.Z.ShouldBe(-6f, 1e-3f);
    }

    [Test]
    public void ItemsOf_ARecorderDrawingABow_CannotJump()
    {
        // OwnerCanJump: GetInternalChargeBeginTime() == 0, the bow's networked m_flChargeBeginTime.
        SceneItem bow = new(30, "CTFCompoundBow", 56, new EconAttributeWire([], [], false), IsWeapon: true) { ChargeBeginTime = 12.5f };
        ScenePlayer recorder = new(1, 0f, 0f, 0f, 2, 125, 2, ActiveWeapon: 30) { Items = [bow] };

        RecorderPrediction.ItemsOf(recorder, hooks: null).OwnerCanJump.ShouldBeFalse();
        RecorderPrediction.ItemsOf(recorder with { Items = [bow with { ChargeBeginTime = 0f }] }, hooks: null).OwnerCanJump.ShouldBeTrue();
    }

    private static PredictedPlayer Airborne() => Standing() with { Origin = new Vector3(0f, 0f, 500f), OnGround = false };

    private static PredictedPlayer AirborneScout() => Airborne() with { PlayerClass = 1, MaxSpeed = 400f };

    private static void Run(
        ref PredictedPlayer player, Core.Container.UserCommand command, MovementItems items, Func<Content.Bsp.BspTrace, VphysicsSurface?>? ground = null) =>
        new TfGameMovement(Floor(), MovementConVars.Defaults) { Items = items, GroundSurface = ground }
            .ProcessMovement(ref player, command, Tick, first: true).ShouldBeTrue();

    /// <summary>A player hook that multiplies one attribute class, or adds to it.</summary>
    private static MovementItems Player(string attribute, float multiply, float add = 0f) =>
        MovementItems.None with { OnPlayer = (name, value) => name == attribute ? (value * multiply) + add : value };

    private static MovementItems Weapon(string attribute, float multiply) =>
        MovementItems.None with { OnActiveWeapon = (name, value) => name == attribute ? value * multiply : value };

    private static Func<Content.Bsp.BspTrace, VphysicsSurface?> Surface(float friction, float jumpFactor = 1f, float maxSpeedFactor = 1f)
    {
        VphysicsSurface surface = new("test", new SurfacePhysicsParams(friction, 0f, 0f, 0f, 0f), false)
        {
            JumpFactor = jumpFactor,
            MaxSpeedFactor = maxSpeedFactor,
        };

        return _ => surface;
    }
}
