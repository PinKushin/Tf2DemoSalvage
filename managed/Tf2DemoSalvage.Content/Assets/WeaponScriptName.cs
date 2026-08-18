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

    /// <summary>Every script name a server class might use, best first.</summary>
    /// <param name="serverClass">The class a demo's schema names, such as <c>CTFRocketLauncher</c>.</param>
    /// <returns>Candidate script names, without a folder or extension.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverClass"/> is null.</exception>
    /// <remarks>
    /// Several are offered because the archive settles which exists — a candidate that names no file
    /// costs a failed lookup, while picking one spelling and being wrong costs the weapon's role.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(string serverClass)
    {
        ArgumentNullException.ThrowIfNull(serverClass);

        if (Irregular.TryGetValue(serverClass, out string? known))
        {
            return [known];
        }

        List<string> candidates = [];

        foreach (string bare in Bare(serverClass))
        {
            Add(candidates, "tf_weapon_" + Lower(bare));
            Add(candidates, "tf_weapon_" + Broken(bare));
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
