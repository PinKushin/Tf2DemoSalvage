using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTargetID::IsValidIDTarget` (:340), `UpdateID` (:719) and `PerformLayout` (:599), through `CMainTargetID`.</summary>
/// <remarks>
/// The local player is entity 1 "Me", RED, not observing, a soldier unless a test says otherwise. A 640 × 480 screen; the
/// health panel is 40 wide. Class 6's script says 300 health.
/// </remarks>
public sealed class TfTargetIdUpdateIdConformanceTests
{
    [Test]
    public void ShouldDraw_AnEnemySpyDisguisedAsOurTeam_ShowsHisDisguiseTargetAndTheDisguiseClassHealth()
    {
        // BLU spy 2 disguised as a RED heavy, as player 5: `InSameDisguisedTeam` passes, the name becomes "Redguy", and the
        // health is 90 of the heavy script's 300 (`GetDisguiseMaxHealth`), not of the spy's own 125.
        ScenePlayer spy = new(2, 0f, 0f, 0f, 3, 125, 8, Conditions: new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
            DisguiseClass: 6, DisguiseTeam: 2)
        { EntityHealth = 125, MaxHealth = 125, DisguiseTarget = 5, DisguiseHealth = 90 };

        TfMainTargetId id = Thought(Playing(spy) with { IdTarget = 2 });

        (id.TargetName, id.TargetHealth.HealthImage.Health).ShouldBe(("Redguy", 0.3f));
    }

    [Test]
    public void ShouldDraw_AnOrdinaryEnemyToASoldier_IsNotATarget() =>
        Built().ShouldDraw(Playing(Enemy()) with { IdTarget = 2 }).ShouldBeFalse();

    [Test]
    public void ShouldDraw_ATeammate_DrawsNoHealthPanelWhileFloatingHealthIsOn()
    {
        TfMainTargetId id = Thought(Playing(Enemy() with { Team = 2 }) with { IdTarget = 2 });

        id.TargetHealth.Visible.ShouldBeFalse("DrawHealthIcon is false for a player unless floating health is disabled (:205)");
    }

    [Test]
    public void PerformLayout_APlayerWithFloatingHealth_PutsTheLabelsAtTheIndentAlone()
    {
        TfMainTargetId id = Thought(Playing(Enemy() with { Team = 2 }) with { IdTarget = 2 });

        VguiLayout.SolveTraverse(id.Parent!, ((HudViewport)id.Parent!).Context!);

        id.FindChildByName("TargetNameLabel")!.X.ShouldBe(8, "XRES( 8 ) with no health icon inside (:648)");
    }

    [Test]
    public void ShouldDraw_OurOwnSentry_NamesItAndShowsUpgradeProgressAndItsHealthInside()
    {
        TfMainTargetId id = Thought(Playing(null, Sentry(team: 2)) with { IdTarget = 55, PlayerClass = 9 });

        (id.TargetName, id.TargetData, id.TargetHealth.Visible, id.TargetHealth.Building).ShouldBe(("Sentry Gun built by Me", "Upgrade: 25 / 200", true, true));
    }

    [Test]
    public void PerformLayout_ABuilding_CountsTheHealthPanel()
    {
        TfMainTargetId id = Thought(Playing(null, Sentry(team: 2)) with { IdTarget = 55 });

        VguiLayout.SolveTraverse(id.Parent!, ((HudViewport)id.Parent!).Context!);

        id.FindChildByName("TargetNameLabel")!.X.ShouldBe(48, "XRES( 8 ) plus the 40-wide health panel");
    }

    [Test]
    public void ShouldDraw_AnEnemySentry_IsATargetOnlyToASpy()
    {
        Built().ShouldDraw(Playing(null, Sentry(team: 3)) with { IdTarget = 55 }).ShouldBeFalse();
        Thought(Playing(null, Sentry(team: 3)) with { IdTarget = 55, PlayerClass = 8 }).TargetName.ShouldBe("Sentry Gun built by Me");
    }

    [Test]
    public void ShouldDraw_ALevelTwoDispenser_ShowsItsLevelAndProgress() =>
        Thought(Playing(null, Sentry(team: 2) with { ObjectType = 0, UpgradeLevel = 2 }) with { IdTarget = 55 })
            .TargetData.ShouldBe("Level 2 25 / 200");

    [Test]
    public void ShouldDraw_ALevelThreeSentry_ShowsNoDataLine() =>
        Thought(Playing(null, Sentry(team: 2) with { UpgradeLevel = 3 }) with { IdTarget = 55 }).TargetData.ShouldBe(string.Empty);

    [Test]
    public void ShouldDraw_ARechargingTeleporterExit_ShowsTheModeAndTheRechargeBeforeTheLevel()
    {
        SceneBuilding exit = Sentry(team: 2) with
        {
            ObjectType = 1,
            ObjectMode = 1,
            TeleporterState = 4,
            TeleporterRechargeTime = 15f,
            TeleporterRechargeDuration = 10f,
        };

        TfMainTargetId id = Thought(Playing(null, exit) with { IdTarget = 55, ServerTime = 10f });

        (id.TargetName, id.TargetData).ShouldBe(("Exit (Exit mode) built by Me", "Recharging 50   Level 1 25 / 200"));
    }

    private static ScenePlayer Enemy() => new(2, 0f, 0f, 0f, 3, 200, 3) { EntityHealth = 200, MaxHealth = 200, MaxHealthForBuffing = 200 };

    private static SceneBuilding Sentry(int team) => new(55)
    {
        Health = 100,
        MaxHealth = 150,
        ObjectType = 2,
        Team = team,
        BuilderEntityIndex = 1,
        UpgradeLevel = 1,
        UpgradeMetal = 25,
        UpgradeMetalRequired = 200,
    };

    private static HudState Playing(ScenePlayer? target, SceneBuilding? building = null) =>
        new(true, true, 0, 100, true, CurTime: 1f, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1, PlayerClass: 3,
            Players: target is { } player ? [new(1, 0f, 0f, 0f, 2, 100, 3), player] : [new(1, 0f, 0f, 0f, 2, 100, 3)],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Blue", [5] = "Redguy" },
            Buildings: building is { } obj ? [obj] : null);

    private static TfMainTargetId Thought(HudState state)
    {
        TfMainTargetId id = Built();

        ((HudViewport)id.Parent!).Think(state);
        id.Visible.ShouldBeTrue();
        return id;
    }

    private static TfMainTargetId Built()
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
                    "KillStreakIcon" { "ControlName" "ImagePanel" "fieldName" "KillStreakIcon" "visible" "0" }
                }
                """),
            ["scripts/playerclasses/heavyweapons.txt"] = Encoding.UTF8.GetBytes("\"PlayerClass\"\n{\n\t\"health\"\t\"300\"\n}\n"),
            ["scripts/objects.txt"] = Encoding.UTF8.GetBytes("""
                "Objects"
                {
                    "OBJ_DISPENSER" { "StatusName" "#TF_Object_Dispenser" }
                    "OBJ_TELEPORTER"
                    {
                        "StatusName" "#TF_Object_Tele"
                        "AltModes"
                        {
                            "AltMode0" { "StatusName" "#TF_Object_Tele_Entrance" "ModeName" "#TF_Teleporter_Mode_Entrance" }
                            "AltMode1" { "StatusName" "#TF_Object_Tele_Exit" "ModeName" "#TF_Teleporter_Mode_Exit" }
                        }
                    }
                    "OBJ_SENTRYGUN" { "StatusName" "#TF_Object_Sentry" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_sameteam"] = "%s1%s2",
            ["TF_playerid_object"] = "%s1 built by %s2",
            ["TF_playerid_object_mode"] = "%s1 (%s3) built by %s2",
            ["TF_playerid_object_upgrading"] = "Upgrade: %s1",
            ["TF_playerid_object_upgrading_level"] = "Level %s1 %s2",
            ["TF_playerid_object_level"] = "Level %s1",
            ["TF_playerid_object_recharging"] = "Recharging %s1",
            ["TF_Object_Sentry"] = "Sentry Gun",
            ["TF_Object_Tele_Exit"] = "Exit",
            ["TF_Teleporter_Mode_Exit"] = "Exit mode",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context, Scripts = new TfWeaponData(files.GetValueOrDefault) };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);
        viewport.Think(Playing(null));

        return id;
    }
}
