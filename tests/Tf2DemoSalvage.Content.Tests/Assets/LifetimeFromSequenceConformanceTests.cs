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

    [Test]
    public void Spawn_RemapInitialScalar_DrawsBirthTimeOntoRotation()
    {
        // `C_INIT_RemapScalar::InitNewParticlesScalar`: the operator's straight line, once, at birth. The medigun's curls
        // map creation time 0..1000 onto rotation 0..8000 — a particle born two seconds in turns to 16 radians.
        ParticleStore particles = new();

        particles.Tick(2f);

        ParticleSystems.Spawn(
            new ParticleSystem(
                "test",
                [],
                [
                    new ParticleFunction("remap initial scalar", "remap", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["input field"] = new DmxValue(DmxAttributeType.Whole, Number: 8d),
                        ["input maximum"] = new DmxValue(DmxAttributeType.Real, Number: 1000d),
                        ["output field"] = new DmxValue(DmxAttributeType.Whole, Number: 4d),
                        ["output maximum"] = new DmxValue(DmxAttributeType.Real, Number: 8000d),
                    }),
                ],
                [],
                [],
                [],
                new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
            particles,
            Here,
            lives: 1f,
            seconds: 0f);

        particles.RotationOf(0).ShouldBe(16f, 1e-3f);
    }

    [Test]
    public void Spawn_RemapInitialScalarOntoLifeDuration_SetsTheLifetime()
    {
        // `muzzle_shotgun_flash` maps creation time 0..0.05 onto LIFE_DURATION (field 1) 0.1..0.05. Without it the
        // flash lives the default second and its quads pile up down the line of fire.
        ParticleStore particles = new();

        particles.Tick(0.025f);

        ParticleSystems.Spawn(Remapping(1, 0.05d, 0.1d, 0.05d), particles, Here, lives: 1f, seconds: 0f);

        particles.LifetimeOf(0).ShouldBe(0.075f, 1e-6f);
    }

    [Test]
    public void Spawn_RemapInitialScalarOntoRadius_IsTheRadiusAtBirth()
    {
        // `CopyInitialAttributeValues` (`particles.h:1265`) runs after EVERY initializer, so the spawn radius an operator
        // scales from is the remapped one. Copied inside `Radius Random` instead, it stayed 32 while the radius was 8.
        ParticleStore particles = new();

        ParticleSystems.Spawn(Remapping(3, 1d, 8d, 8d), particles, Here, lives: 1f, seconds: 0f);

        particles.RadiusAtBirth[0].ShouldBe(8f);
    }

    /// <summary>A system of `Radius Random` 32 then one `remap initial scalar` of creation time onto a field.</summary>
    private static ParticleSystem Remapping(int output, double inputMaximum, double outputMinimum, double outputMaximum) =>
        new(
            "test",
            [],
            [
                new ParticleFunction("Radius Random", "Radius Random", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["radius_min"] = new DmxValue(DmxAttributeType.Real, Number: 32d),
                    ["radius_max"] = new DmxValue(DmxAttributeType.Real, Number: 32d),
                }),
                new ParticleFunction("remap initial scalar", "remap", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["input field"] = new DmxValue(DmxAttributeType.Whole, Number: 8d),
                    ["input maximum"] = new DmxValue(DmxAttributeType.Real, Number: inputMaximum),
                    ["output field"] = new DmxValue(DmxAttributeType.Whole, Number: output),
                    ["output minimum"] = new DmxValue(DmxAttributeType.Real, Number: outputMinimum),
                    ["output maximum"] = new DmxValue(DmxAttributeType.Real, Number: outputMaximum),
                }),
            ],
            [],
            [],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal));

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
