using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// **An effect is drawn when a view collates it, and only drawing keeps it awake** (finding 84, D1). The engine pushes
/// `m_flNextSleepTime` only in `CParticleCollection::Render` (and `DrawModel`'s too-far early-out,
/// particles_new.cpp:528-538), and `DrawModel` is reached only for a renderable the leaf system collected for a view: its
/// box in a listed leaf and inside the frustum (clientleafsystem.cpp:1574-1716). `BuildParticleSimList` then skips an
/// effect at or past its sleep time with live particles (particlemgr.cpp:1775), and its clock stands.
/// </summary>
/// <remarks>
/// The camera looks down −X from the origin: an effect at x = −100 is in view, one at +100 behind it. The definition
/// sleeps 0.15 s (9.9 ticks) after its last draw and emits for ever.
/// </remarks>
[TestFixture]
public sealed class ParticleCollationConformanceTests
{
    private const float Interval = 1f / 66f;

    private static readonly CollationView Looking = new(
        ViewFrustum.Perspective((0f, 0f, 0f), (-1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f), 1f, 10000f, 90f, 60f),
        new RenderableViews(null, null, default),
        Tree: null);

    private static readonly IReadOnlyDictionary<string, ParticleMaterial> NoMaterials =
        new Dictionary<string, ParticleMaterial>(StringComparer.Ordinal);

    /// <remarks>
    /// Created at tick 100 with its sleep at creation plus the no-draw time (`StartEmission`), a burst never drawn steps
    /// ticks 101 to 110 and then stands; one drawn every frame has its sleep pushed on every frame and steps all forty. Only
    /// the drawn one is handed to the draw: 40 frames with particles (tick 100 has none yet).
    /// </remarks>
    [Test]
    public void Build_WithAViewThatDoesNotSeeABurst_LetsItSleepAndDrawsOnlyTheSeen()
    {
        ParticleEffects effects = new();
        ParticleBurst seen = new(1, Sleepy(), At(-100f), 100);
        ParticleBurst behind = new(2, Sleepy(), At(100f), 100);

        for (int tick = 100; tick <= 140; tick++)
        {
            effects.Bursts([seen, behind], Interval, null, tick);
            effects.Build(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, NoMaterials, Looking);
        }

        effects.BurstSteps(1).ShouldBe(40, "drawn every frame, so never asleep");
        effects.BurstSteps(2).ShouldBe(10, "never drawn: asleep from creation plus 9.9 ticks, its clock standing");
        effects.Gathered.ShouldBe(40, "only the seen one is handed to the draw");
    }

    /// <remarks>The control: with no view to collate by, every effect counts as drawn, as before.</remarks>
    [Test]
    public void Build_WithNoView_DrawsAndWakesEveryEffect()
    {
        ParticleEffects effects = new();
        ParticleBurst behind = new(2, Sleepy(), At(100f), 100);

        for (int tick = 100; tick <= 140; tick++)
        {
            effects.Bursts([behind], Interval, null, tick);
            effects.Build(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, NoMaterials);
        }

        effects.BurstSteps(2).ShouldBe(40);
    }

    /// <remarks>
    /// **Whether a view decides is the frame's, not the instance's** (lead's review): a frame drawn with no view pushes
    /// every effect's sleep time, as before, whatever an earlier frame did. Here a view-given frame, then `Clear` (a new
    /// demo), then twenty seconds of frames with none: the effect behind the camera is stepped on every one of them.
    /// </remarks>
    [Test]
    public void Build_WithNoViewAfterAViewAndAClear_PushesEverySleepAgain()
    {
        ParticleEffects effects = new();
        ParticleBurst behind = new(2, Sleepy(), At(100f), 100);

        effects.Bursts([behind], Interval, null, tick: 100);
        effects.Build(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, NoMaterials, Looking);
        effects.Clear();

        for (int tick = 100; tick <= 100 + 1320; tick++)
        {
            effects.Bursts([behind], Interval, null, tick);
            effects.Build(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, NoMaterials);
        }

        effects.BurstSteps(2).ShouldBe(1320, "drawn on every frame with no view, so never asleep");
    }

    /// <remarks>
    /// **A trail sleeps the same way**, by this frame's time against its sleep time, as `BuildParticleSimList` tests
    /// `flNow` (particlemgr.cpp:1775). Created at tick 0, the one behind steps at ticks 1 to 9 and is asleep at tick 10 (its
    /// sleep at 9.9): its own clock stands at nine ticks. The seen one is drawn every frame and ages all forty.
    /// </remarks>
    [Test]
    public void Build_WithAViewThatDoesNotSeeATrail_LetsItSleep()
    {
        ParticleEffects effects = new();
        ParticleSystem smoke = Sleepy();

        for (int tick = 0; tick <= 40; tick++)
        {
            effects.Update([(1, At(-100f), null, 0), (2, At(100f), null, 0)], smoke, Interval, tick: tick);
            effects.Build(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, NoMaterials, Looking);
        }

        effects.TrailEffect(1)!.Particles.Age.ShouldBe(40 * Interval, 1e-4f);
        effects.TrailEffect(2)!.Particles.Age.ShouldBe(9 * Interval, 1e-4f);
    }

    private static ParticleControlPoint At(float x) => new(new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    private static ParticleSystem Sleepy() => new(
        Name: "test_sleepy",
        Emitters:
        [
            new ParticleFunction("emit_continuously", "emitter", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["emission_rate"] = new(DmxAttributeType.Real, 66d),
            }),
        ],
        Initializers:
        [
            new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["lifetime_min"] = new(DmxAttributeType.Real, 0.5d),
                ["lifetime_max"] = new(DmxAttributeType.Real, 0.5d),
            }),
        ],
        Operators: [new ParticleFunction("Lifespan Decay", "decay", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
        Renderers: [],
        Children: [],
        Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["time to sleep when not drawn"] = new(DmxAttributeType.Real, 0.15d),
        });
}
