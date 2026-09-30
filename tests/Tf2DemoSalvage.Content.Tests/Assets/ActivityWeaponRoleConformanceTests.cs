using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which table a held weapon animates the body with — `CTFWeaponBase::GetActivityWeaponRole` and the `ActivityList`
/// switch it feeds (B105).
/// </summary>
/// <remarks>
/// **The engine, written down before the port, with its lines:**
///
/// <code>
///   int CTFWeaponBase::GetActivityWeaponRole() const                          // tf_weaponbase.cpp:4185
///   {
///       int iWeaponRole = GetTFWpnData().m_iWeaponType;                       // the script's WeaponType
///       const CEconItemView *pEconItemView = GetAttributeContainer()->GetItem();
///       if ( pEconItemView )
///       {
///           int iMaybeOverrideAnimSlot = pEconItemView->GetAnimationSlot();      // :4192
///           if ( iMaybeOverrideAnimSlot >= 0 )
///               iWeaponRole = iMaybeOverrideAnimSlot;
///       }
///       if ( mp_forceactivityset.GetInt() >= 0 ) ...                           // FCVAR_DEVELOPMENTONLY, -1 in every build
///       return iWeaponRole;
///   }
/// </code>
///
/// `GetAnimationSlot` is the definition's `m_iAnimationSlot` (econ_item_view.cpp:1093-1103): -1 from
/// `InternalInitialize` (tf_item_schema.cpp:893) unless `anim_slot` names an entry of `g_szWeaponTypeSubstrings`
/// (:1014-1026, :1591), and -2 for `FORCE_NOT_USED` — which the `>= 0` gate treats exactly as it treats an item that
/// says nothing. `ActivityList` (tf_weaponbase.cpp:4208) then switches the role onto one of twelve tables, with
/// `TF_WPN_TYPE_PRIMARY` sharing its body with `default:`.
///
/// **Two overrides sit either side of it, and both are the engine's.** `CTFKatana::GetActivityWeaponRole`
/// (tf_weapon_sword.cpp:577-587) answers ITEM1 for a demoman before the item is asked, and `CTFGrapplingHook` and
/// `CPasstimeGun` replace `ActivityList` itself (tf_weapon_grapplinghook.cpp:150, tf_weapon_passtime_gun.cpp:304), so
/// while either has an owner the role — and with it the item's slot — is never asked at all.
/// </remarks>
public sealed class ActivityWeaponRoleConformanceTests
{
    /// <summary>Where `ActivityList` and its twelve tables live.</summary>
    private const string WeaponBase = "src/game/shared/tf/tf_weaponbase.cpp";

    /// <summary>Where `TF_WPN_TYPE_*` is numbered.</summary>
    private const string ItemConstants = "src/game/shared/tf/tf_item_constants.h";

    /// <summary><c>TF_CLASS_DEMOMAN</c>, <c>tf_shareddefs.h:210</c>.</summary>
    private const int Demoman = 4;

    /// <summary><c>TF_CLASS_SOLDIER</c>, <c>tf_shareddefs.h:209</c>.</summary>
    private const int Soldier = 3;

    /// <remarks>
    /// **Read out of the SDK rather than typed**, the arrangement `WeaponActivityConformanceTests` keeps for the tables
    /// themselves: the enum gives each `TF_WPN_TYPE_*` its number, `ActivityList`'s `case` labels give each number its
    /// table, and a type no label names falls to `default:` — the primary table. Every one of the sixteen is asked,
    /// including the four the switch never mentions (GRENADE, HEAD, MISC and Valve's misspelt `TF_WPM_TYPE_PASSTIME_BALL`).
    ///
    /// **The names must also be tables `WeaponActivityTable` holds**, because that is the lookup the role feeds: a role
    /// spelled differently there — `MELEE_ALLCLASS` against the table's `MELEEALLCLASS` — rewrites no activity at all.
    /// </remarks>
    [Test]
    public void ActivityList_EveryWeaponType_IsTheTableTheEnginesSwitchPicks()
    {
        if (SourceSdk.Text(ItemConstants) is not { } constants || SourceSdk.Text(WeaponBase) is not { } weaponBase)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        List<string> types = WeaponTypes(constants);
        (Dictionary<string, string> cases, string fallback) = Switch(weaponBase);

        // The controls: an extraction that found nothing would agree with anything.
        types.Count.ShouldBe(16, "TF_WPN_TYPE_PRIMARY through TF_WPM_TYPE_PASSTIME_BALL, tf_item_constants.h:19-34");
        cases.Count.ShouldBe(12, "ActivityList names twelve tables, tf_weaponbase.cpp:4226-4276");
        fallback.ShouldBe("PRIMARY", "`case TF_WPN_TYPE_PRIMARY: default:` share a body");

        for (int type = 0; type < types.Count; type++)
        {
            string expected = cases.GetValueOrDefault(types[type], fallback);

            WeaponRoles.ActivityList(type).ShouldBe(expected, $"{types[type]} is {type}");
            WeaponActivityTable.Roles.ShouldContain(expected, $"{expected} must be a table WeaponActivityTable holds");
        }
    }

    /// <remarks>
    /// **`>= 0` is the whole gate.** -1 (no `anim_slot`, or no definition at all) and -2 (`FORCE_NOT_USED`) keep the
    /// script's `WeaponType`; anything else replaces it and is then switched like any role — so a GRENADE or
    /// PASSTIME_BALL slot lands on the primary table by `default:`, exactly as a script typed that way would.
    /// </remarks>
    [TestCase(-1, "PRIMARY")]
    [TestCase(-2, "PRIMARY")]
    [TestCase(0, "PRIMARY")]
    [TestCase(1, "SECONDARY")]
    [TestCase(2, "MELEE")]
    [TestCase(3, "PRIMARY")]
    [TestCase(6, "ITEM1")]
    [TestCase(7, "ITEM2")]
    [TestCase(10, "MELEEALLCLASS")]
    [TestCase(11, "SECONDARY2")]
    [TestCase(12, "PRIMARY2")]
    [TestCase(14, "ITEM4")]
    [TestCase(15, "PRIMARY")]
    public void Suffix_AnItemsAnimSlot_ReplacesTheScriptsWeaponType(int animSlot, string expected)
    {
        // A grenade launcher, whose script says primary — so every row but the first two, and the three the switch
        // sends to `default:`, can only pass by reading the slot.
        WeaponRoles roles = Roles(("CTFGrenadeLauncher", Demoman));

        roles.Suffix("CTFGrenadeLauncher", Demoman, animSlot).ShouldBe(expected);
    }

    /// <remarks>
    /// **The control for the rows above**: the item-less question still answers the script, so the overload is an
    /// override of the script's answer and not a replacement for the lookup.
    /// </remarks>
    [Test]
    public void Suffix_WithNoItem_IsTheScriptsWeaponType()
    {
        WeaponRoles roles = Roles(("CTFGrenadeLauncher", Demoman));

        roles.Suffix("CTFGrenadeLauncher", Demoman).ShouldBe("PRIMARY");
    }

    /// <remarks>
    /// **`CTFKatana` overrides the role before the item is asked** (tf_weapon_sword.cpp:577-587): a demoman's katana is
    /// ITEM1 whatever its item says, and anyone else's falls to the base — the script, then the item's slot. The
    /// soldier rows are the control: without them "always ITEM1" passes.
    /// </remarks>
    [TestCase(Demoman, -1, "ITEM1")]
    [TestCase(Demoman, 10, "ITEM1")]
    [TestCase(Soldier, -1, "MELEE")]
    [TestCase(Soldier, 10, "MELEEALLCLASS")]
    public void Suffix_AKatana_IsItem1OnlyInADemomansHands(int holder, int animSlot, string expected)
    {
        WeaponRoles roles = Roles(("CTFKatana", holder));

        roles.Suffix("CTFKatana", holder, animSlot).ShouldBe(expected);
    }

    /// <remarks>
    /// **Neither class ever asks for its role while it has an owner** — both replace `ActivityList` and return their own
    /// tables (tf_weapon_grapplinghook.cpp:150-168, tf_weapon_passtime_gun.cpp:304-310). So the item's slot — the
    /// grappling hook's `MELEE_ALLCLASS`, the passtime gun's `PASSTIME_BALL` — changes nothing about either, and the
    /// answer stays whatever it was without the item.
    /// </remarks>
    [TestCase("CTFGrapplingHook", 10)]
    [TestCase("CPasstimeGun", 15)]
    public void Suffix_AWeaponThatReplacesActivityList_NeverReadsTheItemsSlot(string weapon, int animSlot)
    {
        WeaponRoles roles = Roles((weapon, Soldier));

        roles.Suffix(weapon, Soldier, animSlot).ShouldBe(roles.Suffix(weapon, Soldier));
    }

    /// <remarks>
    /// **An item's own empty `anim_slot` hides its prefab's.** `MergeDefinitionPrefab` applies the prefabs and then
    /// `RecursiveInheritKeyValues` sets every one of the item's own keys over them, an empty string included
    /// (econ_item_schema.cpp:2909, :2967); `BInitFromKV` then skips the empty value (tf_item_schema.cpp:1016) and the
    /// slot stays at -1. The Half-Zatoichi is the shipped case — `"prefab" "weapon_sword"`, whose slot is `item1`, and
    /// `"anim_slot" ""` of its own — so a soldier's katana takes the script's melee rather than the sword prefab's ITEM1.
    /// The Eyelander, the same prefab and no key of its own, is the control.
    /// </remarks>
    [Test]
    public void AnimSlot_AnItemsOwnEmptySlot_HidesItsPrefabs()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes("""
            "items_game"
            {
                "prefabs"
                {
                    "weapon_sword" { "item_slot" "melee" "anim_slot" "item1" }
                }
                "items"
                {
                    "132" { "prefab" "weapon_sword" }
                    "357" { "prefab" "weapon_sword" "anim_slot" "" }
                }
            }
            """));

        schema.AnimSlot(132).ShouldBe(6, "ITEM1, inherited from the prefab");
        schema.AnimSlot(357).ShouldBe(-1, "the item's own empty value, which BInitFromKV skips");
    }

    /// <summary>Weapon scripts as the game ships them, one key each, keyed by the path WeaponScript asks for.</summary>
    private static readonly Dictionary<string, string> Scripts = new(StringComparer.Ordinal)
    {
        ["scripts/tf_weapon_grenadelauncher.txt"] = Script("primary"),
        ["scripts/tf_weapon_katana.txt"] = Script("melee"),
        ["scripts/tf_weapon_grapplinghook.txt"] = Script("item1"),

        // Measured on the shipped script: "utility", which tf_weapon_parse.cpp:134-167 does not recognise, so
        // m_iWeaponType keeps the constructor's TF_WPN_TYPE_PRIMARY (:51).
        ["scripts/tf_weapon_passtime_gun.txt"] = Script("utility"),
    };

    /// <summary>A script declaring one weapon type.</summary>
    private static string Script(string type) => $"WeaponData\n{{\n\t\"WeaponType\"\t\"{type}\"\n}}\n";

    /// <summary>The roles for the weapons asked about, off the scripts above.</summary>
    private static WeaponRoles Roles(params (string Weapon, int? Class)[] held) =>
        WeaponRoles.Read(path => Scripts.TryGetValue(path, out string? text) ? Encoding.ASCII.GetBytes(text) : null, held);

    /// <summary>The `TF_WPN_TYPE_*` enumerators in order, so each one's index is its value.</summary>
    private static List<string> WeaponTypes(string constants)
    {
        Match block = Regex.Match(
            constants, @"enum\s*\{(?<body>[^}]*TF_WPN_TYPE_PRIMARY\s*=\s*0[^}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(10));

        string body = Regex.Replace(block.Groups["body"].Value, @"//[^\n]*", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(10));

        return
        [
            .. body.Split(',')
                .Select(entry => entry.Split('=')[0].Trim())
                .Where(name => name.StartsWith("TF_WP", StringComparison.Ordinal) && name != "TF_WPN_TYPE_COUNT"),
        ];
    }

    /// <summary>`ActivityList`'s switch: each `case` label's table, and the `default:` one.</summary>
    private static (Dictionary<string, string> Cases, string Fallback) Switch(string weaponBase)
    {
        int start = weaponBase.IndexOf("acttable_t *CTFWeaponBase::ActivityList(", StringComparison.Ordinal);
        int from = weaponBase.IndexOf("switch( iWeaponRole )", start, StringComparison.Ordinal);
        int to = weaponBase.IndexOf("return pTable;", from, StringComparison.Ordinal);

        Dictionary<string, string> cases = new(StringComparer.Ordinal);
        List<string> pending = [];
        string fallback = string.Empty;

        foreach (string raw in weaponBase[from..to].Split('\n'))
        {
            string line = raw.Trim();

            if (Regex.Match(line, @"^case\s+(?<type>\w+)\s*:", RegexOptions.None, TimeSpan.FromSeconds(10)) is { Success: true } label)
            {
                pending.Add(label.Groups["type"].Value);
            }
            else if (line.StartsWith("default:", StringComparison.Ordinal))
            {
                pending.Add("default");
            }
            else if (Regex.Match(line, @"pTable\s*=\s*s_acttable(?<table>\w+)\s*;", RegexOptions.None, TimeSpan.FromSeconds(10)) is { Success: true } table)
            {
                string name = table.Groups["table"].Value.ToUpperInvariant();

                foreach (string type in pending)
                {
                    if (type == "default")
                    {
                        fallback = name;
                    }
                    else
                    {
                        cases[type] = name;
                    }
                }

                pending.Clear();
            }
        }

        return (cases, fallback);
    }
}
