namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Which of a material's textures the hardware linearizes on sampling, as `LightmappedGeneric` sets it.</summary>
/// <remarks>
/// `lightmappedgeneric_dx9_helper.cpp:421-450`: the base texture and `$basetexture2` get `EnableSRGBRead( …, true )`, the
/// HDR lightmap gets `false`, and the bump sampler (`SHADER_SAMPLER4`, line 488) is enabled and never given sRGB read.
/// An ssbump texel is three light weights; read as sRGB, a flat 0.58 samples as 0.29 and every bumped brush draws at half
/// its light — `metal/wall011d`, the roof in the pyro view on `koth_harvest_final`.
/// </remarks>
public sealed class SamplerSrgbConformanceTests
{
    [TestCase(MaterialSampler.BaseTexture, true)]
    [TestCase(MaterialSampler.BaseTexture2, true)]
    [TestCase(MaterialSampler.Lightmap, false)]
    [TestCase(MaterialSampler.Bump, false)]
    [TestCase(MaterialSampler.DiffuseWarp, false)]
    [TestCase(MaterialSampler.PhongExponent, false)]
    [TestCase(MaterialSampler.SelfIllumMask, false)]
    public void ReadsAsSrgb_EachSampler_IsLightmappedGenericsSetting(MaterialSampler sampler, bool expected) =>
        SamplerSrgb.ReadsAsSrgb(sampler).ShouldBe(expected);

    /// <remarks>
    /// Two helpers, two rules. `lightmappedgeneric_dx9_helper.cpp:482`: <c>bSRGBState = ( nDetailBlendMode == 1 )</c>.
    /// `vertexlitgeneric_dx9_helper.cpp:607`: <c>if ( nDetailBlendMode != 0 ) EnableSRGBRead( SHADER_SAMPLER2, true )</c>.
    /// Mod2x (0) is raw in both, so a 0.5 grey doubles to a neutral 1; read through the curve it is 0.21, doubled to 0.43,
    /// and every mod2x-detailed surface drew at under half its brightness.
    /// </remarks>
    [TestCase("LightmappedGeneric", 0, false)]
    [TestCase("LightmappedGeneric", 1, true)]
    [TestCase("LightmappedGeneric", 2, false)]
    [TestCase("WorldVertexTransition", 1, true)]
    [TestCase("VertexLitGeneric", 0, false)]
    [TestCase("VertexLitGeneric", 1, true)]
    [TestCase("VertexLitGeneric", 2, true)]
    [TestCase("UnlitGeneric", 5, true)]
    [TestCase("vertexlitgeneric", 7, true)]
    public void DetailReadsAsSrgb_EachShaderAndMode_IsItsHelpersSetting(string shader, int mode, bool expected) =>
        SamplerSrgb.DetailReadsAsSrgb(shader, mode).ShouldBe(expected);
}
