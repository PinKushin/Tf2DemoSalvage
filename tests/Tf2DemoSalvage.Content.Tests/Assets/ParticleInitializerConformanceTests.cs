using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The initializers that run once, at birth — placement, rotation and blending (B373).
/// </summary>
/// <remarks>
/// **Every value asserted here is one `rockettrail.pcf` states**, so a failure names a divergence
/// from the shipped effect rather than from a number somebody picked.
/// </remarks>
[TestFixture]
public sealed class ParticleInitializerConformanceTests
{
    [Test]
    public void InUnitSphere_EverySample_IsInsideTheUnitSphere()
    {
        for (int particle = 0; particle < 512; particle++)
        {
            (Vector3 point, float radius) = ParticleRandom.InUnitSphere(particle, offset: 0);

            radius.ShouldBeLessThanOrEqualTo(1f);
            point.Length().ShouldBeLessThanOrEqualTo(1.0001f);

            // The returned radius IS the point's length; a caller normalising by it would divide by
            // the wrong number otherwise.
            point.Length().ShouldBe(radius, 0.0001d);
        }
    }

    [Test]
    public void InUnitSphere_TheRadii_AreCubeRootDistributedRatherThanUniform()
    {
        // **The assertion that catches a missing cube root**, which is the one part of
        // `RandomVectorInUnitSphere` a hand-rolled version gets wrong. Uniform in VOLUME means half
        // the points lie beyond r = 0.5^(1/3) = 0.7937, because that radius encloses half the ball.
        // A uniform radius would put only ~21% of them there, and the puff would spawn as a dense
        // core with a thin halo.
        int beyond = 0;

        for (int particle = 0; particle < 4096; particle++)
        {
            if (ParticleRandom.InUnitSphere(particle, offset: 0).Radius > 0.7937f)
            {
                beyond++;
            }
        }

        // Half of 4,096 is 2,048; a uniform radius would give about 845.
        beyond.ShouldBeInRange(1800, 2300);
    }

    [Test]
    public void Spawn_PositionWithinSphereRandom_PlacesInTheSphereAndGivesItSpeed()
    {
        // rockettrail's own numbers: a 1.2-unit sphere, one unit per second outward, ten per second
        // down the control point's LOCAL Z.
        ParticleSystem system = Declaring(new ParticleFunction(
            "Position Within Sphere Random",
            "place",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["distance_min"] = new DmxValue(DmxAttributeType.Real, 0d),
                ["distance_max"] = new DmxValue(DmxAttributeType.Real, 1.2d),
                ["speed_min"] = new DmxValue(DmxAttributeType.Real, 1d),
                ["speed_max"] = new DmxValue(DmxAttributeType.Real, 1d),
                ["speed_in_local_coordinate_system_min"] =
                    new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, -10f, 0f)),
                ["speed_in_local_coordinate_system_max"] =
                    new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, -10f, 0f)),
            }));

        ParticleStore store = new();

        // A point 100 units up, with the world axes as its own, so "local Z" is world Z.
        ParticleControlPoint point = new(
            new Vector3(0f, 0f, 100f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

        const float step = 1f / 66f;

        bool moved = false;

        for (int one = 0; one < 64; one++)
        {
            int index = ParticleSystems.Spawn(system, store, point, lives: 1f, seconds: step);

            // Inside the declared sphere, and NOT at the control point — the whole difference
            // between a plume and a line.
            float away = (store.PositionOf(index) - point.At).Length();

            away.ShouldBeLessThanOrEqualTo(1.2001f);

            moved |= away > 0.05f;

            // Verlet carries `position - previous` as the step's displacement, so the local-frame
            // speed of -10 must show as PREVIOUS sitting ABOVE the particle.
            Vector3 carried = store.PositionOf(index) - store.Previous[index];

            (carried.Z / step).ShouldBeLessThan(0f);
        }

        moved.ShouldBeTrue();
    }

    [Test]
    public void Spawn_RotationRandom_TurnsEachCardWithinTheDeclaredBand()
    {
        // rockettrail: rotation_initial -45, offset 0..45. So every puff is between -45 and 0
        // degrees and no two need agree - which is what stops a trail looking like a grid.
        ParticleSystem system = Declaring(new ParticleFunction(
            "Rotation Random",
            "turn",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["rotation_initial"] = new DmxValue(DmxAttributeType.Real, -45d),
                ["rotation_offset_min"] = new DmxValue(DmxAttributeType.Real, 0d),
                ["rotation_offset_max"] = new DmxValue(DmxAttributeType.Real, 45d),
            }));

        ParticleStore store = new();

        float least = float.MaxValue;
        float most = float.MinValue;

        for (int one = 0; one < 128; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, ParticleControlPoint.Unoriented(Vector3.Zero),
                lives: 1f, seconds: 1f / 66f);

            float degrees = float.RadiansToDegrees(store.RotationOf(index));

            degrees.ShouldBeInRange(-45.001f, 0.001f);

            least = System.MathF.Min(least, degrees);
            most = System.MathF.Max(most, degrees);
        }

        // **They must actually DIFFER.** Every card at the same angle passes the range assertion
        // above and still draws the grid this initializer exists to break up.
        (most - least).ShouldBeGreaterThan(30f);
    }

    [Test]
    public void SpriteBlending_AddSelf_OutranksAdditive()
    {
        // `spritecard.cpp:255` tests bAddSelf and bAddOverBlend BEFORE MATERIAL_VAR_ADDITIVE, so a
        // material setting both takes the first branch. Reversing the order gives 41 of TF2's
        // materials the wrong blend, and none of them would throw.
        Material("\"SpriteCard\" { \"$additive\" \"1\" \"$addself\" \"0.5\" }")
            .SpriteBlending.ShouldBe(SpriteBlend.AddOver);

        Material("\"SpriteCard\" { \"$additive\" \"1\" }")
            .SpriteBlending.ShouldBe(SpriteBlend.Additive);

        Material("\"SpriteCard\" { \"$addoverblend\" \"1\" }")
            .SpriteBlending.ShouldBe(SpriteBlend.AddOver);
    }

    [Test]
    public void SpriteBlending_ADeclaredZero_IsOffRatherThanPresent()
    {
        // Absent and "0" both mean off, and they arrive differently: treating presence alone as on
        // makes `$additive 0` additive.
        Material("\"SpriteCard\" { \"$additive\" \"0\" }")
            .SpriteBlending.ShouldBe(SpriteBlend.Translucent);

        // rocketrailsmoke itself, which is why the trail looked right before any of this existed.
        Material("\"SpriteCard\" { \"$basetexture\" \"effects/smoke/smokelit\" \"$translucent\" \"1\" }")
            .SpriteBlending.ShouldBe(SpriteBlend.Translucent);
    }

    /// <summary>A material parsed from its own text.</summary>
    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>A definition carrying one initializer and nothing else.</summary>
    private static ParticleSystem Declaring(ParticleFunction initializer) =>
        new(
            "test",
            Emitters: [],
            Initializers: [initializer],
            Operators: [],
            Renderers: [],
            Children: [],
            Parameters: new Dictionary<string, DmxValue>(System.StringComparer.Ordinal));
}
