using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// One running particle system: emit, operate, reap (B373).
/// </summary>
[TestFixture]
public sealed class ParticleEffectConformanceTests
{
    [Test]
    public void Step_AFractionalEmissionRate_CarriesTheRemainderRatherThanLosingIt()
    {
        // **The rate is a fraction of a particle per step and truncating each step loses a third of
        // the trail.** At the shipped `emission_rate 128` on a 66-tick clock that is 1.94 a step:
        // truncating emits 1 and rounding emits 2, and only carrying the remainder gives the
        // declared rate over time.
        //
        // Ten steps at 1.5 a step must produce 15, not 10 and not 20.
        ParticleEffect effect = new(System("emission_rate", 99d));

        for (int step = 0; step < 10; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        // 99 per second over 10/66 of a second = 15.
        effect.Particles.Count.ShouldBe(15);
    }

    [Test]
    public void Step_AnEmissionDurationOfZero_MeansForeverAndNotNever()
    {
        // **The one sentinel in this file**, and reading it the other way emits nothing at all.
        // `rockettrail` declares `emission_duration 0` and trails for as long as the rocket flies.
        ParticleEffect effect = new(System("emission_rate", 66d));

        for (int step = 0; step < 30; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        effect.Particles.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Step_ParticlesPastTheirLifetime_AreGoneRatherThanAccumulating()
    {
        // The reap, and the control is that SOME survive: an effect that removed everything would
        // pass a bare "count stops growing" assertion.
        ParticleEffect effect = new(System("emission_rate", 66d));

        for (int step = 0; step < 200; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        // A lifetime of 1 second at 66 a second settles near 66, and certainly not near 200.
        effect.Particles.Count.ShouldBeLessThan(100);
        effect.Particles.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Step_AtMaxParticles_StopsEmittingRatherThanGrowing()
    {
        // `max_particles` is the engine's collection size, so a system at its cap emits nothing.
        Dictionary<string, DmxValue> declared = new(StringComparer.Ordinal)
        {
            ["max_particles"] = new DmxValue(DmxAttributeType.Whole, 5d),
        };

        ParticleEffect effect = new(System("emission_rate", 660d, declared));

        for (int step = 0; step < 20; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        effect.Particles.Count.ShouldBe(5);
    }

    [Test]
    public void Implemented_ASystemNamingAnUnknownOperator_SaysSoRatherThanSkippingSilently()
    {
        // **A missing operator is invisible in the result** - the effect still draws, just wrongly.
        // So the count is reportable, which is what let the probe say "4 of 4" for rockettrail and
        // "2 of 4" before the two it actually uses were written.
        ParticleSystem system = new(
            "trail",
            [],
            [],
            [
                new ParticleFunction("Movement Basic", "move", Empty),
                new ParticleFunction("Some Operator We Have Not Written", "odd", Empty),
            ],
            [],
            [],
            Empty);

        new ParticleEffect(system).Implemented().ShouldBe(1);
    }

    /// <summary>No parameters, so everything takes its default.</summary>
    private static readonly Dictionary<string, DmxValue> Empty = new(StringComparer.Ordinal);

    /// <summary>A system with one continuous emitter and a one-second lifetime.</summary>
    private static ParticleSystem System(
        string named, double value, Dictionary<string, DmxValue>? definition = null) =>
        new(
            "trail",
            [
                new ParticleFunction("emit_continuously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        [named] = new DmxValue(DmxAttributeType.Real, value),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 1d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 1d),
                    }),
            ],
            [],
            [],
            [],
            definition ?? Empty);
}
