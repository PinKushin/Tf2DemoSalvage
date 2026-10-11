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
