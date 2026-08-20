using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Which activity suffix each weapon drives, read from the game's own weapon scripts.
/// </summary>
/// <remarks>
/// **A weapon decides how the whole body animates, not just what is in the hands.**
/// <c>CTFWeaponBase::ActivityList</c> (<c>tf_weaponbase.cpp:4208</c>) switches on the weapon's role
/// and returns an <c>acttable_t</c> whose every entry maps a bare activity to a suffixed one —
/// <c>{ ACT_MP_RUN, ACT_MP_RUN_SECONDARY }</c>. So a medic holding a medigun runs with a different
/// animation from a scout holding a scattergun.
///
/// The role is <c>GetTFWpnData().m_iWeaponType</c>, and that comes from the weapon's script by the
/// <c>WeaponType</c> key, parsed in <c>tf_weapon_parse.cpp:134</c> against exactly the strings
/// below. The scripts ship encrypted as <c>.ctx</c> under the key Valve publishes in
/// <c>tf_shareddefs.cpp:1616</c>, which this project already decrypts for the class scripts.
///
/// **What is NOT implemented, stated rather than hidden:** <c>GetActivityWeaponRole</c> lets an
/// equipped item override the script with its own <c>anim_slot</c>, from <c>items_game.txt</c> via
/// <c>CEconItemView::GetAnimationSlot</c>. That override exists for weapons whose animation slot
/// differs from their script's, and reading it needs the econ schema keyed by
/// <c>m_iItemDefinitionIndex</c>. Until then a weapon carrying such an override animates by its base
/// script, which is right for stock weapons and for most reskins because they share a script.
/// </remarks>
public sealed class WeaponRoles
{
    /// <summary>Valve's own, <c>GetTFEncryptionKey</c>, <c>tf_shareddefs.cpp:1616</c>.</summary>
    internal static readonly byte[] EncryptionKey = "E2NcUkG2"u8.ToArray();

    /// <summary>
    /// The <c>WeaponType</c> strings and the activity suffix each one names.
    /// </summary>
    /// <remarks>
    /// The keys are the strings <c>tf_weapon_parse.cpp</c> compares against; the values are the
    /// suffixes the matching <c>acttable_t</c> produces. "grenade" has no movement table of its own
    /// — <c>TF_WPN_TYPE_GRENADE</c> is not one of the cases in <c>ActivityList</c>, which falls to
    /// the primary table by its <c>default</c> — so it maps to the primary suffix rather than being
    /// left out.
    /// </remarks>
    private static readonly Dictionary<string, string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["primary"] = "PRIMARY",
        ["secondary"] = "SECONDARY",
        ["melee"] = "MELEE",
        ["grenade"] = "PRIMARY",
        ["building"] = "BUILDING",
        ["pda"] = "PDA",
        ["item1"] = "ITEM1",
        ["item2"] = "ITEM2",
    };

    /// <summary>The suffix for one weapon in one class's hands.</summary>
    /// <remarks>
    /// Keyed by both, because a weapon's role is not a property of the weapon alone:
    /// <c>tf_weapon_shotgun</c> is a primary for an engineer and a secondary for a soldier, a heavy
    /// and a pyro. See <see cref="WeaponScriptName.Translate"/>.
    /// </remarks>
    private readonly Dictionary<(string Weapon, int Class), string> _byServerClass = [];

    /// <summary>Reads every weapon script the game ships that this project can name.</summary>
    /// <param name="readFile">Opens a file from the game, or returns null.</param>
    /// <param name="serverClasses">The weapon server classes a demo actually mentions.</param>
    /// <returns>The roles, ready to answer by server class.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// **Driven by what the demo mentions rather than by the whole archive**, because the archive
    /// holds 78 weapon scripts and a recording touches a handful. Each is decrypted once.
    /// </remarks>
    public static WeaponRoles Read(
        Func<string, byte[]?> readFile, IEnumerable<(string Weapon, int? Class)> serverClasses)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(serverClasses);

        WeaponRoles roles = new();
        IceCipher cipher = new(EncryptionKey);

        foreach ((string serverClass, int? playerClass) in serverClasses)
        {
            (string Weapon, int Class) key = (serverClass, playerClass ?? 0);

            if (roles._byServerClass.ContainsKey(key))
            {
                continue;
            }

            foreach (string candidate in WeaponScriptName.Candidates(serverClass, playerClass))
            {
                string name = "scripts/" + candidate;

                // Plain text first and then the encrypted form, which is the engine's own order in
                // ReadEncryptedKVFile — a loose .txt is how a mod overrides a weapon.
                byte[]? script = readFile(name + ".txt");

                if (script is null && readFile(name + ".ctx") is { } encrypted)
                {
                    script = cipher.DecryptAll(encrypted);
                }

                if (script is null)
                {
                    continue;
                }

                if (ScriptKeyValue.First(script, "WeaponType") is { } type &&
                    Suffixes.TryGetValue(type, out string? suffix))
                {
                    roles._byServerClass[key] = suffix;
                }

                // The first script that exists is the weapon's, whether or not it named a type
                // this understands — trying the next candidate would read a different weapon.
                break;
            }
        }

        return roles;
    }

    /// <summary>The activity suffix a weapon drives.</summary>
    /// <param name="serverClass">The weapon's server class, or null when nothing is held.</param>
    /// <returns>The suffix, defaulting to <c>PRIMARY</c>.</returns>
    /// <remarks>
    /// **Primary is the default in the engine too**, not a guess made here:
    /// <c>ActivityList</c>'s switch has <c>case TF_WPN_TYPE_PRIMARY:</c> sharing its body with
    /// <c>default:</c>. A weapon whose script is missing therefore animates exactly as the engine
    /// would animate one whose type it did not recognise.
    /// </remarks>
    public string Suffix(string? serverClass) => Suffix(serverClass, playerClass: null);

    /// <summary>The activity suffix a weapon drives in a particular class's hands.</summary>
    /// <param name="serverClass">The weapon's server class, or null when nothing is held.</param>
    /// <param name="playerClass">Who is holding it, or null when the demo did not say.</param>
    /// <returns>The suffix, defaulting to <c>PRIMARY</c>.</returns>
    public string Suffix(string? serverClass, int? playerClass)
    {
        if (serverClass is null)
        {
            return "PRIMARY";
        }

        if (_byServerClass.TryGetValue((serverClass, playerClass ?? 0), out string? suffix))
        {
            return suffix;
        }

        // Falling back to the classless reading is not the same as giving up: a weapon with no
        // per-class translation was stored under class 0 by whoever asked for it first.
        return _byServerClass.TryGetValue((serverClass, 0), out string? plain) ? plain : "PRIMARY";
    }
}
