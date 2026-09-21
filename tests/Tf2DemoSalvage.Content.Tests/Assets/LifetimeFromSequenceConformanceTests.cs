using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>`C_INIT_SequenceLifeTime::InitNewParticlesScalar`, read out of `particles.lib` (B415).</summary>
/// <remarks>
/// <code>
/// if ( m_flFramerate != 0 &amp;&amp; pParticles->m_Sheet ):
///     span = sheet->m_flFrameSpan[ (int)SEQUENCE_NUMBER ]
///     LIFE_DURATION = span != 0 ? span / m_flFramerate : 1
/// </code>
/// "Frames Per Second" defaults to 30 in its unpack table.
/// </remarks>
public sealed class LifetimeFromSequenceConformanceTests
{
    [Test]
    public void Spawn_LifetimeFromSequence_IsTheSequencesSpanOverTheRate()
    {
        ParticleStore particles = new();

        ParticleSystems.Spawn(System(), particles, Here, lives: 2f, seconds: 0f, sheet: [Sequence(0, 15f)]);

        particles.LifetimeOf(0).ShouldBe(0.5f, 1e-6d, "15 over the default 30 frames a second");
    }

    [Test]
    public void Spawn_LifetimeFromSequenceWithNoSheet_KeepsTheLifetime()
    {
        // `pParticles->m_Sheet` is null for a material that carries no sheet, and the initializer does nothing.
        ParticleStore particles = new();

        ParticleSystems.Spawn(System(), particles, Here, lives: 2f, seconds: 0f, sheet: null);

        particles.LifetimeOf(0).ShouldBe(2f);
    }

    [Test]
    public void Spawn_LifetimeFromSequenceWhoseSpanIsZero_IsOneSecond()
    {
        // The sheet has no sequence 0, so its span is zero and the life is 1.
        ParticleStore particles = new();

        ParticleSystems.Spawn(System(), particles, Here, lives: 2f, seconds: 0f, sheet: [Sequence(1, 15f)]);

        particles.LifetimeOf(0).ShouldBe(1f);
    }

    private static readonly ParticleControlPoint Here = ParticleControlPoint.Unoriented(Vector3.Zero);

    private static SheetSequence Sequence(int id, float span) => new(id, false, span, []);

    private static ParticleSystem System() =>
        new(
            "test",
            [],
            [new ParticleFunction("Lifetime From Sequence", "Lifetime From Sequence", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
            [],
            [],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal));
}
