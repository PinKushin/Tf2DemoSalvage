using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The <c>Sprite</c> shader's gamma path (B521): which materials take it, and the scale it multiplies by.
/// </summary>
/// <remarks>
/// **What the engine does, in the order the sources say it.** *Read from published source:* a Sprite material whose
/// <c>$nosrgb</c> nobody set has it set to ONE by the shader's own <c>SHADER_INIT_PARAMS</c> (`sprite_dx9.cpp:63-67`);
/// then <c>EnableSRGBRead( SHADER_SAMPLER0, false )</c> (`:124`), no <c>GammaToLinear</c> on the vertex colour
/// (`sprite_vs20.fxc:52-56`), <c>FinalOutput( …, TONEMAP_SCALE_GAMMA )</c> (`sprite_ps2x.fxc:51`) and
/// <c>EnableSRGBWrite( false )</c> (`:163`). *Read in disassembly:* <c>GAMMA_LIGHT_SCALE</c> is the tone-map scale to
/// the power <c>1 / 2.2</c> (<c>shaderapidx9.dll 0x180023be0</c>, <c>0x18003ddd0</c>).
///
/// **Valve says what the difference looks like**, beside the write state: "OSX always has to sRGB write (don't do this
/// on Linux/Win GL - it causes glow sprites to be way too dark)" (`:162`). That was this viewer: every glow decoded
/// and added in linear light. The picture-level half is `EntityBeamRenderTests`.
/// </remarks>
public sealed class SpriteGammaSpaceConformanceTests
{
    /// <remarks>
    /// <c>pow( scale, 0.4545… )</c>: one at a scale of one, so a forced exposure of 1 hides it; 0.7297 at TF2's
    /// auto-exposure floor of 0.5, where the linear scale would have halved the glow.
    /// </remarks>
    [TestCase(1f, 1f)]
    [TestCase(0.5f, 0.72974f)]
    [TestCase(0.55f, 0.76204f)]
    [TestCase(2f, 1.37035f)]
    public void From_ALinearToneMapScale_IsThatScaleToTheOneOverTwoPointTwo(float linear, float gamma)
    {
        GammaLightScale.From(linear).ShouldBe(gamma, 0.00001f);
    }

    /// <remarks>
    /// **Only the Sprite shader has the path, and only while <c>$nosrgb</c> stands.** The controls are the two ways out
    /// of it: a material that clears the flag, and the same text under another shader — a rope's <c>Cable</c>, a
    /// trail's <c>UnlitGeneric</c> — which never read it.
    /// </remarks>
    [TestCase("Sprite", true, true)]
    [TestCase("sprite", true, true)]
    [TestCase("Sprite", false, false)]
    [TestCase("UnlitGeneric", true, false)]
    [TestCase("Cable", true, false)]
    public void Init_AMaterialsShaderAndNoSrgb_DecideWhetherItIsDrawnInGammaSpace(string shader, bool noSrgb, bool expected)
    {
        EngineSprite sprite = EngineSprite.Init(
            new MapTexture(128, 128, 128, 128, TextureImage.None, IsTransparent: true),
            [],
            SpriteBlend.Translucent,
            SpriteOrientation.Parallel,
            origin: null,
            constantColor: (1f, 1f, 1f, 1f),
            ignoresVertexColors: false,
            shader: shader,
            noSrgb: noSrgb);

        sprite.Material.GammaSpace.ShouldBe(expected);
    }

    /// <remarks>
    /// **On what the game ships**: the lamp's two materials are bare <c>Sprite</c> materials and take the path; the
    /// cable beside them in the same load is a <c>Cable</c> material and does not.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void SpriteMaterials_AsTheMapLoadReadsThem_PutTheLampsInGammaSpaceAndNotTheCable()
    {
        MapAssets assets = MapCache.Effects();

        assets.SpriteMaterials["sprites/light_glow03.vmt"].Material.GammaSpace.ShouldBeTrue();
        assets.SpriteMaterials["sprites/glow_test02.vmt"].Material.GammaSpace.ShouldBeTrue();
        assets.SpriteMaterials["cable/cable.vmt"].Material.GammaSpace.ShouldBeFalse();
    }
}
