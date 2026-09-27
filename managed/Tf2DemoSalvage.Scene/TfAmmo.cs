using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What `CTFHudWeaponAmmo` reads of the local player's active weapon.</summary>
/// <param name="HasWeapon">Whether there is an active weapon.</param>
/// <param name="Shown">`CTFHudWeaponAmmo::ShouldDraw`'s own refusals passed: a weapon, not the medigun, using primary ammo, not metal, no ubercharge cost.</param>
/// <param name="UsesPrimaryAmmo">`UsesPrimaryAmmo()`.</param>
/// <param name="UsesClips">`UsesClipsForAmmo1()`: `GetMaxClip1() != WEAPON_NOCLIP`.</param>
/// <param name="Clip1">`Clip1()`.</param>
/// <param name="Reserve">`GetAmmoCount( GetPrimaryAmmoType() )`.</param>
/// <param name="MaxAmmo">`GetMaxAmmo( GetPrimaryAmmoType() )`.</param>
/// <param name="MaxClip1">`GetMaxClip1()`.</param>
public readonly record struct TfAmmoState(
    bool HasWeapon, bool Shown, bool UsesPrimaryAmmo, bool UsesClips, int Clip1, int Reserve, int MaxAmmo, int MaxClip1);

/// <summary>The weapon rules the ammo HUD calls, ported.</summary>
/// <remarks>
/// <list type="bullet">
/// <item>`CBaseCombatWeapon::Precache` (basecombatweapon_shared.cpp:254): the ammo type is the script's `primary_ammo`,
/// or `TF_AMMO_METAL` when `mod_use_metal_ammo_type` says so — only for a script that names one. The networked
/// `m_iPrimaryAmmoType` wins when it arrived.</item>
/// <item>`CTFWeaponBase::GetMaxClip1` (tf_weaponbase.cpp:563): the script's `clip_size`, or `mod_max_primary_clip_override`;
/// then `mult_clipsize`; then, for a blast weapon, `mult_clipsize_upgrade_atomic` and `clipsize_increase_on_kill` projectiles
/// added, otherwise `mult_clipsize_upgrade`.</item>
/// <item>`CTFPlayer::GetMaxAmmo` (tf_player_shared.cpp:12979): the class's `AmmoMax`, through the ammo's `mult_maxammo_*`;
/// `TF_AMMO_GRENADES3` is 1; then the haste rune doubles it.</item>
/// <item>`UsesPrimaryAmmo` is false for an energy weapon; `UberChargeAmmoPerShot` is `ubercharge_ammo` × 0.01.</item>
/// </list>
/// **Not modelled:** the Mannpower runes' clip changes (haste doubles clips; precision and vampire multiply blast clips) and
/// the decapitations `clipsize_increase_on_kill` counts, which are not read yet — each changes only the low-ammo warning's
/// threshold, never a number drawn.
/// </remarks>
public static class TfAmmo
{
    private const int AmmoGrenades3 = 6;

    // `IsEnergyWeapon` overrides (tf_weapon_*.h): the Manmelter, the Cow Mangler, the Righteous Bison, the Pomson, the
    // Bison's revenge variant, and the PASS Time gun.
    private static readonly HashSet<string> EnergyWeapons = new(StringComparer.Ordinal)
    {
        "CTFFlareGun_Revenge", "CTFParticleCannon", "CTFRaygun", "CTFDRGPomson", "CTFRaygun_Revenge", "CPasstimeGun",
    };

    // `IsBlastImpactWeapon`: the grenade launcher and its cannon, the rocket launcher and every class derived from it
    // (the rocket launcher's override is `!IsEnergyWeapon()`, which excludes the three energy weapons derived from it).
    private static readonly HashSet<string> BlastWeapons = new(StringComparer.Ordinal)
    {
        "CTFGrenadeLauncher", "CTFCannon", "CTFRocketLauncher", "CTFRocketLauncher_DirectHit", "CTFRocketLauncher_AirStrike",
        "CTFRocketLauncher_Mortar", "CTFCrossbow",
    };

    /// <summary>The ammo HUD's inputs for a player.</summary>
    /// <param name="player">The local player.</param>
    /// <param name="scripts">The weapon and class scripts.</param>
    /// <param name="hooks">The attribute hooks.</param>
    /// <returns>The state.</returns>
    public static TfAmmoState For(ScenePlayer player, TfWeaponData scripts, AttributeHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(hooks);

        SceneItem? weapon = null;

        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.EntityIndex == player.ActiveWeapon)
            {
                weapon = item;
            }
        }

        if (weapon is not { ClassName: { } className })
        {
            return default;
        }

        int scriptClip = scripts.Weapon(className, player.PlayerClass).MaxClip1;
        int ammoType = player.WeaponPrimaryAmmoType ?? PrimaryAmmoType(player, weapon, scripts, hooks.OnWeapon);

        bool energy = EnergyWeapons.Contains(className);
        bool usesPrimary = !energy && ammoType >= 0;
        int maxClip = MaxClip1(player, weapon, className, scriptClip, hooks);
        bool uberCost = hooks.OnWeapon(player, weapon, "ubercharge_ammo", 0f) * 0.01f > 0f;

        return new TfAmmoState(
            HasWeapon: true,
            Shown: className != "CWeaponMedigun" && usesPrimary && ammoType != TfWeaponData.AmmoMetal && !uberCost,
            UsesPrimaryAmmo: usesPrimary,
            UsesClips: maxClip != -1,
            Clip1: player.WeaponClip1 ?? -1,
            Reserve: ammoType >= 0 && player.Ammo is { } ammo && ammoType < ammo.Count ? ammo[ammoType] : 0,
            MaxAmmo: ammoType >= 0 && player.PlayerClass is { } playerClass ? MaxAmmo(player, playerClass, ammoType, scripts, hooks.OnPlayer) : 0,
            MaxClip1: maxClip);
    }

    /// <summary>`CTFWeaponBase::GetMaxClip1`.</summary>
    private static int MaxClip1(ScenePlayer player, SceneItem weapon, string className, int scriptClip, AttributeHooks hooks)
    {
        int overridden = Int(hooks.OnWeapon(player, weapon, "mod_max_primary_clip_override", 0f));
        float clip = overridden != 0 ? overridden : scriptClip;

        // `CALL_ATTRIB_HOOK_INT( flClip, … )`: the float passes through an int on the way in and on the way out.
        if (clip >= 0)
        {
            clip = Int(hooks.OnWeapon(player, weapon, "mult_clipsize", (int)clip));
        }

        if (clip < 0)
        {
            return (int)clip;
        }

        if (BlastWeapons.Contains(className))
        {
            int projectiles = Int(hooks.OnWeapon(player, weapon, "mult_clipsize_upgrade_atomic", 0f));

            return (int)(clip + projectiles);
        }

        return Int(hooks.OnWeapon(player, weapon, "mult_clipsize_upgrade", (int)clip));
    }

    /// <summary>
    /// `m_iPrimaryAmmoType` as `CBaseCombatWeapon::Precache` sets it (basecombatweapon_shared.cpp:254): the networked value
    /// when it arrived, otherwise the script's `primary_ammo`, or `TF_AMMO_METAL` for `mod_use_metal_ammo_type`.
    /// </summary>
    /// <param name="player">The weapon's owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="scripts">The weapon and class scripts.</param>
    /// <param name="onWeapon">`CALL_ATTRIB_HOOK_FLOAT` on the weapon.</param>
    /// <returns>The ammo type, -1 for none.</returns>
    public static int PrimaryAmmoType(
        ScenePlayer player, SceneItem weapon, TfWeaponData scripts, Func<ScenePlayer, SceneItem, string, float, float> onWeapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(onWeapon);

        if (weapon.PrimaryAmmoType is { } networked)
        {
            return networked;
        }

        (_, int scriptAmmo, bool namesAmmo) = scripts.Weapon(weapon.ClassName ?? string.Empty, player.PlayerClass);

        return namesAmmo && Int(onWeapon(player, weapon, "mod_use_metal_ammo_type", 0f)) != 0 ? TfWeaponData.AmmoMetal : scriptAmmo;
    }

    /// <summary>`CTFPlayer::GetMaxAmmo` (tf_player_shared.cpp:12979).</summary>
    /// <param name="player">The player.</param>
    /// <param name="playerClass">`m_PlayerClass`'s class.</param>
    /// <param name="ammoType">The ammo index.</param>
    /// <param name="scripts">The class scripts' `AmmoMax`.</param>
    /// <param name="onPlayer">`CALL_ATTRIB_HOOK_INT` on the player.</param>
    /// <returns>The maximum.</returns>
    public static int MaxAmmo(
        ScenePlayer player, int playerClass, int ammoType, TfWeaponData scripts, Func<ScenePlayer, string, float, float> onPlayer)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(onPlayer);

        int max = scripts.AmmoMax(playerClass, ammoType);
        string? hook = ammoType switch
        {
            1 => "mult_maxammo_primary",
            2 => "mult_maxammo_secondary",
            TfWeaponData.AmmoMetal => "mult_maxammo_metal",
            4 => "mult_maxammo_grenades1",
            _ => null,
        };

        if (hook is not null)
        {
            max = Int(onPlayer(player, hook, max));
        }
        else if (ammoType == AmmoGrenades3)
        {
            // "All classes by default can carry a max of 1 'Grenade3' which is being used as ACTIONSLOT Throwables".
            max = 1;
        }

        // "Haste Powerup Rune adds multiplier to Max Ammo": `iMax *= 2.0f`, a float product truncated back.
        return CarryingRuneType(player.Conditions) == RuneHaste ? (int)(max * 2.0f) : max;
    }

    // `RUNE_HASTE` (tf_shareddefs.h:2642).
    private const int RuneHaste = 1;

    /// <summary>
    /// `CTFPlayerShared::GetCarryingRuneType` (tf_player_shared.cpp:11498): the first rune, in `RuneTypes_t` order, whose
    /// condition is set — `GetConditionFromRuneType` maps them to `TF_COND_RUNE_STRENGTH` (90) through `TF_COND_RUNE_REGEN`
    /// (92) and on; -1 for none.
    /// </summary>
    /// <param name="conditions">The player's conditions.</param>
    /// <returns>The rune type.</returns>
    public static int CarryingRuneType(PlayerConditions conditions)
    {
        for (int rune = 0; rune < RuneConditions.Length; rune++)
        {
            if (conditions.Has(RuneConditions[rune]))
            {
                return rune;
            }
        }

        return -1;
    }

    // `GetConditionFromRuneType` (tf_shareddefs.h:2659), `RUNE_STRENGTH` through `RUNE_SUPERNOVA`, with the conditions'
    // numbers from `ETFCond` (tf_shareddefs.h:780-801).
    private static readonly int[] RuneConditions = [90, 91, 92, 93, 94, 95, 96, 97, 103, 109, 110, 111];

    private static int Int(float value) => AttributeHooks.RoundFloatToInt(value);
}
