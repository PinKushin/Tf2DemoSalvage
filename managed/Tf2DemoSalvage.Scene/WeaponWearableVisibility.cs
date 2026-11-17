using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which wearables a weapon controls are drawn: they draw with their weapon (B770).</summary>
/// <remarks>
/// **`CTFWearable::ShouldDraw`'s last branch** (`tf_item_wearable.cpp:378-410`), reached only when the owner is neither a
/// ghost (`:322-335`) nor disguised (`:344-377`) — those are `DisguiseVisibility`'s:
///
/// <code>
///   CTFWeaponBase *pWeapon = assert_cast&lt; CTFWeaponBase* &gt;( GetWeaponAssociatedWith() );
///   if ( pWeapon )
///   {
///       if ( pOwner &amp;&amp; pOwner-&gt;GetActiveWeapon() != pWeapon ) return false;
///       if ( !IsViewModelWearable() ) if ( !pWeapon-&gt;ShouldDraw() ) return false;
///       if ( pWeapon-&gt;IsBeingRepurposedForTaunt() ) return false;
///   }
///   return BaseClass::ShouldDraw();
/// </code>
///
/// and `CEconWearable::ShouldDraw`'s dead-owner test beneath it (`econ_wearable.cpp:395`), which a tied wearable fails
/// (`ShouldDrawWhenPlayerIsDead` is `GetWeaponAssociatedWith() == NULL`, `tf_item_wearable.h:65`).
///
/// **`pWeapon->ShouldDraw()` is answered by the list itself**: every rule that hides a weapon (`WeaponVisibility`,
/// `DisguiseVisibility`, first person, `LocalPlayerVisibility`) has already taken it out of the list it was in, so a
/// weapon in the moment and not in the list is one whose `ShouldDraw` is false. This runs after all of them.
/// </remarks>
public static class WeaponWearableVisibility
{
    /// <summary>Which props survive the rule.</summary>
    /// <param name="drawn">The list being drawn, after every rule that hides a weapon.</param>
    /// <param name="moment">Every prop of the moment, for the weapon's taunt flag when it is drawn at all.</param>
    /// <param name="players">The players, for the owner's active weapon, life and conditions.</param>
    /// <returns>The props that survive, in the shape <c>DrawList.KeepOnly</c> takes; <paramref name="drawn"/> itself when
    /// nothing is tied.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SceneProp> Visible(
        IReadOnlyList<SceneProp> drawn, IReadOnlyList<SceneProp> moment, IReadOnlyList<ScenePlayer> players)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(players);

        List<SceneProp>? kept = null;

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];
            bool keep = Keep(prop, drawn, moment, players);

            if (!keep && kept is null)
            {
                kept = new List<SceneProp>(drawn.Count);

                for (int before = 0; before < index; before++)
                {
                    kept.Add(drawn[before]);
                }
            }
            else if (keep)
            {
                kept?.Add(prop);
            }
        }

        return kept ?? drawn;
    }

    private static bool Keep(
        SceneProp prop, IReadOnlyList<SceneProp> drawn, IReadOnlyList<SceneProp> moment, IReadOnlyList<ScenePlayer> players)
    {
        // `GetWeaponAssociatedWith`, which the timeline dereferenced against every entity: present means the weapon exists,
        // whether or not it is in the moment (a holstered one carries `EF_NODRAW` and is not).
        if (prop.WeaponAssociatedWith is not { } weapon)
        {
            return true;
        }

        bool viewModel = ViewModelWearables.Is(prop);

        // `CEconWearable::ShouldDraw` refuses a wearable with no player owner (`econ_wearable.cpp:369-373`), which a tied
        // one reaches whichever branch it takes.
        if ((prop.OwnedBy is { } ownedBy ? Find(players, ownedBy) : null) is not { } owner)
        {
            return false;
        }

        // Ghost mode returns before this branch for every wearable, a disguise for a third-person one only
        // (`!IsViewModelWearable()`, `:344`): a disguised spy's viewmodel wearable still answers to its weapon.
        if (DisguiseVisibility.IsGhost(owner) || (!viewModel && owner.Conditions.Has(PlayerConditions.Disguised)))
        {
            return true;
        }

        // `GetActiveWeapon() != pWeapon`. Both are dereferenced at this tick, slot and serial, and a slot has one live
        // occupant, so the slots compare the entities. A dead owner fails `ShouldDrawWhenPlayerIsDead` beneath.
        if (owner.ActiveWeapon != weapon.Entity || !owner.IsAlive)
        {
            return false;
        }

        SceneProp? listed = Find(drawn, weapon.Entity, weapon.Serial);

        // A third-person wearable needs its weapon drawn — in this list; a weapon in a taunt hides either kind.
        return viewModel
            ? (listed ?? Find(moment, weapon.Entity, weapon.Serial)) is not { RepurposedForTaunt: true }
            : listed is { RepurposedForTaunt: false };
    }

    private static SceneProp? Find(IReadOnlyList<SceneProp> props, int entity, int serial)
    {
        foreach (SceneProp prop in props)
        {
            if (prop.EntityIndex == entity && prop.SerialNumber == serial)
            {
                return prop;
            }
        }

        return null;
    }

    private static ScenePlayer? Find(IReadOnlyList<ScenePlayer> players, int entity)
    {
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == entity)
            {
                return player;
            }
        }

        return null;
    }
}
