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

    /// <remarks>
    /// **`C_INIT_CreateWithinSphere` raises its speed draw to `speed_random_exponent`**, as the trail-length initializer
    /// does — `FLD [this+0x58]`, `FLD r`, `FXCH`, `CALL pow` at <c>0x107bb223</c>. An exponent of 4 puts the mean speed
    /// at a fifth of the band where a plain lerp puts it at half.
    /// </remarks>
    [Test]
    public void Spawn_PositionWithinSphereRandomWithASpeedExponent_SkewsTowardTheMinimum()
    {
        ParticleSystem system = Declaring(Sphere(speedLeast: 0d, speedMost: 1d, exponent: 4d));

        ParticleStore store = new();

        const float Step = 1f / 66f;
        const int Count = 512;

        float total = 0f;

        for (int one = 0; one < Count; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: Step);

            total += (store.PositionOf(index) - store.Previous[index]).Length() / Step;
        }

        (total / Count).ShouldBeInRange(0.14f, 0.26f);
    }

    /// <remarks>
    /// **No outward speed at all unless `speed_max` is above zero** — `COMISS` against 0 and `JBE` past the whole draw
    /// (<c>0x107bb1fa</c>), so a declared minimum on its own launches nothing.
    /// </remarks>
    [Test]
    public void Spawn_PositionWithinSphereRandomWithNoSpeedMaximum_LaunchesNothing()
    {
        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(Sphere(speedLeast: 5d, speedMost: 0d, exponent: 1d)),
            store,
            ParticleControlPoint.Unoriented(Vector3.Zero),
            lives: 1f,
            seconds: 1f / 66f);

        (store.PositionOf(index) - store.Previous[index]).ShouldBe(Vector3.Zero);
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

    /// <remarks>
    /// **`C_INIT_RandomTrailLength`, as `client.dll` computes it**: <c>pow( r, exponent ) · ( max − min ) + min</c>
    /// (<c>FUN_107be5c0</c>, B415). `Explosion_CoreFlash` declares 0.33..0.4 with an exponent of 1, so every draw lies
    /// in that band — and the draws must DIFFER, or every ember in a blast is the same length.
    /// </remarks>
    [Test]
    public void Spawn_TrailLengthRandom_DrawsWithinTheDeclaredBand()
    {
        ParticleSystem system = Declaring(TrailLength(least: 0.33d, most: 0.4d, exponent: 1d));

        ParticleStore store = new();

        float least = float.MaxValue;
        float most = float.MinValue;

        for (int one = 0; one < 128; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

            float length = store.TrailLengthOf(index);

            length.ShouldBeInRange(0.33f, 0.4f);

            least = System.MathF.Min(least, length);
            most = System.MathF.Max(most, length);
        }

        (most - least).ShouldBeGreaterThan(0.05f, "a band of 0.07 that every particle agrees on is not a draw");
    }

    /// <remarks>
    /// **The exponent is applied, which the decompiler denied.** Ghidra's pseudocode for the initializer showed a
    /// plain lerp; the disassembly loads <c>length_random_exponent</c> and calls the CRT's x87 <c>pow</c> with it. An
    /// exponent of 4 pushes most draws toward the minimum, so the MEAN falls well below the band's midpoint, which a
    /// lerp ignoring the exponent could never do.
    /// </remarks>
    [Test]
    public void Spawn_TrailLengthRandomWithAnExponent_SkewsTowardTheMinimum()
    {
        ParticleSystem system = Declaring(TrailLength(least: 0d, most: 1d, exponent: 4d));

        ParticleStore store = new();

        float total = 0f;
        const int Count = 512;

        for (int one = 0; one < Count; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

            total += store.TrailLengthOf(index);
        }

        // E[r⁴] for r uniform on [0,1) is 0.2; a lerp without the exponent would sit at 0.5.
        (total / Count).ShouldBeInRange(0.14f, 0.26f);
    }

    /// <remarks>
    /// **With no initializer, a particle takes the collection's own default, <c>0.1</c>** — four lanes of
    /// <c>0x3dcccccd</c> written at <c>10 × 0x30</c> into the constant block by the collection init. `rockettrail_burst`
    /// draws with `render_sprite_trail` and sets no trail length, so this is every rocket's burst.
    /// </remarks>
    [Test]
    public void Spawn_WithNoTrailLengthInitializer_TakesTheCollectionsDefault()
    {
        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(new ParticleFunction("Alpha Random", "alpha", new Dictionary<string, DmxValue>(System.StringComparer.Ordinal))),
            store,
            ParticleControlPoint.Unoriented(Vector3.Zero),
            lives: 1f,
            seconds: 1f / 66f);

        store.TrailLengthOf(index).ShouldBe(0.1f);
    }

    /// <remarks>
    /// **`C_INIT_PositionOffset`, read out of `client.dll`** (<c>FUN_107bc1b0</c>, B415). `Explosion_Flash_1` declares
    /// <c>(50 0 0)</c> IN LOCAL SPACE — fifty units along the control point's forward, and an explosion's forward is
    /// the wall's normal. Without it the flash sits on the wall and the wall hides half of it. BOTH positions move, so
    /// the particle is displaced rather than launched: Verlet reads <c>position − previous</c> as its speed.
    /// </remarks>
    [Test]
    public void Spawn_PositionModifyOffsetRandomInLocalSpace_MovesAlongTheControlPointsForward()
    {
        Vector4 out50 = new(50f, 0f, 0f, 0f);

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(Offset(out50, out50, local: true)),
            store,
            FacingPlusY(new Vector3(10f, 20f, 30f)),
            lives: 1f,
            seconds: 1f / 66f);

        store.PositionOf(index).ShouldBe(new Vector3(10f, 70f, 30f));
        store.Previous[index].ShouldBe(new Vector3(10f, 70f, 30f));
    }

    /// <remarks>
    /// **Local +Y is LEFT here, and it is RIGHT in `Position Within Sphere Random`.** The offset goes through
    /// <c>GetControlPointTransformAtTime</c>, which builds the matrix from <c>( forward, −right, up )</c> — an XORPS with
    /// <c>0x80000000</c> at <c>0x107a05de</c> — while the sphere initializer multiplies its local speed by
    /// <c>m_RightVector</c> directly (<c>0x107bb325</c>). Both are Valve's, so neither may borrow the other's basis.
    /// </remarks>
    [Test]
    public void Spawn_PositionModifyOffsetRandomAlongLocalY_MovesLeftNotRight()
    {
        Vector4 ten = new(0f, 10f, 0f, 0f);

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(Offset(ten, ten, local: true)), store, FacingPlusY(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

        // Right is +X for a point facing +Y, so left is -X.
        store.PositionOf(index).ShouldBe(new Vector3(-10f, 0f, 0f));
    }

    [Test]
    public void Spawn_PositionModifyOffsetRandomInWorldSpace_IgnoresTheOrientation()
    {
        Vector4 five = new(5f, 0f, 0f, 0f);

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(Offset(five, five, local: false)), store, FacingPlusY(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

        store.PositionOf(index).ShouldBe(new Vector3(5f, 0f, 0f));
    }

    /// <remarks>
    /// With <c>offset proportional to radius</c> the engine scales BOTH bounds by the particle's radius before the draw
    /// (<c>0x107bc25f</c>), so the radius an earlier initializer set decides the distance.
    /// </remarks>
    [Test]
    public void Spawn_PositionModifyOffsetRandomProportionalToRadius_ScalesByTheRadius()
    {
        Vector4 two = new(2f, 0f, 0f, 0f);

        ParticleFunction radiusFour = new(
            "Radius Random",
            "radius",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["radius_min"] = new DmxValue(DmxAttributeType.Real, 4d),
                ["radius_max"] = new DmxValue(DmxAttributeType.Real, 4d),
            });

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(radiusFour, Offset(two, two, local: false, proportional: true)),
            store,
            ParticleControlPoint.Unoriented(Vector3.Zero),
            lives: 1f,
            seconds: 1f / 66f);

        store.PositionOf(index).ShouldBe(new Vector3(8f, 0f, 0f));
    }

    [Test]
    public void Spawn_PositionModifyOffsetRandomWithARange_DrawsWithinItAndDiffers()
    {
        ParticleStore store = new();

        ParticleSystem system = Declaring(Offset(Vector4.Zero, new Vector4(0f, 0f, 10f, 0f), local: false));

        float least = float.MaxValue;
        float most = float.MinValue;

        for (int one = 0; one < 64; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

            float z = store.PositionOf(index).Z;

            z.ShouldBeInRange(0f, 10f);

            least = System.MathF.Min(least, z);
            most = System.MathF.Max(most, z);
        }

        (most - least).ShouldBeGreaterThan(5f, "every particle at one offset is not a draw");
    }

    /// <remarks>
    /// **`C_INIT_MoveBetweenPoints`, read out of `client.dll`** (B415, `0x107bf510`): the particle is sent from where
    /// earlier initializers put it towards control point 1 at a drawn speed, and its LIFE is the time the trip takes,
    /// <c>dist / ( speed + FLT_EPSILON )</c>. That is what makes a tracer end exactly at the wall.
    /// </remarks>
    [Test]
    public void Spawn_MoveBetweenTwoControlPoints_LivesForTheTripAndMovesTowardTheEnd()
    {
        const float Step = 1f / 66f;

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(MoveBetween(5000d, 5000d)),
            store,
            ParticleControlPoint.Unoriented(Vector3.Zero),
            lives: 1f,
            seconds: Step,
            points: [ParticleControlPoint.Unoriented(Vector3.Zero), ParticleControlPoint.Unoriented(new Vector3(1000f, 0f, 0f))]);

        store.Lifetime[index].ShouldBe(1000f / (5000f + 1.1920929E-7f));
        store.PositionOf(index).ShouldBe(Vector3.Zero);
        store.Previous[index].X.ShouldBe(-5000f * Step, 0.001f);
        store.Previous[index].Y.ShouldBe(0f);
        store.Previous[index].Z.ShouldBe(0f);
    }

    /// <remarks>An unset control point is the origin, as a collection's are before anything sets them.</remarks>
    [Test]
    public void Spawn_MoveBetweenTwoControlPointsWithNoEndPoint_HeadsForTheOrigin()
    {
        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(MoveBetween(5000d, 5000d)),
            store,
            ParticleControlPoint.Unoriented(new Vector3(100f, 0f, 0f)),
            lives: 1f,
            seconds: 1f / 66f);

        store.Lifetime[index].ShouldBe(100f / (5000f + 1.1920929E-7f));
        store.Previous[index].X.ShouldBeGreaterThan(100f, "moving toward -X means the previous position is behind, at +X");
    }

    [Test]
    public void Spawn_MoveBetweenTwoControlPointsWithASpeedRange_TakesBetweenTheFastestAndSlowestTrip()
    {
        ParticleStore store = new();
        ParticleSystem system = Declaring(MoveBetween(1000d, 4000d));
        ParticleControlPoint[] points =
            [ParticleControlPoint.Unoriented(Vector3.Zero), ParticleControlPoint.Unoriented(new Vector3(0f, 400f, 0f))];

        float least = float.MaxValue;
        float most = float.MinValue;

        for (int one = 0; one < 64; one++)
        {
            int index = ParticleSystems.Spawn(
                system, store, points[0], lives: 1f, seconds: 1f / 66f, points: points);

            float life = store.Lifetime[index];

            life.ShouldBeInRange(0.1f - 0.0001f, 0.4f + 0.0001f);

            least = System.MathF.Min(least, life);
            most = System.MathF.Max(most, life);
        }

        (most - least).ShouldBeGreaterThan(0.1f, "every particle at one speed is not a draw");
    }

    /// <remarks>`end spread` moves the END by a sphere sample of that radius, so the trip is longer or shorter by at most it.</remarks>
    [Test]
    public void Spawn_MoveBetweenTwoControlPointsWithAnEndSpread_EndsWithinTheSpread()
    {
        ParticleStore store = new();
        ParticleSystem system = Declaring(MoveBetween(1000d, 1000d, spread: 50d));
        ParticleControlPoint[] points =
            [ParticleControlPoint.Unoriented(Vector3.Zero), ParticleControlPoint.Unoriented(new Vector3(0f, 0f, 500f))];

        bool moved = false;

        for (int one = 0; one < 32; one++)
        {
            int index = ParticleSystems.Spawn(system, store, points[0], lives: 1f, seconds: 1f / 66f, points: points);

            float trip = store.Lifetime[index] * 1000f;

            trip.ShouldBeInRange(450f - 0.01f, 550f + 0.01f);
            moved |= System.MathF.Abs(trip - 500f) > 1f;
        }

        moved.ShouldBeTrue("a spread that never moves the end is not a spread");
    }

    /// <remarks>
    /// **A Valve bug, reproduced for the particle it lands on.** With <c>start offset</c> above zero the start is moved
    /// toward the end, but the write-back puts y and z at <c>+4</c> and <c>+8</c> (<c>0x107bf7c6</c>, <c>0x107bf7d6</c>)
    /// — the neighbouring particles' x in the four-wide block — instead of <c>+0x10</c> and <c>+0x20</c>. So the particle
    /// itself keeps its y and z and only its x moves, while the trip is measured from the fully moved start.
    /// </remarks>
    [Test]
    public void Spawn_MoveBetweenTwoControlPointsWithAStartOffset_MovesOnlyXAsTheEngineDoes()
    {
        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            Declaring(MoveBetween(1000d, 1000d, offset: 10d)),
            store,
            ParticleControlPoint.Unoriented(Vector3.Zero),
            lives: 1f,
            seconds: 1f / 66f,
            points: [ParticleControlPoint.Unoriented(Vector3.Zero), ParticleControlPoint.Unoriented(new Vector3(100f, 100f, 0f))]);

        float distance = System.MathF.Sqrt(20000f);
        float moved = 10f / (distance + 1.1920929E-7f) * 100f;

        store.PositionOf(index).X.ShouldBe(moved, 0.0001f);
        store.PositionOf(index).Y.ShouldBe(0f);
        store.Lifetime[index].ShouldBe((distance - 10f) / 1000f, 0.0001f);
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

    /// <summary>`Trail Length Random` with its three parameters.</summary>
    private static ParticleFunction TrailLength(double least, double most, double exponent) =>
        new(
            "Trail Length Random",
            "trail",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["length_min"] = new DmxValue(DmxAttributeType.Real, least),
                ["length_max"] = new DmxValue(DmxAttributeType.Real, most),
                ["length_random_exponent"] = new DmxValue(DmxAttributeType.Real, exponent),
            });

    /// <summary>`Position Within Sphere Random` at a point, with only an outward speed.</summary>
    private static ParticleFunction Sphere(double speedLeast, double speedMost, double exponent) =>
        new(
            "Position Within Sphere Random",
            "sphere",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["speed_min"] = new DmxValue(DmxAttributeType.Real, speedLeast),
                ["speed_max"] = new DmxValue(DmxAttributeType.Real, speedMost),
                ["speed_random_exponent"] = new DmxValue(DmxAttributeType.Real, exponent),
            });

    /// <summary>`move particles between 2 control points`, ending at control point 1.</summary>
    private static ParticleFunction MoveBetween(double least, double most, double spread = 0d, double offset = 0d) =>
        new(
            "move particles between 2 control points",
            "tracer",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["minimum speed"] = new DmxValue(DmxAttributeType.Real, least),
                ["maximum speed"] = new DmxValue(DmxAttributeType.Real, most),
                ["end spread"] = new DmxValue(DmxAttributeType.Real, spread),
                ["start offset"] = new DmxValue(DmxAttributeType.Real, offset),
                ["end control point"] = new DmxValue(DmxAttributeType.Whole, 1d),
            });

    /// <summary>`Position Modify Offset Random` with its four parameters.</summary>
    private static ParticleFunction Offset(Vector4 least, Vector4 most, bool local, bool proportional = false) =>
        new(
            "Position Modify Offset Random",
            "offset",
            new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["offset min"] = new DmxValue(DmxAttributeType.Vector3, Vector: least),
                ["offset max"] = new DmxValue(DmxAttributeType.Vector3, Vector: most),
                ["offset in local space 0/1"] = new DmxValue(DmxAttributeType.Boolean, local ? 1d : 0d),
                ["offset proportional to radius 0/1"] = new DmxValue(DmxAttributeType.Boolean, proportional ? 1d : 0d),
            });

    /// <summary>
    /// A point facing +Y, with the basis `AngleVectors` gives yaw 90: right is +X, up is +Z — an explosion on a wall
    /// whose normal is +Y.
    /// </summary>
    private static ParticleControlPoint FacingPlusY(Vector3 at) => new(at, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);

    /// <summary>A definition carrying these initializers and nothing else.</summary>
    private static ParticleSystem Declaring(params ParticleFunction[] initializers) =>
        new(
            "test",
            Emitters: [],
            Initializers: initializers,
            Operators: [],
            Renderers: [],
            Children: [],
            Parameters: new Dictionary<string, DmxValue>(System.StringComparer.Ordinal));
}
