using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>B429: the colour mesh the engine lights on the CPU for an unbaked <c>$staticprop</c> static prop.</summary>
/// <remarks>
/// **Read from `engine.dll`.** `FUN_1800eac60` builds a static prop's colour mesh only when its model has
/// `STUDIOHDR_FLAGS_STATIC_PROP`; `FUN_1800f36e0` uses the `.vhv` when it loads and otherwise calls `FUN_1800ee4a0`
/// per mesh, which lights each vertex with <c>IStudioRender::ComputeLighting</c> (or `ComputeLightingConstDirectional`
/// under flag `0x2000`) and stores <c>round( lineartovertex[ clamp( round( light · 1024 ), 0, 4095 ) ] · 255 )</c>.
/// The table is `BuildGammaTable( 2.2, 2.2, 0, 2 )` (`0x180279a40`; called so from `0x1800d7890`, `0x1800d5e70` and
/// `0x18012ecf0`), whose loop is `mathlib/color_conversion.cpp:248`. The lighting is the handle's static state,
/// `FUN_1801ba590( handle, 0, 1 )`: the leaf cube and the style-0 lights only (`FUN_1801b5a50`).
/// </remarks>
public sealed class StaticPropVertexLightingConformanceTests
{
    [Test]
    public void LinearToVertex_AgainstTheSdk_IsPowOverGammaHalvedByOverbrightTwoAndClamped()
    {
        string source = Skip.Unless(SourceSdk.Text("src/mathlib/color_conversion.cpp"), SourceSdk.Missing);

        source.ShouldContain("f = pow ( i/1024.0, 1.0 / gamma );");
        source.ShouldContain("overbrightFactor = 0.5;");
        source.ShouldContain("lineartovertex[i] = f * overbrightFactor;");
        source.ShouldContain("lineartovertex[i] = 1;");

        string studio = Skip.Unless(SourceSdk.Text("src/public/studio.h"), SourceSdk.Missing);
        studio.ShouldContain("#define STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT\t\t0x00002000");
        StaticPropVertexLighting.ConstantDirectionalLightDot.ShouldBe(0x2000);
    }

    /// <remarks>
    /// Index `round(x · 1024)` clamped to 0..4095 (an unsigned compare, so a negative is 0), the table
    /// <c>min( 1, pow( i / 1024, 1 / 2.2 ) · 0.5 )</c>, then `· 255` rounded half to even (`+ 8388608.0`). Worked by hand:
    /// 1.0 → 0.5 · 255 = 127.5 → 128; 0.25 → 0.25^(1/2.2) = 0.53252, · 0.5 · 255 = 67.9 → 68; past 4 → index 4095,
    /// 3.99902^(1/2.2) = 1.87771, · 0.5 · 255 = 239.4 → 239.
    /// </remarks>
    [TestCase(0f, 0)]
    [TestCase(-1f, 0)]
    [TestCase(1f, 128)]
    [TestCase(0.25f, 68)]
    [TestCase(10f, 239)]
    public void ToByte_OfALinearLight_IsTheLinearToVertexTableTimes255(float linear, int expected)
    {
        StaticPropVertexLighting.ToByte(linear).ShouldBe((byte)expected);
    }

    [TestCase(0x10, true)]
    [TestCase(0x0, false)]
    [TestCase(0x2010, true)]
    [TestCase(0x2000, false)]
    public void Lights_ByStudioFlags_AnyStaticProp(int flags, bool lit)
    {
        StaticPropVertexLighting.Lights(flags).ShouldBe(lit);
    }

    /// <remarks>`engine.dll` `FUN_1800ee4a0`: <c>constdirectionallightdot / 255</c> only under flag `0x2000`.</remarks>
    [TestCase(0x2010, 255, 1f)]
    [TestCase(0x2010, 51, 0.2f)]
    [TestCase(0x10, 255, null)]
    public void ConstantDot_ByFlagsAndByte_IsTheByteOver255OnlyUnderTheFlag(int flags, int dot, float? expected)
    {
        StaticPropVertexLighting.ConstantDot(flags, (byte)dot).ShouldBe(expected);
    }

    // ComputeLightingConstDirectional, `studiorender.dll` `+0x130` = `0x180020f90`. It is `+0x128` (`0x180020bd0`) with
    // the same `0x180020c80` prep — the ambient cube along the vertex normal — and a different per-light table:
    // `0x18009aa50` (`FUN_180001ee0`) instead of `0x18009b260` (`FUN_1800010e0`). Each entry is its normal twin with
    // `max( n · L, 0 )` replaced by `max( dot, 0 )` (the float at `[rsp+0x88]`, passed on as the fifth argument):
    // point `0x180021a10` and directional `0x180021a90` both become `0x180045e30`; spot `0x180021b20` becomes
    // `0x180045e90`, which keeps the cone. Every term still multiplies by the falloff and adds colour.

    /// <remarks>
    /// A lamp of (30, 20, 10) 40 units above with falloff 1 (constant 1), and the sun (0.3, 0.2, 0.1) straight down, at
    /// dot 0.5: 0.5 · (30.3, 20.2, 10.1). The normal is +X, which faces neither — the ordinary path gives zero, so the
    /// constant dot is the only way the light arrives.
    /// </remarks>
    [Test]
    public void At_AConstantDotUnderALampAndTheSun_IsDotTimesColourTimesFalloff()
    {
        (PointLighting lighting, SunLight sun) = LampAndSun();

        Vector3 light = StudioPointLighting.At(lighting, sun, new Vector3(0f, 0f, 10f), Vector3.UnitX, 0.5f);

        light.X.ShouldBe(15.15f, 1e-4f);
        light.Y.ShouldBe(10.1f, 1e-4f);
        light.Z.ShouldBe(5.05f, 1e-4f);
        StudioPointLighting.At(lighting, sun, new Vector3(0f, 0f, 10f), Vector3.UnitX).ShouldBe(Vector3.Zero, "the control");
    }

    /// <summary>The direct terms ignore the normal; the ordinary path is the control that they otherwise do not.</summary>
    [Test]
    public void At_AConstantDotUnderTwoNormals_GivesOneColour()
    {
        (PointLighting lighting, SunLight sun) = LampAndSun();
        Vector3 point = new(0f, 0f, 10f);

        StudioPointLighting.At(lighting, sun, point, Vector3.UnitZ, 0.5f)
            .ShouldBe(StudioPointLighting.At(lighting, sun, point, -Vector3.UnitY, 0.5f));
        StudioPointLighting.At(lighting, sun, point, Vector3.UnitZ)
            .ShouldNotBe(StudioPointLighting.At(lighting, sun, point, -Vector3.UnitY), "the control");
    }

    /// <summary>`0x180020c80` adds the cube for both entry points, along the vertex normal.</summary>
    [Test]
    public void At_AConstantDotUnderACube_StillTakesTheFaceTheNormalMeets()
    {
        PointLighting cube = PointLighting.Bounce(new AmbientCube { PositiveX = (1f, 1f, 1f), PositiveY = (0.25f, 0.25f, 0.25f) });

        StudioPointLighting.At(cube, null, Vector3.Zero, Vector3.UnitX, 0.5f).ShouldBe(Vector3.One);
        StudioPointLighting.At(cube, null, Vector3.Zero, Vector3.UnitY, 0.5f).ShouldBe(new Vector3(0.25f));
    }

    /// <summary>`0x180045e90` keeps the spot's cone: dark outside it, the dot inside it.</summary>
    [TestCase(-1f, 15f)]
    [TestCase(1f, 0f)]
    public void At_AConstantDotUnderASpot_KeepsTheCone(float pointsZ, float expectedRed)
    {
        LocalLight spot = new(0f, 0f, 50f, 30f, 20f, 10f, 1f, 0f, 0f, 0f, (0f, 0f, pointsZ), 0.9f, 0.8f, 0f, true);

        StudioPointLighting.At(new PointLighting(default, [spot]), null, new Vector3(0f, 0f, 10f), Vector3.UnitX, 0.5f)
            .X.ShouldBe(expectedRed, 1e-4f);
    }

    /// <summary>The colour mesh passes the dot through to every corner.</summary>
    [Test]
    public void Colours_WithAConstantDot_AreConstDirectionalLightingThroughTheTable()
    {
        (PointLighting lighting, SunLight sun) = LampAndSun();
        Vector3 expected = StudioPointLighting.At(lighting, sun, new Vector3(0f, 0f, 10f), Vector3.UnitX, 0.5f);

        float[] colours = StaticPropVertexLighting.Colours(
            [new PropVertex(0f, 0f, 0f, 0f, 0f, 0, NormalX: 1f)], new PropTransform(0f, 0f, 10f, 0f, 0f, 0f, 1f), lighting, sun, 0.5f);

        colours[0].ShouldBe(PropModels.FromVertexByte(StaticPropVertexLighting.ToByte(expected.X)));
        colours[0].ShouldBeGreaterThan(0f, "the control: the ordinary path lights a +X corner not at all");
    }

    private static (PointLighting Lighting, SunLight Sun) LampAndSun() =>
        (new PointLighting(default, [new LocalLight(0f, 0f, 50f, 30f, 20f, 10f, 1f, 0f, 0f, 0f)]),
         new SunLight(0.3f, 0.2f, 0.1f, 0f, 0f, -1f));

    /// <remarks>
    /// One corner at the model origin with its normal along +X, the prop yawed 90° and scaled 3 (which `FUN_180276390`'s
    /// matrix does not carry): the normal lands on +Y, which the cube lights with 0.25 — byte 68 — and nothing else
    /// does, so every channel reads the doubled byte, 68 / 255 · 2.
    /// </remarks>
    [Test]
    public void Colours_AYawedCornerUnderACube_TakesTheFaceItsWorldNormalMeets()
    {
        AmbientCube cube = new() { PositiveY = (0.25f, 0.25f, 0.25f), PositiveX = (1f, 1f, 1f) };

        float[] colours = StaticPropVertexLighting.Colours(
            [new PropVertex(0f, 0f, 0f, 0f, 0f, 0, NormalX: 1f, NormalY: 0f, NormalZ: 0f)],
            new PropTransform(0f, 0f, 0f, 0f, 90f, 0f, 1f),
            PointLighting.Bounce(cube),
            null);

        colours.ShouldBe([68f / 255f * 2f, 68f / 255f * 2f, 68f / 255f * 2f], tolerance: 1e-6);
    }

    /// <summary>The per-vertex light is <see cref="StudioPointLighting.At"/>'s, the same `ComputeLighting` the model draw's CPU path uses.</summary>
    [Test]
    public void Colours_UnderALampAndTheSun_AreComputeLightingThroughTheTable()
    {
        LocalLight lamp = new(0f, 0f, 50f, 30f, 20f, 10f, 1f, 0f, 0f, 0f, default, 0f, 0f, 0f, false);
        PointLighting lighting = new(default, [lamp]);
        SunLight sun = new(0.3f, 0.2f, 0.1f, 0f, 0f, -1f);
        Vector3 expected = StudioPointLighting.At(lighting, sun, new Vector3(0f, 0f, 10f), Vector3.UnitZ);

        float[] colours = StaticPropVertexLighting.Colours(
            [new PropVertex(0f, 0f, 0f, 0f, 0f, 0)], new PropTransform(0f, 0f, 10f, 0f, 0f, 0f, 1f), lighting, sun);

        colours.ShouldBe(
            [
                PropModels.FromVertexByte(StaticPropVertexLighting.ToByte(expected.X)),
                PropModels.FromVertexByte(StaticPropVertexLighting.ToByte(expected.Y)),
                PropModels.FromVertexByte(StaticPropVertexLighting.ToByte(expected.Z)),
            ]);
        expected.X.ShouldBeGreaterThan(expected.Z, "the control: the lamp's colour reached the corner");
    }

    /// <remarks>
    /// `FUN_1801b5a50` adds a world light to the handle's static state only when its style is 0; a styled one only sets
    /// its mask bit. <see cref="LevelLighting.LightingAt"/> — the per-frame model state — is the control: it holds both.
    /// </remarks>
    [Test]
    public void StaticPropLightingAt_AStyledAndAStyleZeroLamp_HoldsOnlyTheStyleZeroOne()
    {
        LevelLighting lighting = StyledLightFallbackConformanceTests.Map(
            [StyledLightFallbackConformanceTests.Lamp(5, 0), StyledLightFallbackConformanceTests.Lamp(0, 0)]);

        lighting.LightingAt(0f, 0f, 100f).Locals.ShouldNotBeNull().Count.ShouldBe(2, "the control");
        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.ShouldNotBeNull().ShouldHaveSingleItem();
    }
}
