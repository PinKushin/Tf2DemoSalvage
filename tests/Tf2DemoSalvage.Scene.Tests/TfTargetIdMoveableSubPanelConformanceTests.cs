using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The moveable sub-panel — `CTargetID::UpdateID`'s icon-and-command switch (tf_hud_target_id.cpp:885-925) and its own
/// slice of `PerformLayout` (:658-684) — plus `CTFPlayer::CanPickupBuilding` (tf_player_shared.cpp:12421).
/// </summary>
/// <remarks>
/// The local player is entity 1 "Me", RED, alive, round running, not carrying, no grappling hook or knockout rune.
/// Every fixture here builds its own building 5 units from the local player's own origin — inside
/// <c>TF_BUILDING_PICKUP_RANGE</c> (150) — unless a test names a different position.
/// </remarks>
public sealed class TfTargetIdMoveableSubPanelConformanceTests
{
    [Test]
    public void UpdateId_AnOwnDispenserInRange_ShowsThePickupPromptWithItsIcon()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding()) with { IdTarget = 55, BuildingPickupKey = "MOUSE2" });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeTrue("+attack2 is offered: nothing here blocks the pickup");
        ((VguiIconPanel)id.FindChildByName("MoveableIcon", recurseDown: true)!).Icon!.TextureFile.ShouldBe("vgui/hud/obj_status_dispenser.vmt");
        ((VguiEditablePanel)id.FindChildByName("MoveableSubPanel")!).DialogVariable("movekey").ShouldBe("MOUSE2");
    }

    [Test]
    public void UpdateId_NoKeyBound_LeavesTheDialogVariableEmpty()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding()) with { IdTarget = 55, BuildingPickupKey = null });

        ((VguiEditablePanel)id.FindChildByName("MoveableSubPanel")!).DialogVariable("movekey").ShouldBe(string.Empty);
    }

    [Test]
    public void UpdateId_ASentryLevelOne_ShowsSentryOne() =>
        Icon(OwnBuilding() with { ObjectType = SceneBuilding.Sentrygun, UpgradeLevel = 1 }).ShouldBe("obj_status_sentrygun_1.vmt");

    [Test]
    public void UpdateId_ASentryLevelTwo_ShowsSentryTwo() =>
        Icon(OwnBuilding() with { ObjectType = SceneBuilding.Sentrygun, UpgradeLevel = 2 }).ShouldBe("obj_status_sentrygun_2.vmt");

    [Test]
    public void UpdateId_ASentryLevelThree_ShowsSentryThree() =>
        Icon(OwnBuilding() with { ObjectType = SceneBuilding.Sentrygun, UpgradeLevel = 3 }).ShouldBe("obj_status_sentrygun_3.vmt");

    [Test]
    public void UpdateId_ATeleporterEntrance_ShowsTheEntranceIcon() =>
        Icon(OwnBuilding() with { ObjectType = SceneBuilding.Teleporter, ObjectMode = SceneBuilding.TeleporterEntrance })
            .ShouldBe("obj_status_tele_entrance.vmt");

    [Test]
    public void UpdateId_ATeleporterExit_ShowsTheExitIcon() =>
        Icon(OwnBuilding() with { ObjectType = SceneBuilding.Teleporter, ObjectMode = SceneBuilding.TeleporterExit })
            .ShouldBe("obj_status_tele_exit.vmt");

    [Test]
    public void UpdateId_SomeoneElsesBuilding_ShowsNoIconAndNoPrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding() with { BuilderEntityIndex = 9 }) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("only the builder's own HUD gets the prompt (:886)");
        ((VguiIconPanel)id.FindChildByName("MoveableIcon", recurseDown: true)!).Visible.ShouldBeFalse();
    }

    [Test]
    public void CanPickupBuilding_StillUnderConstruction_HidesThePrompt() => PromptHidden(OwnBuilding() with { Building = true });

    [Test]
    public void CanPickupBuilding_ASentryMidUpgrade_HidesThePrompt() =>
        PromptHidden(OwnBuilding() with { ObjectType = SceneBuilding.Sentrygun, SentryState = SceneBuilding.SentryStateUpgrading });

    [Test]
    public void CanPickupBuilding_Sapped_HidesThePrompt() => PromptHidden(OwnBuilding() with { Sapped = true });

    [Test]
    public void CanPickupBuilding_PlasmaDisabled_HidesThePrompt() => PromptHidden(OwnBuilding() with { PlasmaDisabled = true });

    [Test]
    public void CanPickupBuilding_BelowItsHighestUpgradeLevel_HidesThePrompt() =>
        PromptHidden(OwnBuilding() with { UpgradeLevel = 1, HighestUpgradeLevel = 2 });

    [Test]
    public void CanPickupBuilding_AtItsHighestUpgradeLevel_ShowsThePrompt() =>
        PromptShown(OwnBuilding() with { UpgradeLevel = 2, HighestUpgradeLevel = 2 });

    [Test]
    public void CanPickupBuilding_TheLocalPlayerIsDead_HidesThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding(), meAlive: false) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("!IsAlive() (:12442)");
    }

    [Test]
    public void CanPickupBuilding_TheLocalPlayerIsCarryingAnObject_HidesThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding(), meCarrying: true) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("IsCarryingObject() (:12445)");
    }

    [Test]
    public void CanPickupBuilding_RoundStateIsGameOver_HidesThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding()) with { IdTarget = 55, RoundState = 8 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("GR_STATE_GAME_OVER is none of RND_RUNNING/STALEMATE/BETWEEN_RNDS (:12452)");
    }

    [Test]
    public void CanPickupBuilding_RoundStateIsStalemate_ShowsThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding()) with { IdTarget = 55, RoundState = HudState.RoundStateStalemate });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeTrue();
    }

    [Test]
    public void CanPickupBuilding_GrapplingHook_HidesThePrompt()
    {
        PlayerConditions grappling = new(0, 0, 0, 1 << (98 - 96), 0); // TF_COND_GRAPPLINGHOOK 98, in Ex3.

        TfMainTargetId id = Thought(Playing(OwnBuilding(), meConditions: grappling) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("TF_COND_GRAPPLINGHOOK (:12457)");
    }

    [Test]
    public void CanPickupBuilding_KnockoutRune_HidesThePrompt()
    {
        PlayerConditions knockout = new(0, 0, 0, 1 << (103 - 96), 0); // TF_COND_RUNE_KNOCKOUT 103, in Ex3.

        TfMainTargetId id = Thought(Playing(OwnBuilding(), meConditions: knockout) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("GetCarryingRuneType() == RUNE_KNOCKOUT (:12464)");
    }

    [Test]
    public void CanPickupBuilding_OutOfRange_HidesThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding() with { Position = (1000f, 0f, 0f) }) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("beyond TF_BUILDING_PICKUP_RANGE, 150 (:12475)");
    }

    [Test]
    public void CanPickupBuilding_AtExactlyTheRangeBoundary_ShowsThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding() with { Position = (150f, 0f, 0f) }) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeTrue("150 units is `<=`, not `<`");
    }

    [Test]
    public void CanPickupBuilding_NoOriginSent_HidesThePrompt()
    {
        TfMainTargetId id = Thought(Playing(OwnBuilding() with { Position = null }) with { IdTarget = 55 });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("a range this project cannot measure is not one it can pass");
    }

    [Test]
    public void PerformLayout_ThePromptVisible_AddsItsWidthToThePanel()
    {
        TfMainTargetId shown = Thought(Playing(OwnBuilding()) with { IdTarget = 55, BuildingPickupKey = "MOUSE2" });
        VguiLayout.SolveTraverse(shown.Parent!, ((HudViewport)shown.Parent!).Context!);
        int wideWithPrompt = shown.Wide;

        TfMainTargetId hidden = Thought(Playing(OwnBuilding() with { BuilderEntityIndex = 9 }) with { IdTarget = 55 });
        VguiLayout.SolveTraverse(hidden.Parent!, ((HudViewport)hidden.Parent!).Context!);

        wideWithPrompt.ShouldBeGreaterThan(hidden.Wide, "the sub-panel's own width is added at :680");
    }

    private static string? Icon(SceneBuilding building)
    {
        TfMainTargetId id = Thought(Playing(building) with { IdTarget = 55 });

        return ((VguiIconPanel)id.FindChildByName("MoveableIcon", recurseDown: true)!).Icon?.TextureFile;
    }

    private static void PromptHidden(SceneBuilding building) =>
        Thought(Playing(building) with { IdTarget = 55 }).FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse();

    private static void PromptShown(SceneBuilding building) =>
        Thought(Playing(building) with { IdTarget = 55 }).FindChildByName("MoveableSubPanel")!.Visible.ShouldBeTrue();

    /// <summary>A dispenser 5 units off the local player's own origin, built by the local player himself.</summary>
    private static SceneBuilding OwnBuilding() => new(55)
    {
        Health = 100,
        MaxHealth = 100,
        ObjectType = SceneBuilding.Dispenser,
        Team = 2,
        BuilderEntityIndex = 1,
        UpgradeLevel = 1,
        HighestUpgradeLevel = 1,
        Position = (5f, 0f, 0f),
    };

    private static HudState Playing(
        SceneBuilding building, bool meAlive = true, bool meCarrying = false, PlayerConditions meConditions = default) =>
        new(true, true, 0, 100, true, CurTime: 1f, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1, PlayerClass: 3,
            RoundState: 4, // GR_STATE_RND_RUNNING.
            Players: [new(1, 0f, 0f, 0f, 2, 100, 3, LifeState: meAlive ? 0 : 1, Conditions: meConditions) { CarryingObject = meCarrying }],
            Names: new Dictionary<int, string> { [1] = "Me" },
            Buildings: [building]);

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
                    "MoveableSubPanel"
                    {
                        "ControlName" "EditablePanel"
                        "fieldName" "MoveableSubPanel"
                        "wide" "40"
                        "tall" "40"
                        "visible" "0"
                        "MoveableIcon" { "ControlName" "CIconPanel" "fieldName" "MoveableIcon" "wide" "20" "tall" "20" "visible" "0" }
                        "MoveableSymbolIcon" { "ControlName" "ImagePanel" "fieldName" "MoveableSymbolIcon" "wide" "10" "tall" "10" }
                        "MoveableIconBG" { "ControlName" "CIconPanel" "fieldName" "MoveableIconBG" "wide" "40" "tall" "40" }
                        "MoveableKeyLabel" { "ControlName" "Label" "fieldName" "MoveableKeyLabel" "wide" "20" "tall" "10" "labelText" "%movekey%" }
                    }
                }
                """),
            ["scripts/hud_textures.txt"] = Encoding.UTF8.GetBytes("""
                "sprites/640_hud"
                {
                    TextureData
                    {
                        "obj_status_dispenser" { "file" "vgui/hud/obj_status_dispenser.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                        "obj_status_tele_entrance" { "file" "obj_status_tele_entrance.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                        "obj_status_tele_exit" { "file" "obj_status_tele_exit.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                        "obj_status_sentrygun_1" { "file" "obj_status_sentrygun_1.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                        "obj_status_sentrygun_2" { "file" "obj_status_sentrygun_2.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                        "obj_status_sentrygun_3" { "file" "obj_status_sentrygun_3.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                    }
                }
                """),
            ["scripts/playerclasses/heavyweapons.txt"] = Encoding.UTF8.GetBytes("\"PlayerClass\"\n{\n\t\"health\"\t\"300\"\n}\n"),
            ["scripts/objects.txt"] = Encoding.UTF8.GetBytes("""
                "Objects"
                {
                    "OBJ_DISPENSER" { "StatusName" "#TF_Object_Dispenser" }
                    "OBJ_TELEPORTER" { "StatusName" "#TF_Object_Tele" }
                    "OBJ_SENTRYGUN" { "StatusName" "#TF_Object_Sentry" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_object"] = "%s1 built by %s2",
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
            Icons = HudTextures.Load(context),
        };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);
        viewport.Think(Playing(OwnBuilding()) with { IdTarget = 0 });

        return id;
    }
}
