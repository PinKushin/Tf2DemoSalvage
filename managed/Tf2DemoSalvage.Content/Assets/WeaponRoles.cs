using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Which activity table each held weapon animates the body with — <c>GetActivityWeaponRole</c> and
/// <c>ActivityList</c>, off the game's own weapon scripts and the item's <c>anim_slot</c>.
/// </summary>
/// <remarks>
/// **A weapon decides how the whole body animates, not just what is in the hands.**
/// <c>CTFWeaponBase::ActivityList</c> (<c>tf_weaponbase.cpp:4208</c>) switches on the weapon's role
/// and returns an <c>acttable_t</c> whose every entry maps a bare activity to a suffixed one —
/// <c>{ ACT_MP_RUN, ACT_MP_RUN_SECONDARY }</c>. So a medic holding a medigun runs with a different
/// animation from a scout holding a scattergun.
///
/// **The role is the script's until the item says otherwise** (B105). <c>GetActivityWeaponRole</c>
/// (<c>tf_weaponbase.cpp:4185</c>) starts from <c>GetTFWpnData().m_iWeaponType</c> — the script's
/// <c>WeaponType</c>, parsed in <c>tf_weapon_parse.cpp:134</c> — and replaces it with
/// <c>CEconItemView::GetAnimationSlot</c> whenever that is non-negative: the item definition's
/// <c>anim_slot</c> in <c>items_game.txt</c>, keyed by the weapon's <c>m_iItemDefinitionIndex</c>.
/// Seventy-five blocks declare one, and the stock demoman launchers are among them — his grenade
/// launcher animates as a secondary and his stickybomb launcher as a primary, the reverse of their
/// scripts. The scripts ship encrypted as <c>.ctx</c> under the key Valve publishes in
/// <c>tf_shareddefs.cpp:1616</c>.
///
/// **What each answer names is a TABLE, spelled as <c>WeaponActivityTable</c> keys it** —
/// <c>MELEEALLCLASS</c>, <c>PRIMARY2</c> — because the role is only ever used to look a row up.
/// `mp_forceactivityset` (<c>:4200</c>) is not read: it is FCVAR_DEVELOPMENTONLY and -1 in every
/// shipped build, so no recording can have set it.
/// </remarks>
public sealed class WeaponRoles
{
    /// <summary>
    /// The <c>WeaponType</c> strings and the <c>TF_WPN_TYPE_*</c> each one parses to.
    /// </summary>
    /// <remarks>
    /// Exactly the eight <c>tf_weapon_parse.cpp:134-167</c> compares against. A script naming anything
    /// else — the passtime gun's <c>utility</c> — keeps <c>m_iWeaponType</c>'s
    /// <c>TF_WPN_TYPE_PRIMARY</c> from the constructor (<c>:51</c>), which is what storing nothing and
    /// answering <c>PRIMARY</c> below reproduces.
    /// </remarks>
    private static readonly Dictionary<string, int> WeaponTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["primary"] = 0,
        ["secondary"] = 1,
        ["melee"] = 2,
        ["grenade"] = 3,
        ["building"] = 4,
        ["pda"] = 5,
        ["item1"] = ItemOne,
        ["item2"] = 7,
    };

    /// <summary><c>TF_WPN_TYPE_ITEM1</c>, <c>tf_item_constants.h:25</c>.</summary>
    private const int ItemOne = 6;

    /// <summary><c>TF_CLASS_DEMOMAN</c>, <c>tf_shareddefs.h:210</c>.</summary>
    private const int Demoman = 4;

    /// <summary>The katana's server class, <c>IMPLEMENT_NETWORKCLASS_ALIASED( TFKatana, … )</c> (<c>tf_weapon_sword.cpp:37</c>).</summary>
    private const string Katana = "CTFKatana";

    /// <summary>
    /// The weapons that replace <c>ActivityList</c> itself, so their role — and their item's slot — is
    /// never asked while they have an owner.
    /// </summary>
    /// <remarks>
    /// <c>CTFGrapplingHook::ActivityList</c> (<c>tf_weapon_grapplinghook.cpp:150</c>) and
    /// <c>CPasstimeGun::ActivityList</c> (<c>tf_weapon_passtime_gun.cpp:304</c>) return tables of their
    /// own. Those tables are not ported; what is ported is that the grappling hook's
    /// <c>MELEE_ALLCLASS</c> and the passtime gun's <c>PASSTIME_BALL</c> change nothing.
    /// </remarks>
    private static readonly HashSet<string> OwnActivityLists = new(StringComparer.Ordinal)
    {
        "CTFGrapplingHook",
        "CPasstimeGun",
    };

    /// <summary>The table <c>ActivityList</c> switches a role onto.</summary>
    /// <param name="weaponRole">A <c>TF_WPN_TYPE_*</c>.</param>
    /// <returns>The table's name as <c>WeaponActivityTable</c> keys it.</returns>
    /// <remarks>
    /// <c>tf_weaponbase.cpp:4226-4276</c>. <c>TF_WPN_TYPE_PRIMARY</c> shares its body with
    /// <c>default:</c>, so GRENADE, HEAD, MISC and PASSTIME_BALL — which no case names — run with the
    /// primary table, and so does anything out of range.
    /// </remarks>
    public static string ActivityList(int weaponRole) => weaponRole switch
    {
        1 => "SECONDARY",
        2 => "MELEE",
        4 => "BUILDING",
        5 => "PDA",
        ItemOne => "ITEM1",
        7 => "ITEM2",
        10 => "MELEEALLCLASS",
        11 => "SECONDARY2",
        12 => "PRIMARY2",
        13 => "ITEM3",
        14 => "ITEM4",
        _ => "PRIMARY",
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

        foreach ((string serverClass, int? playerClass) in serverClasses)
        {
            (string Weapon, int Class) key = (serverClass, playerClass ?? 0);

            if (roles._byServerClass.ContainsKey(key))
            {
                continue;
            }

            foreach (string candidate in WeaponScriptName.Candidates(serverClass, playerClass))
            {
                // Plain text first and then the encrypted form, which is the engine's own order in
                // ReadEncryptedKVFile — a loose .txt is how a mod overrides a weapon. WeaponScript owns
                // that order and the cipher key, since the explosion effects read the same files (B415).
                // Stryker disable once : a mutant that empties the guard body leaves 'script' unassigned, CS0165 — B410.
                if (WeaponScript.Read(readFile, candidate) is not { } script)
                {
                    continue;
                }

                if (script.Value("WeaponType") is { } type &&
                    WeaponTypes.TryGetValue(type, out int weaponType))
                {
                    roles._byServerClass[key] = ActivityList(weaponType);
                }

                // The first script that exists is the weapon's, whether or not it named a type
                // this understands — trying the next candidate would read a different weapon.
                break;
            }
        }

        return roles;
    }

    /// <summary>The table a weapon's script drives, before any item is asked.</summary>
    /// <param name="serverClass">The weapon's server class, or null when nothing is held.</param>
    /// <returns>The table's name, defaulting to <c>PRIMARY</c>.</returns>
    /// <remarks>
    /// **Primary is the default in the engine too**, not a guess made here:
    /// <c>ActivityList</c>'s switch has <c>case TF_WPN_TYPE_PRIMARY:</c> sharing its body with
    /// <c>default:</c>. A weapon whose script is missing therefore animates exactly as the engine
    /// would animate one whose type it did not recognise.
    /// </remarks>
    public string Suffix(string? serverClass) => Suffix(serverClass, playerClass: null);

    /// <summary>The table a weapon's script drives in a particular class's hands, before any item is asked.</summary>
    /// <param name="serverClass">The weapon's server class, or null when nothing is held.</param>
    /// <param name="playerClass">Who is holding it, or null when the demo did not say.</param>
    /// <returns>The table's name, defaulting to <c>PRIMARY</c>.</returns>
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

    /// <summary>The table a weapon animates with, once its item has had its say.</summary>
    /// <param name="serverClass">The weapon's server class, or null when nothing is held.</param>
    /// <param name="playerClass">Who is holding it, or null when the demo did not say.</param>
    /// <param name="animSlot">
    /// The item's <c>GetAnimationSlot()</c>: a <c>TF_WPN_TYPE_*</c>, -2 for <c>FORCE_NOT_USED</c>, or -1 when the item
    /// names none or there is no item at all (<c>econ_item_view.cpp:1093-1103</c>).
    /// </param>
    /// <returns>The table's name, defaulting to <c>PRIMARY</c>.</returns>
    /// <remarks>
    /// <code>
    ///   int iWeaponRole = GetTFWpnData().m_iWeaponType;                 // tf_weaponbase.cpp:4187
    ///   ...
    ///       int iMaybeOverrideAnimSlot = pEconItemView->GetAnimationSlot();
    ///       if ( iMaybeOverrideAnimSlot >= 0 )
    ///           iWeaponRole = iMaybeOverrideAnimSlot;                    // :4192-4196
    /// </code>
    ///
    /// **Two classes answer before or instead of that.** <c>CTFKatana::GetActivityWeaponRole</c> returns ITEM1 for a
    /// demoman without asking the item (<c>tf_weapon_sword.cpp:577-587</c>), and the two weapons that replace
    /// <c>ActivityList</c> never ask for a role at all — see <see cref="OwnActivityLists"/>.
    /// </remarks>
    public string Suffix(string? serverClass, int? playerClass, int animSlot)
    {
        if (string.Equals(serverClass, Katana, StringComparison.Ordinal) && playerClass == Demoman)
        {
            return ActivityList(ItemOne);
        }

        return animSlot >= 0 && !OwnActivityLists.Contains(serverClass ?? string.Empty)
            ? ActivityList(animSlot)
            : Suffix(serverClass, playerClass);
    }
}
