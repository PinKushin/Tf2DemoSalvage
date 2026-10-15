using System;

namespace Tf2DemoSalvage.Render;

/// <summary><c>GAMMA_LIGHT_SCALE</c> — what a shader working in gamma space multiplies by where a linear one multiplies by the tone-map scale (B521).</summary>
/// <remarks>
/// <c>FinalOutput( …, TONEMAP_SCALE_GAMMA )</c> is <c>vShaderColor.rgb * GAMMA_LIGHT_SCALE</c>, and
/// <c>GAMMA_LIGHT_SCALE</c> is <c>cLightScale.w</c> (`common_ps_fxc.h:345-356`, read from published source). What the
/// engine puts there is the linear scale raised to <c>1 / 2.2</c> — *read in disassembly*, <c>shaderapidx9.dll</c>
/// <c>0x180023be0</c> (<c>SetToneMappingScaleLinear</c>): after writing <c>cLightScale.x</c> it stores
/// <c>FUN_18003ddd0( x )</c> in <c>.w</c>, and that function is <c>pow( x, 0.45454545 )</c> (the double at its call is
/// <c>0x3FDD1745C6E043B8</c>). A power, not the sRGB curve.
/// </remarks>
public static class GammaLightScale
{
    /// <summary>The gamma light scale for a linear tone-map scale.</summary>
    /// <param name="linearScale"><c>LINEAR_LIGHT_SCALE</c>, the tone-map scale.</param>
    /// <returns><c>pow( linearScale, 1 / 2.2 )</c>.</returns>
    public static float From(float linearScale) => (float)Math.Pow(linearScale, 0.454545454545454545);
}

/// <summary>What a sprite's pixel shader multiplies its colour by — <c>FinalOutput</c>'s tone-map scale (B378).</summary>
/// <remarks>
/// <c>UnlitGeneric</c> and <c>SpriteCard</c> end in <c>TONEMAP_SCALE_LINEAR</c>, <c>vShaderColor.rgb * LINEAR_LIGHT_SCALE</c>
/// (`vertexlit_and_unlit_generic_ps2x.fxc:498`, `spritecard_ps2x.fxc:192`, `common_ps_fxc.h:348-350`); the Sprite
/// shader's gamma path ends in <c>TONEMAP_SCALE_GAMMA</c> (`sprite_ps2x.fxc:51`).
/// </remarks>
public static class SpriteLightScale
{
    /// <summary>The colour scale a sprite batch draws with.</summary>
    /// <param name="gammaSpace">Whether the material takes the Sprite shader's gamma path.</param>
    /// <param name="linearScale"><c>LINEAR_LIGHT_SCALE</c>, the tone-map scale.</param>
    /// <param name="overbright">SpriteCard's <c>$overbrightfactor</c>, one for every other material (B523).</param>
    /// <returns>The gamma light scale, or the overbright factor times the tone-map scale.</returns>
    public static float For(bool gammaSpace, float linearScale, float overbright) =>
        gammaSpace ? GammaLightScale.From(linearScale) : overbright * linearScale;
}
