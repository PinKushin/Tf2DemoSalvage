using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A real rocket's real trail, out of a real demo, through the effect the viewer runs (B490-B496).</summary>
/// <remarks>
/// **The output-level half of the particle follow-ups.** The chain is `MainForm`'s: <see cref="DemoTimeline.PropsAt"/>
/// each tick, the rocket's frame from <see cref="AngleVectors"/>, <see cref="ParticleEffects.Update"/> with the install's own
/// `rockettrail` as <see cref="MapAssets"/> loads it, and <see cref="ParticleEffects.Build(Vector3, Vector3, Vector3, IReadOnlyDictionary{string, ParticleMaterial})"/>.
///
/// **The specimen is z1800's entity 573**, a `CTFProjectile_Rocket` live from tick 17318 to 17346 (`projectiles` probe,
/// 2026-10-04). The installed `rockettrail` declares `initial_particles 1` and `emission_rate 150` (measured through this
/// test, 2026-10-04 — not the 128 older notes quote); one tick is 0.015 s, one sub-step.
/// </remarks>
public sealed class RocketTrailOutputTests
{
    private const string DemoName = "z1800.dem";

    private const int Rocket = 573;

    private const int FirstTick = 17318;

    /// <remarks>
    /// **The first tick draws three puffs where it drew two.** `SimulateFirstFrame` makes the initial particle on the
    /// rocket before the emitter runs (B491), and the emitter adds `floor( 0.015 · 150 )` = 2, dated half and whole way
    /// through the step (B470). The initial one is born at time 0 inside `rockettrail`'s 1.2-unit sphere around the
    /// rocket, and one step of its launch moves it well under a unit more. They reach the renderer: the batches carry six
    /// corners per particle drawn.
    /// </remarks>
    [Test]
    public void Update_ARocketsFirstTickFromARealDemo_DrawsItsInitialPuffOnTheRocket()
    {
        string demo = CommittedDemo.Require(DemoName);
        string tf = GameInstall.Require();
        byte[] map = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_harvest_final.bsp"));

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        MapAssets assets = MapAssets.Load(map, GameArchives.Open(tf), maximumTextureSize: 64);
        ParticleSystem trail = assets.RocketTrail.ShouldNotBeNull("the install's rockettrail must load");

        List<SceneProp> props = [];
        timeline.PropsAt(FirstTick, props);

        SceneProp rocket = props.Single(prop => prop.EntityIndex == Rocket);
        rocket.ClassName.ShouldBe(ParticleEffects.RocketClass, "the specimen must still be the rocket this test was written against");

        ParticleControlPoint here = Frame(rocket);
        ParticleEffects effects = new();

        // Playback arrives from the tick before, with the rocket not yet alive; a first call from nowhere is a seek.
        effects.Update([], trail, timeline.IntervalPerTick, assets.ParticleSystemsByName, FirstTick - 1);
        effects.Update([(Rocket, here, null, 0)], trail, timeline.IntervalPerTick, assets.ParticleSystemsByName, FirstTick);

        ParticleStore puffs = effects.TrailEffect(Rocket).ShouldNotBeNull().Particles;

        puffs.Count.ShouldBe(3, "one initial particle and two emitted");
        Enumerable.Range(0, puffs.Count).Select(puffs.AgeOf).ShouldBe([0.015f, 0.0075f, 0f], "born at 0, then spread across the step");
        Vector3.Distance(puffs.PositionOf(0), here.At).ShouldBeLessThan(2f, "inside the 1.2-unit sphere, one step on");

        Vector3 eye = here.At + new Vector3(-200f, 0f, 0f);
        IReadOnlyList<ParticleBatch> batches = effects.Build(eye, -Vector3.UnitY, Vector3.UnitZ, assets.ParticleMaterials);

        batches.Sum(batch => batch.Corners.Count).ShouldBeGreaterThanOrEqualTo(18, "the trail's three puffs reach the renderer");
    }

    /// <summary>The rocket's own frame, as `MainForm` builds it for `PATTACH_POINT_FOLLOW`.</summary>
    private static ParticleControlPoint Frame(SceneProp rocket)
    {
        (float fx, float fy, float fz) = AngleVectors.Forward(rocket.Pose.Pitch, rocket.Pose.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(rocket.Pose.Pitch, rocket.Pose.Yaw, rocket.Pose.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(rocket.Pose.Pitch, rocket.Pose.Yaw, rocket.Pose.Roll);

        return new ParticleControlPoint(
            new Vector3(rocket.Pose.X, rocket.Pose.Y, rocket.Pose.Z),
            new Vector3(fx, fy, fz),
            new Vector3(rx, ry, rz),
            new Vector3(ux, uy, uz));
    }
}
