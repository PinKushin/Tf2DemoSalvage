using System;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What the <c>Refract</c> shader draws a strip with — <c>DrawRefract_DX9</c>'s inputs (`refract_dx9_helper.cpp:86-293`),
/// for the combination TF2's refracting trails select (B476).
/// </summary>
/// <param name="NormalMap"><c>$normalmap</c>, loaded as a bump map: read raw, never through the sRGB curve.</param>
/// <param name="RefractAmount"><c>$refractamount</c>; <c>c5.x</c>.</param>
/// <param name="RefractTint"><c>$refracttint</c> as written, gamma; the shader is handed it linear (<c>:282</c>).</param>
/// <param name="RefractTintTexture"><c>$refracttinttexture</c>, read through the sRGB curve; null when it is no texture.</param>
/// <param name="BlurAmount">The <c>BLUR</c> combo, 0 or 1.</param>
/// <param name="VertexColorModulate">The <c>COLORMODULATE</c> combo, <c>$vertexcolormodulate</c>.</param>
/// <param name="WritesDepth"><c>bWriteZ</c>: <c>$nowritez</c> is 0.</param>
/// <param name="Fogged">
/// The <c>PIXELFOGTYPE</c> combo is on: <c>GetPixelFogCombo()</c> (`:260`, `:267`) unless <c>$nofog</c>.
/// </param>
/// <remarks>
/// **The image warped is the FRAME**, copied into <c>_rt_PowerOfTwoFB</c> before each translucent renderable that
/// needs it (`viewrender.cpp:4609-4635`) and bound when there is no <c>$basetexture</c> (`refract_dx9_helper.cpp:
/// 226-233`). That copy is the renderer's; this is only the material.
/// </remarks>
public sealed record RefractMaterial(
    MapTexture NormalMap,
    float RefractAmount,
    (float Red, float Green, float Blue) RefractTint,
    MapTexture? RefractTintTexture,
    int BlurAmount,
    bool VertexColorModulate,
    bool WritesDepth,
    bool Fogged = true)
{
    /// <summary><c>#define MAXBLUR 1</c> (`refract_dx9_helper.cpp:16`).</summary>
    private const int MaximumBlur = 1;

    /// <summary><c>SHADER_PARAM( REFRACTAMOUNT, SHADER_PARAM_TYPE_FLOAT, "2", "" )</c> (`refract.cpp:19`).</summary>
    private const float DefaultRefractAmount = 2f;

    /// <summary>
    /// Parameters that select a combo this strip pass does not draw: <c>$basetexture</c> replaces the frame as the image,
    /// and the rest select <c>CUBEMAP</c>, <c>SECONDARY_NORMAL</c>, <c>MASKED</c> and <c>FADEOUTONSILHOUETTE</c>.
    /// </summary>
    /// <remarks>
    /// **No sprite or particle material TF2 ships selects one** (`refract-census`, 2026-10-05): none of the 123 Refract
    /// materials states <c>$masked</c>, <c>$fadeoutonsilhouette</c> or <c>$normalmap2</c>, and the 11 with
    /// <c>$envmap</c> and 2 with <c>$basetexture _rt_Camera</c> are all <c>$model</c> materials. The model path reads the
    /// rest through here too (B506), and those 13 stay refused there: they are HL2 props and shader tests TF2 never draws.
    /// </remarks>
    private static readonly string[] Refused = ["$basetexture", "$envmap", "$normalmap2"];

    private static readonly string[] RefusedWhenSet = ["$masked", "$fadeoutonsilhouette"];

    /// <summary>Reads a <c>Refract</c> material, or null for a combination this port does not draw.</summary>
    /// <param name="material">The material.</param>
    /// <param name="normalMap">Its <c>$normalmap</c>, decoded.</param>
    /// <param name="tintTexture">Its <c>$refracttinttexture</c>, decoded, or null.</param>
    /// <returns>The parameters, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is null.</exception>
    /// <remarks>
    /// **An undeclared <c>$bluramount</c> is 0, not the parameter's default of 1**: <c>InitParamsRefract_DX9</c> sets 0
    /// when it is not defined (`:47-50`), and <c>DrawRefract_DX9</c> clamps it to [0, <c>MAXBLUR</c>] (`:99-106`).
    /// </remarks>
    public static RefractMaterial? Read(VmtMaterial material, MapTexture normalMap, MapTexture? tintTexture)
    {
        ArgumentNullException.ThrowIfNull(material);

        foreach (string key in Refused)
        {
            if (material.Value(key) is not null)
            {
                return null;
            }
        }

        foreach (string key in RefusedWhenSet)
        {
            if (Integer(material, key) != 0)
            {
                return null;
            }
        }

        return new RefractMaterial(
            normalMap,
            material.Number("$refractamount", DefaultRefractAmount),
            material.Colour("$refracttint"),
            tintTexture,
            Math.Clamp(Integer(material, "$bluramount"), 0, MaximumBlur),
            Integer(material, "$vertexcolormodulate") != 0,
            Integer(material, "$nowritez") == 0,
            Integer(material, "$nofog") == 0);
    }

    /// <summary>A parameter as <c>GetIntValue</c> reads it: the integer part, 0 when undeclared.</summary>
    private static int Integer(VmtMaterial material, string key) =>
        material.Value(key) is { } text && int.TryParse(
            text.Trim().Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
}
