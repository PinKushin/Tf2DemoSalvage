using System;

using Tf2DemoSalvage.Content.Bsp;

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
    /// **The clear colour is bytes, scaled in gamma, truncated.** <c>SetClearColorToFogColor</c> (<c>client.dll</c>
    /// <c>0x18034d560</c>, <c>viewrender.cpp:754-768</c>) takes <c>GetFogColor</c>'s bytes and under integer HDR multiplies
    /// each, as a float, by <c>LinearToGammaFullRange( scale )</c> = <c>pow( scale, 1/2.2 )</c>, CVTTSS2SI. Byte 200 at
    /// scale 0.5 is 200 · 0.72974 = 145.9, so 145. shaderapi clears with sRGB writes OFF and stores the bytes as they are
    /// (<c>0x180014bd0</c>), so the value is the sRGB view's decode of 145, which stores 145.
    /// </remarks>
    [Test]
    public void WaterClearColour_ByteTwoHundredAtHalfScale_IsTheTruncatedGammaScaledByte()
    {
        float linear = WorldRenderer.WaterClearColour(200f / 255f, 0.5f, scaled: true);

        SrgbTarget.Encode(linear).ShouldBe(145);
        linear.ShouldBe(FogConstants.SrgbDecode(145f / 255f), 1e-7f);
    }

    /// <remarks>
    /// **Above 255 the byte WRAPS**: the client stores CVTTSS2SI's low byte (<c>0x18034d5dc</c>-<c>0x18034d5ec</c>), no
    /// clamp. 200 at scale 4 is 200 · 1.8779 = 375.6, 375 &amp; 255 = 119, where a clamp would give 255. 190 at scale 2 is
    /// 190 · 1.3703 = 260.4, so 4. Unscaled (<c>scaled: false</c>), the byte is kept whatever the scale.
    /// </remarks>
    [TestCase(200f, 4f, true, 119)]
    [TestCase(190f, 2f, true, 4)]
    [TestCase(200f, 4f, false, 200)]
    public void WaterClearColour_AProductAbove255_WrapsAsAnUnsignedChar(float fogByte, float scale, bool scaled, int expected)
    {
        SrgbTarget.Encode(WorldRenderer.WaterClearColour(fogByte / 255f, scale, scaled)).ShouldBe(expected);
    }

    /// <remarks>
    /// **The byte is the truncated one** <c>FogColor3fv</c> stores (<c>materialsystem.dll</c> <c>0x18002bf40</c>):
    /// <c>$fogcolor "[0.5 0.5 0.5]"</c> is 127.5, stored 127, where rounding would give 128.
    /// </remarks>
    [Test]
    public void WaterClearColour_AColourBetweenBytes_TakesFogColor3fvsTruncatedByte()
    {
        SrgbTarget.Encode(WorldRenderer.WaterClearColour(0.5f, 1f, scaled: false)).ShouldBe(127);
    }

    /// <remarks>
    /// Which clears scale (<c>viewrender.cpp</c>): <c>SetClearColorToFogColor</c> in the refraction view (<c>:6061</c>)
    /// and the intersection view (<c>:6097</c>); <c>GetFogColor</c> straight into <c>ClearColor4ub</c> in the eye-in-volume
    /// simple view (<c>:5780</c>), the under-water main view (<c>:6178</c>) and the under-water refraction (<c>:6228</c>).
    /// </remarks>
    [TestCase(WaterViewKind.Refraction, true)]
    [TestCase(WaterViewKind.Intersection, true)]
    [TestCase(WaterViewKind.UnderRefraction, false)]
    [TestCase(WaterViewKind.Simple, false)]
    [TestCase(WaterViewKind.UnderMain, false)]
    [TestCase(WaterViewKind.Reflection, false)]
    public void ClearScalesWithToneMap_EachView_IsSetClearColorToFogColorsOnly(WaterViewKind kind, bool expected)
    {
        WorldRenderer.ClearScalesWithToneMap(kind).ShouldBe(expected);
    }

    /// <remarks>
    /// The eye-in-volume simple view clears colour to the volume's fog, unscaled (<c>:5772-5780</c>); the under-water main
    /// view sets the colour but clears depth only (<c>:6134</c>), and an above-water view clears to black.
    /// </remarks>
    [Test]
    public void MainViewFogClear_TheSimpleViewFromInsideTheVolume_IsTheFogBytesUnscaled()
    {
        VolumeFog inside = new(false, 0f, (51f / 255f, 43f / 255f, 13f / 255f), -100f, 400f, 1f);
        WaterView simple = new(WaterViewKind.Simple, ViewDraws.None, ViewClears.Color | ViewClears.Depth, WaterViewTarget.BackBuffer,
            WaterViewFog.Volume, true, HeightClip.None);

        (float Red, float Green, float Blue) clear = WorldRenderer.MainViewFogClear(simple, inside)!.Value;

        (SrgbTarget.Encode(clear.Red), SrgbTarget.Encode(clear.Green), SrgbTarget.Encode(clear.Blue)).ShouldBe((51, 43, 13));
        WorldRenderer.MainViewFogClear(simple with { Kind = WaterViewKind.UnderMain, Clear = ViewClears.Depth }, inside)
            .ShouldBeNull();
        WorldRenderer.MainViewFogClear(simple with { ClearToFogColor = false }, inside).ShouldBeNull();
    }

    /// <remarks>
    /// **c16 follows the HDR type both ways** (review): integer HDR is <c>GammaToLinear( x ) · 4</c>, so 0.5 gives
    /// pow( 128/255, 2.2 ) · 4 = 0.87808 and 1.5 gives 4; no HDR is <c>SetPixelShaderConstantGammaToLinear</c> times 1, so 1.5
    /// passes through. Inverting the flag reddens both cases.
    /// </remarks>
    [TestCase(HdrType.IntegerHdr, 0.87807888f, 4f)]
    [TestCase(HdrType.None, 0.21951972f, 1.5f)]
    public void ReflectTintConstant_EachHdrType_IsWaterCppsC16(HdrType hdr, float atHalf, float aboveOne)
    {
        (float red, float green, _) = WorldRenderer.ReflectTintConstant((0.5f, 1.5f, 0f), hdr);

        red.ShouldBe(atHalf, 1e-5f);
        green.ShouldBe(aboveOne, 1e-6f);
    }
}
