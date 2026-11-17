using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which of a disguised spy's cosmetics and weapons are drawn, and to whom.
/// </summary>
/// <remarks>
/// **The server sends a disguise's gear as its own entities, bone-merged to the spy**, so an ENEMY
/// sees a convincing soldier. This project drew every one of them regardless of who was looking,
/// which put soldier hats and a rocket launcher on a spy's skeleton — measured at the ticks the
/// owner named:
///
/// <code>
///   SPY 2 class 8 team 3 enemy False as 3/2
///   PROP-AT-SPY 1031 '.../items/soldier/hwn2023_warlocks_warcloak.mdl' attached 2
///   PROP-AT-SPY 1015 '.../c_rocketlauncher.mdl'                        attached 2
/// </code>
///
/// **Two engine functions, and they are mirror images rather than the same rule twice.**
///
/// `CTFWearable::ShouldDraw` (<c>tf_item_wearable.cpp:344</c>) — while the owner is disguised,
/// EVERY third-person wearable is hidden, and the disguise's own are then shown back to an enemy:
///
/// <code>
///   if ( pOwner-&gt;m_Shared.InCond( TF_COND_DISGUISED ) &amp;&amp; !IsViewModelWearable() )
///   {
///       if ( m_bDisguiseWearable &amp;&amp; pLocalPlayer )
///       {
///           if ( GetEnemyTeam( pOwner-&gt;GetTeamNumber() ) != iLocalPlayerTeam )
///               return false;
///           else if ( disguiseClass == TF_CLASS_SPY &amp;&amp; disguiseTeam == iLocalPlayerTeam )
///               return false;
///           else
///               return BaseClass::ShouldDraw();
///       }
///       return false;
///   }
/// </code>
///
/// **That final `return false` is the line most easily missed**: a spy's OWN cosmetics are hidden
/// while he is disguised, from everyone. A teammate sees a bare spy, not a spy in his own hats.
///
/// `CTFWeaponBase::ShouldDraw` (<c>tf_weaponbase.cpp:3226</c>) is the other direction:
///
/// <code>
///   if ( iLocalPlayerTeam != pOwner-&gt;GetTeamNumber() &amp;&amp; iLocalPlayerTeam != TEAM_SPECTATOR )
///       if ( GetDisguiseWeapon() != this ) return false;   // enemy: ONLY the disguise weapon
///   else
///       if ( m_bDisguiseWeapon ) return false;             // friendly: never the disguise weapon
/// </code>
///
/// So an enemy loses the spy's REAL weapon and a teammate loses the disguise's. Implementing one
/// direction and not the other leaves a spy holding two weapons to somebody.
///
/// **Not implemented, named rather than omitted:** the coaching substitution in both functions
/// (`m_bIsCoaching` / `m_hStudent`), the weapon rule's `GetWeaponAssociatedWith` branch for wearables tied to a weapon
/// (B770), and `TEAM_SPECTATOR` — a SourceTV recording has no local player, and this treats that as "not an enemy", so a
/// spectator sees the spy's own loadout rather than the disguise's. The control stun is here (B769); the sniper-zoom hide
/// is the local player's own, in `LocalPlayerVisibility` (B598).
/// </remarks>
public static class DisguiseVisibility
{
    /// <summary><c>TF_CLASS_SPY</c>, <c>tf_shareddefs.h:214</c>.</summary>
    private const int SpyClass = 8;

    /// <summary>Which props survive the disguise rules.</summary>
    /// <param name="drawn">Everything this moment would draw.</param>
    /// <param name="players">The players, for their disguise state.</param>
    /// <returns>A predicate the draw list can be filtered by.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <returns>The props that survive, in the shape <c>DrawList.KeepOnly</c> takes.</returns>
    /// <param name="ghostWearable">
    /// Whether an item is tagged `ghost_wearable` (`HasEconTag`), the one kind a ghost still wears; null for no schema, when
    /// a ghost wears nothing.
    /// </param>
    public static IReadOnlyList<SceneProp> Visible(
        IReadOnlyList<SceneProp> drawn, IReadOnlyList<ScenePlayer> players, Func<int, bool>? ghostWearable = null)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(players);

        // The monitor's list is empty on most frames, and now comes through here too (B769): no set to build for it.
        if (drawn.Count == 0)
        {
            return drawn;
        }

        // **Only the disguised ones, the ghosts and the control-stunned, because each rule sits inside its own `InCond`.**
        // An ordinary player's cosmetics must be untouched, and building the set first means the walk below returns the
        // list unchanged in the overwhelmingly common case.
        Dictionary<int, ScenePlayer> affected = [];

        foreach (ScenePlayer player in players)
        {
            if (player.Conditions.Has(PlayerConditions.Disguised) || IsGhost(player) || player.IsControlStunned)
            {
                affected[player.EntityIndex] = player;
            }
        }

        if (affected.Count == 0)
        {
            return drawn;
        }

        List<SceneProp> kept = new(drawn.Count);

        foreach (SceneProp prop in drawn)
        {
            if (Keep(prop, affected, ghostWearable))
            {
                kept.Add(prop);
            }
        }

        return kept;
    }

    /// <summary>`TF_COND_HALLOWEEN_GHOST_MODE`.</summary>
    internal static bool IsGhost(ScenePlayer player) => player.Conditions.Has(PlayerConditions.HalloweenGhostMode);

    private static bool Keep(SceneProp prop, Dictionary<int, ScenePlayer> affected, Func<int, bool>? ghostWearable)
    {
        // **A player's own body is never removed here.** Which model a spy wears is
        // `Disguise.VisibleClass`'s job; a visibility rule that dropped players would blank him
        // rather than undress him.
        if (affected.ContainsKey(prop.EntityIndex))
        {
            return true;
        }

        // `GetOwnerEntity()`, which for a viewmodel wearable is not its move parent: that is the viewmodel it follows (B771,
        // `econ_wearable.cpp:185-194`).
        int? owner = ViewModelWearables.Is(prop) ? prop.OwnedBy : prop.AttachedTo ?? prop.OwnedBy;

        if (owner is not { } wearer || !affected.TryGetValue(wearer, out ScenePlayer player))
        {
            return true;
        }

        // **A control stun first of all, and for the weapon only** (B769): `CTFWeaponBase::ShouldDraw` returns false on it
        // before the ghost and disguise rules (`tf_weaponbase.cpp:3219-3220`); `CTFWearable::ShouldDraw` never asks. A
        // wearable tied to that weapon (`m_hWeaponAssociatedWith`) hides with it in TF2, and here does not: B770.
        if (prop.WeaponState is not null && player.IsControlStunned)
        {
            return false;
        }

        // Stunned, neither disguised nor a ghost: everything else he carries is his, and nothing below applies.
        if (!player.Conditions.Has(PlayerConditions.Disguised) && !IsGhost(player))
        {
            return true;
        }

        // **Ghost mode first, in both functions** (B767): a ghost has no weapon (`tf_weaponbase.cpp:3222-3224`) and wears
        // only a `ghost_wearable` (`tf_item_wearable.cpp:325-336`), whatever his disguise.
        if (IsGhost(player))
        {
            return prop.WeaponState is null && prop.ItemDefinitionIndex is { } item && ghostWearable?.Invoke(item) == true;
        }

        // **The disguise branch is for a third-person wearable only** (`!IsViewModelWearable()`, `tf_item_wearable.cpp:344`):
        // a disguised spy's viewmodel wearable falls to the weapon branch, `WeaponWearableVisibility`'s (B771).
        if (prop.WeaponState is null && ViewModelWearables.Is(prop))
        {
            return true;
        }

        return prop.WeaponState is not null
            ? KeepWeapon(prop, player)
            : KeepWearable(prop, player);
    }

    /// <summary>`CTFWearable::ShouldDraw`'s disguise branch.</summary>
    private static bool KeepWearable(SceneProp prop, ScenePlayer spy)
    {
        // His own cosmetics are hidden while disguised — the outer branch's final `return false`.
        if (!prop.OfDisguise)
        {
            return false;
        }

        // A teammate does not see the disguise at all.
        if (!spy.IsEnemy)
        {
            return false;
        }

        // An enemy spy impersonating one of OUR spies shows nothing, so his gear cannot give him
        // away: `disguiseClass == TF_CLASS_SPY && disguiseTeam == iLocalPlayerTeam`. We are the
        // local player, so "our team" is whichever team the spy is NOT on.
        return spy.DisguiseClass != SpyClass || spy.DisguiseTeam == spy.Team;
    }

    /// <summary>`CTFWeaponBase::ShouldDraw`'s disguise branch, which is the mirror image.</summary>
    private static bool KeepWeapon(SceneProp prop, ScenePlayer spy) =>
        spy.IsEnemy

            // An enemy sees ONLY the disguise weapon, so the spy's real one goes.
            ? prop.OfDisguise

            // A teammate never sees the disguise weapon, and keeps seeing the real one.
            : !prop.OfDisguise;
}
