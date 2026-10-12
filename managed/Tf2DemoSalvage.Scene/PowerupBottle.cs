using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A canteen's skin — `CTFPowerupBottle::GetSkin` (`tf_item_powerup_bottle.cpp:677-685`, B523).
/// </summary>
/// <remarks>
/// <code>
/// if ( !IsBasePowerUpBottle() )                        // set_weapon_mode != 1, tf_item_powerup_bottle.h:68
///     return ( ( GetNumCharges() > 0 ) ? 1 : 0 );
/// return BaseClass::GetSkin();
/// </code>
/// **A cosmetic canteen's skins are empty and filled, not RED and BLU.** The entity still networks the team in
/// `m_nSkin` like any wearable, and drawing that put the filled skin on every BLU canteen and the empty one on every
/// RED — outside Mann vs. Machine no canteen holds a charge, so the engine draws them all empty.
///
/// *Not built:* `GetWorldModelIndex`'s swap of the BASE canteen's model for the flask of its powerup while it holds a
/// charge (`:647-674`), which is Mann vs. Machine alone.
/// </remarks>
public static class PowerupBottle
{
    /// <summary>The server class of every canteen.</summary>
    public const string ClassName = "CTFPowerupBottle";

    /// <summary>`CTFPowerupBottle::GetSkin`.</summary>
    /// <param name="wire">The entity's own skin — `BaseClass::GetSkin()`.</param>
    /// <param name="weaponMode">`set_weapon_mode` hooked on the bottle; 1 is the base Power Up Canteen.</param>
    /// <param name="charges">`m_usNumCharges`.</param>
    /// <returns>The skin drawn.</returns>
    public static int Skin(int wire, int weaponMode, int charges)
    {
        if (weaponMode == 1)
        {
            return wire;
        }

        return charges > 0 ? 1 : 0;
    }

    /// <summary>Gives every canteen in a draw list the skin its wearer's item decides.</summary>
    /// <param name="props">The draw list; a canteen's prop is replaced in place.</param>
    /// <param name="players">This moment's players, whose items carry the charges.</param>
    /// <param name="hook">`CALL_ATTRIB_HOOK` on one of a player's items — <see cref="AttributeHooks.OnWeapon"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>A canteen whose wearer or item this moment does not hold keeps the skin the demo stated.</remarks>
    public static void Apply(
        IList<SceneProp> props,
        IReadOnlyList<ScenePlayer> players,
        Func<ScenePlayer, SceneItem, string, float, float> hook)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(hook);

        for (int index = 0; index < props.Count; index++)
        {
            SceneProp prop = props[index];

            if (!string.Equals(prop.ClassName, ClassName, StringComparison.Ordinal) ||
                Held(prop, players) is not var (wearer, item))
            {
                continue;
            }

            int mode = AttributeHooks.RoundFloatToInt(hook(wearer, item, "set_weapon_mode", 0f));
            int skin = Skin(prop.Pose.Skin, mode, item.NumCharges);

            if (skin != prop.Pose.Skin)
            {
                props[index] = prop with { Pose = prop.Pose with { Skin = skin } };
            }
        }
    }

    /// <summary>The player wearing a canteen and his record of it, or null when this moment holds neither.</summary>
    private static (ScenePlayer Wearer, SceneItem Item)? Held(SceneProp prop, IReadOnlyList<ScenePlayer> players)
    {
        int? wearer = prop.OwnedBy ?? prop.AttachedTo;

        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex != wearer)
            {
                continue;
            }

            foreach (SceneItem item in player.Items ?? [])
            {
                if (item.EntityIndex == prop.EntityIndex)
                {
                    return (player, item);
                }
            }
        }

        return null;
    }
}
