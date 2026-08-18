using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Which weapon script belongs to a server class, such as <c>CTFBat</c> to <c>tf_weapon_bat</c>.
/// </summary>
/// <remarks>
/// **A demo carries the server class and the script is named for the ENTITY class.** The two are
/// paired in the SDK by <c>LINK_ENTITY_TO_CLASS( tf_weapon_bat, CTFBat )</c>, which is source rather
/// than shipped data — so the correspondence has to be reproduced here, and
/// <c>WeaponScriptNameTests</c> checks it against every pair the SDK declares.
///
/// **Most of it is regular and a few are not, which is why this is a rule plus a list rather than
/// either alone.** Stripping the class prefix and lowercasing covers about half; also breaking the
/// name at each capital covers most of the rest — <c>CTFBreakableSign</c> is
/// <c>tf_weapon_breakable_sign</c> and <c>CTFDRGPomson</c> is <c>tf_weapon_drg_pomson</c>. What
/// remains is genuinely irregular: a scout's pistols are <c>handgun</c>, the medic's syringe gun
/// carries a <c>_medic</c> suffix, and a fireball is a rocket launcher.
///
/// **The exceptions are enumerated rather than absorbed by a fallback**, because a name that fails
/// to resolve does not report anything — it just leaves the weapon with no script, and the animation
/// silently keeps the primary suffix it would have had anyway. That is the failure mode where a
/// guess and a correct answer look identical.
/// </remarks>
public static class WeaponScriptName
{
    /// <summary>Pairs no naming rule reproduces.</summary>
    /// <remarks>
    /// Taken from <c>LINK_ENTITY_TO_CLASS</c> one by one. Each is a name Valve chose that does not
    /// follow from the class: the scout's pistols were re-scripted as "handgun", the classic and
    /// decapitation sniper rifles keep <c>sniperrifle</c> unbroken while the rule would split it,
    /// the syringe gun gained a class suffix, and the base classes drop the underscore that the
    /// rule inserts.
    /// </remarks>
    private static readonly Dictionary<string, string> Irregular = new(StringComparer.Ordinal)
    {
        ["CTFPistol_ScoutPrimary"] = "tf_weapon_handgun_scout_primary",
        ["CTFPistol_ScoutSecondary"] = "tf_weapon_handgun_scout_secondary",
        ["CTFSniperRifleClassic"] = "tf_weapon_sniperrifle_classic",
        ["CTFSniperRifleDecap"] = "tf_weapon_sniperrifle_decap",
        ["CTFSyringeGun"] = "tf_weapon_syringegun_medic",
        ["CTFWeaponFlameBall"] = "tf_weapon_rocketlauncher_fireball",
        ["CTFWeaponBaseGrenade"] = "tf_weaponbase_grenade",
        ["CTFWeaponBaseGrenadeProj"] = "tf_weaponbase_grenade_proj",
        ["CTFWeaponBaseMelee"] = "tf_weaponbase_melee",
        ["CTFWeaponBaseMerasmusGrenade"] = "tf_weaponbase_merasmus_grenade",
    };

    /// <summary>
    /// Weapons whose script depends on who is holding them, indexed by class number.
    /// </summary>
    /// <remarks>
    /// **<c>pszWpnEntTranslationList</c>**, <c>tf_shareddefs.cpp:1628</c>. One weapon entity can
    /// belong to several classes and animate differently for each, so the engine rewrites the base
    /// entity name before reading its script. An empty entry means no translation for that class.
    ///
    /// **The role really does change**, which is why this is worth carrying: <c>tf_weapon_shotgun</c>
    /// is a primary in its own script, but a soldier's, heavy's and pyro's translate to
    /// <c>_soldier</c>, <c>_hwg</c> and <c>_pyro</c> — all secondaries — while only the engineer's
    /// <c>_primary</c> stays a primary. The same holds for the parachute (soldier secondary, demoman
    /// primary), the revolver (engineer secondary) and the throwable (medic primary).
    ///
    /// Class numbers are TF's own: 1 Scout, 2 Sniper, 3 Soldier, 4 Demoman, 5 Medic, 6 Heavy,
    /// 7 Pyro, 8 Spy, 9 Engineer, with 0 undefined.
    ///
    /// **Valve's parachute row is missing a comma** — the spy's <c>""</c> and the engineer's
    /// <c>""</c> sit adjacent, so C concatenates them into one literal and the initialiser is a
    /// element short. Harmless there because both are empty; reproduced here as the two empties it
    /// was meant to be rather than as the nine-element row it compiles to.
    /// </remarks>
    private static readonly Dictionary<string, string[]> PerClass = new(StringComparer.Ordinal)
    {
        ["tf_weapon_shotgun"] =
        [
            "", "", "", "tf_weapon_shotgun_soldier", "", "",
            "tf_weapon_shotgun_hwg", "tf_weapon_shotgun_pyro", "", "tf_weapon_shotgun_primary",
        ],
        ["tf_weapon_pistol"] =
        [
            "", "tf_weapon_pistol_scout", "", "", "", "", "", "", "", "tf_weapon_pistol",
        ],
        ["tf_weapon_shovel"] =
        [
            "", "", "", "tf_weapon_shovel", "tf_weapon_bottle", "", "", "", "", "",
        ],
        ["tf_weapon_bottle"] =
        [
            "", "", "", "tf_weapon_shovel", "tf_weapon_bottle", "", "", "", "", "",
        ],
        ["saxxy"] =
        [
            "", "tf_weapon_bat", "tf_weapon_club", "tf_weapon_shovel", "tf_weapon_bottle",
            "tf_weapon_bonesaw", "tf_weapon_fireaxe", "tf_weapon_fireaxe", "tf_weapon_knife",
            "tf_weapon_wrench",
        ],
        ["tf_weapon_throwable"] =
        [
            "", "tf_weapon_throwable_secondary", "tf_weapon_throwable_secondary",
            "tf_weapon_throwable_secondary", "tf_weapon_throwable_secondary",
            "tf_weapon_throwable_primary", "tf_weapon_throwable_secondary",
            "tf_weapon_throwable_secondary", "tf_weapon_throwable_secondary",
            "tf_weapon_throwable_secondary",
        ],
        ["tf_weapon_parachute"] =
        [
            "", "", "", "tf_weapon_parachute_secondary", "tf_weapon_parachute_primary",
            "", "", "", "", "",
        ],
        ["tf_weapon_revolver"] =
        [
            "", "", "", "", "", "", "", "", "tf_weapon_revolver", "tf_weapon_revolver_secondary",
        ],
    };

    /// <summary>The script a weapon uses in a particular class's hands.</summary>
    /// <param name="entityClass">The base entity name, such as <c>tf_weapon_shotgun</c>.</param>
    /// <param name="playerClass">Who is holding it, or null when the demo did not say.</param>
    /// <returns>The translated name, or the base one when no translation applies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityClass"/> is null.</exception>
    public static string Translate(string entityClass, int? playerClass)
    {
        ArgumentNullException.ThrowIfNull(entityClass);

        if (playerClass is not { } who ||
            !PerClass.TryGetValue(entityClass, out string[]? byClass) ||
            who < 0 ||
            who >= byClass.Length)
        {
            return entityClass;
        }

        // An empty entry means this class has no translation, not that it has no weapon.
        return byClass[who].Length > 0 ? byClass[who] : entityClass;
    }

    /// <summary>Every script name a server class might use, best first.</summary>
    /// <param name="serverClass">The class a demo's schema names, such as <c>CTFRocketLauncher</c>.</param>
    /// <returns>Candidate script names, without a folder or extension.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverClass"/> is null.</exception>
    /// <remarks>
    /// Several are offered because the archive settles which exists — a candidate that names no file
    /// costs a failed lookup, while picking one spelling and being wrong costs the weapon's role.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(string serverClass) =>
        Candidates(serverClass, playerClass: null);

    /// <summary>The same, for a weapon in a particular class's hands.</summary>
    /// <param name="serverClass">The class a demo's schema names.</param>
    /// <param name="playerClass">Who is holding it, or null when the demo did not say.</param>
    /// <returns>Candidate script names, the class's own translation first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverClass"/> is null.</exception>
    /// <remarks>
    /// **The translation goes in FRONT rather than replacing the list.** A class that has no
    /// translation for this weapon falls through to the ordinary names, and a translation naming a
    /// script the install does not ship still leaves the base one to answer.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(string serverClass, int? playerClass)
    {
        ArgumentNullException.ThrowIfNull(serverClass);

        List<string> candidates = [];

        if (Irregular.TryGetValue(serverClass, out string? known))
        {
            Add(candidates, Translate(known, playerClass));
            Add(candidates, known);

            return candidates;
        }

        List<string> plain = [];

        foreach (string bare in Bare(serverClass))
        {
            Add(plain, "tf_weapon_" + Lower(bare));
            Add(plain, "tf_weapon_" + Broken(bare));
        }

        // Each name's own translation first, then every untranslated name — so a shotgun in a
        // soldier's hands asks for tf_weapon_shotgun_soldier before tf_weapon_shotgun.
        foreach (string candidate in plain)
        {
            Add(candidates, Translate(candidate, playerClass));
        }

        foreach (string candidate in plain)
        {
            Add(candidates, candidate);
        }

        return candidates;
    }

    /// <summary>The class name with each recognised prefix removed, longest first.</summary>
    /// <remarks>
    /// <c>CTFWeaponBuilder</c> is <c>tf_weapon_builder</c> rather than
    /// <c>tf_weapon_weapon_builder</c>, so "CTFWeapon" has to be tried before "CTF" — and both have
    /// to be tried, because <c>CTFWeaponBase</c> is <c>tf_weapon_base</c> while a class whose own
    /// name begins Weapon would lose a word.
    /// </remarks>
    private static IEnumerable<string> Bare(string serverClass)
    {
        if (serverClass.StartsWith("CTFWeapon", StringComparison.Ordinal))
        {
            yield return serverClass["CTFWeapon".Length..];
        }

        if (serverClass.StartsWith("CTF", StringComparison.Ordinal))
        {
            yield return serverClass[3..];
        }

        if (serverClass.StartsWith("CWeapon", StringComparison.Ordinal))
        {
            yield return serverClass["CWeapon".Length..];
        }

        if (serverClass.StartsWith('C'))
        {
            yield return serverClass[1..];
        }
    }

    /// <summary>The name run together in lower case.</summary>
    private static string Lower(string bare) =>
#pragma warning disable CA1308 // Building a file name, not a comparison key; the engine's are lower.
        bare.ToLowerInvariant();
#pragma warning restore CA1308

    /// <summary>The name broken at each capital that starts a word.</summary>
    private static string Broken(string bare)
    {
        StringBuilder built = new(bare.Length * 2);

        for (int index = 0; index < bare.Length; index++)
        {
            char letter = bare[index];

            // A capital starts a word when the letter before it is not a capital, or when the one
            // after it is lower case — the second half is what keeps DRGPomson together as DRG and
            // Pomson rather than splitting every letter of the acronym.
            bool boundary = index > 0 &&
                char.IsUpper(letter) &&
                (!char.IsUpper(bare[index - 1]) ||
                 (index + 1 < bare.Length && char.IsLower(bare[index + 1])));

            if (boundary && built.Length > 0 && built[^1] != '_')
            {
                built.Append('_');
            }

            built.Append(char.ToLowerInvariant(letter));
        }

        return built.ToString();
    }

    /// <summary>Adds a candidate once.</summary>
    private static void Add(List<string> candidates, string candidate)
    {
        if (candidate.Length > "tf_weapon_".Length && !candidates.Contains(candidate))
        {
            candidates.Add(candidate);
        }
    }
}
