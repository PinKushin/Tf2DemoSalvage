using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudItemEffectMeter_Weapon&lt; T &gt;` (tf_hud_itemeffectmeter.cpp:619-720): a meter reading one weapon's regen.</summary>
/// <remarks>
/// <para>
/// `GetWeapon` is `Weapon_OwnsThisID( m_iWeaponID )` — the first of the player's weapons whose `GetWeaponID` matches —
/// then `dynamic_cast&lt; T* &gt;`, so a weapon sharing the ID but not of the meter's class leaves the meter without one. A
/// meter that finds none disables itself for good.
/// </para>
/// <para>
/// What the weapon's own virtuals answer — `GetProgress`, `GetEffectLabelText`, `GetCount` — is dispatched here by the
/// weapon's server class, as the C++ dispatches by its dynamic type; the meter's own specialisations are the subclasses
/// below. **Interpolated:** a handle holds while the player still carries the entity.
/// </para>
/// </remarks>
public class TfItemEffectMeterWeapon : TfHudItemEffectMeter
{
    // `TF_AMMO_GRENADES1` and `TF_AMMO_GRENADES2` (tf_shareddefs.h).
    private const int AmmoGrenades1 = 4;
    private const int AmmoGrenades2 = 5;

    // `TF_COND_ENERGY_BUFF` (tf_shareddefs.h).
    private const int ConditionEnergyBuff = 19;

    // `DAMAGE_TO_FILL_MINICRIT_METER` (tf_weapon_smg.cpp:8) and `ENERGY_WEAPON_MAX_CHARGE` (tf_weaponbase.h:254).
    private const float DamageToFillMinicritMeter = 100.0f;
    private const int EnergyWeaponMaxCharge = 20;

    /// <summary>
    /// Each weapon class's `GetWeaponID()` (tf_weapon_*.h), for the classes a meter asks after — and every class that
    /// shares one of their IDs, since `Weapon_OwnsThisID` takes the first match.
    /// </summary>
    private static readonly Dictionary<string, string> WeaponIds = new(StringComparer.Ordinal)
    {
        ["CTFBat_Wood"] = "TF_WEAPON_BAT_WOOD",
        ["CTFBat_Giftwrap"] = "TF_WEAPON_BAT_GIFTWRAP",
        ["CTFLunchBox"] = "TF_WEAPON_LUNCHBOX",
        ["CTFLunchBox_Drink"] = "TF_WEAPON_LUNCHBOX",
        ["CTFJarMilk"] = "TF_WEAPON_JAR_MILK",
        ["CTFSodaPopper"] = "TF_WEAPON_SODA_POPPER",
        ["CTFPEPBrawlerBlaster"] = "TF_WEAPON_PEP_BRAWLER_BLASTER",
        ["CTFCleaver"] = "TF_WEAPON_CLEAVER",
        ["CTFMinigun"] = "TF_WEAPON_MINIGUN",
        ["CTFJar"] = "TF_WEAPON_JAR",
        ["CTFSniperRifleDecap"] = "TF_WEAPON_SNIPERRIFLE_DECAP",
        ["CTFSniperRifle"] = "TF_WEAPON_SNIPERRIFLE",
        ["CTFChargedSMG"] = "TF_WEAPON_CHARGED_SMG",
        ["CTFSword"] = "TF_WEAPON_SWORD",
        ["CTFKatana"] = "TF_WEAPON_SWORD",
        ["CTFBuffItem"] = "TF_WEAPON_BUFF_ITEM",
        ["CTFParticleCannon"] = "TF_WEAPON_PARTICLE_CANNON",
        ["CTFRaygun"] = "TF_WEAPON_RAYGUN",
        ["CTFRocketLauncher"] = "TF_WEAPON_ROCKETLAUNCHER",
        ["CTFRocketLauncher_AirStrike"] = "TF_WEAPON_ROCKETLAUNCHER",
        ["CTFRocketLauncher_Mortar"] = "TF_WEAPON_ROCKETLAUNCHER",
        ["CTFKnife"] = "TF_WEAPON_KNIFE",
        ["CTFWeaponBuilder"] = "TF_WEAPON_BUILDER",
        ["CTFWeaponSapper"] = "TF_WEAPON_BUILDER",
        ["CTFRevolver"] = "TF_WEAPON_REVOLVER",
        ["CTFShotgun_Revenge"] = "TF_WEAPON_SENTRY_REVENGE",
        ["CTFDRGPomson"] = "TF_WEAPON_DRG_POMSON",
        ["CTFFlameThrower"] = "TF_WEAPON_FLAMETHROWER",
        ["CTFFlareGun_Revenge"] = "TF_WEAPON_FLAREGUN_REVENGE",
        ["CTFRocketPack"] = "TF_WEAPON_ROCKETPACK",
        ["CWeaponMedigun"] = "TF_WEAPON_MEDIGUN",
        ["CTFBonesaw"] = "TF_WEAPON_BONESAW",
        ["CTFThrowablePrimary"] = "TF_WEAPON_THROWABLE",
        ["CTFThrowableSecondary"] = "TF_WEAPON_THROWABLE",
        ["CTFThrowableMelee"] = "TF_WEAPON_THROWABLE",
        ["CTFThrowableUtility"] = "TF_WEAPON_THROWABLE",
        ["CTFSpellBook"] = "TF_WEAPON_SPELLBOOK",
    };

    // `GetPowerupType`'s attributes, in the order it asks them (tf_item_powerup_bottle.cpp:116-148).
    private static readonly string[] PowerupAttributes = ["critboost", "ubercharge", "recall", "refill_ammo", "building_instant_upgrade"];

    private readonly string _weaponId;
    private readonly HashSet<string> _castable;
    private readonly bool _beeps;
    private readonly string? _resFile;
    private int? _weapon;

    /// <summary>`CHudItemEffectMeter_Weapon( pszElementName, pPlayer, iWeaponID, bBeeps, pszResFile )` (:621).</summary>
    /// <param name="manager">The manager.</param>
    /// <param name="playerIndex">`m_pPlayer`.</param>
    /// <param name="weaponId">`m_iWeaponID`, by name.</param>
    /// <param name="castable">The server classes that are a `T`.</param>
    /// <param name="beeps">`m_bBeeps`.</param>
    /// <param name="resFile">`m_pszResFile`, or null for the default.</param>
    public TfItemEffectMeterWeapon(
        TfItemEffectMeterManager manager, int playerIndex, string weaponId, IEnumerable<string> castable, bool beeps, string? resFile)
        : base(manager, playerIndex)
    {
        _weaponId = weaponId;
        _castable = new HashSet<string>(castable, StringComparer.Ordinal);
        _beeps = beeps;
        _resFile = resFile;
    }

    /// <summary>`GetWeapon` (:651): found once, the meter disabled for good when there is none.</summary>
    /// <returns>The weapon, or null.</returns>
    public virtual SceneItem? Weapon()
    {
        if (MeterEnabled && Player is { } player && Held(player) is null)
        {
            _weapon = OwnsThisId(player, _weaponId, _castable)?.EntityIndex;

            if (_weapon is null)
            {
                MeterEnabled = false;
            }
        }

        return Player is { } owner ? Held(owner) : null;
    }

    /// <summary>`m_hWeapon`, found by a specialisation's own search.</summary>
    /// <param name="weapon">The weapon, or null.</param>
    protected void SetWeapon(SceneItem? weapon) => _weapon = weapon?.EntityIndex;

    /// <summary>`m_hWeapon.Get()`: the cached weapon while the player carries it.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The weapon, or null.</returns>
    protected SceneItem? Held(ScenePlayer player)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.EntityIndex == _weapon)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`dynamic_cast&lt; T* &gt;( Weapon_OwnsThisID( id ) )`.</summary>
    private static SceneItem? OwnsThisId(ScenePlayer player, string weaponId, HashSet<string> castable)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.IsWeapon && item.ClassName is { } className && WeaponIds.GetValueOrDefault(className) == weaponId)
            {
                return castable.Contains(className) ? item : null;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>`IsEnabled` (:675): a weapon, else `m_bEnabled`.</remarks>
    public override bool IsEnabled() => Weapon() is not null || base.IsEnabled();

    /// <inheritdoc/>
    /// <remarks>`m_hWeapon ? m_hWeapon-&gt;GetEffectLabelText() : ""` — the cached handle, with no search.</remarks>
    public override string LabelText() => Player is { } player && Held(player) is { } weapon ? EffectLabelText(player, weapon) : string.Empty;

    /// <inheritdoc/>
    /// <remarks>`GetProgress` (:689): the weapon's, else 0.</remarks>
    public override float Progress() => Weapon() is { } weapon && Player is { } player ? WeaponProgress(player, weapon) : 0f;

    /// <inheritdoc/>
    public override bool ShouldBeep() => _beeps;

    /// <inheritdoc/>
    public override string ResFile() => _resFile ?? base.ResFile();

    /// <inheritdoc/>
    public override int Count() => -1;

    /// <inheritdoc/>
    public override bool ShouldFlash() => false;

    /// <summary>The weapon's `GetEffectLabelText()` by its class.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The label token.</returns>
    protected string EffectLabelText(ScenePlayer player, SceneItem weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return weapon.ClassName switch
        {
            "CTFBat_Wood" or "CTFBat_Giftwrap" => "#TF_BALL",
            "CTFLunchBox_Drink" => "#TF_ENERGYDRINK",
            "CTFJar" or "CTFJarMilk" => "#TF_JAR",
            "CTFCleaver" => "#TF_CLEAVER",
            "CTFJarGas" => "#TF_Gas",
            "CTFThrowablePrimary" or "CTFThrowableSecondary" or "CTFThrowableMelee" or "CTFThrowableUtility" => "#TF_Throwable",
            "CTFSodaPopper" => "#TF_HYPE",
            "CTFPEPBrawlerBlaster" => "#TF_Boost",
            "CTFMinigun" => "#TF_Rage",
            "CTFBuffItem" => "#TF_RAGE",
            "CTFFlameThrower" => "#TF_PYRORAGE",
            "CWeaponMedigun" => "#TF_Rescue",
            "CTFSniperRifle" or "CTFSniperRifleClassic" => "#TF_SNIPERRAGE",
            "CTFSniperRifleDecap" or "CTFSword" => "#TF_BERZERK",
            "CTFChargedSMG" => "#TF_SmgCharge",
            "CTFParticleCannon" => "#TF_MANGLER",
            "CTFRaygun" => "#TF_BISON",
            "CTFDRGPomson" => "#TF_POMSON_HUD",
            "CTFRocketLauncher_AirStrike" => "#TF_KILLS",
            "CTFCrossbow" => "#TF_BOLT",
            "CTFKnife" => "#TF_KNIFE",
            "CTFWeaponBuilder" or "CTFWeaponSapper" => "#TF_Sapper",
            "CTFRevolver" => ItemHookInt(player, weapon, "extra_damage_on_hit") != 0 ? "#TF_BONUS" : "#TF_CRITS",
            "CTFShotgun_Revenge" => "#TF_REVENGE",
            "CTFFlareGun_Revenge" => "#TF_CRITS",
            "CTFBonesaw" => "#TF_ORGANS",
            "CTFSpellBook" => "#TF_KART",
            "CTFPowerupBottle" => PowerupBottleLabel(player, weapon),
            _ => string.Empty,
        };
    }

    /// <summary>The weapon's `GetProgress()` by its class.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The fraction.</returns>
    protected float WeaponProgress(ScenePlayer player, SceneItem weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return weapon.ClassName switch
        {
            // tf_weapon_bat.h:91-92, :112.
            "CTFBat_Wood" or "CTFBat_Giftwrap" => EffectBarProgress(player, weapon, 10f, AmmoGrenades1),

            // `TF_SANDWICH_REGENTIME` (tf_weapon_lunchbox.h:32, :112-113).
            "CTFLunchBox_Drink" => EffectBarProgress(player, weapon, 30f, PrimaryAmmoType(player, weapon)),

            // tf_weapon_jar.h:54, :59, :120; tf_weapon_jar_gas.h:62.
            "CTFJar" or "CTFJarMilk" => EffectBarProgress(player, weapon, 20.1f, PrimaryAmmoType(player, weapon)),
            "CTFCleaver" => EffectBarProgress(player, weapon, 5.1f, PrimaryAmmoType(player, weapon)),
            "CTFJarGas" => EffectBarProgress(player, weapon, 0f, PrimaryAmmoType(player, weapon)),

            // `CTFThrowable::InternalGetEffectBarRechargeTime` (tf_weapon_throwable.cpp:107): the attribute, or 10.
            "CTFThrowablePrimary" or "CTFThrowableSecondary" or "CTFThrowableMelee" or "CTFThrowableUtility" =>
                EffectBarProgress(player, weapon, ThrowableRechargeTime(player, weapon), PrimaryAmmoType(player, weapon)),

            // c_tf_weapon_builder.h:77-79.
            "CTFWeaponBuilder" or "CTFWeaponSapper" => EffectBarProgress(player, weapon, 15.0f, AmmoGrenades2),

            // tf_weapon_shotgun.cpp:459, :497.
            "CTFSodaPopper" or "CTFPEPBrawlerBlaster" => (player.HypeMeter ?? 0f) * 0.01f,

            // tf_weapon_minigun.cpp:737, tf_weapon_buff_item.cpp:444, tf_weapon_flamethrower.cpp:2070,
            // tf_weapon_medigun.cpp:2652, tf_weapon_sniperrifle.cpp:1278.
            "CTFMinigun" or "CTFBuffItem" or "CTFFlameThrower" or "CWeaponMedigun" or "CTFSniperRifle" or "CTFSniperRifleClassic" =>
                (player.RageMeter ?? 0f) / 100.0f,

            "CTFChargedSMG" => ChargedSmgProgress(player, weapon),

            // tf_weapon_particle_cannon.cpp:320, tf_weapon_raygun.cpp:123.
            "CTFParticleCannon" or "CTFRaygun" or "CTFDRGPomson" => weapon.Energy / MaxEnergy(player, weapon),

            // tf_weapon_knife.h:103.
            "CTFKnife" => weapon.KnifeExists ? 1.0f : (State.ServerTime - weapon.KnifeMeltTimestamp) / weapon.KnifeRegenerateDuration,
            _ => 0f,
        };
    }

    /// <summary>`CTFFlameThrower::GetBuffType` and its kind (tf_weapon_*.h): `set_buff_type` from 0.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The buff type.</returns>
    protected int BuffType(ScenePlayer player, SceneItem weapon) => ItemHookInt(player, weapon, "set_buff_type");

    /// <summary>`IsRageFull() || IsRageDraining()`: every rage weapon's `EffectMeterShouldFlash`.</summary>
    /// <param name="player">The owner.</param>
    /// <returns>Whether the bar flashes.</returns>
    protected static bool RageShouldFlash(ScenePlayer player) => (player.RageMeter ?? 0f) >= 100.0f || player.RageDraining;

    /// <summary>`CTFWeaponBase::GetEffectBarProgress` (tf_weaponbase.cpp:6509): full unless the bar's ammo is short.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="rechargeTime">`InternalGetEffectBarRechargeTime()`.</param>
    /// <param name="ammoType">`GetEffectBarAmmo()`.</param>
    /// <returns>The fraction.</returns>
    protected float EffectBarProgress(ScenePlayer player, SceneItem weapon, float rechargeTime, int ammoType)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        int count = ammoType >= 0 && player.Ammo is { } ammo && ammoType < ammo.Count ? ammo[ammoType] : 0;
        int max = ammoType >= 0 && Manager.Viewport.Scripts is { } scripts && player.PlayerClass is { } playerClass
            ? TfAmmo.MaxAmmo(player, playerClass, ammoType, scripts, PlayerHook)
            : 0;

        if (count < max)
        {
            // `GetEffectBarRechargeTime`: the base time through `effectbar_recharge_rate` (tf_weaponbase.h:629).
            float time = ItemHook(player, weapon, "effectbar_recharge_rate", rechargeTime);

            return (time - (weapon.EffectBarRegenTime - State.ServerTime)) / time;
        }

        return 1f;
    }

    /// <summary>`m_iPrimaryAmmoType`: the networked value, else the script's; -1 with no scripts.</summary>
    private int PrimaryAmmoType(ScenePlayer player, SceneItem weapon) =>
        Manager.Viewport.Scripts is { } scripts ? TfAmmo.PrimaryAmmoType(player, weapon, scripts, ItemHook) : weapon.PrimaryAmmoType ?? -1;

    /// <summary>`CTFThrowable::InternalGetEffectBarRechargeTime` (tf_weapon_throwable.cpp:107).</summary>
    private float ThrowableRechargeTime(ScenePlayer player, SceneItem weapon)
    {
        float recharge = ItemHook(player, weapon, "throwable_recharge_time", 0f);

        return recharge != 0f ? recharge : 10.0f;
    }

    /// <summary>`CTFChargedSMG::GetProgress` (tf_weapon_smg.cpp:117).</summary>
    /// <remarks>
    /// `m_flMinicritStartTime` is not networked: only `SecondaryAttack` sets it (:169), which a demo's client never runs,
    /// so it stays at the 0 `WeaponReset` gives it (:149) and a boosted bar reads what Valve's own playback reads.
    /// </remarks>
    private float ChargedSmgProgress(ScenePlayer player, SceneItem weapon)
    {
        if (!player.Conditions.Has(ConditionEnergyBuff))
        {
            return weapon.MinicritCharge / DamageToFillMinicritMeter;
        }

        // `int flBuffDuration = 0; CALL_ATTRIB_HOOK_FLOAT( flBuffDuration, … )`: the float truncated into an int.
        int buffDuration = Truncate(ItemHook(player, weapon, "minicrit_boost_when_charged", 0f));

        if (buffDuration <= 0)
        {
            return 0f;
        }

        const float MinicritStartTime = 0f;
        float elapsed = State.ServerTime - MinicritStartTime;

        return Math.Clamp((buffDuration - elapsed) / buffDuration, 0f, 1f);
    }

    /// <summary>`Energy_GetMaxEnergy` (tf_weaponbase.cpp:6357): the shots a full charge holds, upgraded, times their cost.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The maximum energy.</returns>
    protected float MaxEnergy(ScenePlayer player, SceneItem weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        float cost = EnergyShotCost(player, weapon);
        int shots = Truncate(EnergyWeaponMaxCharge / cost);

        shots = Truncate(ItemHook(player, weapon, "mult_clipsize_upgrade", shots));

        return shots * cost;
    }

    /// <summary>`Energy_GetShotCost`: 5 for the Mangler (tf_weapon_particle_cannon.h:88), the Bison's 0 under `energy_weapon_no_drain` (tf_weapon_raygun.h:52).</summary>
    private float EnergyShotCost(ScenePlayer player, SceneItem weapon) => weapon.ClassName switch
    {
        "CTFRaygun" or "CTFDRGPomson" => ItemHookInt(player, weapon, "energy_weapon_no_drain") > 0 ? 0.0f : 5.0f,
        "CTFParticleCannon" => 5.0f,
        _ => 4.0f,
    };

    /// <summary>`CTFPowerupBottle::GetEffectLabelText` (tf_item_powerup_bottle.cpp:568).</summary>
    private string PowerupBottleLabel(ScenePlayer player, SceneItem bottle)
    {
        if (HudViewport.ConVarsOf(this).GetBool("cl_hud_minmode"))
        {
            return "#TF_PVE_UsePowerup_MinMode";
        }

        return PowerupType(player, bottle) switch
        {
            "ubercharge" => "#TF_PVE_UsePowerup_Ubercharge",
            "recall" => "#TF_PVE_UsePowerup_Recall",
            "refill_ammo" => "#TF_PVE_UsePowerup_RefillAmmo",
            "building_instant_upgrade" => "#TF_PVE_UsePowerup_BuildinginstaUpgrade",
            _ => "#TF_PVE_UsePowerup_CritBoost",
        };
    }

    /// <summary>`CTFPowerupBottle::GetPowerupType` (tf_item_powerup_bottle.cpp:114): the first of its attributes set, or none.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="bottle">The bottle.</param>
    /// <returns>The attribute class that decided it, or null for `POWERUP_BOTTLE_NONE`.</returns>
    protected string? PowerupType(ScenePlayer player, SceneItem bottle)
    {
        foreach (string powerup in PowerupAttributes)
        {
            if (ItemHookInt(player, bottle, powerup) != 0)
            {
                return powerup;
            }
        }

        return null;
    }

    /// <summary>A float stored into an `int`: `cvttss2si`, which gives `0x80000000` for anything out of range or NaN.</summary>
    /// <param name="value">The float.</param>
    /// <returns>The int.</returns>
    protected static int Truncate(float value) =>
        float.IsNaN(value) || value >= 2147483648f || value < -2147483648f ? int.MinValue : (int)value;
}

/// <summary>The kill streak meter: `CHudItemEffectMeter_Weapon&lt; CTFWeaponBase &gt;` for `TF_WEAPON_NONE` (:726-763).</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterKillStreak(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_NONE", [], false, "resource/UI/HudItemEffectMeter_KillStreak.res")
{
    /// <inheritdoc/>
    /// <remarks>`killstreak_tier` on the local player, non-zero.</remarks>
    public override bool IsEnabled() => LocalPlayer is { } local && PlayerHookInt(local, "killstreak_tier") != 0;

    /// <inheritdoc/>
    /// <remarks>`m_Shared.GetStreak( kTFStreak_Kills )` of the local player.</remarks>
    public override int Count() => LocalPlayer is { } local ? local.KillStreak ?? 0 : 0;

    /// <inheritdoc/>
    public override string LabelText() => "TF_KillStreak";

    /// <inheritdoc/>
    public override bool IsKillstreakMeter() => true;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFSword &gt;` (:768-803): a sword that can decapitate, and its heads.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterSword(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SWORD", ["CTFSword"], false, "resource/UI/HudItemEffectMeter_Demoman.res")
{
    /// <inheritdoc/>
    /// <remarks>`GetWeapon` (:768): the sword kept only while `CanDecapitate` (tf_weapon_sword.cpp:164).</remarks>
    public override SceneItem? Weapon()
    {
        if (MeterEnabled && Player is { } player && Held(player) is null)
        {
            SceneItem? sword = base.Weapon();

            if (sword is not null && !CanDecapitate(player, sword))
            {
                SetWeapon(null);
                MeterEnabled = false;
            }
        }

        return Player is { } owner ? Held(owner) : null;
    }

    /// <inheritdoc/>
    /// <remarks>`CTFSword::GetCount` (tf_weapon_sword.cpp:456): the decapitations, while it can decapitate.</remarks>
    public override int Count() =>
        Weapon() is { } sword && Player is { } player && CanDecapitate(player, sword) ? player.Decapitations ?? 0 : 0;

    /// <summary>`CanDecapitate`: a known item whose `decapitate_type` is not 0.</summary>
    private bool CanDecapitate(ScenePlayer player, SceneItem sword) =>
        sword.DefinitionIndex is not null && ItemHookInt(player, sword, "decapitate_type") != 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFBuffItem &gt;` (:808-838): the banners, not the parachute.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterBuffItem(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_BUFF_ITEM", ["CTFBuffItem"], true, null)
{
    // `EParachute` (tf_weapon_buff_item.h:32).
    private const int BuffParachute = 4;

    /// <inheritdoc/>
    public override bool ShouldFlash() => Weapon() is not null && Player is { } player && RageShouldFlash(player);

    /// <inheritdoc/>
    /// <remarks>"do not draw for the parachute".</remarks>
    public override bool ShouldDraw(HudState state)
    {
        if (Player is not { } player || Weapon() is not { } banner || BuffType(player, banner) == BuffParachute)
        {
            return false;
        }

        return base.ShouldDraw(state);
    }
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFFlameThrower &gt;` (:840-871): the Phlogistinator's rage.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterFlameThrower(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_FLAMETHROWER", ["CTFFlameThrower"], true, "resource/UI/HudItemEffectMeter_Pyro.res")
{
    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && Weapon() is { } weapon && BuffType(player, weapon) > 0;

    /// <inheritdoc/>
    public override bool ShouldFlash() => Weapon() is not null && Player is { } player && RageShouldFlash(player);
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFSodaPopper &gt;` (:876): flashing at full hype.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterSodaPopper(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SODA_POPPER", ["CTFSodaPopper"], true, "resource/UI/HudItemEffectMeter_SodaPopper.res")
{
    /// <inheritdoc/>
    public override bool ShouldFlash() => Player is { } player && (player.HypeMeter ?? 0f) >= 100.0f;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFChargedSMG &gt;` (:888): flashing when charged.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterChargedSmg(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_CHARGED_SMG", ["CTFChargedSMG"], false, null)
{
    /// <inheritdoc/>
    /// <remarks>`ShouldFlashChargeBar` (tf_weapon_smg.cpp:109): `m_flMinicritCharge &gt;= DAMAGE_TO_FILL_MINICRIT_METER`.</remarks>
    public override bool ShouldFlash() => Player is not null && Weapon() is { } smg && smg.MinicritCharge >= 100.0f;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFMinigun &gt;` (:904-1009): the MvM rage bar, and the kill combo's class icons.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterMinigun(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_MINIGUN", ["CTFMinigun"], true, "resource/UI/HudItemEffectMeter_Heavy.res")
{
    // `pszClassIcons` (:944).
    private static readonly string[] ClassIcons =
    [
        string.Empty,
        "../hud/leaderboard_class_scout",
        "../hud/leaderboard_class_sniper",
        "../hud/leaderboard_class_soldier",
        "../hud/leaderboard_class_demo",
        "../hud/leaderboard_class_medic",
        "../hud/leaderboard_class_heavy",
        "../hud/leaderboard_class_pyro",
        "../hud/leaderboard_class_spy",
        "../hud/leaderboard_class_engineer",
    ];

    /// <inheritdoc/>
    /// <remarks>`generate_rage_on_dmg` on the player, with a minigun.</remarks>
    public override bool IsEnabled() => Player is { } player && Weapon() is not null && PlayerHookInt(player, "generate_rage_on_dmg") != 0;

    /// <inheritdoc/>
    public override bool ShouldFlash() => Weapon() is not null && Player is { } player && RageShouldFlash(player);

    /// <inheritdoc/>
    /// <remarks>`Update` (:957): with `kill_combo_fire_rate_boost`, one icon per combo kill of the combo's class.</remarks>
    public override void Update(ScenePlayer? player)
    {
        if (Weapon() is { } minigun && Player is { } owner)
        {
            if (ItemHook(owner, minigun, "kill_combo_fire_rate_boost", 0f) > 0.0f)
            {
                SetControlVisible("ItemEffectMeterLabel2", true);

                for (int icon = 0; icon < 3; icon++)
                {
                    VguiPanel? panel = FindChildByName(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"KillComboClassIcon{icon + 1}"));

                    if (panel is null)
                    {
                        continue;
                    }

                    if (minigun.KillComboCount > icon)
                    {
                        string image = minigun.KillComboClass < ClassIcons.Length ? ClassIcons[minigun.KillComboClass] : string.Empty;

                        switch (panel)
                        {
                            case VguiImagePanel imagePanel:
                                imagePanel.SetImage(image);
                                break;
                            case VguiScalableImagePanel scalable:
                                scalable.SetImage(image);
                                break;
                            default:
                                break;
                        }

                        panel.Visible = true;
                    }
                    else
                    {
                        panel.Visible = false;
                    }
                }
            }
            else
            {
                SetControlVisible("ItemEffectMeterLabel2", false);
                SetControlVisible("KillComboClassIcon1", false);
                SetControlVisible("KillComboClassIcon2", false);
                SetControlVisible("KillComboClassIcon3", false);
            }
        }

        base.Update(player);
    }

    /// <summary>`EditablePanel::SetControlVisible`: the direct child of that name.</summary>
    private void SetControlVisible(string name, bool visible)
    {
        if (FindChildByName(name) is { } control)
        {
            control.Visible = visible;
        }
    }
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFShotgun_Revenge &gt;` (:1014): the Frontier Justice's revenge crits.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterShotgunRevenge(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SENTRY_REVENGE", ["CTFShotgun_Revenge"], false, "resource/UI/HUDItemEffectMeter_Engineer.res")
{
    /// <inheritdoc/>
    public override int Count() => Weapon() is not null && Player is { } player ? player.RevengeCrits ?? 0 : 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFFlareGun_Revenge &gt;` (:1027-1051): the Manmelter's crits, while it is out.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterFlareGunRevenge(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_FLAREGUN_REVENGE", ["CTFFlareGun_Revenge"], false, "resource/UI/HUDItemEffectMeter_Engineer.res")
{
    /// <inheritdoc/>
    /// <remarks>
    /// `IsActiveByLocalPlayer` (c_basecombatweapon.cpp:336): carried by the local player and `m_iState == WEAPON_IS_ACTIVE`.
    /// **Interpolated:** the weapon is the owner's `m_hActiveWeapon`, which `Deploy` and `Holster` keep in step with the state.
    /// </remarks>
    public override bool IsEnabled() =>
        Player is { } player && Weapon() is { } flareGun && player.EntityIndex == State.LocalIndex && State.HasLocalPlayer
        && player.ActiveWeapon == flareGun.EntityIndex;

    /// <inheritdoc/>
    public override int Count() => Weapon() is not null && Player is { } player ? player.RevengeCrits ?? 0 : 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFRocketPack &gt;` (:1053-1149): the Thermal Thruster's two charges.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterRocketPack(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_ROCKETPACK", ["CTFRocketPack"], false, "resource/UI/HudRocketPack.res")
{
    // `LOADOUT_POSITION_SECONDARY`: `GetRocketPackCharge` is its item charge (tf_player_shared.h:477-478).
    private const int LoadoutSecondary = 1;

    /// <inheritdoc/>
    public override string LabelText()
    {
        if (Weapon() is { } pack)
        {
            return pack.RocketPackEnabled ? "#TF_RocketPack_Charges" : "#TF_RocketPack_Disabled";
        }

        // `CHudItemEffectMeter::GetLabelText()`: the cloak meter's, not the template's.
        return CloakLabelText();
    }

    /// <inheritdoc/>
    public override string IconName() => Weapon() is { RocketPackEnabled: true } ? "../hud/pyro_jetpack" : "../hud/pyro_jetpack_off2";

    /// <inheritdoc/>
    public override int NumProgressBar() => 2;

    /// <inheritdoc/>
    /// <remarks>Red until `IsRocketPackReady`: a charge of at least 50.</remarks>
    public override (byte Red, byte Green, byte Blue, byte Alpha) ProgressBarColor()
    {
        if (Player is { } player)
        {
            return Charge(player) >= 50.0f ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)255, (byte)0, (byte)0, (byte)255);
        }

        return base.ProgressBarColor();
    }

    /// <inheritdoc/>
    public override float Progress() => Player is { } player ? Charge(player) / 100.0f : 0f;

    /// <inheritdoc/>
    public override (byte Red, byte Green, byte Blue, byte Alpha) LabelTextColor()
    {
        if (Weapon() is { } pack)
        {
            return pack.RocketPackEnabled ? ((byte)235, (byte)235, (byte)235, (byte)255) : ((byte)178, (byte)178, (byte)178, (byte)255);
        }

        return base.LabelTextColor();
    }

    /// <inheritdoc/>
    /// <remarks>`ROCKETPACK_ENABLED` 1 or `ROCKETPACK_DISABLED` 0.</remarks>
    public override int MeterState()
    {
        if (Weapon() is { } pack)
        {
            return pack.RocketPackEnabled ? 1 : 0;
        }

        return base.MeterState();
    }

    /// <inheritdoc/>
    public override bool ShouldAutoAdjustPosition() => false;

    private static float Charge(ScenePlayer player) =>
        player.ItemChargeMeter is { Count: > LoadoutSecondary } meters ? meters[LoadoutSecondary] : 0f;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFSniperRifleDecap &gt;` (:1154): the Bazaar Bargain's heads.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterSniperRifleDecap(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SNIPERRIFLE_DECAP", ["CTFSniperRifleDecap"], false, "resource/UI/HudItemEffectMeter_Sniper.res")
{
    /// <inheritdoc/>
    public override int Count() => Weapon() is not null && Player is { } player ? player.Decapitations ?? 0 : 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFParticleCannon &gt;` (:1170): red until it can charge fire.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterParticleCannon(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_PARTICLE_CANNON", ["CTFParticleCannon"], false, "resource/UI/HUDItemEffectMeter_ParticleCannon.res")
{
    /// <inheritdoc/>
    /// <remarks>`CanChargeFire` (tf_weapon_particle_cannon.h:91): fully charged and not already charging.</remarks>
    public override (byte Red, byte Green, byte Blue, byte Alpha) ProgressBarColor() =>
        Weapon() is { } cannon && Player is { } player && cannon.Energy >= MaxEnergy(player, cannon) && cannon.ChargeBeginTime <= 0
            ? ((byte)255, (byte)255, (byte)255, (byte)255)
            : ((byte)255, (byte)0, (byte)0, (byte)255);
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFRevolver &gt;` (:1184-1233): the Diamondback's crits or the Enforcer-style bonus.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterRevolver(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_REVOLVER", ["CTFRevolver"], false, "resource/UI/HUDItemEffectMeter_Spy.res")
{
    /// <inheritdoc/>
    /// <remarks>`CTFRevolver::GetCount` (tf_weapon_revolver.cpp:229).</remarks>
    public override int Count()
    {
        if (Weapon() is not { } revolver || Player is not { } player)
        {
            return 0;
        }

        if (SapperKillsCollectCrits(player, revolver))
        {
            return player.RevengeCrits ?? 0;
        }

        return ItemHookInt(player, revolver, "extra_damage_on_hit") != 0 ? Math.Min(200, player.Decapitations ?? 0) : 0;
    }

    /// <inheritdoc/>
    public override bool IsEnabled() =>
        Weapon() is { } revolver && Player is { } player
        && (SapperKillsCollectCrits(player, revolver) || ItemHookInt(player, revolver, "extra_damage_on_hit") != 0);

    /// <inheritdoc/>
    public override bool ShowPercentSymbol() =>
        Weapon() is { } revolver && Player is { } player && ItemHookInt(player, revolver, "extra_damage_on_hit") != 0;

    /// <summary>`SapperKillsCollectCrits` (tf_weapon_revolver.h:51): `sapper_kills_collect_crits == 1`.</summary>
    private bool SapperKillsCollectCrits(ScenePlayer player, SceneItem revolver) =>
        ItemHookInt(player, revolver, "sapper_kills_collect_crits") == 1;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFKnife &gt;` (:1238): the Spy-cicle.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterKnife(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_KNIFE", ["CTFKnife"], true, "resource/UI/HUDItemEffectMeter_SpyKnife.res")
{
    // `KNIFE_ICICLE` (tf_weapon_knife.h:25).
    private const int KnifeIcicle = 3;

    /// <inheritdoc/>
    /// <remarks>`GetKnifeType`: `set_weapon_mode` (tf_weapon_knife.h:43).</remarks>
    public override bool IsEnabled() => Weapon() is { } knife && Player is { } player && ItemHookInt(player, knife, "set_weapon_mode") == KnifeIcicle;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFSniperRifle &gt;` (:1252-1291): the Hitman's Heatmaker's focus.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterSniperRifle(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SNIPERRIFLE", ["CTFSniperRifle"], true, "resource/UI/HudItemEffectMeter_SniperFocus.res")
{
    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && Weapon() is { } rifle && BuffType(player, rifle) > 0;

    /// <inheritdoc/>
    public override bool ShouldFlash() => Weapon() is not null && Player is { } player && RageShouldFlash(player);

    /// <inheritdoc/>
    public override string BeepSound() =>
        Weapon() is { } rifle && Player is { } player && BuffType(player, rifle) > 0 ? "Weapon_Bison.SingleCrit" : base.BeepSound();
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; C_TFWeaponBuilder &gt;` (:1296-1335): the MvM robot sapper.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterBuilder(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_BUILDER", ["CTFWeaponBuilder", "CTFWeaponSapper"], true, "resource/UI/HudItemEffectMeter_Sapper.res")
{
    /// <inheritdoc/>
    public override bool IsEnabled() => Player is not null && State.Rules.MannVsMachine;

    /// <inheritdoc/>
    public override bool ShouldBeep() => Player is { } player && PlayerHookInt(player, "robo_sapper") > 0;

    /// <inheritdoc/>
    /// <remarks>`C_TFWeaponBuilder::EffectMeterShouldFlash` (c_tf_weapon_builder.cpp:436): a robot sapper, recharged.</remarks>
    public override bool ShouldFlash() =>
        Player is { } player && Weapon() is { } builder && PlayerHookInt(player, "robo_sapper") != 0 && WeaponProgress(player, builder) >= 1.0f;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFPowerupBottle &gt;` (:1337-1400): the MvM canteen, found among the wearables.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterPowerupBottle(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_NONE", [], true, "resource/UI/HudItemEffectMeter_PowerupBottle.res")
{
    /// <inheritdoc/>
    /// <remarks>`GetWeapon` (:1337): the first `CTFPowerupBottle` among the wearables.</remarks>
    public override SceneItem? Weapon()
    {
        if (MeterEnabled && Player is { } player && Held(player) is null)
        {
            SceneItem? bottle = null;

            foreach (SceneItem item in player.Items ?? [])
            {
                if (!item.IsWeapon && item.ClassName == "CTFPowerupBottle")
                {
                    bottle = item;
                    break;
                }
            }

            SetWeapon(bottle);

            if (bottle is null)
            {
                MeterEnabled = false;
            }
        }

        return Player is { } owner ? Held(owner) : null;
    }

    /// <inheritdoc/>
    public override bool IsEnabled() => Player is not null && Weapon() is { NumCharges: > 0 };

    /// <inheritdoc/>
    public override int Count() => Weapon() is { } bottle ? bottle.NumCharges : 0;

    /// <inheritdoc/>
    /// <remarks>`CTFPowerupBottle::GetEffectIconName` (tf_item_powerup_bottle.cpp:601).</remarks>
    public override string IconName()
    {
        if (Weapon() is { } bottle && Player is { } player)
        {
            return PowerupType(player, bottle) switch
            {
                "ubercharge" => "../hud/ico_powerup_ubercharge_red",
                "recall" => "../hud/ico_powerup_recall_red",
                "refill_ammo" => "../hud/ico_powerup_refill_ammo_red",
                "building_instant_upgrade" => "../hud/ico_powerup_building_instant_red",
                _ => "../hud/ico_powerup_critboost_red",
            };
        }

        return base.IconName();
    }
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CWeaponMedigun &gt;` (:1405-1439): the MvM medic's shield rage.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterMedigun(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_MEDIGUN", ["CWeaponMedigun"], true, "resource/UI/HudItemEffectMeter_Scout.res")
{
    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && PlayerHookInt(player, "generate_rage_on_heal") != 0 && Weapon() is not null;

    /// <inheritdoc/>
    public override bool ShouldFlash() => Weapon() is not null && Player is { } player && RageShouldFlash(player);
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFThrowable &gt;` (:1466): shown while the throwable says so.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterThrowable(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(
        manager,
        playerIndex,
        "TF_WEAPON_THROWABLE",
        ["CTFThrowablePrimary", "CTFThrowableSecondary", "CTFThrowableMelee", "CTFThrowableUtility"],
        true,
        "resource/UI/HudItemEffectMeter_Action.res")
{
    /// <inheritdoc/>
    /// <remarks>`ShowHudElement` is true for every throwable (tf_weapon_throwable.h:50).</remarks>
    public override bool IsEnabled() => Weapon() is not null;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFBonesaw &gt;` (:1536-1562): the Vita-Saw's organs.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterBonesaw(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_BONESAW", ["CTFBonesaw"], false, "resource/UI/HUDItemEffectMeter_Organs.res")
{
    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && PlayerHook(player, "ubercharge_preserved_on_spawn_max", 0f) != 0f;

    /// <inheritdoc/>
    public override int Count() => Player is { } player ? player.Decapitations ?? 0 : 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFRocketLauncher_AirStrike &gt;` (:1568): the Air Strike's kills.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterAirStrike(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_ROCKETLAUNCHER", ["CTFRocketLauncher_AirStrike"], false, "resource/UI/HudItemEffectMeter_Demoman.res")
{
    /// <inheritdoc/>
    public override int Count() => Weapon() is not null && Player is { } player ? player.Decapitations ?? 0 : 0;
}

/// <summary>`CHudItemEffectMeter_Weapon&lt; CTFSpellBook &gt;` (:1582-1631): the Halloween kart's boost and damage.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterSpellBook(TfItemEffectMeterManager manager, int playerIndex)
    : TfItemEffectMeterWeapon(manager, playerIndex, "TF_WEAPON_SPELLBOOK", ["CTFSpellBook"], true, "resource/UI/HudItemEffectMeter_KartCharge.res")
{
    // `TF_COND_HALLOWEEN_KART` (tf_shareddefs.h) and `GR_STATE_RND_RUNNING` (teamplayroundbased_gamerules.h).
    private const int ConditionHalloweenKart = 82;
    private const int RoundRunning = 4;

    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && player.Conditions.Has(ConditionHalloweenKart) && Weapon() is not null;

    /// <inheritdoc/>
    /// <remarks>`GetKartSpeedBoost` (tf_player_shared.cpp:13640), `tf_halloween_kart_boost_recharge` being 5.</remarks>
    public override float Progress()
    {
        if (Player is not { } player || !player.Conditions.Has(ConditionHalloweenKart))
        {
            return 0f;
        }

        float recharge = HudViewport.ConVarsOf(this).GetFloat("tf_halloween_kart_boost_recharge");
        float next = player.KartNextAvailableBoost ?? 0f;
        float now = State.ServerTime;

        if (next < now)
        {
            return 1.0f;
        }

        if (next > now + recharge)
        {
            return 0.0f;
        }

        // `RemapValClamped( curtime, next - recharge, next, 0, 1 )`.
        return Math.Clamp((now - (next - recharge)) / recharge, 0f, 1f);
    }

    /// <inheritdoc/>
    public override int Count() => Player is { } player && player.Conditions.Has(ConditionHalloweenKart) ? player.KartHealth ?? 0 : 0;

    /// <inheritdoc/>
    public override bool ShowPercentSymbol() => true;

    /// <inheritdoc/>
    /// <remarks>
    /// In a minigame: not a ghost, not behind the scoreboard, and only while the round runs; out of one, never during the
    /// match summary, then the base meter's refusals.
    /// </remarks>
    public override bool ShouldDraw(HudState state)
    {
        if (state.Rules.ActiveMinigame)
        {
            if (Player is { } player && player.Conditions.Has(ConditionHalloweenGhostMode))
            {
                return false;
            }

            return !Manager.ScoreboardVisible && state.RoundState == RoundRunning;
        }

        return !state.Rules.ShowMatchSummary && base.ShouldDraw(state);
    }
}
