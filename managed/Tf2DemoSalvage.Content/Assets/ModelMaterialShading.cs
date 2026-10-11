using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>What the generic model shaders do with a material's colour and with light, by shader name (B514).</summary>
/// <remarks>
/// *Read from published source* (`src/materialsystem/stdshaders`, `src/mathlib`). Two rules a material reader
/// cannot see and a renderer has to apply:
///
/// - **`$color` and `$color2` are gamma colours on a model shader.** `VertexLitGeneric`, `UnlitGeneric`, the skin
///   path and `UnlitTwoTexture` all send `ComputeModulationColor` through
///   `SetModulationPixelShaderDynamicState_LinearColorSpace` (`vertexlitgeneric_dx9_helper.cpp:1456`,
///   `skin_dx9_helper.cpp:731`, `unlittwotexture_dx9.cpp:218`) — `color > 1 ? color : GammaToLinear( color )`,
///   `BaseVSShader.cpp:652-661`. `LightmappedGeneric` sends the numbers as they are
///   (`lightmappedgeneric_dx9_helper.cpp:677-688`). Multiplying a linear albedo by the gamma number draws a
///   `[.46 .46 .64]` material 2.5 times too bright.
/// - **An unlit shader reads no light.** `hasDiffuseLighting = bVertexLitGeneric`
///   (`vertexlitgeneric_dx9_helper.cpp:394`), the light state is not fetched (`:1250-1254`), and the pixel shader's
///   diffuse stays 1 (`vertexlit_and_unlit_generic_ps2x.fxc:344-348`).
///
/// Not modeled: `$hdrcolorscale`, which scales the linear colour under HDR (`vertexlitgeneric_dx9_helper.cpp:1450-1453`).
/// </remarks>
public static class ModelMaterialShading
{
    /// <summary><c>GammaToLinear</c> returns 1 from here up (<c>mathlib/color_conversion.cpp:282-288</c>).</summary>
    private const float FullFrom = 0.95f;

    /// <summary>One channel of the modulation colour as the pixel shader receives it.</summary>
    /// <param name="gamma">The channel of <c>$color</c> times <c>$color2</c>, as the material states it.</param>
    /// <returns><c>gamma &gt; 1 ? gamma : GammaToLinear( gamma )</c>.</returns>
    /// <remarks>
    /// <c>GammaToLinear</c> (<c>mathlib/color_conversion.cpp:273-291</c>): 0 below 0, 1 from 0.95, and otherwise
    /// <c>g_Mathlib_GammaToLinear[ RoundFloatToInt( gamma * 255 ) ]</c>, a table of <c>pow( i / 255, 2.2 )</c>
    /// (<c>:210</c>, through <c>GammaToLinearFullRange</c>). The rounding to a byte is the engine's, kept.
    /// </remarks>
    public static float ModulationToLinear(float gamma)
    {
        if (gamma > 1f)
        {
            return gamma;
        }

        return GammaToLinear(gamma);
    }

    /// <summary><c>$envmaptint</c> as the pixel shader receives it (B521).</summary>
    /// <param name="shader">The material's shader name.</param>
    /// <param name="phong">Whether the material sets <c>$phong</c>, which routes <c>VertexLitGeneric</c> to the skin shader.</param>
    /// <param name="tint">The tint as the material states it.</param>
    /// <returns>The tint through <c>GammaToLinear</c> for <c>VertexLitGeneric</c> without phong and <c>UnlitGeneric</c>; unchanged otherwise.</returns>
    /// <remarks>
    /// *Read from published source:* `SetEnvMapTintPixelShaderDynamicStateGammaToLinear`
    /// (`vertexlitgeneric_dx9_helper.cpp:1094-1101`) calls `GetLinearVecValue` (`commandbuilder.h:279-287`); the skin
    /// path reads `GetVecValue` (`skin_dx9_helper.cpp:785-786`, reached through `InitVertexLitGeneric_DX9`'s
    /// `$phong` test, `vertexlitgeneric_dx9_helper.cpp:263-270`) and so does `LightmappedGeneric`
    /// (`lightmappedgeneric_dx9_helper.cpp:677`). *Interpolated:* `GetLinearVecValue` as `GammaToLinear` per
    /// component, with no pass-through above 1 — `cmaterialvar.cpp` is not published.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is null.</exception>
    public static (float Red, float Green, float Blue) EnvMapTint(
        string shader, bool phong, (float Red, float Green, float Blue) tint)
    {
        ArgumentNullException.ThrowIfNull(shader);

        bool linear = (Is(shader, "VertexLitGeneric") && !phong) || Is(shader, "UnlitGeneric");

        return linear
            ? (GammaToLinear(tint.Red), GammaToLinear(tint.Green), GammaToLinear(tint.Blue))
            : tint;
    }

    /// <summary><c>GammaToLinear</c>, <c>mathlib/color_conversion.cpp:273-291</c>.</summary>
    private static float GammaToLinear(float gamma)
    {
        if (gamma < 0f)
        {
            return 0f;
        }

        return gamma >= FullFrom ? 1f : MathF.Pow(MathF.Round(gamma * 255f) / 255f, 2.2f);
    }

    /// <summary>What masks a <c>$phong</c> material's reflection — the skin shader's rule (B521).</summary>
    /// <param name="normalMapAlphaEnvMapMask"><c>$normalmapalphaenvmapmask</c>.</param>
    /// <param name="baseMapAlphaPhongMask"><c>$basemapalphaphongmask</c>.</param>
    /// <param name="invertPhongMask"><c>$invertphongmask</c>.</param>
    /// <param name="selfIllumFresnel"><c>$selfillum</c> with <c>$selfillumfresnel</c>.</param>
    /// <returns>The mask; never <see cref="EnvMapMask.None"/>, because this path has no unmasked reflection.</returns>
    /// <remarks>
    /// *Read from published source*, `skin_ps20b.fxc:200` and `:228-242`:
    /// `fEnvMapMask = lerp( baseColor.a, SELFILLUMFRESNEL ? g_fInvertPhongMask : fSpecMask, normalmapalphaenvmapmask )`,
    /// then `lerp( fEnvMapMask, 1-fEnvMapMask, g_fInvertPhongMask )`, with
    /// `fSpecMask = lerp( normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask )`. `$basealphaenvmapmask` is not read
    /// here at all. Not modeled: `$envmapfresnel`, `lerp( 1, fFresnelRanges, g_EnvMapFresnel.x )`.
    /// </remarks>
    public static EnvMapMask SkinEnvMapMask(
        bool normalMapAlphaEnvMapMask, bool baseMapAlphaPhongMask, bool invertPhongMask, bool selfIllumFresnel)
    {
        if (normalMapAlphaEnvMapMask && selfIllumFresnel)
        {
            return EnvMapMask.Nothing;
        }

        if (normalMapAlphaEnvMapMask && !baseMapAlphaPhongMask)
        {
            return invertPhongMask ? EnvMapMask.InvertedBumpAlpha : EnvMapMask.BumpAlpha;
        }

        return invertPhongMask ? EnvMapMask.InvertedBaseAlpha : EnvMapMask.BaseAlpha;
    }

    /// <summary>Whether a shader sends its modulation colour through <see cref="ModulationToLinear"/>.</summary>
    /// <param name="shader">The material's shader name.</param>
    /// <returns>True for <c>VertexLitGeneric</c>, <c>UnlitGeneric</c> and <c>UnlitTwoTexture</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is null.</exception>
    public static bool ConvertsModulation(string shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        return Is(shader, "VertexLitGeneric") || !TakesDiffuseLighting(shader);
    }

    /// <summary>Whether a shader's pixel shader reads any diffuse light.</summary>
    /// <param name="shader">The material's shader name.</param>
    /// <returns>False for <c>UnlitGeneric</c> and <c>UnlitTwoTexture</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is null.</exception>
    public static bool TakesDiffuseLighting(string shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        return !Is(shader, "UnlitGeneric") && !Is(shader, "UnlitTwoTexture");
    }

    /// <summary>A shader by name, case aside; a hardware fallback's suffix (<c>_DX8</c>) counts as the same shader.</summary>
    private static bool Is(string shader, string name) => shader.StartsWith(name, StringComparison.OrdinalIgnoreCase);
}
