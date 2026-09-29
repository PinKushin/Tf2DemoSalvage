using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>How the vertex-lit shader reads a static prop's colour mesh: <c>GammaToLinear( colour · 2 )</c> (B424's open note).</summary>
/// <remarks>
/// **Every DX9 route agrees, so no combo choice is involved.** The vertex-lit shader's vertex path lights in
/// <c>DoLighting</c> / <c>DoLightingUnrolled</c> (`common_vs_fxc.h:870-874`, `:902`) and its pixel path in
/// <c>PixelShaderDoLightingLinear</c> (`common_vertexlitgeneric_dx9.h:272`); all three take the static colour as
/// <c>GammaToLinear( staticLightingColor * cOverbright )</c> with <c>cOverbright</c> 2 (`common_vs_fxc.h:59`) and
/// <c>GammaToLinear</c> <c>pow( x, 2.2 )</c> (`common_fxc.h:189-192`). Only `_X360` differs (<c>col * col</c>). Neither
/// HDR nor LDR branches it. The bytes are written by the inverse: vrad's `.vhv` (`vradstaticprops.cpp:1583-1586`,
/// <c>ConvertRGBExp32ToRGBA8888</c> → <c>ConvertLinearToRGBA8888</c> → <c>LinearToVertexLight</c>,
/// `lightmap.cpp:3553-3599`, the same for `sp_hdr_` and `sp_`) and the engine's CPU bake both go through
/// <c>lineartovertex</c> = <c>min( 1, pow( L, 1 / 2.2 ) · 0.5 )</c> (`color_conversion.cpp:248-255`).
/// </remarks>
public sealed class StaticPropColourDecodeConformanceTests
{
    [Test]
    public void FromVertexByte_AgainstTheSdk_IsGammaToLinearOfTwiceTheColour()
    {
        string vs = Skip.Unless(SourceSdk.Text("src/materialsystem/stdshaders/common_vs_fxc.h"), SourceSdk.Missing);
        vs.ShouldContain("#define cOverbright\t\t\t2.0f");
        vs.ShouldContain("float3 col = staticLightingColor * cOverbright;");
        vs.ShouldContain("linearColor += GammaToLinear( col );");
        vs.ShouldContain("linearColor += GammaToLinear( staticLightingColor * cOverbright );");

        string ps = Skip.Unless(SourceSdk.Text("src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h"), SourceSdk.Missing);
        ps.ShouldContain("linearColor += GammaToLinear( staticLightingColor * cOverbright );");

        string common = Skip.Unless(SourceSdk.Text("src/materialsystem/stdshaders/common_fxc.h"), SourceSdk.Missing);
        common.ShouldContain("return pow( gamma, 2.2f );");

        string vrad = Skip.Unless(SourceSdk.Text("src/utils/vrad/lightmap.cpp"), SourceSdk.Missing);
        vrad.ShouldContain("vertexColor[0] = LinearToVertexLight((*pSrcLinear)[0]);");
        vrad.ShouldContain("pDst[0] = RoundFloatToByte(vertexColor[0] * 255.0f);");
    }

    /// <remarks>pow( 2b / 255, 2.2 ): 64 → 0.2195, 128 → 1.0087, 255 → 4.5948 (over one, carried, not clamped).</remarks>
    [TestCase(0, 0f)]
    [TestCase(64, 0.21951f)]
    [TestCase(128, 1.00865f)]
    [TestCase(255, 4.59479f)]
    public void FromVertexByte_OfAByte_IsPowTwiceItOver255To2Point2(int value, float expected)
    {
        PropModels.FromVertexByte((byte)value).ShouldBe(expected, 1e-4f);
    }

    /// <remarks>
    /// The round trip the engine relies on: a linear light through <c>lineartovertex</c> into a byte, and back through the
    /// shader, lands between the decodes of the neighbouring bytes — equal within one byte's step.
    /// </remarks>
    [TestCase(0.05f)]
    [TestCase(0.2f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(3.5f)]
    public void RoundTrip_LinearLightThroughTheCpuBake_ReturnsTheLightWithinAByte(float linear)
    {
        byte stored = StaticPropVertexLighting.ToByte(linear);

        float decoded = PropModels.FromVertexByte(stored);
        float below = PropModels.FromVertexByte((byte)Math.Max(0, stored - 1));
        float above = PropModels.FromVertexByte((byte)Math.Min(255, stored + 1));

        decoded.ShouldBeInRange(below, above);
        linear.ShouldBeInRange(below, above);
    }
}
