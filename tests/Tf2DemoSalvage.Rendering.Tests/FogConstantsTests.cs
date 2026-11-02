using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The two float4s the pixel shader's fog reads, packed as Valve's shader API packs them.
/// </summary>
/// <remarks>
/// Layout cited in FogConformanceTests: colour is <c>g_LinearFogColor</c> (common_ps_fxc.h:45) with w
/// saying whether fog is on; parameters are <c>fogParams</c> as <c>CalcRangeFog</c> unpacks them —
/// x start/(end-start), y the water height (unused, zero), z max density, w 1/(end-start).
/// </remarks>
public sealed class FogConstantsTests
{
    /// <summary>cp_process's controller, as both movement-test recordings carry it.</summary>
    private static readonly SceneFog Process = new(100f, 11000f, 120f / 255f, 133f / 255f, 158f / 255f, 1f);

    [Test]
    public void For_TheProcessController_PacksValvesConstants()
    {
        float[] packed = FogConstants.For(Process);

        packed.Length.ShouldBe(8);

        // Colour: gamma 0..1 from GetFogColor, made linear by shaderapi's sRGB decode (0x18003ddf0, B653), not pow 2.2:
        // 0.1878, 0.2346, 0.3419 where pow 2.2 gives 0.1905, 0.2388, 0.3489.
        packed[0].ShouldBe((float)SrgbTarget.Decode(120), 1e-6f);
        packed[1].ShouldBe((float)SrgbTarget.Decode(133), 1e-6f);
        packed[2].ShouldBe((float)SrgbTarget.Decode(158), 1e-6f);
        packed[3].ShouldBe(1f);

        packed[4].ShouldBe(100f / 10900f, 1e-9f);
        packed[5].ShouldBe(0f);
        packed[6].ShouldBe(1f);
        packed[7].ShouldBe(1f / 10900f, 1e-12f);
    }

    [Test]
    public void For_NoFog_IsOffAndAllZero()
    {
        FogConstants.For(null).ShouldBe(new float[8]);
    }

    [Test]
    public void For_ASkyboxScaleOfSixteen_DividesTheDistancesBySixteen()
    {
        // Enable3dSkyboxFog: FogStart( start * (1 / scale) ), FogEnd( end * (1 / scale) ). Start
        // over range is unchanged by a common scale; one over the range grows by the scale.
        float[] packed = FogConstants.For(Process, 1f / 16f);

        packed[4].ShouldBe(100f / 10900f, 1e-7f);
        packed[7].ShouldBe(16f / 10900f, 1e-9f);
    }

    [Test]
    public void For_RadialFog_MarksTheColoursWAsTwo()
    {
        // w carries the PIXEL_FOG_TYPE the shader selects: 0 off (none), 1 range, 2 radial — the
        // shader's own numbering is RANGE 0 and RANGE_RADIAL 2 (common_ps_fxc.h:69-71), shifted by
        // one so that zero can mean no fog.
        FogConstants.For(Process)[3].ShouldBe(1f);
        FogConstants.For(Process with { Radial = true })[3].ShouldBe(2f);
    }

    /// <remarks>
    /// **The knee is the binary's 0.04045** (<c>0x180082924</c> = <c>0x3D25AEE6</c>; B653 review), compared with COMISS/JA
    /// so the knee itself takes the linear branch. 0.04 falls between 0.04045 and the other published knee, 0.03928, so
    /// only the right knee sends it down the linear branch: 0.04 · (1/12.92) = 0.00309598. The pow branch would give
    /// 0.00309550 there. The curve is built to be nearly continuous at its knee, so the two differ by only 4.8e-7. That
    /// is still 2,000 float steps at this magnitude, and the first assertion proves the gap exceeds the tolerance.
    /// </remarks>
    [Test]
    public void SrgbDecode_BetweenTheTwoPublishedKnees_TakesTheLinearBranch()
    {
        double linear = 0.04 / 12.92;
        double curved = Math.Pow((0.04 + 0.055) / 1.055, 2.4);

        Math.Abs(linear - curved).ShouldBeGreaterThan(1e-7, "the instrument: the two branches must differ here");
        FogConstants.SrgbDecode(0.04f).ShouldBe((float)linear, 1e-8f);
    }

    /// <remarks>Above the knee, the curve: ((0.5 + 0.055) / 1.055)^2.4 = 0.2140411 (the binary's own constants).</remarks>
    [Test]
    public void SrgbDecode_AboveTheKnee_IsTheSrgbCurve()
    {
        FogConstants.SrgbDecode(0.5f).ShouldBe((float)Math.Pow(0.555 / 1.055, 2.4), 1e-6f);
    }

    /// <remarks>
    /// **The binary clamps before it decodes** (<c>0x18003de0b</c> MAXSS with 0, <c>0x18003de11</c> MINSS with 1). A
    /// byte colour never leaves 0..1, so this is the engine's guard, held as written.
    /// </remarks>
    [TestCase(-0.5f, 0f)]
    [TestCase(1.5f, 1f)]
    public void SrgbDecode_OutsideZeroToOne_IsClamped(float gamma, float expected)
    {
        FogConstants.SrgbDecode(gamma).ShouldBe(expected);
    }

    /// <remarks>
    /// **The height-fog grey has one owner** (B653 review): the shader's <c>LinearGrey</c> is written from
    /// <see cref="FogConstants.SrgbDecode"/> of 128/255, not a hand copy, so a change to the decode reaches it.
    /// </remarks>
    [Test]
    public void ShaderSource_TheHeightFogGrey_IsTheSrgbDecodeOf128()
    {
        string grey = FogConstants.SrgbDecode(128f / 255f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        WorldRenderer.CompiledShaderSourceText.ShouldContain($"static const float LinearGrey = {grey}f;", Case.Sensitive);
    }

    [Test]
    public void For_AMaxDensityBelowOne_IsCarriedUnchanged()
    {
        FogConstants.For(Process with { MaxDensity = 0.6f })[6].ShouldBe(0.6f);
    }
}
