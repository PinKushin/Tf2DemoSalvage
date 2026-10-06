using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The <c>Refract</c> shader's parameters as <c>refract.cpp</c> declares them and <c>refract_dx9_helper.cpp</c> reads
/// them for a sprite trail (B476).
/// </summary>
/// <remarks>
/// **The specimen is `effects/beam001_white` as TF2 ships it** (`vmt` probe, 2026-10-05): <c>$normalmap
/// effects/beam001_normal</c>, <c>$refractamount .2</c>, <c>$translucent 1</c>, <c>$bluramount 1</c>,
/// <c>$refracttinttexture effects/white</c>, <c>$vertexcolormodulate 1</c>.
/// </remarks>
public sealed class RefractMaterialConformanceTests
{
    private const string Beam001White = """
        "$normalmap" "effects/beam001_normal"
        "$refractamount" ".2"
        "$vertexalpha" "1"
        "$vertexcolor" "1"
        "$translucent" "1"
        "$forcerefract" 1
        "$bluramount" "1"
        "$refracttinttexture" "effects/white"
        "$vertexcolormodulate" "1"
        """;

    private static readonly MapTexture Normal = new(4, 4, 4, 4, TextureImage.None, IsTransparent: false);

    private static readonly MapTexture White = new(4, 4, 4, 4, TextureImage.None, IsTransparent: false);

    /// <remarks>
    /// **Each declared value reaches the draw**: <c>c5.x = $refractamount</c> (`refract_dx9_helper.cpp:285-287`), the
    /// <c>BLUR</c> combo from <c>$bluramount</c> (`:93`), <c>COLORMODULATE</c> from
    /// <c>$vertexcolormodulate</c> (`:96`), <c>REFRACTTINTTEXTURE</c> when that texture is one (`:91`), and the
    /// tint left at <c>SHADER_PARAM( REFRACTTINT, …, "[1 1 1]" )</c> (`refract.cpp:20`).
    /// </remarks>
    [Test]
    public void Read_Beam001White_TakesItsDeclaredParameters()
    {
        RefractMaterial refract = Read(Beam001White, White).ShouldNotBeNull();

        refract.RefractAmount.ShouldBe(0.2f);
        refract.BlurAmount.ShouldBe(1);
        refract.VertexColorModulate.ShouldBeTrue();
        refract.RefractTint.ShouldBe((1f, 1f, 1f));
        refract.RefractTintTexture.ShouldBe(White);
        refract.NormalMap.ShouldBe(Normal);
    }

    /// <remarks>
    /// **An undeclared amount is the parameter's default, 2** — <c>SHADER_PARAM( REFRACTAMOUNT, …, "2", "" )</c>
    /// (`refract.cpp:19`), which <c>InitParamsRefract_DX9</c> does not override.
    /// </remarks>
    [Test]
    public void Read_NoRefractAmount_IsTheParameterDefaultTwo() =>
        Read("\"$normalmap\" \"effects/beam001_normal\"", null).ShouldNotBeNull().RefractAmount.ShouldBe(2f);

    /// <remarks>
    /// **An undeclared blur is 0, not the parameter's 1**: <c>InitParamsRefract_DX9</c> sets 0 when it is not defined
    /// (`refract_dx9_helper.cpp:47-50`). A declared one is clamped to <c>MAXBLUR</c>, 1 (`:16`, `:99-106`).
    /// </remarks>
    [TestCase("", 0)]
    [TestCase("\"$bluramount\" \"2\"", 1)]
    [TestCase("\"$bluramount\" \"-1\"", 0)]
    public void Read_ABlurAmount_IsClampedToMaxBlur(string line, int expected) =>
        Read("\"$normalmap\" \"x\"\n" + line, null).ShouldNotBeNull().BlurAmount.ShouldBe(expected);

    /// <remarks>
    /// **The shader WRITES depth unless told not to**, translucent or not: <c>EnableDepthWrites( bWriteZ )</c> with
    /// <c>bWriteZ = $nowritez == 0</c> (`refract_dx9_helper.cpp:97`, `:119`).
    /// </remarks>
    [TestCase("", true)]
    [TestCase("\"$nowritez\" \"1\"", false)]
    public void Read_NoWriteZ_DecidesWhetherDepthIsWritten(string line, bool writes) =>
        Read("\"$normalmap\" \"x\"\n" + line, null).ShouldNotBeNull().WritesDepth.ShouldBe(writes);

    /// <remarks>
    /// **Pixel fog applies unless <c>$nofog</c>**: the dynamic <c>PIXELFOGTYPE</c> combo is
    /// <c>GetPixelFogCombo()</c> (`refract_dx9_helper.cpp:260`, `:267`), which a material flagged
    /// <c>MATERIAL_VAR_NOFOG</c> turns off. `beam001_*` comments its <c>$nofog</c> out, so it fogs; 84 of the 123 shipped
    /// Refract materials state it (`refract-census`, 2026-10-05).
    /// </remarks>
    [TestCase("", true)]
    [TestCase("\"$nofog\" \"1\"", false)]
    public void Read_NoFog_DecidesWhetherPixelFogApplies(string line, bool fogged) =>
        Read("\"$normalmap\" \"x\"\n" + line, null).ShouldNotBeNull().Fogged.ShouldBe(fogged);

    /// <remarks>
    /// **The combos this port does not draw are refused rather than drawn wrong**: a <c>$basetexture</c> replaces the
    /// frame as the warped image (`:226-233`), and <c>$envmap</c>, <c>$normalmap2</c>, <c>$masked</c> and
    /// <c>$fadeoutonsilhouette</c> select the <c>CUBEMAP</c>, <c>SECONDARY_NORMAL</c>, <c>MASKED</c> and
    /// <c>FADEOUTONSILHOUETTE</c> combos. No trail TF2 ships declares one (B476).
    /// </remarks>
    [TestCase("\"$basetexture\" \"x\"")]
    [TestCase("\"$envmap\" \"env_cubemap\"")]
    [TestCase("\"$normalmap2\" \"x\"")]
    [TestCase("\"$masked\" \"1\"")]
    [TestCase("\"$fadeoutonsilhouette\" \"1\"")]
    public void Read_ACombinationThisPortDoesNotDraw_IsNull(string line) =>
        Read("\"$normalmap\" \"x\"\n" + line, null).ShouldBeNull();

    private static RefractMaterial? Read(string body, MapTexture? tint) =>
        RefractMaterial.Read(
            VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Refract\"\n{\n" + body + "\n}")), Normal, tint);
}
