using System;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A point-of-view recorder's observer views, against the engine's own arithmetic (B745).</summary>
/// <remarks>
/// Each expectation is the quoted function worked by hand: <c>C_TFPlayer::CalcDeathCamView</c> (<c>c_tf_player.cpp:7177</c>),
/// <c>C_BasePlayer::CalcFreezeCamView</c> (<c>c_baseplayer.cpp:1652</c>), <c>CalcChaseCamView</c> (<c>:1487</c>),
/// <c>CalcInEyeCamView</c> (<c>:1732</c>) and <c>CalcRoamingView</c> (<c>:1612</c>), with TF2's view vectors
/// (<c>tf_gamerules.cpp:1306</c>): view 72, duck view 45, hull 82 tall, dead view 14.
/// </remarks>
public sealed class ObserverCameraConformanceTests
{
    private const float Tolerance = 0.001f;

    private const int Recorder = 1;

    private const int Killer = 2;

    /// <summary>The recorder standing at the world origin with a soldier's eye, looking along +Y.</summary>
    private static readonly ObserverSubject Self = new(
        Recorder, Vector3.Zero, ObserverSubjectKind.Player, IsAlive: false, Ducking: false, ModelScale: 1f, (0f, 90f, 0f), 68f);

    /// <summary>A living killer 500 units along +X; another player, so his view offset never arrived.</summary>
    private static readonly ObserverSubject Shooter = new(
        Killer, new Vector3(500f, 0f, 0f), ObserverSubjectKind.Player, IsAlive: true, Ducking: false, ModelScale: 1f, (10f, 20f, 0f));

    [Test]
    public void DeathCam_AtTheMomentOfDeath_BacksSixteenAlongTheRecordersOwnEyeAngles()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f));

        ShouldBe(view.Origin, new Vector3(0f, -16f, 68f));
        ShouldBe(view.Angles, (0f, 90f, 0f));
    }

    [Test]
    public void DeathCam_OneSecondAfterDeath_FacesTheKillersFeet()
    {
        // Half of TF_DEATH_ANIMATION_TIME: the swing is done. The killer's EyePosition() is his origin, because another
        // player's m_vecViewOffset is never received.
        ObserverView view = Camera().Calc(Death(curTime: 11d, frameTime: 0f));

        (float Pitch, float Yaw, float Roll) atKiller = AngleVectors.Angles(500f, 0f, -68f);
        (float x, float y, float z) = AngleVectors.Forward(atKiller.Pitch, atKiller.Yaw);

        ShouldBe(view.Angles, atKiller);
        ShouldBe(view.Origin, new Vector3(0f, 0f, 68f) - (16f * new Vector3(x, y, z)));
    }

    [Test]
    public void DeathCam_HalfASecondOfFrames_GrowsTheDistanceFortyEightASecond()
    {
        ObserverCamera camera = Camera();

        camera.Calc(Death(curTime: 10d, frameTime: 0f));
        ObserverView view = camera.Calc(Death(curTime: 10d, frameTime: 0.5f));

        ShouldBe(view.Origin, new Vector3(0f, -40f, 68f), "16, the minimum, then 24 more");
    }

    [Test]
    public void DeathCam_WithASimulatedCorpse_BacksOffFromItsRootFourteenUp()
    {
        ObserverInput input = Death(curTime: 10d, frameTime: 0f) with
        {
            Self = Self with { Ragdoll = new ObserverRagdoll(DeathAnimation: false, new Vector3(100f, 100f, 5f)) },
        };

        ShouldBe(Camera().Calc(input).Origin, new Vector3(100f, 84f, 19f));
    }

    [Test]
    public void DeathCam_PlayingADeathAnimation_LooksFromFourDeadHeightsAboveTheEye()
    {
        ObserverInput input = Death(curTime: 10d, frameTime: 0f) with
        {
            Self = Self with { Ragdoll = new ObserverRagdoll(DeathAnimation: true, new Vector3(100f, 100f, 5f)) },
        };

        ShouldBe(Camera().Calc(input).Origin, new Vector3(0f, -16f, 68f + 56f));
    }

    /// <remarks>
    /// **A decapitated recorder looks out of his own head** (B746): <c>CalcDeathCamView</c>'s <c>m_hHeadGib</c> branch
    /// (<c>c_tf_player.cpp:7199-7223</c>) puts the eye six over the head's mass centre and, at the moment of death, looks
    /// along the head's own angles.
    /// </remarks>
    [Test]
    public void DeathCam_FromADecapitatedHeadAtDeath_LooksSixOverItsMassCentreAlongTheHead()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with { Self = Headless });

        ShouldBe(view.Origin, HeadCentre + new Vector3(0f, 0f, 6f));
        ShouldBe(view.Angles, (10f, 20f, 0f));
    }

    /// <remarks>
    /// One second on, half the death animation time, the swing is done: it faces the headless body,
    /// <c>VectorAngles( m_hRagdoll->GetAbsOrigin() - eyeOrigin )</c>, whatever the killer is doing.
    /// </remarks>
    [Test]
    public void DeathCam_FromADecapitatedHeadOneSecondOn_FacesTheBody()
    {
        ObserverView view = Camera().Calc(Death(curTime: 11d, frameTime: 0f) with { Self = Headless });

        Vector3 toBody = BodyRoot - (HeadCentre + new Vector3(0f, 0f, 6f));

        ShouldBe(view.Origin, HeadCentre + new Vector3(0f, 0f, 6f));
        ShouldBe(view.Angles, AngleVectors.Angles(toBody.X, toBody.Y, toBody.Z));
    }

    /// <remarks>
    /// **A gibbed target is orbited at his first piece** (B746): <c>GetObserverCamOrigin</c> takes <c>m_hFirstGib</c>'s mass
    /// centre before the death view (<c>c_tf_player.cpp:6817-6838</c>), then the chase raises it by the dead offset, 14.
    /// </remarks>
    [Test]
    public void Chase_OnAGibbedTarget_OrbitsHisFirstPiece()
    {
        Vector3 piece = new(300f, 40f, 10f);

        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with
        {
            Mode = ObserverModes.Chase,
            Target = Shooter with { IsAlive = false, Ragdoll = new ObserverRagdoll(false, null, FirstGib: piece) },
            ViewAngles = (0f, 0f, 0f),
        });

        ShouldBe(view.Origin, piece + new Vector3(-96f, 0f, 14f));
    }

    private static readonly Vector3 HeadCentre = new(10f, 20f, 30f);

    private static readonly Vector3 BodyRoot = new(100f, 100f, 5f);

    /// <summary>The recorder decapitated: his body at <see cref="BodyRoot"/>, his head turned (10, 20) at <see cref="HeadCentre"/>.</summary>
    private static readonly ObserverSubject Headless = Self with
    {
        Ragdoll = new ObserverRagdoll(
            DeathAnimation: false,
            BodyRoot,
            FirstGib: HeadCentre,
            HeadGib: new ObserverHeadGib(HeadCentre, HeadCentre - new Vector3(1f, 0f, 0f), (10f, 20f, 0f))),
    };

    [Test]
    public void DeathCam_AWallHalfwayBack_ClipsAndKeepsTheShorterDistance()
    {
        ObserverCamera camera = Camera();

        ObserverView clipped = camera.Calc(Death(curTime: 10d, frameTime: 0f) with { Sweep = (_, _, _, _) => 0.5f });
        ObserverView after = camera.Calc(Death(curTime: 10d, frameTime: 0f) with { Sweep = (_, _, _, _) => 1f });

        ShouldBe(clipped.Origin, new Vector3(0f, -8f, 68f));
        ShouldBe(after.Origin, new Vector3(0f, -16f, 68f), "8 is below the minimum, which clamps it back to 16");
    }

    [Test]
    public void DeathCam_AHalfScaleKiller_ClampsTheDistanceToAQuarter()
    {
        // The minimum and maximum are 16 and 96 times the killer's scale squared: 4 and 24.
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 1f) with { Target = Shooter with { ModelScale = 0.5f } });

        ShouldBe(view.Origin, new Vector3(0f, -24f, 68f), "48 grown, clamped to 24");
    }

    [Test]
    public void FreezeCam_OnADeadTarget_AimsFourteenOverHisDeathViewAndStandsAtHisFeet()
    {
        (float distance, float offset) = Draws();

        ObserverView view = Started().Calc(Freeze(curTime: 20.4d) with { Target = Shooter with { IsAlive = false } });

        // His death view is his origin (no view offset, no corpse); the dead chase offset raises it 14, and looking over
        // the ragdoll raises the aim 14 more.
        Vector3 aim = new(500f, 0f, 28f);
        Vector3 stand = new(500f - distance, 0f, offset);
        Vector3 look = aim - stand;

        ShouldBe(view.Origin, stand);
        ShouldBe(view.Angles, AngleVectors.Angles(look.X, look.Y, look.Z));
    }

    [Test]
    public void FreezeCam_LeftAndEnteredAgain_DrawsItsDistanceAndHeightAgain()
    {
        UniformRandomStream random = new();
        random.SetSeed(Seed);
        random.RandomFloat(96f, 200f);
        random.RandomFloat(-30f, 20f);
        float distance = random.RandomFloat(96f, 200f);
        float offset = random.RandomFloat(-30f, 20f);

        ObserverCamera camera = Started();
        camera.Calc(Freeze(curTime: 20.4d));
        camera.Calc(Freeze(curTime: 21d) with { Mode = ObserverModes.Chase, FreezeSince = null });
        camera.Remember(new Vector3(0f, 0f, 64f), (0f, 0f, 0f));

        ObserverView view = camera.Calc(Freeze(curTime: 30.4d) with { FreezeSince = 30d });

        ShouldBe(view.Origin, new Vector3(500f - distance, 0f, offset));
        view.FreezeFrameStarted.ShouldBeTrue("a new freeze cam sends its freeze frame again");
    }

    [Test]
    public void FreezeCam_WithNoTarget_IsTheDeathCam()
    {
        ObserverView view = Camera().Calc(Freeze(curTime: 10d) with { Target = null });

        ShouldBe(view.Origin, new Vector3(0f, -16f, 68f));
        view.FreezeFrameStarted.ShouldBeFalse();
    }

    [Test]
    public void FreezeCam_AtTheEndOfTheTravel_StandsTheDrawnDistanceBeforeTheChestAtTheFeetPlusTheDrawnOffset()
    {
        (float distance, float offset) = Draws();

        ObserverView view = Started().Calc(Freeze(curTime: 20.4d));

        // Desired 72 up, aimed 41 lower at the chest; from the last view, the plane runs straight along +X.
        Vector3 aim = new(500f, 0f, 31f);
        Vector3 stand = new(500f - distance, 0f, offset);
        Vector3 look = aim - stand;

        ShouldBe(view.Origin, stand);
        ShouldBe(view.Angles, AngleVectors.Angles(look.X, look.Y, look.Z));
        view.FreezeFrameStarted.ShouldBeTrue("the travel is over, so FreezeFrame( spec_freeze_time ) is called");
    }

    [Test]
    public void FreezeCam_HalfwayThroughTheTravel_IsHalfwayFromTheLastView()
    {
        (float distance, float offset) = Draws();

        ObserverView view = Started().Calc(Freeze(curTime: 20.2d));

        ShouldBe(view.Origin, Vector3.Lerp(new Vector3(0f, 0f, 64f), new Vector3(500f - distance, 0f, offset), 0.5f));
        view.FreezeFrameStarted.ShouldBeFalse();
    }

    [Test]
    public void FreezeCam_PastTheTravelTwice_StartsTheFreezeFrameOnce()
    {
        ObserverCamera camera = Started();

        camera.Calc(Freeze(curTime: 20.4d)).FreezeFrameStarted.ShouldBeTrue();
        camera.Calc(Freeze(curTime: 20.5d)).FreezeFrameStarted.ShouldBeFalse();
    }

    [Test]
    public void FreezeCam_AWallHalfwayOut_RisesEightyFivePercentTowardTheTargetsEyes()
    {
        (float distance, float offset) = Draws();
        int traces = 0;

        ObserverView view = Started().Calc(Freeze(curTime: 20.4d) with
        {
            Sweep = (_, _, _, _) => ++traces == 1 ? 0.5f : 1f,
        });

        Vector3 clipped = new Vector3(500f, 0f, 31f) + ((new Vector3(500f - distance, 0f, offset) - new Vector3(500f, 0f, 31f)) * 0.5f);
        clipped.Z += MathF.Abs(72f - clipped.Z) * 0.85f;

        ShouldBe(view.Origin, clipped);
        traces.ShouldBe(2);
    }

    [Test]
    public void FreezeCam_LeftForTheChaseCamera_EndsTheFreezeFrame()
    {
        ObserverCamera camera = Started();

        camera.Calc(Freeze(curTime: 20.4d));

        camera.Calc(Freeze(curTime: 21d) with { Mode = ObserverModes.Chase, FreezeSince = null }).FreezeFrameEnded.ShouldBeTrue();
    }

    [Test]
    public void Chase_OnALivingTarget_OrbitsItsViewHeightNinetySixBackAtTheDemosAngles()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with
        {
            Mode = ObserverModes.Chase,
            Target = Shooter with { Origin = Vector3.Zero },
            ViewAngles = (0f, 0f, 0f),
        });

        ShouldBe(view.Origin, new Vector3(-96f, 0f, 72f));
        ShouldBe(view.Angles, (0f, 0f, 0f));
    }

    [Test]
    public void InEye_OnALivingTarget_IsHisOriginRaisedByTheRecordersOwnViewOffset()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with { Mode = ObserverModes.InEye });

        ShouldBe(view.Origin, new Vector3(500f, 0f, 68f));
        ShouldBe(view.Angles, (10f, 20f, 0f));
    }

    [Test]
    public void InEye_OnADeadTarget_ChasesHimAtTheLastViewsAngles()
    {
        ObserverCamera camera = Camera();
        camera.Remember(Vector3.Zero, (0f, 0f, 0f));

        ObserverView view = camera.Calc(Death(curTime: 10d, frameTime: 0f) with
        {
            Mode = ObserverModes.InEye,
            Target = Shooter with { IsAlive = false },
        });

        ShouldBe(view.Origin, new Vector3(500f - 96f, 0f, 14f));
    }

    [Test]
    public void Roaming_WithATarget_IsHisFeetAtHisEyeAngles()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with { Mode = ObserverModes.Roaming });

        ShouldBe(view.Origin, new Vector3(500f, 0f, 0f));
        ShouldBe(view.Angles, (10f, 20f, 0f));
    }

    [Test]
    public void Roaming_WithNoTarget_IsTheRecordersEye()
    {
        ObserverView view = Camera().Calc(Death(curTime: 10d, frameTime: 0f) with { Mode = ObserverModes.Roaming, Target = null });

        ShouldBe(view.Origin, new Vector3(0f, 0f, 68f));
        ShouldBe(view.Angles, (0f, 90f, 0f));
    }

    private const int Seed = 7;

    private static ObserverCamera Camera()
    {
        UniformRandomStream random = new();
        random.SetSeed(Seed);

        return new ObserverCamera(random);
    }

    /// <summary>A camera whose last view was 64 up at the world origin.</summary>
    private static ObserverCamera Started()
    {
        ObserverCamera camera = Camera();
        camera.Remember(new Vector3(0f, 0f, 64f), (0f, 0f, 0f));

        return camera;
    }

    /// <summary>The distance and height <c>PostDataUpdate</c> draws, in its order, from the same stream.</summary>
    private static (float Distance, float Offset) Draws()
    {
        UniformRandomStream random = new();
        random.SetSeed(Seed);

        return (random.RandomFloat(96f, 200f), random.RandomFloat(-30f, 20f));
    }

    private static ObserverInput Death(double curTime, float frameTime) =>
        new(ObserverModes.DeathCam, curTime, frameTime, Self, Shooter, (0f, 0f, 0f), DeathTime: 10f, FreezeSince: null, Sweep: null);

    private static ObserverInput Freeze(double curTime) =>
        Death(curTime, 0f) with { Mode = ObserverModes.FreezeCam, FreezeSince = 20d };

    private static void ShouldBe(Vector3 actual, Vector3 expected, string? because = null)
    {
        actual.X.ShouldBe(expected.X, Tolerance, because);
        actual.Y.ShouldBe(expected.Y, Tolerance, because);
        actual.Z.ShouldBe(expected.Z, Tolerance, because);
    }

    private static void ShouldBe((float Pitch, float Yaw, float Roll) actual, (float Pitch, float Yaw, float Roll) expected)
    {
        Wrapped(actual.Pitch - expected.Pitch).ShouldBe(0f, Tolerance);
        Wrapped(actual.Yaw - expected.Yaw).ShouldBe(0f, Tolerance);
        Wrapped(actual.Roll - expected.Roll).ShouldBe(0f, Tolerance);
    }

    /// <summary>A difference of angles, brought into -180..180: <c>QuaternionAngles</c> answers -90 where 270 went in.</summary>
    private static float Wrapped(float degrees) => ((degrees % 360f) + 540f) % 360f - 180f;
}
