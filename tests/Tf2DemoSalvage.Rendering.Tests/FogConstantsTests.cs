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
    /// **The constants are the binary's, bit for bit** (review: the knee test alone passes the pre-change code). Above the
    /// knee the pow is taken in double on <c>x · (1/1.055) + 0.055/1.055</c>: byte 16 gives <c>0x3BA9C9B5</c>, where the
    /// float pow on <c>(x + 0.055) / 1.055</c> gives one step more. Below it the binary MULTIPLIES by 1/12.92
    /// (<c>0x3D9E8391</c>): at <c>0x37C5F89D</c> (2.36e-5) that is <c>0x35F52A5F</c>, where dividing by 12.92 gives one
    /// step less.
    /// </remarks>
    [TestCase(0x3D808081, 0x3BA9C9B5)]
    [TestCase(0x37C5F89D, 0x35F52A5F)]
    public void SrgbDecode_EitherBranch_IsTheBinarysArithmeticBitForBit(int input, int expected)
    {
        BitConverter.SingleToInt32Bits(FogConstants.SrgbDecode(BitConverter.Int32BitsToSingle(input))).ShouldBe(expected);
    }

    /// <remarks>
    /// **c29 starts from a truncated byte**: <c>FogColor3fv</c> stores <c>clamp( (int)( x · 255 ), 0, 255 )</c>
    /// (<c>materialsystem.dll</c> <c>0x18002bf40</c>) and shaderapi reads it back as byte · 1/255 (<c>0x180014200</c>).
    /// 0.5 is 127.5, stored 127, where rounding gives 128; 0.999 is 254, not 255. Out of range clamps.
    /// </remarks>
    [TestCase(0.5f, 127)]
    [TestCase(0.999f, 254)]
    [TestCase(120f / 255f, 120)]
    [TestCase(1.5f, 255)]
    [TestCase(-0.2f, 0)]
    public void StoredChannel_AnyGamma_IsTheTruncatedByteOver255(float gamma, int fogByte)
    {
        FogConstants.StoredByte(gamma).ShouldBe(fogByte);
        FogConstants.StoredChannel(gamma).ShouldBe(fogByte * (1f / 255f));
    }

    /// <remarks>A blended colour between bytes takes the truncated byte into the range fog's decode: 0.5 is decode( 127/255 ), 0.2122 against 0.2158 at 128.</remarks>
    [Test]
    public void For_AColourBetweenBytes_DecodesTheTruncatedByte()
    {
        float red = FogConstants.For(Process with { Red = 0.5f })[0];

        red.ShouldBe(FogConstants.SrgbDecode(127f / 255f));
        red.ShouldNotBe(FogConstants.SrgbDecode(128f / 255f), "the instrument: the two bytes must decode apart");
    }

    /// <remarks>
    /// **<c>r_shader_srgb</c> on the height branch** (<c>0x180025b1f</c>-<c>0x180025be7</c>, through
    /// <c>NeedsShaderSRGBConversion</c> <c>0x180004bc0</c>): 0 the sRGB decode, 1 <c>pow( x, 2.2 )</c> in double, and -1 the
    /// caps bit, taken as hardware sRGB. At byte 16 the two curves are 8.5 stored levels apart: 0.005182 against 0.003535.
    /// </remarks>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(-1)]
    public void HeightFogChannel_EachShaderSrgb_TakesItsCurve(int shaderSrgb)
    {
        float stored = FogConstants.StoredChannel(16f / 255f);
        float expected = shaderSrgb == 1 ? (float)Math.Pow(stored, 2.200000047683716) : FogConstants.SrgbDecode(stored);

        Math.Abs(FogConstants.SrgbDecode(stored) - (float)Math.Pow(stored, 2.2)).ShouldBeGreaterThan(1e-3f, "the instrument");
        FogConstants.HeightFogChannel(16f / 255f, shaderSrgb).ShouldBe(expected);
    }

    /// <remarks>
    /// **The modulating grey under height fog follows <c>r_shader_srgb</c> too** (B637): the bytes 0x80 the fog-mode
    /// applier hands c29 (<c>0x180014200</c>, modes 1 and 3) go through the same height branch, so they are packed into the
    /// water view's <c>heightFogColour.w</c> beside the colour, and the shader reads them there.
    /// </remarks>
    [TestCase(0)]
    [TestCase(1)]
    public void PackHeightFog_TheModulatingGrey_IsTheHeightBranchOf128(int shaderSrgb)
    {
        float[] view = new float[12];

        WorldRenderer.PackHeightFog(view, new VolumeFog(true, 10f, (51f / 255f, 43f / 255f, 13f / 255f), -100f, 400f, 1f), shaderSrgb);

        view[8].ShouldBe(FogConstants.HeightFogChannel(51f / 255f, shaderSrgb));
        view[11].ShouldBe(FogConstants.HeightFogChannel(128f / 255f, shaderSrgb));
        WorldRenderer.CompiledShaderSourceText.ShouldContain("FogTarget(tintControl.z, heightFogColour.rgb, heightFogColour.w)", Case.Sensitive);
    }

    [Test]
    public void For_AMaxDensityBelowOne_IsCarriedUnchanged()
    {
        FogConstants.For(Process with { MaxDensity = 0.6f })[6].ShouldBe(0.6f);
    }
}
