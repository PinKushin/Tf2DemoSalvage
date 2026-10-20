using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Who ends a particle's life (B557 row 3).</summary>
/// <remarks>
/// Read in the disassembly of the SDK's `particles.lib`: `CParticleCollection::Simulate` (`particles.obj`) runs the
/// emitters and operators and applies the kill list (+0x276c) after an operator that wrote it — and has no age test of
/// its own. In `builtin_particle_ops.obj` exactly six operators write that list: `C_OP_Decay` ("Lifespan Decay"),
/// `C_OP_VelocityDecay`, `C_OP_Cull`, `C_OP_PlaneCull`, `C_OP_ModelCull` and `C_OP_FadeAndKill` ("Alpha Fade and Decay").
/// `C_OP_Decay` kills a particle when <c>lifetime &lt;= curtime − creation</c> or <c>lifetime &lt;= 0</c>. So a
/// particle in a system that declares none of them outlives its lifetime, held until the collection is full.
/// </remarks>
[TestFixture]
public sealed class ParticleKillConformanceTests
{
    private static readonly ParticleControlPoint Origin = ParticleControlPoint.Unoriented(Vector3.Zero);

    [Test]
    public void Step_NoOperatorThatKills_KeepsEveryParticlePastItsLifetime()
    {
        ParticleEffect effect = new(System());

        for (int step = 0; step < 200; step++)
        {
            effect.Step(Origin, seconds: 1f / 66f);
        }

        // 66 a second for 200/66 s with a one-second life: every one born is still there.
        effect.Particles.Count.ShouldBe(200);
    }

    [Test]
    public void Step_LifespanDecay_KillsAParticleAtItsLifetime()
    {
        ParticleEffect effect = new(System("Lifespan Decay"));

        for (int step = 0; step < 200; step++)
        {
            effect.Step(Origin, seconds: 1f / 66f);
        }

        // One second's worth, 66, give or take the step a particle dies on.
        effect.Particles.Count.ShouldBeInRange(65, 67);
    }

    /// <remarks>
    /// **A stand-in, not parity**: `Cull Random` kills in the engine and is not implemented here, so its particles are
    /// ended at their lifetime rather than left immortal by our gap. `alpha_fade` is a name TF2's `client.dll` carries and
    /// the SDK's `particles.lib` does not define, so whether it kills is unread: the same stand-in.
    /// </remarks>
    [TestCase("Cull Random")]
    [TestCase("alpha_fade")]
    public void Step_OnlyAKillerNotImplemented_EndsParticlesAtTheirLifetime(string killer)
    {
        ParticleEffect effect = new(System(killer));

        for (int step = 0; step < 200; step++)
        {
            effect.Step(Origin, seconds: 1f / 66f);
        }

        effect.Particles.Count.ShouldBeInRange(65, 67);
    }

    /// <remarks>
    /// `C_OP_ContinuousEmitter::MayCreateMoreParticles` (`0x00067430`, `builtin_particle_emitters.obj`): false when the
    /// context's stopped flag (+0x10) is set or the rate (+0x40) is not above 0; true for a zero duration; otherwise
    /// <c>curtime − dt &lt;= start + duration</c>. So an empty system whose only emitter has rate 0 is finished
    /// (`IsFinished`, particles.h:1119); the control at rate 66 is not, because a zero duration is forever.
    /// </remarks>
    [TestCase(0d, true)]
    [TestCase(66d, false)]
    public void Finished_EmptyWithAContinuousEmitter_IsDecidedByTheRate(double rate, bool expected)
    {
        ParticleSystem system = System() with
        {
            Emitters =
            [
                new ParticleFunction("emit_continuously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["emission_rate"] = new DmxValue(DmxAttributeType.Real, rate),
                }),
            ],
        };

        new ParticleEffect(system).Finished.ShouldBe(expected);
    }

    /// <remarks>
    /// `emission_rate` unpacks with the default `"100"` (`DmxElementUnpackInit&lt;C_OP_ContinuousEmitter&gt;`, +0x40). A
    /// map's pakfile manifest (B554) can ship a system that leaves it out: it emits 100 a second and is not finished.
    /// </remarks>
    [Test]
    public void Step_AContinuousEmitterDeclaringNoRate_EmitsAHundredASecondAndIsNotFinished()
    {
        ParticleEffect effect = new(System() with
        {
            Emitters = [new ParticleFunction("emit_continuously", "emit", Empty)],
        });

        bool finished = effect.Finished;

        for (int step = 0; step < 66; step++)
        {
            effect.Step(Origin, seconds: 1f / 66f);
        }

        (finished, effect.Particles.Count).ShouldBe((false, 100));
    }

    private static readonly Dictionary<string, DmxValue> Empty = new(StringComparer.Ordinal);

    private static ParticleSystem System(params string[] operators) =>
        new(
            "trail",
            [
                new ParticleFunction("emit_continuously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 66d),
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
            [.. Array.ConvertAll(operators, one => new ParticleFunction(one, one, Empty))],
            [],
            [],
            Empty);
}
