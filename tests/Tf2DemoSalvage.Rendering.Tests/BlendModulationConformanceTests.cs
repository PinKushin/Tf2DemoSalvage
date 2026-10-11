using System;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// `$blendmodulatetexture`: a WorldVertexTransition's two base textures are not mixed by the vertex alpha itself but by
/// the vertex alpha pushed through a per-texel smoothstep (B514).
/// </summary>
/// <remarks>
/// *Read from published source*, `lightmappedgeneric_ps2_3_x.h:319-333`:
///
/// <code>
/// #if (SELFILLUM == 0) &amp;&amp; (PIXELFOGTYPE != PIXEL_FOG_TYPE_HEIGHT) &amp;&amp; (FANCY_BLENDING)
///     float4 modt=tex2D(BlendModulationSampler,i.lightmapTexCoord3.zw);
///     ...
///     float minb=saturate(modt.g-modt.r);
///     float maxb=saturate(modt.g+modt.r);
///     blendfactor=smoothstep(minb,maxb,blendfactor);
/// </code>
///
/// Green is where the edge sits and red is half its width, so the dirt-to-grass line follows the mask's pattern and is
/// as sharp as the mask says. `FANCY_BLENDING` is the texture being one (`lightmappedgeneric_dx9_helper.cpp:315-317,
/// 568`); the sampler is 3, read raw (`:430-434, 858-861`). The `MASKEDBLENDING` variant (`:321-327`, `modt.g` alone)
/// is not built: no shipped material declares `$maskedblending` (`vmt-param`, 0 of 30,993).
/// </remarks>
public sealed class BlendModulationConformanceTests
{
    private const string PixelShader = "src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h";
    private const string Helper = "src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp";

    [Test]
    public void Sdk_TheFancyBlendingBranch_IsTheArithmeticPorted()
    {
        string shader = Sdk(PixelShader);

        shader.ShouldContain("#if (SELFILLUM == 0) && (PIXELFOGTYPE != PIXEL_FOG_TYPE_HEIGHT) && (FANCY_BLENDING)", Case.Sensitive);
        shader.ShouldContain("float4 modt=tex2D(BlendModulationSampler,i.lightmapTexCoord3.zw);", Case.Sensitive);
        shader.ShouldContain("float minb=saturate(modt.g-modt.r);", Case.Sensitive);
        shader.ShouldContain("float maxb=saturate(modt.g+modt.r);", Case.Sensitive);
        shader.ShouldContain("blendfactor=smoothstep(minb,maxb,blendfactor);", Case.Sensitive);

        string helper = Sdk(Helper);

        helper.ShouldContain("SET_STATIC_PIXEL_SHADER_COMBO( FANCY_BLENDING, bHasBlendModulateTexture );", Case.Sensitive);
        helper.ShouldContain("pShaderShadow->EnableSRGBRead( SHADER_SAMPLER3, false );", Case.Sensitive);
    }

    /// <remarks>Green 0.5, red 0.25: the edge runs from 0.25 to 0.75.</remarks>
    [TestCase(0.2f, 0f)]
    [TestCase(0.25f, 0f)]
    [TestCase(0.375f, 0.15625f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(0.625f, 0.84375f)]
    [TestCase(0.75f, 1f)]
    [TestCase(0.8f, 1f)]
    public void Factor_AVertexBlendAcrossTheMasksRange_IsTheSmoothstepBetweenItsBounds(float blend, float expected) =>
        BlendModulation.Factor(blend, red: 0.25f, green: 0.5f).ShouldBe(expected, 1e-6f);

    /// <remarks>
    /// Red 0 puts both bounds on green, and `smoothstep` divides by their difference: below is `saturate(-inf)`, above
    /// `saturate(+inf)`. Exactly on it is `saturate(0/0)`, which Direct3D 11 defines as 0 — *interpolated* for TF2's
    /// Direct3D 9, on a set of measure zero.
    /// </remarks>
    [TestCase(0.49f, 0f)]
    [TestCase(0.5f, 0f)]
    [TestCase(0.51f, 1f)]
    public void Factor_AMaskWithNoRed_IsAHardStepAtGreen(float blend, float expected) =>
        BlendModulation.Factor(blend, red: 0f, green: 0.5f).ShouldBe(expected);

    /// <remarks>
    /// The bounds are saturated BEFORE the smoothstep. Green 0.9, red 0.3: 0.6 to 1.0 rather than to 1.2, so a blend of
    /// 0.8 is the midpoint, 0.5, where the unsaturated range would give 0.2593. Green 0.1, red 0.3: 0 to 0.4 rather than
    /// from -0.2, so 0.2 is the midpoint where the unsaturated range would give 0.7407.
    /// </remarks>
    [TestCase(0.8f, 0.3f, 0.9f, 0.5f)]
    [TestCase(0.2f, 0.3f, 0.1f, 0.5f)]
    public void Factor_AMaskWhoseBoundsLeaveZeroToOne_SaturatesThemFirst(float blend, float red, float green, float expected) =>
        BlendModulation.Factor(blend, red, green).ShouldBe(expected, 1e-6f);

    /// <remarks>
    /// The sample sits inside `if( bBaseTexture2 )` and behind `SELFILLUM == 0` (`lightmappedgeneric_ps2_3_x.h:317-319`),
    /// so a mask on a material with one base texture, or a self-illuminated one, changes nothing.
    /// </remarks>
    [TestCase(true, true, false, true)]
    [TestCase(false, true, false, false)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, true, false)]
    public void Applies_EachOfTheShadersGuards_DecidesWhetherTheMaskIsRead(bool mask, bool second, bool selfIllum, bool expected) =>
        BlendModulation.Applies(mask, second, selfIllum).ShouldBe(expected);

    [Test]
    public void BlendModulateTexture_AMaterialNamingOne_IsThatTexture()
    {
        VmtMaterial material = VmtMaterial.Parse(
            "\"WorldVertexTransition\"\n{\n\t\"$basetexture\" \"Nature/dirtground009\"\n\t\"$basetexture2\" \"Nature/grass_07\"\n\t\"$blendmodulatetexture\" \"Nature/grass_blendmask\"\n}\n"u8.ToArray());

        material.BlendModulateTexture.ShouldBe("Nature/grass_blendmask");
        VmtMaterial.Parse("\"LightmappedGeneric\"\n{\n\t\"$basetexture\" \"a\"\n}\n"u8.ToArray()).BlendModulateTexture.ShouldBeNull();
    }

    /// <summary>
    /// The output-level half: the production material table for the parity map carries the mask for its ground.
    /// </summary>
    [Test]
    public void Load_CpProcessF12_CarriesTheBlendModulationTextureForItsGround()
    {
        if (!MapCache.Exists("cp_process_f12"))
        {
            Assert.Ignore("cp_process_f12 or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.Load(mapName: "cp_process_f12");

        int ground = Enumerable.Range(0, assets.Materials.Count).FirstOrDefault(
            index => assets.Materials[index].Name.Equals("NATURE/BLENDGROUNDTOGRASS007", StringComparison.OrdinalIgnoreCase),
            -1);

        ground.ShouldBeGreaterThanOrEqualTo(0, "the control: the map names the material");
        assets.BlendModulations.Count.ShouldBe(assets.Textures.Count);
        assets.BlendModulations[ground].ShouldNotBeNull("materials/nature/blendgroundtograss007.vmt names Nature/grass_blendmask");
        assets.BlendModulations[ground]!.Value.Width.ShouldBeGreaterThan(0);
        assets.BlendTextures[ground].ShouldNotBeNull("and the second base texture the mask chooses against");

        int carrying = assets.BlendModulations.Count(mask => mask is not null);

        TestContext.Out.WriteLine($"F12 BLEND MODULATION {carrying} of {assets.BlendModulations.Count} materials carry a mask");
        carrying.ShouldBeLessThan(assets.BlendModulations.Count / 2, "the control: a mask is the exception, not every material");
    }

    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
