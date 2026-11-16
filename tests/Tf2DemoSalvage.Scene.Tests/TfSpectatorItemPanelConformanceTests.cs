using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFSpectatorGUI::UpdateItemPanel` (tf_spectatorgui.cpp:928): the spectated player's items, one every eight seconds, in
/// the spectator GUI's `itempanel` (B756).
/// </summary>
/// <remarks>
/// A SourceTV recording following entity 2, "Spy", whose weapons are a stock knife (200), a hidden weapon (201) and a revolver
/// (202), and who wears a hat (300). `GetInspectItem` (c_tf_player.cpp:10257) walks the weapons, skipping the hidden one,
/// then the wearables. The `.res` block is the shipped `Spectator.res`'s.
/// </remarks>
public sealed class TfSpectatorItemPanelConformanceTests
{
    private const int Unique = 6;

    [Test]
    public void Update_SpectatingAPlayer_ShowsHisFirstItemEvenAStockOne()
    {
        // No quality test, unlike the freeze panel (tf_hud_freezepanel.cpp:450): the stock knife is shown.
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, Spectating(1f));

        gui.ItemPanel.Visible.ShouldBeTrue();
        gui.ItemPanel.ModelPanel!.Item!.DefinitionIndex.ShouldBe(200);
        ((VguiLabel)gui.ItemPanel.FindChildByName("ItemLabel", false)!).Text.ShouldBe("Spy is carrying:");
    }

    [Test]
    public void Update_SpectatingAPlayer_PinsThePanelToTheBottomRight()
    {
        // `SetPos( ScreenWidth() - XRES( 10 ) - wide, ScreenHeight() - YRES( 12 ) - tall )` (:1011).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, Spectating(1f));

        (gui.ItemPanel.X, gui.ItemPanel.Y).ShouldBe((640 - 10 - gui.ItemPanel.Wide, 480 - 12 - gui.ItemPanel.Tall));
    }

    [Test]
    public void Update_EveryEightSeconds_ShowsTheNextItemSkippingHiddenWeaponsThenTheWearables()
    {
        // `m_flNextItemPanelUpdate = curtime + 8.0` (:959): 200, then 202 (201 is hidden), then the hat.
        (HudViewport viewport, TfSpectatorGui gui) = Built();
        List<int?> shown = [];

        foreach (float now in (float[])[1f, 5f, 9.5f, 18f])
        {
            Frame(viewport, gui, Spectating(now));
            shown.Add(gui.ItemPanel.Visible ? gui.ItemPanel.ModelPanel!.Item?.DefinitionIndex : null);
        }

        shown.ShouldBe([200, 200, 202, 300]);
    }

    [Test]
    public void Update_AfterTheLastItem_HidesThePanelAndShowsThatPlayerNoMore()
    {
        // `GetInspectItem` wraps to the first item; the first already shown, the loop is done and the panel hides (:969-975),
        // and `m_bShownItems` keeps it hidden for that player (:962).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        foreach (float now in (float[])[1f, 9.5f, 18f, 26.5f])
        {
            Frame(viewport, gui, Spectating(now));
        }

        gui.ItemPanel.Visible.ShouldBeFalse("the loop is done");
        Frame(viewport, gui, Spectating(35f));
        gui.ItemPanel.Visible.ShouldBeFalse("shown already");
    }

    [Test]
    public void Update_WithClSpecCarriedItemsOff_HidesThePanel()
    {
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, Spectating(1f, carriedItems: "0"));

        gui.ItemPanel.Visible.ShouldBeFalse();
    }

    [Test]
    public void Update_WhileTheRoundIsWon_HidesThePanel()
    {
        // `State_Get() == GR_STATE_TEAM_WIN` (:934).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, Spectating(1f));
        gui.ItemPanel.Visible.ShouldBeTrue("the control: shown before the round is won");

        Frame(viewport, gui, Spectating(9.5f) with { RoundState = 5 });
        gui.ItemPanel.Visible.ShouldBeFalse();
    }

    [Test]
    public void Update_AnItemFromAnotherPlayersBackpack_SaysWhose()
    {
        // `GetPlayerByAccountID( GetAccountID() )` is Pyro, not the spectated Spy: "#FreezePanel_ItemOtherOwner" (:986-1005).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, Spectating(1f, knifeAccount: 300u));

        ((VguiLabel)gui.ItemPanel.FindChildByName("ItemLabel", false)!).Text.ShouldBe("Spy is carrying Pyro's:");
    }

    [Test]
    public void Update_OnAWideScreen_PinsWithTheScaledOffsetsTruncatedOnce()
    {
        // `ScreenWidth() - XRES( 10 ) - wide`: 1366 - 21.34375 - wide, truncated once (:1011); per term it would be one more.
        (HudViewport viewport, TfSpectatorGui gui) = Built(wide: 1366, tall: 768);

        Frame(viewport, gui, Spectating(1f));

        (gui.ItemPanel.X, gui.ItemPanel.Y).ShouldBe((
            (int)(1366 - (10 * (1366 / 640.0)) - gui.ItemPanel.Wide),
            (int)(768 - (12 * (768 / 480.0)) - gui.ItemPanel.Tall)));
    }

    [Test]
    public void Update_ARecordersNewObserverTarget_ShowsTheNewTargetsItemAtOnce()
    {
        // `spec_target_updated` (:906): the recorder's `m_hObserverTarget` arrives changed at t=3, inside the eight seconds,
        // and the new player's walk starts at once (:952).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, PointOfView(1f, target: 2));
        gui.ItemPanel.ModelPanel!.Item!.DefinitionIndex.ShouldBe(200);

        Frame(viewport, gui, PointOfView(3f, target: 3));
        gui.ItemPanel.Visible.ShouldBeTrue();
        gui.ItemPanel.ModelPanel.Item.DefinitionIndex.ShouldBe(400);
    }

    [Test]
    public void Update_ARecorderTargetingHimself_ShowsNothing()
    {
        // `pPlayer != pLocalPlayer` (:944).
        (HudViewport viewport, TfSpectatorGui gui) = Built();

        Frame(viewport, gui, PointOfView(1f, target: 1));

        gui.ItemPanel.Visible.ShouldBeFalse();
    }

    [Test]
    public void ShowPanel_TheFirstShowWithATarget_LeavesThePanelAsTheResHasItUntilTheNextUpdate()
    {
        // `CTFSpectatorGUI::ShowPanel` runs `UpdateItemPanel` (:891), then `CSpectatorGUI::ShowPanel`'s first show reloads the
        // scheme at once (SpectatorGUI.cpp:609-613), which loads `Spectator.res` again: `visible 0`. Eight seconds on, the
        // walk has moved past the first item.
        (HudViewport viewport, TfSpectatorGui gui) = Built(showFirst: false);

        Frame(viewport, gui, Spectating(1f));
        gui.ItemPanel.Visible.ShouldBeFalse("the reload's `visible 0`");

        Frame(viewport, gui, Spectating(9.5f));
        gui.ItemPanel.Visible.ShouldBeTrue();
        gui.ItemPanel.ModelPanel!.Item!.DefinitionIndex.ShouldBe(202);
    }

    private static void Frame(HudViewport viewport, TfSpectatorGui gui, HudState state)
    {
        viewport.SetState(state);
        gui.Update(state);
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static HudState Spectating(float now, string carriedItems = "1", int target = 2, uint knifeAccount = 200u)
    {
        Dictionary<string, string> client = new() { ["cl_spec_carrieditems"] = carriedItems };

        return new HudState(InGame: true, HasLocalPlayer: false, HideHud: 0, Health: 0, Alive: false)
        {
            IsHltv = true,
            CurTime = now,
            RealTime = now,
            ObserverMode = 4,
            ObserverTarget = target,
            ConVars = new HudConVars(null, client.GetValueOrDefault),
            Players = [Spy(knifeAccount), Pyro()],
            Names = new Dictionary<int, string> { [1] = "Me", [2] = "Spy", [3] = "Pyro" },
            AccountIds = new Dictionary<int, uint> { [1] = 100u, [2] = 200u, [3] = 300u },
        };
    }

    /// <summary>A point-of-view recording: the recorder, entity 1, dead and observing <paramref name="target"/>.</summary>
    private static HudState PointOfView(float now, int target) =>
        Spectating(now, target: target) with
        {
            IsHltv = false,
            HasLocalPlayer = true,
            LocalIndex = 1,
            SpectatorGuiShown = true,
            // The recorder carries the revolver too, so targeting himself has an item to wrongly show.
            Players =
            [
                new ScenePlayer(1, 0f, 0f, 0f, 2, 0, 3) { LifeState = 2, ObserverMode = 4, ObserverTarget = target, Items = [Item(60, 202, Unique, weapon: true, 100u)] },
                Spy(200u), Pyro(),
            ],
        };

    private static ScenePlayer Spy(uint knifeAccount) =>
        new(2, 0f, 0f, 0f, 3, 125, 8)
        {
            EntityHealth = 125,
            MaxHealth = 125,
            ActiveWeapon = 30,
            Items =
            [
                Item(30, 200, 0, weapon: true, knifeAccount), Item(31, 201, Unique, weapon: true), Item(32, 202, Unique, weapon: true),
                Item(40, 300, Unique, weapon: false),
            ],
        };

    private static ScenePlayer Pyro() =>
        new(3, 0f, 0f, 0f, 3, 175, 7) { EntityHealth = 175, MaxHealth = 175, ActiveWeapon = 50, Items = [Item(50, 400, Unique, weapon: true, 300u)] };

    private static SceneItem Item(int entity, int definition, int quality, bool weapon, uint account = 200u) =>
        new(entity, weapon ? "CTFKnife" : "CTFWearable", definition, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: weapon)
        {
            Quality = quality,
            Level = 1,
            AccountId = account,
        };

    /// <summary>The GUI, shown at `game_newmap` before any target (baseviewport.cpp:706) unless <paramref name="showFirst"/> is false.</summary>
    private static (HudViewport Viewport, TfSpectatorGui Gui) Built(bool showFirst = true, int wide = 640, int tall = 480)
    {
        Dictionary<string, byte[]> files = new()
        {
            [TfSpectatorGui.SpectatorRes] = Encoding.UTF8.GetBytes("""
                "Resource/UI/Spectator.res"
                {
                    "specgui" { }
                    "topbar" { "tall" "84" }
                    "itempanel"
                    {
                        "ControlName" "CItemModelPanel" "fieldName" "itempanel" "xpos" "0" "ypos" "0" "zpos" "10" "wide" "190"
                        "tall" "100" "visible" "0" "bgcolor_override" "255 255 255 0" "PaintBackgroundType" "0" "model_ypos" "10"
                        "model_center_x" "1" "model_wide" "90" "model_tall" "60" "text_xpos" "10" "text_ypos" "10" "text_wide" "170"
                        "text_center" "1" "max_text_height" "100" "padding_height" "10" "resize_to_text" "1" "text_forcesize" "2"
                        "itemmodelpanel" { "fieldName" "itemmodelpanel" "use_item_rendertarget" "0" "useparentbg" "1" }
                        "ItemLabel"
                        {
                            "ControlName" "Label" "fieldName" "ItemLabel" "font" "DefaultSmall" "xpos" "10" "ypos" "3" "zpos" "1"
                            "wide" "270" "tall" "9" "visible" "1" "labelText" "#FreezePanel_Item" "textAlignment" "Left"
                        }
                    }
                }
                """),
            ["Resource/UI/econ/ItemModelPanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/ItemModelPanel.res"
                {
                    "MainContentsContainer"
                    {
                        "ControlName" "EditablePanel" "fieldName" "MainContentsContainer" "wide" "f0" "tall" "f0" "visible" "1"
                        "itemmodelpanel" { "ControlName" "CEmbeddedItemModelPanel" "fieldName" "itemmodelpanel" "wide" "140" "tall" "100" }
                        "namelabel" { "ControlName" "CExLabel" "fieldName" "namelabel" "wide" "140" "tall" "30" "labelText" "%itemname%" }
                        "attriblabel" { "ControlName" "CExLabel" "fieldName" "attriblabel" "ypos" "30" "wide" "140" "tall" "60" "labelText" "%attriblist%" }
                    }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["FreezePanel_Item"] = "%killername% is carrying:",
            ["FreezePanel_ItemOtherOwner"] = "%killername% is carrying %ownername%'s:",
        };
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, tall), scheme.Find("Fonts")!, wide, tall, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new()
        {
            Wide = wide,
            Tall = tall,
            Context = context,
            Items = ItemSchema.Read(Encoding.UTF8.GetBytes("""
                "items_game"
                {
                    "items"
                    {
                        "200" { "name" "Knife" "item_name" "#Knife" "item_slot" "melee" "model_player" "models/knife.mdl" }
                        "201" { "name" "Hidden" "item_name" "#Hidden" "item_slot" "pda" "hidden" "1" }
                        "202" { "name" "Revolver" "item_name" "#Revolver" "item_slot" "secondary" "model_player" "models/revolver.mdl" }
                        "300" { "name" "Hat" "item_name" "#Hat" "item_slot" "head" "model_player" "models/hat.mdl" }
                        "400" { "name" "Flamethrower" "item_name" "#Flamethrower" "item_slot" "primary" "model_player" "models/flamethrower.mdl" }
                    }
                }
                """)),
        };
        HudState state = Spectating(0f);

        viewport.SetState(state);

        TfSpectatorGui gui = new(viewport);

        VguiLayout.SolveTraverse(viewport, context, forceApplySchemeSettings: true);

        if (showFirst)
        {
            Frame(viewport, gui, Spectating(0f, target: 0));
        }

        return (viewport, gui);
    }
}
