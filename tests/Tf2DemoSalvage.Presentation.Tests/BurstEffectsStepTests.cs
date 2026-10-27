using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The half of <see cref="BurstEffects"/> that reads a demo: explosions (`TFExplosionCallback`, tf_fx_explosions.cpp),
/// tracers (`ParticleTracerCallback`), blood (`TFBloodSprayCallback`), splashes (`TFSplashCallbackHelper`,
/// tf_fx_impacts.cpp:141), sentry and weapon muzzle flashes (`CTFWeaponBase::CreateMuzzleFlashEffects`) and dispatched
/// particles (`ParticleEffectCallback`, c_particle_system.cpp:181).
/// </summary>
/// <remarks>
/// **Every timeline here is one this file wrote** (D38, D213): a temp entity with chosen fields, through the decoder
/// and the feeds production uses, then the burst the presenter offers for it — its key, its system, its control points
/// and its tick. Slice 1 of D214 moved this code with only four screenshots holding it; these are what it lacked.
/// </remarks>
public sealed class BurstEffectsStepTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int Soldier = 1;
    private const int Scout = 2;
    private const int Weapon = 50;
    private const int Sentry = 30;

    private const long TracerKeys = 1L << 32;
    private const long BloodKeys = 2L << 32;
    private const long MuzzleFlashKeys = 3L << 32;
    private const long ParticleDispatchKeys = 4L << 32;
    private const long WeaponFlashKeys = 5L << 32;
    private const long TfParticleKeys = 6L << 32;
    private const long SplashKeys = 8L << 32;

    private static readonly string[] EffectNames = [string.Empty, "ParticleEffect", SentryMuzzleFlash.EffectName, "Impact"];

    private static readonly string[] ParticleNames = [string.Empty, "rocketjump_smoke", "not_loaded", "test_finished"];

    /// <summary>The dispatched system emits for ever, so an offer outlives its first step; every other one has no emitter and finishes at once.</summary>
    private static readonly Dictionary<string, ParticleSystem> Systems = new[]
    {
        ExplosionEffects.DefaultEffect, "bullet_tracer01_red", BloodEffects.Impact, BloodEffects.Spray, BloodEffects.SprayFar,
        "water_bulletsplash01", SentryMuzzleFlash.Level1, SentryMuzzleFlash.Level2, "rocketjump_smoke", "muzzle_scattergun",
        WeaponMuzzleFlashes.BackblastSystem, "test_finished",
    }.ToDictionary(
        static name => name,
        static name => new ParticleSystem(
            name,
            name == "rocketjump_smoke"
                ? [new ParticleFunction("emit_continuously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 10f),
                })]
                : [],
            [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
        StringComparer.Ordinal);

    private static readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal)
    {
        ["scripts/tf_weapon_scattergun.txt"] = Encoding.UTF8.GetBytes(
            "WeaponData\n{\n\"MuzzleFlashParticleEffect\" \"muzzle_scattergun\"\n}\n"),
        ["scripts/tf_weapon_rocketlauncher.txt"] = Encoding.UTF8.GetBytes("WeaponData\n{\n}\n"),
    };

    private static readonly ItemSchema Items = ItemSchema.Read(Encoding.UTF8.GetBytes(
        """
        "items_game"
        {
            "items"
            {
                "200" { "item_class" "tf_weapon_scattergun" }
                "513" { "item_class" "tf_weapon_rocketlauncher" }
            }
        }
        """));

    private static readonly Dictionary<string, SoundScriptEntry> Scripts = new(StringComparer.Ordinal)
    {
        [TracerWhiz.Sound] = new SoundScriptEntry(
            TracerWhiz.Sound, 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), ["whiz.wav"]),
    };

    private static readonly SyntheticEffects.Placed[] Players =
    [
        new(Soldier, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, Red)),
        new(Scout, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(300f, 0f, 0f, Blue)),
    ];

    private static readonly FreeCamera FarAway = new() { Origin = (5000f, 5000f, 5000f) };

    private static DemoTimeline Timeline(params SyntheticEffects.Effect[] effects) =>
        DemoTimeline.Build(SyntheticEffects.Demo(Players, EffectNames, ParticleNames, [], effects));

    private static SyntheticEffects.Effect Effect(int tick, string className, params (string Name, PropertyValue Value)[] values) =>
        new(tick, className, values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal));

    private static (string, PropertyValue) Number(string name, int value) => (name, PropertyValue.FromInt(value));

    private static (string, PropertyValue) Real(string name, float value) => (name, PropertyValue.FromFloat(value));

    private static (string, PropertyValue) Triple(string name, float x, float y, float z) =>
        (name, PropertyValue.FromVector(x, y, z));

    private static (BurstEffects Bursts, FakePosedEntities Models, ParticleEffects Particles) Presenter()
    {
        FakePosedEntities models = new();
        ParticleEffects particles = new();

        return (new BurstEffects(particles, models, new BulletStrikes(models, new RecordingLogger()), new RecordingLogger()), models, particles);
    }

    private static BurstFrame Frame(DemoTimeline timeline, int tick, FreeCamera? viewing = null, bool silent = false, bool firstPerson = false, int? followed = null, bool drawViewmodel = true) =>
        new(timeline, null, null, tick, viewing ?? FarAway, [], null, tick, true, firstPerson, followed, drawViewmodel, false, null, silent);

    private static BurstWorld World(IReadOnlyList<ShotTracer>? tracers = null, IReadOnlyList<ShotImpact>? impacts = null) =>
        new(Systems, tracers, impacts, null, static path => Files.GetValueOrDefault(path), Items, Scripts);

    private static SyntheticEffects.Effect Explosion(int tick) => Effect(
        tick,
        "CTETFExplosion",
        Real("m_vecOrigin[0]", 100f), Real("m_vecOrigin[1]", 200f), Real("m_vecOrigin[2]", 300f),
        Triple("m_vecNormal", 0f, 0f, 1f), Number("m_iWeaponID", 0), Number("entindex", 2047),
        Number("m_iCustomParticleIndex", 65535));

    [Test]
    public void StepOver_AnExplosionOnAFloor_IsTheDefaultEffectAtItsOriginFacingAlongTheNormal()
    {
        DemoTimeline timeline = Timeline(Explosion(100));
        int fired = timeline.Explosions.All.ShouldHaveSingleItem().Tick;
        (BurstEffects bursts, _, ParticleEffects particles) = Presenter();

        bursts.StepOver(Frame(timeline, fired), World()).ShouldBeEmpty("an explosion makes no whiz");

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();

        burst.Key.ShouldBe(0, "an explosion's key is its index in the feed");
        burst.Definition.Name.ShouldBe(ExplosionEffects.DefaultEffect);
        burst.At.At.ShouldBe(new Vector3(100f, 200f, 300f));
        Vector3.Distance(burst.At.Forward, Vector3.UnitZ).ShouldBeLessThan(1e-5f, "VectorAngles of the normal, then AngleVectors");
        burst.Tick.ShouldBe(fired);
        burst.End.ShouldBeNull();
        particles.BurstCount.ShouldBe(1, "what is offered is what is stepped");
    }

    /// <remarks>**The frame's moment reaches the bursts** (D221): a frame half a tick on steps the explosion by half a tick.</remarks>
    [Test]
    public void StepOver_AFrameHalfATickLater_StepsTheBurstByHalfATick()
    {
        DemoTimeline timeline = Timeline(Explosion(100));
        int fired = timeline.Explosions.All.ShouldHaveSingleItem().Tick;
        (BurstEffects bursts, _, ParticleEffects particles) = Presenter();

        // An explosion still running: one with nothing in it is finished after its first step and, rightly, not stepped again.
        Dictionary<string, ParticleSystem> running = new(Systems, StringComparer.Ordinal)
        {
            [ExplosionEffects.DefaultEffect] = new ParticleSystem(
                ExplosionEffects.DefaultEffect,
                [
                    new ParticleFunction("emit_continuously", "emitter", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        // Not 0: a rate-0 emitter may create no more, so the engine finishes it at once (B557 row 17).
                        ["emission_rate"] = new(DmxAttributeType.Real, 1d / 60d),
                        ["emission_duration"] = new(DmxAttributeType.Real, 3600d),
                    }),
                ],
                [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
        };

        bursts.StepOver(Frame(timeline, fired + 1), World() with { Systems = running });
        float before = particles.BurstParticles(0)!.Age;

        bursts.StepOver(Frame(timeline, fired + 1) with { ShownTick = fired + 1.5d }, World() with { Systems = running });

        (particles.BurstParticles(0)!.Age - before).ShouldBe(0.5f * timeline.IntervalPerTick, 1e-6f);
        particles.BurstSteps(0).ShouldBe(2, "one tick on landing, then one step for the half");
    }

    [TestCase(-1, 0, TestName = "StepOver_TheTickBeforeAnExplosionFires_OffersNone")]
    [TestCase(200, 1, TestName = "StepOver_TwoHundredTicksAfterAnExplosion_StillOffersIt")]
    [TestCase(201, 0, TestName = "StepOver_PastAnExplosionsWindow_OffersNone")]
    public void StepOver_AroundAnExplosionsWindow_OffersItForTwoHundredTicks(int after, int expected)
    {
        DemoTimeline timeline = Timeline(Explosion(100));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Explosions.All[0].Tick + after), World());

        bursts.Bursts.Count.ShouldBe(expected);
    }

    [Test]
    public void StepOver_WithNoGameToReadFrom_OffersNothing()
    {
        DemoTimeline timeline = Timeline(Explosion(100));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Explosions.All[0].Tick), World() with { Read = null });

        bursts.Bursts.ShouldBeEmpty();
    }

    [Test]
    public void StepOver_AnEffectWhoseSystemTheMapDidNotLoad_DrawsNothing()
    {
        DemoTimeline timeline = Timeline(Explosion(100));
        (BurstEffects bursts, _, _) = Presenter();
        Dictionary<string, ParticleSystem> without = new(Systems, StringComparer.Ordinal);

        without.Remove(ExplosionEffects.DefaultEffect);
        bursts.StepOver(Frame(timeline, timeline.Explosions.All[0].Tick), World() with { Systems = without });

        bursts.Bursts.ShouldBeEmpty("a missing effect is a missing effect, not a chequer");
    }

    private static ShotTracer Tracer(int tick, int bullet = 0) =>
        new(0, bullet, tick, Soldier, Weapon, "bullet_tracer01_red", (0f, 0f, 64f), (500f, 0f, 64f), (1000f, 0f, 64f));

    [Test]
    public void StepOver_ATracerOnItsOwnTick_RunsFromTheWeaponsMuzzleToWhereTheBulletStopped()
    {
        DemoTimeline timeline = Timeline();
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));
        bursts.StepOver(Frame(timeline, 100), World([Tracer(100)]));

        ParticleBurst tracer = bursts.Bursts.ShouldHaveSingleItem();

        tracer.Key.ShouldBe(TracerKeys);
        tracer.Definition.Name.ShouldBe("bullet_tracer01_red");
        tracer.At.At.ShouldBe(new Vector3(20f, 0f, 60f), "FireBullet's pWeapon->GetAttachment( iAttachment, vecStart )");
        tracer.End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(500f, 0f, 64f)));
        Vector3.Distance(tracer.At.Forward, Vector3.Normalize(new Vector3(480f, 0f, 4f))).ShouldBeLessThan(1e-5f, "control point 0 faces the end");
        tracer.Tick.ShouldBe(100);
    }

    [Test]
    public void StepOver_ATracerWhoseGunMovesAfterwards_KeepsThePathItWasFirstDrawnOn()
    {
        DemoTimeline timeline = Timeline();
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ShotTracer[] tracers = [Tracer(100)];

        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));
        bursts.StepOver(Frame(timeline, 100), World(tracers));
        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(-90f, 50f, 60f));
        bursts.StepOver(Frame(timeline, 101), World(tracers));

        bursts.Bursts.ShouldHaveSingleItem().At.At.ShouldBe(new Vector3(20f, 0f, 60f), "read once, as the engine reads it when the shot arrives");
    }

    [Test]
    public void StepOver_ATracerWhoseWeaponIsNotPosed_StartsAtTheBulletsOrigin()
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Timeline(), 100), World([Tracer(100)]));

        bursts.Bursts.ShouldHaveSingleItem().At.At.ShouldBe(new Vector3(0f, 0f, 64f));
    }

    [TestCase(120, 1, TestName = "StepOver_ATracerAHundredAndTwentyTicksOld_IsStillOffered")]
    [TestCase(121, 0, TestName = "StepOver_ATracerPastItsWindow_IsNotOffered")]
    public void StepOver_AroundATracersWindow_OffersItForItsLongestTrip(int after, int expected)
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Timeline(), 100 + after), World([Tracer(100)]));

        bursts.Bursts.Count.ShouldBe(expected);
    }

    /// <summary>A camera the tracer of <see cref="Tracer"/> passes ten units beside.</summary>
    private static readonly FreeCamera BesideThePath = new() { Origin = (250f, 10f, 64f) };

    [Test]
    public void StepOver_ATracerPassingTheListenerOnItsOwnTick_WhizzesFromItsStartInItsShotsOrder()
    {
        DemoTimeline timeline = Timeline();
        (BurstEffects bursts, _, _) = Presenter();

        SceneSound whiz = bursts.StepOver(Frame(timeline, 100, BesideThePath), World([Tracer(100, bullet: 4)])).ShouldHaveSingleItem();

        whiz.ShouldBe(TracerWhiz.SoundAt(100, (0f, 0f, 64f), Scripts, new ClientSoundOrder(ClientSoundPhase.TempEntities, 0, 4)).ShouldNotBeNull());
    }

    [Test]
    public void StepOver_ATracerPassingTheListenerInASeeksReplay_IsSilent()
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Timeline(), 100, BesideThePath, silent: true), World([Tracer(100)])).ShouldBeEmpty();
    }

    [Test]
    public void StepOver_ATracerFirstMetTicksAfterItWasFired_MakesNoWhiz()
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Timeline(), 105, BesideThePath), World([Tracer(100)])).ShouldBeEmpty("made in a stretch nobody heard");
        bursts.Bursts.ShouldHaveSingleItem().End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(500f, 0f, 64f)));
    }

    [Test]
    public void StepOver_TwoTracersPassingWithinATenthOfASecond_WhizOnce()
    {
        (BurstEffects bursts, _, _) = Presenter();
        ShotTracer[] tracers = [Tracer(100), Tracer(103, bullet: 1)];

        bursts.StepOver(Frame(Timeline(), 100, BesideThePath), World(tracers)).Count.ShouldBe(1);
        bursts.StepOver(Frame(Timeline(), 103, BesideThePath), World(tracers)).ShouldBeEmpty("g_BulletWhiz.m_nextWhizTime: 0.1 s is 6.7 ticks");
    }

    [Test]
    public void StepOver_Blood_IsItsImpactAndItsSprayTurnedAgainstTheViewItArrivedUnder()
    {
        DemoTimeline timeline = Timeline(Effect(
            100,
            "CTETFBlood",
            Real("m_vecOrigin[0]", 0f), Real("m_vecOrigin[1]", 0f), Real("m_vecOrigin[2]", 50f),
            Triple("m_vecNormal", 1f, 0f, 0f), Number("entindex", Scout)));
        SceneBlood blood = timeline.Blood.All.ShouldHaveSingleItem();
        FreeCamera viewing = new() { Origin = (-300f, 0f, 50f), Angles = (0f, 0f, 0f) };
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, blood.Tick, viewing), World());

        (BloodBurst impact, BloodBurst spray) = BloodEffects.For(
            blood, new Vector3(-300f, 0f, 50f), (0f, 0f, 0f), SeededDraw.For(SeededDraw.Of(0, 0, salt: 2)));

        bursts.Bursts.Count.ShouldBe(2);
        bursts.Bursts[0].ShouldBe(new ParticleBurst(
            BloodKeys, Systems[impact.System], impact.Point, blood.Tick, ParticleControlPoint.Unoriented(impact.Point.At)));
        bursts.Bursts[1].ShouldBe(new ParticleBurst(
            BloodKeys + 1, Systems[spray.System], spray.Point, blood.Tick, ParticleControlPoint.Unoriented(spray.Point.At)));
        spray.Point.ShouldNotBe(impact.Point, "the control: the normal faces the view, so the spray is pushed aside");

        // The camera turns away; the spray does not swing with it.
        bursts.StepOver(Frame(timeline, blood.Tick + 1, new FreeCamera { Origin = (0f, 900f, 50f), Angles = (0f, -90f, 0f) }), World());

        bursts.Bursts[1].At.ShouldBe(spray.Point, "MainViewOrigin and MainViewAngles are read inside the callback, once");
    }

    private static ShotImpact IntoWater(int tick) =>
        new(0, 0, tick, Soldier, Red, (0f, 0f, 64f), (500f, 0f, 0f), (1000f, 0f, 0f), -1)
        {
            WaterEntry = (250f, 0f, 32f),
            Splash = "water_bulletsplash01",
        };

    [TestCase(0, 1, TestName = "StepOver_ABulletIntoWater_SplashesWhereItEntered")]
    [TestCase(66, 1, TestName = "StepOver_ASplashASecondOld_IsStillOffered")]
    [TestCase(67, 0, TestName = "StepOver_ASplashPastItsWindow_IsNotOffered")]
    public void StepOver_ABulletThatEnteredWater_SplashesAtTheSurfaceForASecond(int after, int expected)
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Timeline(), 100 + after), World(impacts: [IntoWater(100), IntoWater(100) with { WaterEntry = null }]));

        bursts.Bursts.Count.ShouldBe(expected, "and the bullet that entered no water makes none");

        if (expected == 1)
        {
            bursts.Bursts[0].ShouldBe(new ParticleBurst(
                SplashKeys, Systems["water_bulletsplash01"], ParticleControlPoint.Unoriented(new Vector3(250f, 0f, 32f)), 100));
        }
    }

    private static SyntheticEffects.Effect SentryFlash(int tick, int level) => Effect(
        tick, "CTEEffectDispatch", Number("m_iEffectName", 2), Number("m_fFlags", level), Number("entindex", Sentry),
        Number("m_nAttachmentIndex", 3));

    [TestCase(1, SentryMuzzleFlash.Level1, TestName = "StepOver_ALevelOneSentrysFlash_IsMuzzleSentryOnItsBarrel")]
    [TestCase(2, SentryMuzzleFlash.Level2, TestName = "StepOver_ALevelTwoSentrysFlash_IsMuzzleSentry2OnItsBarrel")]
    public void StepOver_ASentrysMuzzleFlash_FollowsTheBarrelItNames(int level, string system)
    {
        DemoTimeline timeline = Timeline(SentryFlash(100, level));
        int fired = timeline.Dispatches.All.ShouldHaveSingleItem().Tick;
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint barrel = ParticleControlPoint.Facing(new Vector3(700f, 0f, 40f), 30f);

        models.Numbered[(Sentry, 3)] = barrel;
        bursts.StepOver(Frame(timeline, fired), World());

        bursts.Bursts.ShouldHaveSingleItem().ShouldBe(new ParticleBurst(MuzzleFlashKeys, Systems[system], barrel, fired));

        // PATTACH_POINT_FOLLOW: the barrel is read every frame.
        ParticleControlPoint turned = ParticleControlPoint.Facing(new Vector3(700f, 0f, 40f), 90f);

        models.Numbered[(Sentry, 3)] = turned;
        bursts.StepOver(Frame(timeline, fired + 1), World());

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(turned);
    }

    [Test]
    public void StepOver_AFlashOnASentryNotPosedHere_IsNotStarted()
    {
        DemoTimeline timeline = Timeline(SentryFlash(100, 1));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Dispatches.All[0].Tick), World());

        bursts.Bursts.ShouldBeEmpty("which stands in for the callback's IsDormant refusal");
    }

    private static SyntheticEffects.Effect Particle(int tick, int flags, int attach, params (string, PropertyValue)[] more) => Effect(
        tick,
        "CTEEffectDispatch",
        [
            Number("m_iEffectName", 1), Number("m_nHitBox", 1), Number("m_fFlags", flags), Number("m_nDamageType", attach),
            Number("entindex", Sentry), Number("m_nAttachmentIndex", 3),
            Real("m_vOrigin[0]", 5f), Real("m_vOrigin[1]", 6f), Real("m_vOrigin[2]", 7f),
            Real("m_vStart[0]", 1f), Real("m_vStart[1]", 2f), Real("m_vStart[2]", 3f),
            Triple("m_vAngles", 0f, 90f, 0f),
            .. more,
        ]);

    [Test]
    public void StepOver_AParticleDispatchedAtAPoint_IsAtItsOriginFacingItsAnglesWithItsStartAsPointOne()
    {
        DemoTimeline timeline = Timeline(Particle(100, flags: 0, attach: 0));
        int fired = timeline.Dispatches.All.ShouldHaveSingleItem().Tick;
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, fired), World());

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();

        burst.Key.ShouldBe(ParticleDispatchKeys);
        burst.Definition.Name.ShouldBe("rocketjump_smoke", "GetParticleSystemNameFromIndex( m_nHitBox )");
        burst.At.At.ShouldBe(new Vector3(5f, 6f, 7f));
        Vector3.Distance(burst.At.Forward, Vector3.UnitY).ShouldBeLessThan(1e-5f);
        burst.End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f)));
        burst.Tick.ShouldBe(fired);
    }

    [Test]
    public void StepOver_AParticleDispatchThatSendsControlPointOne_UsesItOverTheStart()
    {
        DemoTimeline timeline = Timeline(Particle(
            100, flags: 0, attach: 0, Number("m_bControlPoint1", 1), Real("m_ControlPoint1.m_vecOffset[0]", 40f),
            Real("m_ControlPoint1.m_vecOffset[1]", 50f), Real("m_ControlPoint1.m_vecOffset[2]", 60f)));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Dispatches.All[0].Tick), World());

        bursts.Bursts.ShouldHaveSingleItem().End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(40f, 50f, 60f)));
    }

    [Test]
    public void StepOver_AParticleFollowingAnAttachment_IsOnItEveryFrameWithNoPointOne()
    {
        DemoTimeline timeline = Timeline(Particle(100, flags: 1, attach: 4));
        int fired = timeline.Dispatches.All[0].Tick;
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint first = ParticleControlPoint.Unoriented(new Vector3(70f, 0f, 0f));
        ParticleControlPoint moved = ParticleControlPoint.Unoriented(new Vector3(90f, 0f, 0f));

        bursts.StepOver(Frame(timeline, fired), World());
        bursts.Bursts.ShouldHaveSingleItem().Undrawn.ShouldBeTrue(
            "an entity not posed here offers its effect undrawn: unoffered, it would be retired and replayed from its dispatch tick");

        models.Numbered[(Sentry, 3)] = first;
        bursts.StepOver(Frame(timeline, fired), World());
        ParticleBurst offered = bursts.Bursts.ShouldHaveSingleItem();

        // An entity's effect carries its owner's dormancy, asked per tick (owner awake here); the rest is the dispatch's.
        offered.DormantAt.ShouldNotBeNull()(fired).ShouldBeFalse();
        (offered with { DormantAt = null }).ShouldBe(new ParticleBurst(ParticleDispatchKeys, Systems["rocketjump_smoke"], first, fired));

        models.Numbered[(Sentry, 3)] = moved;
        bursts.StepOver(Frame(timeline, fired + 1), World());
        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(moved, "PATTACH_POINT_FOLLOW");
    }

    [Test]
    public void StepOver_AParticleCreatedAtAnAttachment_StaysWhereItWasCreated()
    {
        DemoTimeline timeline = Timeline(Particle(100, flags: 1, attach: 3));
        int fired = timeline.Dispatches.All[0].Tick;
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint first = ParticleControlPoint.Unoriented(new Vector3(70f, 0f, 0f));

        models.Numbered[(Sentry, 3)] = first;
        bursts.StepOver(Frame(timeline, fired), World());
        models.Numbered[(Sentry, 3)] = ParticleControlPoint.Unoriented(new Vector3(90f, 0f, 0f));
        bursts.StepOver(Frame(timeline, fired + 1), World());

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(first, "PATTACH_POINT, not the following kind");

        // A new map forgets where it was fixed.
        bursts.ClearMap();
        bursts.StepOver(Frame(timeline, fired + 2), World());

        bursts.Bursts.ShouldHaveSingleItem().At.At.ShouldBe(new Vector3(90f, 0f, 0f));
    }

    /// <remarks>
    /// **A finished entity effect is not offered again while playing** (finding 86): `ProcessPSystem` flags a finished
    /// effect for removal (particlemgr.cpp:1581-1584), and nothing updates its control points after that. A stopless
    /// dispatch was re-offered every frame for the rest of the demo, each with an attachment query and a new burst — 434 a
    /// frame at tick 30000 on f12, 1,169 at 90000. **A seek still offers it**, since a seek rebuilds from the offer (D221).
    /// </remarks>
    [Test]
    public void StepOver_AnEntityParticleFinishedWhilePlaying_IsNoLongerOfferedUntilASeek()
    {
        DemoTimeline timeline = Timeline(Effect(
            100,
            "CTEEffectDispatch",
            Number("m_iEffectName", 1),
            Number("m_nHitBox", 3),
            Number("m_fFlags", 1),
            Number("m_nDamageType", 4),
            Number("entindex", Sentry),
            Number("m_nAttachmentIndex", 3)));
        int fired = timeline.Dispatches.All[0].Tick;
        (BurstEffects bursts, FakePosedEntities models, ParticleEffects particles) = Presenter();
        models.Numbered[(Sentry, 3)] = ParticleControlPoint.Unoriented(new Vector3(70f, 0f, 0f));

        bursts.StepOver(Frame(timeline, fired), World());
        bursts.Bursts.ShouldHaveSingleItem("the control: offered when it fires");
        bursts.StepOver(Frame(timeline, fired + 1), World());
        particles.IsGone(ParticleDispatchKeys).ShouldBeTrue("the precondition: a system with no emitter finishes at once");

        bursts.StepOver(Frame(timeline, fired + 2), World());
        bursts.Bursts.ShouldBeEmpty();

        bursts.StepOver(Frame(timeline, fired + 2) with { Seeked = true }, World());
        bursts.Bursts.ShouldHaveSingleItem("a seek rebuilds from the offer");

        bursts.StepOver(Frame(timeline, fired + 4), World());
        bursts.StepOver(Frame(timeline, fired + 5), World());
        bursts.Bursts.ShouldBeEmpty("finished again while playing");
        bursts.StepOver(Frame(timeline, fired + 3), World());
        bursts.Bursts.ShouldHaveSingleItem("a clock gone back lands as surely as a seek");
    }

    [Test]
    public void StepOver_AParticleAtACustomOriginOnAnEntity_WaitsForTheEntityAndThenTakesTheSentOrigin()
    {
        DemoTimeline timeline = Timeline(Particle(100, flags: 1, attach: 2));
        int fired = timeline.Dispatches.All[0].Tick;
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        bursts.StepOver(Frame(timeline, fired), World());
        bursts.Bursts.ShouldHaveSingleItem().Undrawn.ShouldBeTrue("an owner not posed here offers its effect undrawn");

        models.Posed.Add(Sentry);
        bursts.StepOver(Frame(timeline, fired), World());

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();

        burst.At.At.ShouldBe(new Vector3(5f, 6f, 7f));
        burst.End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f)), "PATTACH_CUSTOMORIGIN sets point 1 from m_vStart");
    }

    [Test]
    public void StepOver_ADispatchThatIsNotAParticleEffect_StartsNoParticle()
    {
        DemoTimeline timeline = Timeline(Effect(100, "CTEEffectDispatch", Number("m_iEffectName", 3), Number("m_nHitBox", 1)));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Dispatches.All.ShouldHaveSingleItem().Tick), World());

        bursts.Bursts.ShouldBeEmpty("m_nHitBox is a hitbox to any other effect");
    }

    [TestCase(660, 1, TestName = "StepOver_ADispatchedParticleTenSecondsOld_IsStillOffered")]
    [TestCase(661, 0, TestName = "StepOver_ADispatchedParticlePastItsWindow_IsNotOffered")]
    public void StepOver_AroundADispatchedParticlesWindow_OffersItForTenSeconds(int after, int expected)
    {
        DemoTimeline timeline = Timeline(Particle(100, flags: 0, attach: 0));
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, timeline.Dispatches.All[0].Tick + after), World());

        bursts.Bursts.Count.ShouldBe(expected);
    }

    [Test]
    public void StepOver_ATfParticleEffect_IsTheSameDispatchUnderItsOwnKeys()
    {
        DemoTimeline timeline = Timeline(Effect(
            100,
            "CTETFParticleEffect",
            Real("m_vecOrigin[0]", 5f), Real("m_vecOrigin[1]", 6f), Real("m_vecOrigin[2]", 7f),
            Real("m_vecStart[0]", 1f), Real("m_vecStart[1]", 2f), Real("m_vecStart[2]", 3f),
            Number("m_iParticleSystemIndex", 1), Number("entindex", 2047)));
        int fired = timeline.TfParticleEffects.All.ShouldHaveSingleItem().Tick;
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(timeline, fired), World());

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();

        burst.Key.ShouldBe(TfParticleKeys);
        burst.Definition.Name.ShouldBe("rocketjump_smoke");
        burst.At.At.ShouldBe(new Vector3(5f, 6f, 7f));
        burst.End.ShouldBe(ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f)));
    }

    /// <summary>A timeline whose weapon, of an item and owned by a player, raises its muzzle flash parity at tick 100.</summary>
    /// <remarks>The soldier, in slot 1, is this SourceTV recording's local player (`docs/findings/58`); the scout is not.</remarks>
    private static DemoTimeline Firing(int item, int owner = Scout) =>
        DemoTimeline.Build(SyntheticEffects.Demo(
            [
                .. Players,
                new SyntheticEffects.Placed(Weapon, SyntheticEffects.WeaponClass, new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
                {
                    ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(item),
                    ["DT_BaseEntity.m_iTeamNum"] = PropertyValue.FromInt(Red),
                    ["m_hOwnerEntity"] = PropertyValue.FromInt(owner),
                    ["m_nMuzzleFlashParity"] = PropertyValue.FromInt(0),
                }),
            ],
            EffectNames,
            ParticleNames,
            [
                new SyntheticEffects.Change(100, Weapon, new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
                {
                    ["m_nMuzzleFlashParity"] = PropertyValue.FromInt(1),
                }),
            ]));

    [Test]
    public void StepOver_AWeaponThatFired_FlashesItsScriptsParticleOnItsMuzzle()
    {
        DemoTimeline timeline = Firing(item: 200);
        SceneMuzzleFlash flash = timeline.MuzzleFlashes.All.ShouldHaveSingleItem();
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint muzzle = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));

        flash.Tick.ShouldBe(100, "an entity's counter is read on the tick it arrives, with no event delay");
        models.Named[(Weapon, "muzzle")] = muzzle;
        bursts.StepOver(Frame(timeline, 100), World());

        bursts.Bursts.ShouldHaveSingleItem().ShouldBe(new ParticleBurst(WeaponFlashKeys, Systems["muzzle_scattergun"], muzzle, 100));
    }

    [Test]
    public void StepOver_AWeaponNotPosedHere_FlashesNothing()
    {
        (BurstEffects bursts, _, _) = Presenter();

        bursts.StepOver(Frame(Firing(item: 200), 100), World());

        bursts.Bursts.ShouldBeEmpty();
    }

    [Test]
    public void StepOver_ARocketLauncherThatFired_BlastsBackFromItsOwnAttachment()
    {
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint behind = ParticleControlPoint.Unoriented(new Vector3(-30f, 0f, 60f));

        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));
        models.Named[(Weapon, WeaponMuzzleFlashes.BackblastAttachment)] = behind;
        bursts.StepOver(Frame(Firing(item: 513), 100), World());

        bursts.Bursts.ShouldHaveSingleItem().ShouldBe(
            new ParticleBurst(WeaponFlashKeys + 1, Systems[WeaponMuzzleFlashes.BackblastSystem], behind, 100),
            "its script names no muzzle particle, and the backblast takes the second key");
    }

    [Test]
    public void StepOver_TheLocalPlayersOwnRocketLauncher_NeverBlastsBack()
    {
        DemoTimeline timeline = Firing(item: 513, owner: Soldier);
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        timeline.RecorderEntityIndex.ShouldBe(Soldier, "the control: slot 0 is entity 1");
        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));
        models.Named[(Weapon, WeaponMuzzleFlashes.BackblastAttachment)] = ParticleControlPoint.Unoriented(new Vector3(-30f, 0f, 60f));
        bursts.StepOver(Frame(timeline, 100), World());

        bursts.Bursts.ShouldBeEmpty("pOwner->IsLocalPlayer(): tf_weapon_rocketlauncher.cpp:383");
    }

    private const int Viewmodel = 60;

    /// <summary>What the by-value walk produced over the B577 playback (B592); see its use.</summary>
    private const ulong PlaybackFingerprint = 3714805257722811873ul;

    /// <summary>
    /// A timeline whose viewmodel, the scout's and holding his scattergun, raises its own muzzle flash parity at tick
    /// 100 — the first-person flash, which the server sends in the viewmodel's counter and to its owner alone (B415).
    /// </summary>
    private static readonly DemoTimeline FiringInFirstPerson = DemoTimeline.Build(SyntheticEffects.Demo(
        [
            .. Players,
            new SyntheticEffects.Placed(Weapon, SyntheticEffects.WeaponClass, new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
            {
                ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(200),
                ["DT_BaseEntity.m_iTeamNum"] = PropertyValue.FromInt(Blue),
                ["m_hOwnerEntity"] = PropertyValue.FromInt(Scout),
                ["DT_BaseAnimating.m_nMuzzleFlashParity"] = PropertyValue.FromInt(0),
            }),
            new SyntheticEffects.Placed(Viewmodel, SyntheticEffects.ViewmodelClass, new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
            {
                ["m_hWeapon"] = PropertyValue.FromInt(Weapon),
                ["m_hOwner"] = PropertyValue.FromInt(Scout),
                ["DT_BaseViewModel.m_nMuzzleFlashParity"] = PropertyValue.FromInt(0),
            }),
        ],
        EffectNames,
        ParticleNames,
        [
            new SyntheticEffects.Change(100, Viewmodel, new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
            {
                ["DT_BaseViewModel.m_nMuzzleFlashParity"] = PropertyValue.FromInt(1),
            }),
        ]));

    private static readonly ParticleControlPoint OnTheHands = ParticleControlPoint.Unoriented(new Vector3(5f, 5f, 60f));

    [Test]
    public void StepOver_AViewmodelFlashInItsOwnersEyes_StartsOnTheWeaponAttachedToTheHands()
    {
        SceneMuzzleFlash flash = FiringInFirstPerson.MuzzleFlashes.All.ShouldHaveSingleItem();
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        flash.ShouldBe(new SceneMuzzleFlash(100, Weapon, 200, Blue) { Viewmodel = Viewmodel, Owner = Scout }, "the control: the feed's own reading");
        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = OnTheHands;
        models.Named[(ViewmodelScene.ArmsEntityIndex, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(-9f, -9f, 60f));
        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(300f, 0f, 60f));
        bursts.StepOver(Frame(FiringInFirstPerson, 100, firstPerson: true, followed: Scout), World());

        bursts.Bursts.ShouldHaveSingleItem().ShouldBe(
            new ParticleBurst(WeaponFlashKeys, Systems["muzzle_scattergun"], OnTheHands, 100),
            "GetAppropriateWorldOrViewModel: the viewmodel's weapon, not the world model");
    }

    /// <remarks>
    /// B591: the per-flash line is a diagnostic, written at Debug and only when Debug is enabled — an Information line
    /// per first-person shot was flushed per line on the main thread (the B191 shape, profile 2026-10-24).
    /// </remarks>
    [Test]
    public void StepOver_AViewmodelFlashWithDebugEnabled_LogsItsLineAtDebug()
    {
        RecordingLogger log = new();
        FakePosedEntities models = new();
        BurstEffects bursts = new(new ParticleEffects(), models, new BulletStrikes(models, new RecordingLogger()), log);

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = OnTheHands;
        bursts.StepOver(Frame(FiringInFirstPerson, 100, firstPerson: true, followed: Scout), World());

        log.Lines.Where(static line => line.Message.Contains("viewmodel muzzle flash", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
    }

    /// <remarks>B591, the guard covers the WORK: below Debug the logger is never called, so the string is never built.</remarks>
    [Test]
    public void StepOver_AViewmodelFlashWithDebugDisabled_NeverCallsTheLogger()
    {
        InformationOnly log = new();
        FakePosedEntities models = new();
        BurstEffects bursts = new(new ParticleEffects(), models, new BulletStrikes(models, new RecordingLogger()), log);

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = OnTheHands;
        bursts.StepOver(Frame(FiringInFirstPerson, 100, firstPerson: true, followed: Scout), World());

        bursts.Bursts.Count.ShouldBe(1, "the control: the flash was offered");
        log.Calls.ShouldBe(0);
    }

    /// <summary>Enabled at Information and above; counts every <see cref="ILogger.Log{TState}"/> call whatever its level.</summary>
    private sealed class InformationOnly : ILogger
    {
        public int Calls { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Calls++;
    }

    [Test]
    public void StepOver_AViewmodelFlashWithNoWeaponOnTheHands_StartsOnTheViewmodelItself()
    {
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();
        ParticleControlPoint arms = ParticleControlPoint.Unoriented(new Vector3(-9f, -9f, 60f));

        models.Named[(ViewmodelScene.ArmsEntityIndex, "muzzle")] = arms;
        bursts.StepOver(Frame(FiringInFirstPerson, 100, firstPerson: true, followed: Scout), World());

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(arms);
    }

    [TestCase(false, Scout, true, TestName = "StepOver_AViewmodelFlashOutsideFirstPerson_StartsNothing")]
    [TestCase(true, Soldier, true, TestName = "StepOver_AViewmodelFlashInSomeoneElsesEyes_StartsNothing")]
    [TestCase(true, Scout, false, TestName = "StepOver_AViewmodelFlashWithTheViewmodelHidden_StartsNothing")]
    public void StepOver_AViewmodelFlashItsOwnerDoesNotSee_StartsNothing(bool firstPerson, int followed, bool drawViewmodel)
    {
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = OnTheHands;
        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(300f, 0f, 60f));
        bursts.StepOver(
            Frame(FiringInFirstPerson, 100, firstPerson: firstPerson, followed: followed, drawViewmodel: drawViewmodel), World());

        bursts.Bursts.ShouldBeEmpty("CTFViewModel::ProcessMuzzleFlashEvent, and r_drawviewmodel 0");
    }

    [TestCase(66, 1, TestName = "StepOver_AWeaponsFlashASecondOld_IsStillOffered")]
    [TestCase(67, 0, TestName = "StepOver_AWeaponsFlashPastItsWindow_IsNotOffered")]
    public void StepOver_AroundAWeaponFlashsWindow_OffersItForASecond(int after, int expected)
    {
        (BurstEffects bursts, FakePosedEntities models, _) = Presenter();

        models.Named[(Weapon, "muzzle")] = ParticleControlPoint.Unoriented(new Vector3(20f, 0f, 60f));
        bursts.StepOver(Frame(Firing(item: 200), 100 + after), World());

        bursts.Bursts.Count.ShouldBe(expected);
    }

    /// <remarks>
    /// **B577: visiting only the live owned dispatches offers exactly what the walk from the first did**, frame by frame,
    /// in order, and leaves the effects in the same state. The reference is the same presenter with
    /// <see cref="BurstEffects.WalkEveryOwnedDispatch"/> set, run through its own <see cref="ParticleEffects"/> so the
    /// finished-effect feedback (`IsGone` read from the frame before) is real on both sides. A fixed pseudo-random
    /// playback: several frames a tick, same-tick dispatches, RESET stops, owners posed and unposed, seeks back and
    /// forward, and a clock gone back without a seek flag.
    /// </remarks>
    [Test]
    public void StepOver_LiveOwnedDispatchesOverAPlayback_OfferWhatTheWalkFromTheFirstOffered()
    {
        (DemoTimeline timeline, List<PlaybackFrame> frames) = Playback();
        (BurstEffects reference, FakePosedEntities referenceModels, ParticleEffects referenceParticles) = Presenter();
        (BurstEffects live, FakePosedEntities liveModels, ParticleEffects liveParticles) = Presenter();
        reference.WalkEveryOwnedDispatch = true;

        int offered = 0;
        Fingerprint fingerprint = new();

        for (int frame = 0; frame < frames.Count; frame++)
        {
            PlaybackFrame played = frames[frame];

            played.Toggle(referenceModels);
            played.Toggle(liveModels);

            BurstFrame step = Frame(timeline, played.At) with { Seeked = played.Seeked };

            reference.StepOver(step, World());
            live.StepOver(step, World());

            live.Bursts.Select(Comparable).ShouldBe(reference.Bursts.Select(Comparable), $"frame {frame} at tick {played.At}");
            liveParticles.Census().ShouldBe(referenceParticles.Census(), $"frame {frame} at tick {played.At}");
            offered += live.Bursts.Count;
            fingerprint.Fold(liveParticles);
        }

        // B592: the whole playback's census and step counters, recorded from `ParticleEffects.Bursts` while it still
        // walked its list with a by-value `foreach`; the by-reference walk must reproduce it exactly.
        fingerprint.Value.ShouldBe(PlaybackFingerprint, "the census and counters the by-value walk produced");

        (liveParticles.BurstBuilds, liveParticles.ReplaySteps, liveParticles.Steps, liveParticles.Slept).ShouldBe(
            (referenceParticles.BurstBuilds, referenceParticles.ReplaySteps, referenceParticles.Steps, referenceParticles.Slept));
        offered.ShouldBeGreaterThan(1000, "the control: the playback offers owned effects at all");
        referenceParticles.BurstBuilds.ShouldBeGreaterThan(50, "the control: effects are built and rebuilt");
    }

    /// <remarks>
    /// **`r_threaded_particles` over B577's playback** (finding 103 §5): two presenters, each with its own effects, one
    /// serial and one forking, frame by frame — the offers, the census and counters, every batch's corner bytes in order,
    /// and the whizzes in order. Unrendered, the forking side must also reproduce B592's fingerprint, which the serial
    /// by-value walk recorded. Rendered, the smoke has a material and a renderer and tracers pass the listener, so the
    /// bytes and the sounds are not empty — the controls below say so.
    /// </remarks>
    [TestCase(false, false, TestName = "StepOver_APlaybackStepsBackwardsOnWorkers_IsTheSerialPlaybackAndItsFingerprint")]
    [TestCase(true, false, TestName = "StepOver_ARenderedPlaybackStepsBackwardsOnWorkers_IsTheSerialPlaybackByteForByte")]
    [TestCase(true, true, TestName = "StepOver_ARenderedPlaybackStepsOnThreads_IsTheSerialPlaybackByteForByte")]
    public void StepOver_APlaybackOnWorkers_IsTheSerialPlayback(bool rendered, bool parallel)
    {
        (DemoTimeline timeline, List<PlaybackFrame> frames) = Playback();
        WorkerLane workers = parallel
            ? new WorkerLane(new GameWorkers { Degree = 8 }) { Threshold = 0 }
            : new WorkerLane(new GameWorkers(static (count, body) =>
            {
                for (int index = count - 1; index >= 0; index--)
                {
                    body(index);
                }
            }));
        (BurstEffects serial, FakePosedEntities serialModels, ParticleEffects serialParticles) = Presenter();
        (BurstEffects threaded, FakePosedEntities threadedModels, ParticleEffects threadedParticles) = Presenter();
        serialParticles.Workers = new WorkerLane { Threaded = false };
        threadedParticles.Workers = workers;

        Dictionary<string, ParticleSystem> systems = rendered ? RenderedSystems : Systems;
        ShotTracer[]? tracers = rendered ? [.. Enumerable.Range(0, 12).Select(static at => Tracer(150 + (at * 120), bullet: at))] : null;
        FreeCamera camera = rendered ? BesideThePath : FarAway;
        Fingerprint fingerprint = new();
        long corners = 0;
        int sounds = 0;

        for (int frame = 0; frame < frames.Count; frame++)
        {
            PlaybackFrame played = frames[frame];

            played.Toggle(serialModels);
            played.Toggle(threadedModels);

            BurstFrame step = Frame(timeline, played.At, camera) with { Seeked = played.Seeked };
            BurstWorld world = World(tracers) with { Systems = systems };
            string at = $"frame {frame} at tick {played.At}";

            IReadOnlyList<SceneSound> heard = serial.StepOver(step, world);

            threaded.StepOver(step, world).ShouldBe(heard, at);
            threaded.Bursts.Select(Comparable).ShouldBe(serial.Bursts.Select(Comparable), at);
            threadedParticles.Census().ShouldBe(serialParticles.Census(), at);
            (threadedParticles.BurstBuilds, threadedParticles.ReplaySteps, threadedParticles.Steps, threadedParticles.Slept)
                .ShouldBe((serialParticles.BurstBuilds, serialParticles.ReplaySteps, serialParticles.Steps, serialParticles.Slept), at);

            byte[] drawn = BatchBytes(serialParticles);

            BatchBytes(threadedParticles).ShouldBe(drawn, at);
            corners += drawn.Length;
            sounds += heard.Count;
            fingerprint.Fold(threadedParticles);
        }

        workers.Forks.ShouldBeGreaterThan(100, "the control: the played steps were forked");

        if (rendered)
        {
            corners.ShouldBeGreaterThan(0, "the control: the smoke is drawn");
            sounds.ShouldBeGreaterThan(0, "the control: a tracer whizzes past the listener");
        }
        else
        {
            fingerprint.Value.ShouldBe(PlaybackFingerprint, "the census and counters the serial walk produced");
        }
    }

    private static readonly Dictionary<string, ParticleSystem> RenderedSystems = new(Systems, StringComparer.Ordinal)
    {
        ["rocketjump_smoke"] = Systems["rocketjump_smoke"] with
        {
            Renderers = [new ParticleFunction("render_animated_sprites", "draw", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
            Parameters = new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["material"] = new DmxValue(DmxAttributeType.Text, Text: "effects\\smoke.vmt"),
            },
        },
    };

    private static readonly Dictionary<string, ParticleMaterial> SmokeMaterial = new(StringComparer.OrdinalIgnoreCase)
    {
        ["effects/smoke"] = new ParticleMaterial(null, [], SpriteBlend.Translucent),
    };

    /// <summary>Every batch this frame draws, its blend then its corners' bytes, in order.</summary>
    private static byte[] BatchBytes(ParticleEffects particles)
    {
        List<byte> bytes = [];

        foreach (ParticleBatch batch in particles.Build(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, SmokeMaterial))
        {
            bytes.Add((byte)batch.Material.Blend);
            bytes.AddRange(System.Runtime.InteropServices.MemoryMarshal.AsBytes(batch.Corners.ToArray().AsSpan()).ToArray());
        }

        return [.. bytes];
    }

    private static ParticleBurst Comparable(ParticleBurst burst) => burst with { DormantAt = null };

    /// <summary>One frame of B577's playback: the tick, whether it was sought, and the owner whose point it toggles.</summary>
    private readonly record struct PlaybackFrame(int At, bool Seeked, int? Owner, ParticleControlPoint Point)
    {
        /// <summary>Poses the owner's attachment if it was not, unposes it if it was.</summary>
        public void Toggle(FakePosedEntities models)
        {
            if (Owner is { } owner && !models.Numbered.Remove((owner, 3)))
            {
                models.Numbered[(owner, 3)] = Point;
            }
        }
    }

    /// <summary>
    /// B577's fixed pseudo-random playback: several frames a tick, same-tick dispatches, RESET stops, owners posed and
    /// unposed, seeks back and forward, and a clock gone back without a seek flag.
    /// </summary>
    /// <remarks>A fixed linear congruential sequence, so a failure names a reproducible case; drawn in one order, once.</remarks>
    private static (DemoTimeline Timeline, List<PlaybackFrame> Frames) Playback()
    {
        uint state = 577;
        int Next(int below)
        {
            state = (state * 1664525u) + 1013904223u;

            return (int)(state >> 8) % below;
        }

        List<SyntheticEffects.Effect> effects = [];
        int[] owners = [Sentry, Sentry + 1, Sentry + 2];

        for (int tick = 100; tick < 1600; tick += Next(25))
        {
            int owner = owners[Next(owners.Length)];
            int flags = Next(4) switch { 0 => 0, 1 => 3, _ => 1 };

            effects.Add(Effect(
                tick,
                "CTEEffectDispatch",
                Number("m_iEffectName", 1),
                Number("m_nHitBox", Next(2) == 0 ? 1 : 3),
                Number("m_fFlags", flags),
                Number("m_nDamageType", Next(2) == 0 ? 4 : 3),
                Number("entindex", owner),
                Number("m_nAttachmentIndex", 3)));
        }

        List<PlaybackFrame> frames = [];
        int at = 90;

        for (int frame = 0; frame < 3000; frame++)
        {
            int roll = Next(100);
            bool seeked = roll < 2;

            at = roll switch
            {
                < 1 => Math.Max(90, at - 1 - Next(400)),
                < 2 => at + 1 + Next(400),
                < 3 => Math.Max(90, at - 1 - Next(30)),
                < 50 => at,
                _ => at + 1,
            };

            int? toggled = Next(20) == 0 ? owners[Next(owners.Length)] : null;

            frames.Add(new PlaybackFrame(
                at, seeked, toggled, ParticleControlPoint.Unoriented(new Vector3(toggled ?? 0, frame, 0f))));
        }

        return (Timeline([.. effects]), frames);
    }

    /// <summary>FNV-1a over every frame's census and counters, so one number records a whole playback.</summary>
    private sealed class Fingerprint
    {
        public ulong Value { get; private set; } = 14695981039346656037ul;

        public void Fold(ParticleEffects particles)
        {
            Fold(string.Join(';', particles.Census()));
            Fold($"{particles.BurstBuilds},{particles.ReplaySteps},{particles.Steps},{particles.Slept}");
        }

        private void Fold(string text)
        {
            foreach (char c in text)
            {
                Value = (Value ^ c) * 1099511628211ul;
            }
        }
    }
}
