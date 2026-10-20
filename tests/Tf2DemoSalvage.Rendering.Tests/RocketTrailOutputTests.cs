using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A rocket's trail through the effect the viewer runs, on the install's own `rockettrail` (B490-B496).</summary>
/// <remarks>
/// **The output-level half of the particle follow-ups.** The chain is `MainForm`'s: the rocket's frame from
/// <see cref="AngleVectors"/>, <see cref="ParticleEffects.Update"/> with the install's own `rockettrail` as
/// <see cref="MapAssets"/> loads it, and
/// <see cref="ParticleEffects.Build(Vector3, Vector3, Vector3, IReadOnlyDictionary{string, ParticleMaterial})"/>.
///
/// **The rocket is written down, not read from a recording** (D213): z1800's entity 573, a `CTFProjectile_Rocket`, as
/// `moment-dump z1800 17318 573` printed it on 2026-10-11 — the only things this test takes from it are where it is,
/// which way it points and what class it is. The installed `rockettrail` declares `initial_particles 1` and
/// `emission_rate 150` (measured through this test, 2026-10-04 — not the 128 older notes quote); one tick is 0.015 s,
/// one sub-step.
/// </remarks>
public sealed class RocketTrailOutputTests
{
    private const int Rocket = 573;

    private const int FirstTick = 17318;

    /// <summary>z1800's tick: 66.67 a second.</summary>
    private const float IntervalPerTick = 0.015f;

    /// <remarks>
    /// **The first tick draws the initial puff alone; the next adds two.** The trail's creation frame is
    /// `Simulate( 0.0f )` — `ProcessPSystem` on the first-frame flag (particlemgr.cpp:1572-1575, set at construction,
    /// particles_new.cpp:53; the older binding the same, :135 and :1977-1980), read from published source — and
    /// `CParticleCollection::Simulate` runs `SimulateFirstFrame` and then stops at its `dt >= 1e-22` test (particles.obj,
    /// disassembly): the initial particle, born at 0 inside `rockettrail`'s 1.2-unit sphere, and nothing emitted (B557 row 6).
    /// **The expectation this replaced was our older model's**, which stepped the creation frame: "three puffs where it
    /// drew two" is that model, not the engine. The second tick is the first simulated one: the emitter adds
    /// `floor( 0.015 · 150 )` = 2, dated half and whole way through the step (B470). Six corners per particle drawn.
    /// </remarks>
    [Test]
    public void Update_ARocketsFirstTick_DrawsItsInitialPuffOnTheRocket()
    {
        MapAssets assets = MapCache.Load();
        ParticleSystem trail = assets.RocketTrail.ShouldNotBeNull("the install's rockettrail must load");

        SceneProp rocket = new(
            EntityIndex: Rocket,
            ModelPath: "models/weapons/w_models/w_rocket.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose { X = -921f, Y = -1549f, Z = 91f, Pitch = 5.714286f, Yaw = 85.71429f },
            ClassName: ParticleEffects.RocketClass);

        ParticleControlPoint here = Frame(rocket);
        ParticleEffects effects = new();

        // Playback arrives from the tick before, with the rocket not yet alive; a first call from nowhere is a seek.
        effects.Update([], trail, IntervalPerTick, assets.ParticleSystemsByName, FirstTick - 1);
        effects.Update([(Rocket, here, null, 0)], trail, IntervalPerTick, assets.ParticleSystemsByName, FirstTick);

        ParticleStore puffs = effects.TrailEffect(Rocket).ShouldNotBeNull().Particles;

        (puffs.Count, puffs.AgeOf(0)).ShouldBe((1, 0f), "the creation frame: the initial particle, nothing simulated");
        Vector3.Distance(puffs.PositionOf(0), here.At).ShouldBeLessThan(2f, "inside the 1.2-unit sphere");

        effects.Update([(Rocket, here, null, 1)], trail, IntervalPerTick, assets.ParticleSystemsByName, FirstTick + 1);

        puffs.Count.ShouldBe(3, "one initial particle and two emitted on the first simulated tick");
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
