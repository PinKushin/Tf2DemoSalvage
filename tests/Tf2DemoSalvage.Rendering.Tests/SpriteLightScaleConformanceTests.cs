namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>What a sprite's pixel shader multiplies its colour by before it reaches the frame (B378).</summary>
/// <remarks>
/// *Read from published source*: <c>UnlitGeneric</c> ends in
/// <c>FinalOutput( float4( result.rgb, alpha ), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, … )</c>
/// (`vertexlit_and_unlit_generic_ps2x.fxc:498`, `:504`), <c>SpriteCard</c> in
/// <c>FinalOutput( blended_rgb, 0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR )</c> (`spritecard_ps2x.fxc:192`), and
/// <c>TONEMAP_SCALE_LINEAR</c> is <c>result.rgb = vShaderColor.rgb * LINEAR_LIGHT_SCALE</c> (`common_ps_fxc.h:348-350`).
/// The Sprite shader's gamma path is <c>TONEMAP_SCALE_GAMMA</c> (`sprite_ps2x.fxc:51`). The linear path multiplied by
/// SpriteCard's <c>$overbrightfactor</c> alone, so every UnlitGeneric and SpriteCard sprite drew at a tone-map scale of
/// one — the sun's glow among them.
/// </remarks>
public sealed class SpriteLightScaleConformanceTests
{
    [Test]
    public void For_ALinearMaterial_IsItsOverbrightTimesTheToneMapScale() =>
        SpriteLightScale.For(gammaSpace: false, linearScale: 0.7f, overbright: 2f).ShouldBe(1.4f, 1e-6f);

    [Test]
    public void For_AGammaMaterial_IsTheGammaLightScale() =>
        SpriteLightScale.For(gammaSpace: true, linearScale: 0.7f, overbright: 2f).ShouldBe(GammaLightScale.From(0.7f));

    /// <remarks>The control: at a scale of one the linear path is the overbright factor, as it was.</remarks>
    [Test]
    public void For_ALinearMaterialAtScaleOne_IsItsOverbright() =>
        SpriteLightScale.For(gammaSpace: false, linearScale: 1f, overbright: 2f).ShouldBe(2f);
}
