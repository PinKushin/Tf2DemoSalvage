using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `C_TFPlayer::GetTargetIDDataString`'s `weapon_blocks_healing` line (c_tf_player.cpp:9811-9827): a local medic looking at
/// a teammate whose active weapon blocks healing.
/// </summary>
public sealed class TfTargetIdNoHealConformanceTests
{
    private const int Weapon = 30;

    [Test]
    public void UpdateId_ALocalMedicAtAWeaponThatBlocksHealing_NamesTheWeapon() =>
        DataText(BlocksHealing, definition: 228).ShouldBe("Black Box blocks healing!");

    [Test]
    public void UpdateId_AnOwnedKillstreakWeaponThatBlocksHealing_NamesItWithItsPrefix() =>
        // `GetItemName()` (:9821) is the description's name line (econ_item_view.cpp:1131-1145), built from the item's own
        // attributes: tier 1 is "ItemNameKillStreakv0" (econ_item_description.cpp), and with no backpack the quality is
        // `AE_NORMAL`, so no article (econ_item_view.cpp:446-456).
        DataText(BlocksHealing, definition: 228, attributes: [new EconAttributeValue(2025, BitConverter.SingleToInt32Bits(1f))])
            .ShouldBe("Killstreak Black Box blocks healing!");

    [Test]
    public void UpdateId_AWeaponWithNoDefinition_NamesItEmpty() =>
        // `GetAttributeContainer()->GetItem()` (:9819) is the weapon's embedded `m_Item`, never null, so the unknown line is
        // never reached; with no definition the name is "" (econ_item_description.cpp:628-632).
        DataText(BlocksHealing, definition: null).ShouldBe(" blocks healing!");

    [Test]
    public void UpdateId_AWeaponThatAllowsHealing_ShowsNoLine() =>
        DataText((_, _, _, initial) => initial, definition: 228).ShouldBe(string.Empty);

    [Test]
    public void UpdateId_NoHealAndAKillStreak_ShowsTheStreak()
    {
        // `if ( !bIsAmmoData )` (:9844) runs after the no-heal line and overwrites it.
        DataText(BlocksHealing, definition: 228, killStreak: 4).ShouldBe("Streak 4");
    }

    /// <remarks>
    /// A medic target's charge line (c_tf_player.cpp:9783-9800): a medigun whose `GetItemQuality()` is not `AE_NORMAL` is named
    /// by `GetItemName()`, so with its own killstreak prefix and, without a backpack, no article; a normal one is not named.
    /// </remarks>
    [TestCase(6, 6, "50% with Killstreak Black Box")]
    [TestCase(0, 0, "50% charge")]
    [TestCase(6, 0, "50% with Killstreak Black Box")]
    [TestCase(0, 6, "50% charge")]
    public void UpdateId_AMedicTargetWithAMedigun_NamesItOnlyWhenItIsNotNormal(int quality, int tupleQuality, string expected)
    {
        // The gate is the medigun ITEM's own `GetItemQuality()` (:9792), not the copy `Medigun` carries for the charge.
        TfMainTargetId id = Built((_, _, _, initial) => initial);
        SceneItem medigun = new(31, "CWeaponMedigun", 228, new EconAttributeWire([], [new EconAttributeValue(2025, BitConverter.SingleToInt32Bits(1f))], true), IsWeapon: true)
        {
            Quality = quality,
            ItemId = 77ul,
        };
        HudState state = new(
            true, true, 0, 150, true, CurTime: 1f, Team: 2, LocalIndex: 1, PlayerClass: 3, IdTarget: 2,
            Players:
            [
                new(1, 0f, 0f, 0f, 2, 150, 3),
                new(2, 100f, 0f, 0f, 2, 150, 5, ActiveWeapon: 31) { Items = [medigun], Medigun = (0.5f, tupleQuality, 228) },
            ],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Mate" });

        ((HudViewport)id.Parent!).Think(state);

        id.TargetData.ShouldBe(expected);
    }

    private static float BlocksHealing(ScenePlayer owner, SceneItem weapon, string attribute, float initial) =>
        attribute == "weapon_blocks_healing" ? 1f : initial;

    private static string DataText(
        Func<ScenePlayer, SceneItem, string, float, float> weaponAttribute, int? definition, int? killStreak = null, EconAttributeValue[]? attributes = null)
    {
        bool owned = attributes is not null;
        TfMainTargetId id = Built(weaponAttribute);
        HudState state = new(
            true, true, 0, 150, true, CurTime: 1f, Team: 2, LocalIndex: 1, PlayerClass: 5, IdTarget: 2,
            Players:
            [
                new(1, 0f, 0f, 0f, 2, 150, 5),
                new(2, 100f, 0f, 0f, 2, 200, 3, ActiveWeapon: Weapon)
                {
                    Items =
                    [
                        new SceneItem(Weapon, "CTFRocketLauncher", definition, new EconAttributeWire([], attributes ?? [], owned), IsWeapon: true)
                        {
                            Quality = 6,
                            ItemId = owned ? 77ul : SceneItem.InvalidItemId,
                        },
                    ],
                    KillStreak = killStreak,
                },
            ],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Mate" });

        ((HudViewport)id.Parent!).Think(state);

        return id.TargetData;
    }

    private static TfMainTargetId Built(Func<ScenePlayer, SceneItem, string, float, float> weaponAttribute)
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/TargetID.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/TargetID.res"
                {
                    "TargetIDBG" { "ControlName" "CTFImagePanel" "fieldName" "TargetIDBG" "wide" "100" "tall" "39" }
                    "SpectatorGUIHealth" { "fieldName" "SpectatorGUIHealth" "wide" "40" "tall" "40" }
                    "TargetNameLabel" { "ControlName" "Label" "fieldName" "TargetNameLabel" "wide" "720" "tall" "27" "labelText" "%targetname%" }
                    "TargetDataLabel" { "ControlName" "Label" "fieldName" "TargetDataLabel" "wide" "315" "tall" "17" "labelText" "%targetdata%" }
                }
                """),
            ["scripts/playerclasses/soldier.txt"] = Encoding.UTF8.GetBytes("\"PlayerClass\"\n{\n\t\"health\"\t\"200\"\n}\n"),
        };

        // As tf_english.txt ships them, but the streak line, which the test only needs to tell apart.
        Dictionary<string, string> strings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_sameteam"] = "%s1%s2",
            ["TF_playerid_noheal"] = "%s1 blocks healing!",
            ["TF_playerid_noheal_unknown"] = "Healing is blocked!",
            ["TF_playerid_ammo"] = "Streak %s1",
            ["TF_playerid_mediccharge"] = "%s1% charge",
            ["TF_playerid_mediccharge_wpn"] = "%s1% with %s2",
            ["ItemNameFormat"] = "%s1%s2",
            ["ItemNameNormalOrUniqueQualityFormat"] = "%s1%s2%s3%s4%s5",
            ["TF_Unique_Prepend_Proper"] = "The ",
            ["ItemNameKillStreakv0"] = "Killstreak ",
            ["BlackBox"] = "Black Box",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new()
        {
            Wide = 640,
            Tall = 480,
            Context = context,
            Scripts = new TfWeaponData(files.GetValueOrDefault),
            WeaponAttribute = weaponAttribute,
            // A proper name, so a wrongly unique quality would show as "The".
            Items = ItemSchema.Read(Encoding.UTF8.GetBytes("""
                "items_game"
                {
                    "items" { "228" { "name" "The Black Box" "item_name" "#BlackBox" "propername" "1" } }
                    "attributes" { "2025" { "name" "killstreak tier" "hidden" "1" } }
                }
                """)),
        };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);

        return id;
    }
}
