using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The water material's colour parameters and the water view's clear colour, converted as the engine converts them
/// (B653 review). No device.
/// </summary>
/// <remarks>
/// **The material parameters do not go through shaderapi's fog upload.** <c>water.cpp</c> sends <c>$refracttint</c>
/// (<c>:291</c>), <c>$reflecttint</c> (<c>:311</c>; under integer HDR <c>GammaToLinear( x ) · 4</c>, <c>:296-307</c>) and
/// <c>$fogcolor</c> (<c>:342</c>) through <c>CBaseVSShader::SetPixelShaderConstantGammaToLinear</c>
/// (<c>BaseVSShader.cpp:146-165</c>): <c>x &gt; 1 ? x : GammaToLinear( x )</c>. mathlib's <c>GammaToLinear</c>
/// (<c>color_conversion.cpp:274-293</c>) is 0 below 0, 1 from 0.95, otherwise a byte table of <c>pow( i / 255, 2.2 )</c>
/// (<c>:210</c>). So the water's own fog colour is <c>pow 2.2</c>, and the height fog under it is the sRGB decode
/// (shaderapi, B653). That seam is Valve's.
/// </remarks>
public sealed class WaterColourConformanceTests
{
    /// <remarks>
    /// The three regimes of <c>SetPixelShaderConstantGammaToLinear</c>: the byte table (0.5 rounds to 128, so
    /// pow( 128/255, 2.2 )), 1 from 0.95 (plain pow 2.2 would give 0.9354 at 0.97), and the pass-through above 1.
    /// </remarks>
    [TestCase(0.5f, 0.21951972f)]
    [TestCase(0.97f, 1f)]
    [TestCase(1.5f, 1.5f)]
    public void Linear_AWaterColourParameter_IsSetPixelShaderConstantGammaToLinear(float gamma, float expected)
    {
        WorldRenderer.Linear(gamma).ShouldBe(expected, 1e-6f);
    }

    /// <remarks>
    /// **<c>$reflecttint</c> above 1:** under integer HDR <c>water.cpp</c> calls <c>GammaToLinear</c> directly
    /// (<c>:303-305</c>), which returns 1 from 0.95, so 1.5 becomes 1. Otherwise it goes through
    /// <c>SetPixelShaderConstantGammaToLinear</c>, which passes it through as 1.5.
    /// </remarks>
    [TestCase(true, 1f)]
    [TestCase(false, 1.5f)]
    public void ReflectTint_AboveOne_PassesThroughOnlyOutsideIntegerHdr(bool integerHdr, float expected)
    {
        WorldRenderer.ReflectTint(1.5f, integerHdr).ShouldBe(expected);
    }

    /// <remarks>
    /// **The clear colour is bytes, scaled in gamma, truncated.** <c>SetClearColorToFogColor</c>
    /// (<c>viewrender.cpp:754-768</c>) takes <c>GetFogColor</c>'s bytes, and under integer HDR multiplies each by
    /// <c>LinearToGammaFullRange( scale )</c> = <c>pow( scale, 1/2.2 )</c> into an <c>unsigned char</c>, which
    /// truncates. Then <c>ClearColor4ub</c>. Byte 200 at scale 0.5 is 200 · 0.72974 = 145.9, so 145.
    ///
    /// **Read as stored bytes:** the D3D11 view here is sRGB, so the clear is handed <c>SrgbDecode( 145/255 )</c>
    /// and stores 145. Whether D3D9's clear ran with sRGB writes on is open (RISKS B653). D3D9 honours
    /// <c>D3DRS_SRGBWRITEENABLE</c> on a clear, which would treat the bytes as linear.
    /// </remarks>
    [Test]
    public void WaterClearColour_ByteTwoHundredAtHalfScale_IsTheTruncatedGammaScaledByte()
    {
        float linear = WorldRenderer.WaterClearColour(200f / 255f, 0.5f);

        SrgbTarget.Encode(linear).ShouldBe(145);
        linear.ShouldBe(FogConstants.SrgbDecode(145f / 255f), 1e-7f);
    }

    /// <remarks>Outside integer HDR the scale is not applied (the <c>if</c> at <c>:759</c>), so the byte is kept: 1 is passed for it.</remarks>
    [Test]
    public void WaterClearColour_AtScaleOne_IsTheByteItself()
    {
        SrgbTarget.Encode(WorldRenderer.WaterClearColour(37f / 255f, 1f)).ShouldBe(37);
    }
}
