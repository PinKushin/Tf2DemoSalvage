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
    public void ReadsAsSrgb_EachSampler_IsLightmappedGenericsSetting(MaterialSampler sampler, bool expected) =>
        SamplerSrgb.ReadsAsSrgb(sampler).ShouldBe(expected);
}
