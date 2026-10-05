using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The scalar initializers' draws on a seeded collection: `m_nRandomSeed + m_nRandomQueryCount++` (B495).
/// </summary>
/// <remarks>
/// Read in the disassembly of the SDK's `particles.lib`. `builtin_initializers.obj`'s `InitNewParticlesScalar` bodies each
/// read `[collection+0x2740]` (the query count), increment it, and index `s_pRandomFloats[ ( [collection+0x2744] + count )
/// &amp; 0xfff ]`. On a seeded collection `SimulateFirstFrame` and `InitializeNewParticles` (`particles.obj`) call each
/// initializer that is not scrub-safe once per new particle, initializer by initializer. Per particle:
/// <code>
/// C_INIT_RandomLifeTime, RandomRadius, RandomAlpha, CGeneralRandomRotation   one query, RandomFloatExp
/// C_INIT_RandomSequence, RandomColor (all three channels)                    one query
/// C_INIT_CreateWithinSphere   one query for the sphere vector; one for the speed when speed_max &gt; 0; one per local axis
/// C_INIT_PositionOffset       one query, entries query + 2·seed + 0, 1, 2  (the seed added twice)
/// C_INIT_MoveBetweenPoints    one for the end spread when it is &gt; 0; one for the speed
/// C_INIT_CreateAlongPath      two queries, entries q (t) and q + 1, q + 2, q + 3 (the jitter)
/// C_OP_InstantaneousEmitter::InitializeContextData   one query at Init, for a count with a minimum
/// </code>
/// </remarks>
[TestFixture]
public sealed class ParticleQueryConformanceTests
{
    private const int Seed = 100;

    /// <remarks>Lifetime runs over both particles before rotation runs over either: queries 0, 1 then 2, 3.</remarks>
    [Test]
    public void Step_TwoInitializersOverTwoParticles_DrawInitializerByInitializer()
    {
        ParticleEffect effect = new(Burst(2, Lifetime(), Rotation()), others: null, sheets: null, seed: Seed);

        effect.Step(ParticleControlPoint.Unset, 0.01f);

        (effect.Particles.LifetimeOf(1), effect.Particles.RotationOf(0), effect.Particles.Queries)
            .ShouldBe((Lerp(1, 10f, 20f), float.DegreesToRadians(Lerp(2, 0f, 90f)), 4));
    }

    /// <remarks>The count is drawn at construction, so the first particle's lifetime is query 1, not 0.</remarks>
    [Test]
    public void Constructor_ABurstWithAMinimum_DrawsItsCountFirst()
    {
        ParticleEffect effect = new(Burst(5, Lifetime()) with { Emitters = [Emitter(5, minimum: 0)] }, others: null, sheets: null, seed: Seed);
        int drawn = effect.Particles.Queries;

        effect.Step(ParticleControlPoint.Unset, 0.01f);

        (drawn, effect.Particles.Count, effect.Particles.LifetimeOf(0))
            .ShouldBe((1, ParticleRandom.Whole(Seed, 0, 0, 0, 5), Lerp(1, 10f, 20f)));
    }

    /// <remarks>
    /// No outward speed, so the local speed's three axes are queries 1, 2 and 3 — three draws, where this port took one
    /// for all three. `PREV_XYZ = XYZ − velocity · m_flPreviousDt`, 0.05 on the first call.
    /// </remarks>
    [Test]
    public void Step_ASphereLocalSpeed_DrawsEachAxisSeparately()
    {
        ParticleEffect effect = new(Burst(1, Sphere()), others: null, sheets: null, seed: Seed);

        effect.Step(Frame, 0.01f);

        Vector3 launched = (effect.Particles.PositionOf(0) - effect.Particles.PreviousOf(0)) / 0.05f;

        launched.X.ShouldBe(Lerp(1, 0f, 10f), 1e-4f);
        launched.Y.ShouldBe(-Lerp(2, 0f, 10f), 1e-4f, "local +Y times m_RightVector, which is −Y for this frame");
        launched.Z.ShouldBe(Lerp(3, 0f, 10f), 1e-4f);
    }

    /// <remarks>`RandomVector( m_nRandomQueryCount++, … )` adds the seed, then `RandomFloat` adds it again.</remarks>
    [Test]
    public void Step_AnOffset_IndexesWithTheSeedTwice()
    {
        ParticleEffect effect = new(Burst(1, Offset()), others: null, sheets: null, seed: Seed);

        effect.Step(ParticleControlPoint.Unset, 0.01f);

        effect.Particles.PositionOf(0).ShouldBe(new Vector3(
            10f * ParticleRandom.Sample(Seed, Seed, 0),
            10f * ParticleRandom.Sample(Seed, Seed, 1),
            10f * ParticleRandom.Sample(Seed, Seed, 2)));
    }

    /// <summary>`r · ( most − least ) + least` for query <paramref name="query"/> of a collection seeded <see cref="Seed"/>.</summary>
    private static float Lerp(int query, float least, float most) =>
        (ParticleRandom.Sample(Seed, query, 0) * (most - least)) + least;

    private static readonly ParticleControlPoint Frame = new(Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    private static ParticleFunction Emitter(int count, int minimum = -1) =>
        new("emit_instantaneously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, count),
            ["num_to_emit_minimum"] = new DmxValue(DmxAttributeType.Whole, minimum),
        });

    private static ParticleSystem Burst(int count, params ParticleFunction[] initializers) =>
        new("burst", [Emitter(count)], initializers, [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    private static ParticleFunction Lifetime() =>
        new("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 10d),
            ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 20d),
        });

    private static ParticleFunction Rotation() =>
        new("Rotation Random", "turn", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["rotation_offset_min"] = new DmxValue(DmxAttributeType.Real, 0d),
            ["rotation_offset_max"] = new DmxValue(DmxAttributeType.Real, 90d),
        });

    private static ParticleFunction Sphere() =>
        new("Position Within Sphere Random", "place", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["speed_in_local_coordinate_system_min"] = new DmxValue(DmxAttributeType.Vector3, Vector: Vector4.Zero),
            ["speed_in_local_coordinate_system_max"] = new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(10f, 10f, 10f, 0f)),
        });

    private static ParticleFunction Offset() =>
        new("Position Modify Offset Random", "offset", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["offset min"] = new DmxValue(DmxAttributeType.Vector3, Vector: Vector4.Zero),
            ["offset max"] = new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(10f, 10f, 10f, 0f)),
        });
}
