using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudPlayerClass` and `CTFClassImage` (game/client/tf/tf_hud_playerstatus.cpp:89-562, :1121).</summary>
/// <remarks>
/// A synthetic `HudPlayerClass.res` in the shipped file's shape, at 640×480 so a proportional value is itself. Every
/// glyph is 10 wide and 12 tall (<see cref="TextRecorder"/>). The local player is entity 1.
/// </remarks>
public sealed class TfHudPlayerClassConformanceTests
{
    private const int Red = 2;
    private const int Blu = 3;
    private const int Soldier = 3;
    private const int Scout = 1;
    private const int Medic = 5;
    private const int Spy = 8;
    private const int Disguised = 1 << 3;
    private const int Stealthed = 1 << 4;

    private const string Res = """
        "Resource/UI/HudPlayerClass.res"
        {
            "HudPlayerClass" { "ControlName" "EditablePanel" "xpos" "0" "ypos" "0" "wide" "f0" "tall" "480" }
            "PlayerStatusClassImage" { "ControlName" "CTFClassImage" "xpos" "25" "ypos" "r88" "zpos" "2" "wide" "75" "tall" "75" "image" "../hud/class_scoutred" "scaleImage" "1" }
            "PlayerStatusSpyImage" { "ControlName" "CTFImagePanel" "xpos" "3" "ypos" "r67" "zpos" "2" "wide" "55" "tall" "55" "image" "../hud/class_spyred" "scaleImage" "1" }
            "PlayerStatusSpyOutlineImage" { "ControlName" "CTFImagePanel" "xpos" "3" "ypos" "r67" "zpos" "7" "wide" "55" "tall" "55" "visible" "0" "image" "../hud/class_spy_outline" "scaleImage" "1" }
            "PlayerStatusClassImageBG" { "ControlName" "CTFImagePanel" "xpos" "9" "ypos" "r60" "zpos" "1" "wide" "100" "tall" "50" "image" "../hud/character_red_bg" "scaleImage" "1" }
            "classmodelpanelBG" { "ControlName" "CTFImagePanel" "xpos" "-1" "ypos" "r60" "zpos" "1" "wide" "109" "tall" "50" "visible" "1" "image" "../hud/character_red_bg_clipped" "scaleImage" "1" }
            "CarryingWeapon"
            {
                "ControlName" "EditablePanel" "xpos" "20" "ypos" "r27" "zpos" "100" "wide" "500" "tall" "28"
                "CarryingBackground" { "ControlName" "CTFImagePanel" "xpos" "0" "ypos" "0" "wide" "500" "tall" "28" "image" "../hud/color_panel_brown" "scaleImage" "1" }
                "CarryingLabel" { "ControlName" "CExLabel" "xpos" "5" "ypos" "3" "zpos" "1" "wide" "200" "tall" "12" "textAlignment" "North-West" "labelText" "%carrying%" }
                "OwnerLabel" { "ControlName" "Label" "xpos" "5" "ypos" "12" "wide" "200" "tall" "12" "textAlignment" "North-West" }
            }
        }
        """;

    [TestCase(Blu, 2, 0, "../hud/class_sniperblue")]
    [TestCase(Red, Spy, 1, "../hud/class_spyred_halfcloak")]
    [TestCase(Red, Spy, 2, "../hud/class_spyred_cloak")]
    [TestCase(0, 4, 0, "../hud/class_demored")]
    [TestCase(1, 9, 0, "../hud/class_engired")]
    [TestCase(Blu, 10, 0, "../hud/class_scoutblue")]
    public void SetClass_TeamClassAndCloak_NameTheImage(int team, int playerClass, int cloak, string image)
    {
        TfClassImage classImage = new(null, "Image");

        classImage.SetClass(team, playerClass, cloak);

        classImage.ImageName.ShouldBe(image);
    }

    [Test]
    public void SetClass_NoClass_SetsNothing()
    {
        TfClassImage classImage = new(null, "Image");
        classImage.SetImage("before");

        classImage.SetClass(Blu, 0, 0);

        classImage.ImageName.ShouldBe("before", "an empty name is not set (:1147)");
    }

    [Test]
    public void Think_WithinHalfASecond_DoesNotReadAgain()
    {
        (_, TfHudPlayerClass panel, _) = Built();

        panel.Think(State(Local(Red, Soldier), curTime: 10f));
        panel.ClassImage.ImageName.ShouldBe("../hud/class_soldierred");

        panel.Think(State(Local(Red, Scout), curTime: 10.49f));
        panel.ClassImage.ImageName.ShouldBe("../hud/class_soldierred", "m_flNextThink is 10.5");

        panel.Think(State(Local(Red, Scout), curTime: 10.5f));
        panel.ClassImage.ImageName.ShouldBe("../hud/class_scoutred", "10.5 is not > 10.5");
    }

    [Test]
    public void Think_NoModelPanel_TakesTheTwoDimensionalPath()
    {
        (_, TfHudPlayerClass panel, _) = Built();

        default(HudConVars).GetBool("cl_hud_playerclass_use_playermodel").ShouldBeTrue("defaults to 1 (:39)");
        panel.Think(State(Local(Blu, Medic), curTime: 1f));

        panel.ClassImage.Visible.ShouldBeTrue();
        panel.ClassImageBackground.Visible.ShouldBeTrue();
        panel.PlayerModelPanelBackground.Visible.ShouldBeFalse("the 2D branch hides the model panel's background (:280)");
        panel.SpyImage.Visible.ShouldBeFalse();
    }

    [Test]
    public void Think_SpyHalfwayDecloaked_ShowsTheHalfCloakImage()
    {
        (_, TfHudPlayerClass panel, _) = Built();

        // Stealthed with 0.5 s left on the SERVER's clock: invisibility 0.5 — above 0.1, not above 0.9 — cloak level 1.
        ScenePlayer spy = Local(Red, Spy, Stealthed) with { InvisChangeCompleteTime = 20.5f };

        panel.Think(State(spy, curTime: 3f) with { ServerTime = 20f });

        panel.ClassImage.ImageName.ShouldBe("../hud/class_spyred_halfcloak");
    }

    [Test]
    public void Think_SpyDisguised_ShowsTheDisguiseAndTheSpyImage()
    {
        (_, TfHudPlayerClass panel, _) = Built();

        ScenePlayer spy = Local(Red, Spy, Disguised) with { DisguiseClass = Medic, DisguiseTeam = Blu };

        // The first think records the class; `m_nClass == TF_CLASS_SPY` is then what lets a disguise alone refresh it.
        panel.Think(State(spy, curTime: 1f));

        panel.SpyImage.Visible.ShouldBeTrue();
        panel.ClassImage.ImageName.ShouldBe("../hud/class_medicblue");
        panel.CarryingWeaponPanel.Visible.ShouldBeFalse("hidden while disguised — the panels overlap (:308)");
    }

    [Test]
    public void Think_HoldingAnotherPlayersWeapon_SizesTheBackgroundToTheLongerLabel()
    {
        (HudViewport viewport, TfHudPlayerClass panel, _) = Built();
        viewport.ItemName = (definition, quality) => definition == 13 && quality == 6 ? "Gun" : null;

        ScenePlayer local = Local(Red, Soldier) with { ActiveWeapon = 30, WeaponItem = 13, WeaponAccountId = 200u, WeaponQuality = 6 };
        ScenePlayer owner = new(2, 0f, 0f, 0f, Team: Red, Health: 100, PlayerClass: Scout);
        HudState state = State(local, curTime: 1f) with
        {
            Players = [local, owner],
            AccountIds = new Dictionary<int, uint> { [1] = 100u, [2] = 200u },
            Names = new Dictionary<int, string> { [2] = "Bob" },
        };

        panel.Think(state);

        panel.CarryingWeaponPanel.Visible.ShouldBeTrue();
        panel.CarryingWeaponPanel.DialogVariable("carrying").ShouldBe("Gun");
        panel.CarryingOwnerLabel.Text.ShouldBe("Dropped by Bob");

        // "Dropped by Bob" is 14 glyphs, 140 wide, against "Gun"'s 30: 140 + 5 × 2.
        panel.CarryingBackground.Wide.ShouldBe(150);

        // The owner label's y 12 and its tall, 12, plus YRES( 2 ), 2 at 480.
        panel.CarryingBackground.Tall.ShouldBe(26);
        panel.CarryingLabel.FgColor.ShouldBe(((byte)201, (byte)188, (byte)162, (byte)255), "no rarity: TanLight (:328)");
    }

    [Test]
    public void Think_HoldingOwnWeapon_HidesTheCarryingPanel()
    {
        (_, TfHudPlayerClass panel, _) = Built();

        ScenePlayer local = Local(Red, Soldier) with { ActiveWeapon = 30, WeaponItem = 13, WeaponAccountId = 100u };
        panel.Think(State(local, curTime: 1f) with { AccountIds = new Dictionary<int, uint> { [1] = 100u } });

        panel.CarryingWeaponPanel.Visible.ShouldBeFalse();
    }

    [Test]
    public void HandleGameEvent_LocalPostInventoryApplication_HidesTheTwoDimensionalImages()
    {
        (_, TfHudPlayerClass panel, _) = Built();
        HudState state = State(Local(Red, Soldier), curTime: 1f);
        Dictionary<int, PlayerInfo> roster = new() { [1] = new PlayerInfo("Me", 7, "[U:1:1]", 1, false, false) };

        panel.Think(state);
        panel.HandleGameEvent(new SceneGameEvent(0, "post_inventory_application", new Dictionary<string, object?> { ["userid"] = 8 }, roster), state);
        panel.ClassImage.Visible.ShouldBeTrue("another player's inventory is not a refresh");

        // `UpdateModelPanel` with `m_bUsePlayerModel` on hides the old UI (:436-442) whether or not a model panel exists.
        panel.HandleGameEvent(new SceneGameEvent(0, "post_inventory_application", new Dictionary<string, object?> { ["userid"] = 7 }, roster), state);
        (panel.ClassImage.Visible, panel.ClassImageBackground.Visible, panel.SpyImage.Visible).ShouldBe((false, false, false));
    }

    [Test]
    public void LocalPlayerChangeDisguise_ASpyPuttingOnADisguise_FiresDisguised()
    {
        ScenePlayer plain = Local(Red, Spy);
        ScenePlayer disguised = Local(Red, Spy, Disguised) with { DisguiseClass = Medic };

        TfHudPlayerClass.LocalPlayerChangeDisguise(plain, disguised).ShouldBe(true);
        TfHudPlayerClass.LocalPlayerChangeDisguise(disguised, plain).ShouldBe(false);
        TfHudPlayerClass.LocalPlayerChangeDisguise(disguised, disguised).ShouldBeNull("nothing changed");
        TfHudPlayerClass.LocalPlayerChangeDisguise(plain with { PlayerClass = Soldier }, disguised).ShouldBeNull("only a spy's old class fires it");
    }

    [Test]
    public void PaintTraverse_TheHudTree_DrawsTheClassImageQuad()
    {
        (HudViewport viewport, TfHudPlayerClass _, VguiContext context) = Built();
        HudState state = State(Local(Red, Soldier), curTime: 1f);
        TextRecorder surface = new();

        viewport.Think(state);
        VguiLayout.SolveTraverse(viewport, context);
        VguiLayout.SolveTraverse(viewport, context);
        viewport.PaintTraverse(surface, context);

        // At (25, 480 − 88) 75 square, scaled to the panel: the texture then its rectangle in panel space.
        int texture = surface.Calls.IndexOf("texture vgui/../hud/class_soldierred");
        texture.ShouldBeGreaterThanOrEqualTo(0, string.Join(" | ", surface.Calls.Where(call => call.StartsWith("texture", System.StringComparison.Ordinal))));
        surface.Calls[texture + 1].ShouldBe("textured 0 0 75 75");
    }

    private static ScenePlayer Local(int team, int playerClass, int conditions = 0) =>
        new(1, 0f, 0f, 0f, Team: team, Health: 100, PlayerClass: playerClass, Conditions: new PlayerConditions(conditions, 0, 0, 0, 0));

    private static HudState State(ScenePlayer local, float curTime) =>
        new(true, true, 0, 100, true, 100, 150, curTime, local.Team ?? 0, LocalIndex: 1, Players: [local], ServerTime: curTime);

    private static (HudViewport Viewport, TfHudPlayerClass Panel, VguiContext Context) Built()
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        // `HudLayout.res` gives `HudPlayerStatus` the whole screen.
        TfHudPlayerStatus status = new(viewport) { Wide = 640, Tall = 480 };

        VguiLayout.SolveTraverse(viewport, context);
        VguiLayout.SolveTraverse(viewport, context);

        return (viewport, status.PlayerClass, context);
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    Colors { "TanLight" "201 188 162 255" }
                    BaseSettings { "Label.TextColor" "10 20 30 255" }
                    Borders { }
                    Fonts { "Default" { "1" { "name" "Arial" "tall" "12" } } }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiSchemeFonts fonts = VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), _ => null, "english", 480);
        Dictionary<string, byte[]> files = new() { ["resource/UI/HudPlayerClass.res"] = Encoding.UTF8.GetBytes(Res) };
        Dictionary<string, string> strings = new() { ["TF_WhoDropped"] = "Dropped by %s1" };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english", fonts)
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
    }
}
