using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The particle phase's work, counted where it happens (B243): effects alive, steps taken on played frames, effects
/// skipped asleep, and effects the gather handed to the draw. Each counter's control is a frame whose work is known.
/// </summary>
[TestFixture]
public sealed class ParticleWorkCounterTests
{
    private const float Interval = 1f / 66f;

    private static readonly ParticleControlPoint Origin = new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    [Test]
    public void Steps_ABurstPlayedFiveTicks_CountsFiveAndAReplayNone()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(1, Beam(sleep: 8d), Origin, 100);

        for (int tick = 100; tick <= 105; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Steps.ShouldBe(5, "one step a played frame, from tick 101 to 105");

        // A burst met mid-life is replayed, which the replay counter has, not this one.
        ParticleEffects late = new();
        late.Bursts([beam], Interval, null, tick: 140);
        late.Steps.ShouldBe(0);
        late.ReplaySteps.ShouldBe(40);
    }

    [Test]
    public void Alive_EveryCall_AddsTheEffectsHeld()
    {
        ParticleEffects effects = new();
        ParticleBurst one = new(1, Beam(sleep: 8d), Origin, 100);
        ParticleBurst two = new(2, Beam(sleep: 8d), Origin, 100);

        effects.Bursts([one, two], Interval, null, tick: 100);
        effects.Bursts([one, two], Interval, null, tick: 101);

        effects.Alive.ShouldBe(4, "two held, on each of two frames");
    }

    /// <remarks>
    /// A 0.15 s sleep is 9.9 ticks: drawn to tick 110, it steps ten more undrawn ticks (111 to 120) and then stands. From
    /// tick 121 to 130 it is asleep: ten frames skipped, none stepped.
    /// </remarks>
    [Test]
    public void Slept_AnUndrawnBurstPastItsNoDrawTime_CountsEachFrameItStands()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(1, Beam(sleep: 0.15d), Origin, 100);

        for (int tick = 100; tick <= 110; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        for (int tick = 111; tick <= 130; tick++)
        {
            effects.Bursts([beam with { Undrawn = true }], Interval, null, tick);
        }

        effects.Steps.ShouldBe(20, "ten drawn, then ten undrawn before the sleep");
        effects.Slept.ShouldBe(10, "ticks 121 to 130");
    }

    [Test]
    public void Gathered_ABuild_CountsTheEffectsWithParticlesHandedToTheDraw()
    {
        ParticleEffects effects = new();
        ParticleBurst live = new(1, Beam(sleep: 8d), Origin, 100);
        ParticleBurst none = new(2, Beam(sleep: 8d) with { Emitters = [] }, Origin, 100);

        effects.Bursts([live, none], Interval, null, tick: 100);
        effects.Bursts([live, none], Interval, null, tick: 101);
        effects.Build(Vector3.UnitX * -100f, Vector3.UnitY, Vector3.UnitZ, new Dictionary<string, ParticleMaterial>(StringComparer.Ordinal));

        effects.Gathered.ShouldBe(1, "the emitting one; the empty one has nothing to draw");
    }

    private static ParticleSystem Beam(double sleep) => new(
        Name: "test_beam",
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
            ["time to sleep when not drawn"] = new(DmxAttributeType.Real, sleep),
        });
}
