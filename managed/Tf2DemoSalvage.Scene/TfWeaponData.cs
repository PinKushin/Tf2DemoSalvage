using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a weapon's script and a class's script say about ammunition, read once per name.</summary>
/// <remarks>
/// `FileWeaponInfo_t::Parse` (game/shared/weapon_parse.cpp): `clip_size` defaults to `WEAPON_NOCLIP` (-1),
/// `primary_ammo` names an ammo definition — "None" or an unknown name is -1 (`GetAmmoDef()->Index`). TF's ammo
/// definitions are registered in `ETFAmmoType` order with `TF_AMMO_DUMMY` first "to make the CAmmoDef indices correct",
/// so a name's index is its enum value. `TFPlayerClassData_t::ParseData` (tf_classdata.cpp:170) reads `AmmoMax` by those
/// same names. Both files are encrypted `.ctx`, read the way <see cref="WeaponScript"/> reads them.
/// </remarks>
/// <param name="read">Reads a game file.</param>
public sealed class TfWeaponData(Func<string, byte[]?> read)
{
    /// <summary>`TF_AMMO_METAL`.</summary>
    public const int AmmoMetal = 3;

    // `g_aAmmoNames`' order, which is `ETFAmmoType`'s (tf_shareddefs.h:357).
    private static readonly string[] AmmoNames =
    [
        "TF_AMMO_DUMMY", "TF_AMMO_PRIMARY", "TF_AMMO_SECONDARY", "TF_AMMO_METAL", "TF_AMMO_GRENADES1", "TF_AMMO_GRENADES2",
        "TF_AMMO_GRENADES3",
    ];

    // `g_aPlayerClassNames_NonLocalized`, 1 Scout through 9 Engineer.
    private static readonly string[] ClassFiles =
    [
        "", "scout", "sniper", "soldier", "demoman", "medic", "heavyweapons", "pyro", "spy", "engineer",
    ];

    private readonly Dictionary<(string ServerClass, int PlayerClass), (int MaxClip1, int PrimaryAmmo, bool NamesAmmo)> _weapons = [];
    private readonly Dictionary<int, int[]> _ammoMax = [];
    private readonly Dictionary<int, int?> _classHealth = [];
    private KeyValuesTree? _objects;

    // `g_ObjectInfos`' names, in `ObjectType_t` order (tf_shareddefs.cpp).
    private static readonly string[] ObjectNames = ["OBJ_DISPENSER", "OBJ_TELEPORTER", "OBJ_SENTRYGUN", "OBJ_ATTACHMENT_SAPPER"];
    private readonly Dictionary<(string ServerClass, int PlayerClass), (bool DrawCrosshair, IReadOnlyList<Hud.HudTexture> Icons)> _hud = [];

    /// <summary>A weapon's script: its `clip_size` and its primary ammo type.</summary>
    /// <param name="serverClass">The weapon's server class, such as `CTFScatterGun`.</param>
    /// <param name="playerClass">Who holds it, for the per-class script names.</param>
    /// <returns>
    /// `iMaxClip1`, the ammo index, and whether `szAmmo1` is non-empty — `Precache`'s guard for the metal override;
    /// (-1, -1, false) when no script could be found.
    /// </returns>
    public (int MaxClip1, int PrimaryAmmo, bool NamesAmmo) Weapon(string serverClass, int? playerClass)
    {
        ArgumentNullException.ThrowIfNull(serverClass);

        (string, int) key = (serverClass, playerClass ?? 0);

        if (_weapons.TryGetValue(key, out (int, int, bool) known))
        {
            return known;
        }

        (int MaxClip1, int PrimaryAmmo, bool NamesAmmo) found = (-1, -1, false);

        foreach (string candidate in WeaponScriptName.Candidates(serverClass, playerClass))
        {
            if (WeaponScript.Read(read, candidate) is not { } script)
            {
                continue;
            }

            // Parsed as KeyValues rather than scanned: the shipped scripts write some keys bare — `clip_size 4`.
            KeyValuesTree data = KeyValuesTree.Load(script.Text.ToArray(), candidate, _ => null);

            string? ammo = data.Find("primary_ammo")?.Value;

            found = (Int(data.Find("clip_size")?.Value, -1), AmmoIndex(ammo), ammo is { Length: > 0 });
            break;
        }

        _weapons[key] = found;

        return found;
    }

    /// <summary>What the HUD reads of a weapon's script: `m_bDrawCrosshair` and the `TextureData` icons.</summary>
    /// <param name="serverClass">The weapon's server class.</param>
    /// <param name="playerClass">Who holds it.</param>
    /// <returns>
    /// `DrawCrosshair` (tf_weapon_parse.cpp:78, `GetInt( "DrawCrosshair", 1 ) > 0`) and the script's icons as
    /// `LoadWeaponSprites` reads them (weapons_resource.cpp:111); true and none when no script is found.
    /// </returns>
    public (bool DrawCrosshair, IReadOnlyList<Hud.HudTexture> Icons) WeaponHud(string serverClass, int? playerClass)
    {
        ArgumentNullException.ThrowIfNull(serverClass);

        (string, int) key = (serverClass, playerClass ?? 0);

        if (_hud.TryGetValue(key, out (bool, IReadOnlyList<Hud.HudTexture>) known))
        {
            return known;
        }

        (bool DrawCrosshair, IReadOnlyList<Hud.HudTexture> Icons) found = (true, []);

        foreach (string candidate in WeaponScriptName.Candidates(serverClass, playerClass))
        {
            if (WeaponScript.Read(read, candidate) is not { } script)
            {
                continue;
            }

            KeyValuesTree data = KeyValuesTree.Load(script.Text.ToArray(), candidate, _ => null);

            found = (Int(data.Find("DrawCrosshair")?.Value, 1) > 0, Hud.HudTextures.Parse(data));
            break;
        }

        _hud[key] = found;

        return found;
    }

    /// <summary>`m_aAmmoMax[ammo]` for a class — before any attribute.</summary>
    /// <param name="playerClass">1 Scout through 9 Engineer.</param>
    /// <param name="ammo">The ammo index.</param>
    /// <returns>The maximum; 0 for a class or ammo the scripts do not name.</returns>
    public int AmmoMax(int playerClass, int ammo)
    {
        if (playerClass < 1 || playerClass >= ClassFiles.Length || ammo < 0 || ammo >= AmmoNames.Length)
        {
            return 0;
        }

        if (!_ammoMax.TryGetValue(playerClass, out int[]? maxima))
        {
            maxima = new int[AmmoNames.Length];

            if (WeaponScript.Read(read, "playerclasses/" + ClassFiles[playerClass]) is { } script &&
                KeyValuesTree.Load(script.Text.ToArray(), script.Name, _ => null).Find("AmmoMax") is { } ammoMax)
            {
                // `ParseData` reads indices 1 up; index 0 stays `TF_AMMO_DUMMY`, which is 0.
                for (int index = 1; index < AmmoNames.Length; index++)
                {
                    maxima[index] = Int(ammoMax.Find(AmmoNames[index])?.Value, 0);
                }
            }

            _ammoMax[playerClass] = maxima;
        }

        return maxima[ammo];
    }

    /// <summary>`TFPlayerClassData_t::m_nMaxHealth`: the class script's <c>"health"</c> — what `GetDisguiseMaxHealth` returns.</summary>
    /// <param name="playerClass">1 Scout through 9 Engineer.</param>
    /// <returns>The health, or null when the class has no script (`GetDisguiseMaxHealth`'s fallback is the caller's).</returns>
    public int? ClassMaxHealth(int playerClass)
    {
        if (playerClass < 1 || playerClass >= ClassFiles.Length)
        {
            return null;
        }

        if (!_classHealth.TryGetValue(playerClass, out int? health))
        {
            health = WeaponScript.Read(read, "playerclasses/" + ClassFiles[playerClass]) is { } script
                ? Int(KeyValuesTree.Load(script.Text.ToArray(), script.Name, _ => null).Find("health")?.Value, 0)
                : null;
            _classHealth[playerClass] = health;
        }

        return health;
    }

    /// <summary>`GetObjectInfo( type )` as the target ID reads it (tf_shareddefs.cpp:1452, `scripts/objects.txt`).</summary>
    /// <param name="objectType">`OBJ_DISPENSER` 0, `OBJ_TELEPORTER` 1, `OBJ_SENTRYGUN` 2, `OBJ_ATTACHMENT_SAPPER` 3.</param>
    /// <param name="mode">`GetObjectMode()`.</param>
    /// <returns>
    /// `m_AltModes[mode].pszStatusName` and `.pszModeName`, and `m_iNumAltModes` — `iIndex - 1` over the `AltModeN` blocks
    /// (:1533), and mode 0's status name the object's own (:1537).
    /// </returns>
    public (string? StatusName, string? ModeName, int NumAltModes) ObjectInfo(int objectType, int mode)
    {
        if (objectType < 0 || objectType >= ObjectNames.Length)
        {
            return (null, null, 0);
        }

        _objects ??= read("scripts/objects.txt") is { } bytes ? KeyValuesTree.Load(bytes, "scripts/objects.txt", _ => null) : null;

        if (_objects?.Find(ObjectNames[objectType]) is not { } info)
        {
            return (null, null, 0);
        }

        int count = 0;

        if (info.Find("AltModes") is { } altModes)
        {
            while (altModes.Find("AltMode" + count.ToString(CultureInfo.InvariantCulture)) is not null)
            {
                count++;
            }

            count--;
        }

        KeyValuesTree? altMode = info.Find("AltModes")?.Find("AltMode" + mode.ToString(CultureInfo.InvariantCulture));
        string? statusName = mode == 0 ? info.Find("StatusName")?.Value : altMode?.Find("StatusName")?.Value;

        return (statusName, altMode?.Find("ModeName")?.Value, Math.Max(count, 0));
    }

    /// <summary>`KeyValues::GetInt`: the value's leading integer, or the default when the key is absent.</summary>
    private static int Int(string? value, int fallback) => value is null ? fallback : Hud.PanelLayout.Atoi(value);

    /// <summary>`CAmmoDef::Index`: the name's position, or -1.</summary>
    private static int AmmoIndex(string? name) =>
        name is null ? -1 : Array.FindIndex(AmmoNames, 1, AmmoNames.Length - 1, known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
}
