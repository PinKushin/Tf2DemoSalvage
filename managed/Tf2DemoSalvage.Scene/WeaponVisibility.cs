using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which of a player's weapons is drawn in the world.
/// </summary>
/// <remarks>
/// **`C_BaseCombatWeapon::ShouldDraw`, <c>c_basecombatweapon.cpp:399</c>.** For a weapon owned by
/// somebody other than the viewer, the whole rule is two lines:
///
/// <code>
/// // If it's a player, then only show active weapons
/// if ( pOwner->IsPlayer() )
/// {
///     // Show it if it's active...
///     return bIsActive;
/// }
/// </code>
///
/// with <c>bIsActive = ( m_iState == WEAPON_IS_ACTIVE )</c>. A player carries three weapons and
/// holds one; without this every player wears all three at once, bone-merged into the same hand.
///
/// **Unowned weapons always draw**, which is the branch above it — <c>if ( !pOwner ) return
/// true;</c>. That is a dropped weapon lying on the floor, and it sends a real origin of its own.
///
/// **Wearables are NOT weapons and must not be filtered, which is the trap here.** The Mantreads,
/// a demoman's shield and a sniper's Razorback are worn whatever is in the player's hands, and they
/// live under <c>models/weapons/</c> like everything else — so a rule that matched on the model
/// path would strip them off. The owner named all three as cases that must survive.
///
/// Valve's own class split answers it and no list is needed: <c>m_iState</c> is declared by
/// <c>DT_BaseCombatWeapon</c> (<c>basecombatweapon_shared.cpp:2871</c>), so an entity that is not a
/// <c>CBaseCombatWeapon</c> never sends it. A null state means "this is not a weapon" and the rule
/// does not apply.
/// </remarks>
public static class WeaponVisibility
{
    /// <summary>Drops the weapons a player is carrying but not holding.</summary>
    /// <param name="scene">Everything the timeline says exists at this tick.</param>
    /// <returns>What to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <remarks>
    /// Returns the original list when nothing was dropped, so a scene with no carried weapons —
    /// every point-of-view demo of a lone player, and every tick before anyone spawns — pays
    /// nothing.
    /// </remarks>
    public static IReadOnlyList<SceneProp> Visible(IReadOnlyList<SceneProp> scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int holstered = 0;

        foreach (SceneProp prop in scene)
        {
            if (IsHolstered(prop))
            {
                holstered++;
            }
        }

        if (holstered == 0)
        {
            return scene;
        }

        List<SceneProp> drawn = new(scene.Count - holstered);

        foreach (SceneProp prop in scene)
        {
            if (!IsHolstered(prop))
            {
                drawn.Add(prop);
            }
        }

        return drawn;
    }

    /// <summary>Whether this is a weapon a player owns and is not holding.</summary>
    /// <remarks>
    /// All three conditions matter and each covers a different case: no state means a wearable, no
    /// owner means a weapon on the ground, and the active state means the one in their hands.
    /// </remarks>
    private static bool IsHolstered(SceneProp prop) =>
        prop.WeaponState is { } state &&
        prop.OwnedBy is not null &&
        state != EntityState.WeaponActive;
}
