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
    public void UpdateId_TheWeaponHasNoItem_SaysHealingIsBlocked() =>
        // `GetAttributeContainer()->GetItem()` fails (:9819): no definition index arrived for the weapon.
        DataText(BlocksHealing, definition: null).ShouldBe("Healing is blocked!");

    [Test]
    public void UpdateId_AWeaponThatAllowsHealing_ShowsNoLine() =>
        DataText((_, _, _, initial) => initial, definition: 228).ShouldBe(string.Empty);

    [Test]
    public void UpdateId_NoHealAndAKillStreak_ShowsTheStreak()
    {
        // `if ( !bIsAmmoData )` (:9844) runs after the no-heal line and overwrites it.
        DataText(BlocksHealing, definition: 228, killStreak: 4).ShouldBe("Streak 4");
    }

    private static float BlocksHealing(ScenePlayer owner, SceneItem weapon, string attribute, float initial) =>
        attribute == "weapon_blocks_healing" ? 1f : initial;

    private static string DataText(
        Func<ScenePlayer, SceneItem, string, float, float> weaponAttribute, int? definition, int? killStreak = null)
    {
        TfMainTargetId id = Built(weaponAttribute);
        HudState state = new(
            true, true, 0, 150, true, CurTime: 1f, Team: 2, LocalIndex: 1, PlayerClass: 5, IdTarget: 2,
            Players:
            [
                new(1, 0f, 0f, 0f, 2, 150, 5),
                new(2, 100f, 0f, 0f, 2, 200, 3, ActiveWeapon: Weapon)
                {
                    Items = [new SceneItem(Weapon, "CTFRocketLauncher", definition, new EconAttributeWire([], [], false), IsWeapon: true)],
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
            ItemName = (definition, _) => definition == 228 ? "Black Box" : "?",
        };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);

        return id;
    }
}
