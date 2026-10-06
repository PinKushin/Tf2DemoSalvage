using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A material's cloak pass — <c>$cloakpassenabled</c> on VertexLitGeneric, drawn by <c>DrawCloakBlendedPass</c>
/// (`cloak_blended_pass_helper.cpp`) after the standard pass: the frame copy, warped and tinted, blended over the model.
/// </summary>
/// <param name="ColorTint"><c>$cloakcolortint</c>, handed to the pixel shader as written (c7).</param>
/// <param name="RefractAmount"><c>$refractamount</c> (c6.y).</param>
/// <param name="CloakFactor">
/// <c>$cloakfactor</c> as the material declares it — what the pass sees when no invisibility proxy writes it.
/// </param>
/// <remarks>
/// **A cloaked spy is not a Refract material and not an alpha fade.** The player, his weapons and his cosmetics are
/// VertexLitGeneric with this pass; the <c>spy_invis</c> and <c>invis</c> proxies write <c>$cloakfactor</c> per entity
/// (`c_tf_player.cpp:1667`, `tf_viewmodel.cpp:521`).
/// </remarks>
public sealed record CloakPass((float Red, float Green, float Blue) ColorTint, float RefractAmount, float CloakFactor)
{
    /// <summary><c>kDefaultRefractAmount</c> (`cloak_blended_pass_helper.h`).</summary>
    private const float DefaultRefractAmount = 0.1f;

    /// <summary>
    /// <c>CloakBlendedPassIsFullyOpaque</c>, negated: whether <c>SHADER_DRAW</c> still draws the standard pass.
    /// </summary>
    /// <param name="cloakFactor">The bound <c>$cloakfactor</c>.</param>
    /// <returns>False once the cloak alone covers the model — <c>clamp( Lerp( cf, 1, 1 − 1.35 ) ) ≤ 0.4</c>.</returns>
    public static bool DrawsStandardPass(float cloakFactor)
    {
        // "Assume V.N = 0.0f" — the silhouette, where the cloak is weakest.
        const float fresnel = 1f - 0f;
        float factor = Math.Clamp(cloakFactor, 0f, 1f);
        float lerp = Math.Clamp(1f + (factor * (fresnel - 1.35f - 1f)), 0f, 1f);

        return lerp > 0.4f;
    }

    /// <summary>The per-frame test in <c>SHADER_DRAW</c> (`vertexlitgeneric_dx9.cpp:512`): strictly between 0 and 1.</summary>
    /// <param name="cloakFactor">The bound <c>$cloakfactor</c>.</param>
    /// <returns>Whether the cloak pass draws this frame.</returns>
    public static bool DrawsCloakPass(float cloakFactor) => cloakFactor > 0f && cloakFactor < 1f;

    /// <summary><c>CSpyInvisProxy::OnBind</c>'s <c>$cloakColorTint</c> for a player (`c_tf_player.cpp:1737-1749`).</summary>
    /// <param name="team">The player's team.</param>
    /// <returns>RED (1, 0.5, 0.4); BLUE and default (0.4, 0.5, 1).</returns>
    public static (float Red, float Green, float Blue) TeamTint(int? team) =>
        team == 2 ? (1f, 0.5f, 0.4f) : (0.4f, 0.5f, 1f);

    /// <summary>Reads the pass off a material, or null when it does not enable one.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The pass, with <c>InitParamsCloakBlendedPass</c>'s defaults for what is undeclared.</returns>
    public static CloakPass? Read(VmtMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        // `params[CLOAKPASSENABLED]->GetIntValue()`: the integer part.
        if (material.Value("$cloakpassenabled") is not { } enabled ||
            !int.TryParse(enabled.Trim().Split('.')[0], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int on) || on == 0)
        {
            return null;
        }

        return new CloakPass(
            material.Value("$cloakcolortint") is null ? (1f, 1f, 1f) : material.Colour("$cloakcolortint"),
            material.Number("$refractamount", DefaultRefractAmount),
            material.Number("$cloakfactor", 0f));
    }
}
