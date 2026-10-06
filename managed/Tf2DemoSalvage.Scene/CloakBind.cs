using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the invisibility proxies write when one entity is bound — per entity, as every proxy is.</summary>
/// <param name="SpyInvis">
/// <c>CSpyInvisProxy</c>'s <c>$cloakfactor</c> (`c_tf_player.cpp:1720`): the owner's
/// <c>GetEffectiveInvisibilityLevel</c>.
/// </param>
/// <param name="Invis">
/// <c>CInvisProxy</c>'s (`tf_viewmodel.cpp:521`): the effective level, or the local player's remapped one.
/// </param>
/// <param name="PlayerTint">
/// The team's <c>$cloakColorTint</c>, which <c>spy_invis</c> writes only when the entity IS the player; null otherwise.
/// </param>
/// <param name="Undrawn">
/// <c>C_TFPlayer::DrawModel</c>'s early return (`c_tf_player.cpp:6938`): the player body at effective level 1.
/// </param>
public readonly record struct CloakBind(
    float SpyInvis, float Invis, (float Red, float Green, float Blue)? PlayerTint = null, bool Undrawn = false)
{
    /// <summary>Whether either proxy puts a cloak pass on screen — what makes the model translucent this frame.</summary>
    /// <remarks>
    /// The shader's per-frame <c>IsTranslucent</c> answers true while <c>$cloakfactor</c> is strictly inside (0, 1)
    /// (`cloak_blended_pass_helper.cpp`'s template), so the entity is collated with the translucent renderables.
    /// </remarks>
    public bool Cloaking => CloakPass.DrawsCloakPass(SpyInvis) || CloakPass.DrawsCloakPass(Invis);

    /// <summary>A material's bound <c>$cloakfactor</c> and <c>$cloakColorTint</c> after its proxies run, in file order.</summary>
    /// <param name="pass">The material's cloak pass.</param>
    /// <param name="proxies">The material's proxies; the last invisibility proxy wins.</param>
    /// <returns>The factor and the tint the cloak pass draws with.</returns>
    /// <remarks>
    /// <c>vm_invis</c> is read as <c>invis</c>: the two differ only for a non-local player's non-viewmodel entity, where
    /// <c>vm_invis</c> takes <c>GetPercentInvisible</c> without the teammate cap. **Not ported, named:** the tint a
    /// <c>spy_invis</c> bind on one player leaves in the shared material for the next entity bound without one.
    /// </remarks>
    public (float Factor, (float Red, float Green, float Blue) Tint) For(CloakPass pass, IReadOnlyList<MaterialProxy> proxies)
    {
        System.ArgumentNullException.ThrowIfNull(pass);
        System.ArgumentNullException.ThrowIfNull(proxies);

        float factor = pass.CloakFactor;
        (float Red, float Green, float Blue) tint = pass.ColorTint;

        foreach (MaterialProxy proxy in proxies)
        {
            if (proxy.Name.Equals("spy_invis", System.StringComparison.OrdinalIgnoreCase))
            {
                factor = SpyInvis;
                tint = PlayerTint ?? tint;
            }
            else if (proxy.Name.Equals("invis", System.StringComparison.OrdinalIgnoreCase) ||
                     proxy.Name.Equals("vm_invis", System.StringComparison.OrdinalIgnoreCase))
            {
                factor = Invis;
            }
        }

        return (factor, tint);
    }
}
