using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The freeze panel's `CItemModelPanel` (game/client/econ/item_model_panel.cpp) and the branch that fills it
/// (tf_hud_freezepanel.cpp:411-490), driven through the scheme, think and solve traversal as production drives it (B751).
/// </summary>
/// <remarks>
/// The recorder is entity 1, killed by entity 2 "Killer", a BLU scout holding weapon 30. The `.res` files keep the real
/// ones' structure: the freeze panel's `itempanel` block with its `itemmodelpanel`, `ItemLabel` and `attriblabel`
/// sub-blocks, and `ItemModelPanel.res`'s `MainContentsContainer` holding the model panel and the two labels.
/// </remarks>
public sealed class TfItemModelPanelConformanceTests
{
    private const int Unique = 6;
    private const int Strange = 11;

    [Test]
    public void Show_AKillerWithAUniqueWeapon_ShowsTheItemPanelNamingHim()
    {
        // `GetItemQuality() != AE_NORMAL` (:450) shows the panel, `killername` set and the item label "#FreezePanel_Item"
        // since the item came from his own backpack (:470-474).
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.Visible.ShouldBeTrue();
        panel.ItemPanel.DialogVariable("killername").ShouldBe("Killer");
        ((VguiLabel)panel.ItemPanel.FindChildByName("ItemLabel", true)!).Text.ShouldBe("Killer is carrying:");
        panel.ItemPanel.Y.ShouldBe(viewport.Tall - 12 - panel.ItemPanel.Tall, "pinned YRES(12) above the screen's bottom (:486)");
    }

    [Test]
    public void Show_AKillerWithANormalWeapon_HidesTheItemPanel()
    {
        // The control: a stock weapon's quality is `AE_NORMAL`, and the panel hidden at :371-374 stays hidden.
        (_, TfFreezePanel panel) = Shown(0, account: 200u);

        panel.ItemPanel.Visible.ShouldBeFalse();
    }

    [Test]
    public void Show_AWeaponFromAnotherPlayersBackpack_SaysWhose()
    {
        // `GetPlayerByAccountID( GetAccountID() )` is the recorder, not the killer: "#FreezePanel_ItemOtherOwner" with his
        // name as `ownername` (:466-470).
        (_, TfFreezePanel panel) = Shown(Unique, account: 100u);

        ((VguiLabel)panel.ItemPanel.FindChildByName("ItemLabel", true)!).Text.ShouldBe("Killer is carrying Me's:");
        panel.ItemPanel.DialogVariable("ownername").ShouldBe("Me");
    }

    [Test]
    public void LoadData_ARenderTargetIcon_ShowsTheContentOnceCopiedThenTheDescriptionAFrameLater()
    {
        // `use_item_rendertarget` from the freeze `.res` (:1501): the icon is loading, so the content hides (:3014-3017).
        // Each frame `LoadData` runs the lowest task asked for the frame before (:3446-3456): none on the first, icons
        // until the copy lands, then the description.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        TfItemModelPanel item = panel.ItemPanel;
        List<(bool Content, string? Name)> frames = [];

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
            frames.Add((item.MainContent!.Visible, item.MainContent.DialogVariable("itemname")));
        }

        frames.ShouldBe(
        [
            (false, string.Empty),
            (false, string.Empty),
            (true, string.Empty),
            (true, string.Empty),
            (true, "Scattergun"),
            (true, "Scattergun"),
        ]);
    }

    [Test]
    public void UpdateDescription_AUniqueItem_ColorsTheNameByItsQuality()
    {
        // No rarity: `EconQuality_GetColorString( AE_UNIQUE )`, "QualityColorUnique" (:2675-2679).
        (HudViewport viewport, TfFreezePanel panel) = Loaded(Unique);

        panel.ItemPanel.NameLabel!.FgColor.ShouldBe(((byte)255, (byte)215, (byte)0, (byte)255));
        _ = viewport;
    }

    [Test]
    public void UpdateDescription_AStrangeQualityItemWithoutAScore_ColorsByTheNetworkedQualityButNamesItNormal()
    {
        // The color reads `GetItemQuality()`, the networked quality (:2668); the name reads `GetQuality()`, normal without
        // a backpack (B755).
        (_, TfFreezePanel panel) = Loaded(Strange);

        panel.ItemPanel.NameLabel!.FgColor.ShouldBe(((byte)207, (byte)106, (byte)50, (byte)255));
        panel.ItemPanel.MainContent!.DialogVariable("itemname").ShouldBe("Scattergun");
    }

    [Test]
    public void UpdateDescription_AWarPaintedStrangeItem_ColorsTheNameByItsWarPaintsGrade()
    {
        // `m_ItemData.GetRarity()` (:2663-2671): kit 350's War Paint tool (205) is in an "uncommon" block, so the painted
        // launcher takes that grade's color over its strange quality — TF2 draws the rgl-pug killer's Horror Holiday
        // launcher's name in the Mercenary blue (B754). The Strange test above is the control: no kit, the quality's.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Strange, account: 200u, definition: 203);

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame, quality: Strange, definition: 203);
        }

        panel.ItemPanel.NameLabel!.FgColor.ShouldBe(((byte)75, (byte)105, (byte)255, (byte)255));
    }

    [Test]
    public void UpdateDescription_TheAttributeList_IsEveryLineButTheName()
    {
        (_, TfFreezePanel panel) = Loaded(Unique);

        panel.ItemPanel.MainContent!.DialogVariable("attriblist").ShouldBe("Level 1 Scattergun\nIt scatters.");
    }

    [Test]
    public void UpdateDescription_TheAttributeLines_StartAColorAtEachLineThatChangesIt()
    {
        // `AddColorChange( col, unCurrentAttrTextStreamIndex )` (:2809-2814): the name line skipped, the level line's color
        // at 0, the description's at the level line's length plus its newline: "Level 1 Scattergun" is 18, so 19.
        (_, TfFreezePanel panel) = Loaded(Unique);
        VguiLabel attrib = (VguiLabel)panel.ItemPanel.FindChildByName("attriblabel", true)!;

        attrib.TextImage.ColorChanges.ShouldBe(
        [
            (0, ((byte)10, (byte)20, (byte)30, (byte)255)),
            (19, ((byte)40, (byte)50, (byte)60, (byte)255)),
        ]);
    }

    [Test]
    public void UpdateDescription_TwoNeutralLinesInARow_ChangeColorOnce()
    {
        // `prevAttrColor != col` (:2809): a neutral attribute line ("+5 thing", 8 characters) then the neutral description;
        // the second does not change the color again.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, (6, System.BitConverter.SingleToInt32Bits(5f))));

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
        }

        ((VguiLabel)panel.ItemPanel.FindChildByName("attriblabel", true)!).TextImage.ColorChanges.ShouldBe(
        [
            (0, ((byte)10, (byte)20, (byte)30, (byte)255)),
            (19, ((byte)40, (byte)50, (byte)60, (byte)255)),
        ]);
    }

    [Test]
    public void PerformLayout_TwoIconsOnA1366By768Screen_PinThemTopRightOneBelowTheOther()
    {
        // "pin icons to top right corner" (:1919-1951): x = m_iBaseWide - XRES(1), y = YRES(1), each icon at x - its
        // width, y stepping by 0.9 of each one's height, strange before loaner. `m_iBaseWide` is the `.res` "wide", 0.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u, wide: 1366, tall: 768);

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, (214, 5), (2167, 5), (2168, 0)));
        Frame(viewport, 1);

        VguiPanel strange = panel.ItemPanel.FindChildByName("is_strange_icon", true)!;
        VguiPanel loaner = panel.ItemPanel.FindChildByName("is_loaner_icon", true)!;

        // x = (int)(0 - 2.134375) = -2, y = (int)1.6 = 1. The two icons differ in size (16x16 and 20x24 before scaling), so
        // the loaner's step is the STRANGE icon's height and each X its own width.
        // Proportional at 768 tall: 16 to (int)25.6 = 25; 20 to 32; 24 to (int)38.4 = 38.
        ((strange.Wide, strange.Tall), (loaner.Wide, loaner.Tall)).ShouldBe(((25, 25), (32, 38)));
        ((strange.X, strange.Y), (loaner.X, loaner.Y)).ShouldBe(
            ((-2 - strange.Wide, 1), (-2 - loaner.Wide, 1 + (int)(strange.Tall * 0.9))));
    }

    [Test]
    public void UpdatePanels_APaintedItem_ShowsItsSplatInThePaintsColor()
    {
        // `GetModifiedRGBValue( false )` and `( true )` (:3147-3157): one paint, so one color; the float's value is the
        // packed color, 0xE7B53B.
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u, paint: 15185211f);
        TfPaintIconPanel icon = (TfPaintIconPanel)panel.ItemPanel.FindChildByName("paint_icon", true)!;

        icon.Visible.ShouldBeTrue();
        icon.PaintColors.ShouldBe([((byte)0xE7, (byte)0xB5, (byte)0x3B, (byte)255)]);
    }

    [Test]
    public void UpdatePanels_AnUnpaintedItem_HidesTheSplat()
    {
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.FindChildByName("paint_icon", true)!.Visible.ShouldBeFalse();
    }

    [Test]
    public void Construct_TheEmbeddedModelPanel_PaintsNoBorder()
    {
        // `CPotteryWheelPanel` turns border and background painting off (potterywheelpanel.cpp:238-239), so the
        // `TFFatLineBorder` the item panel gives it (:1486) is never drawn.
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u);
        TfEmbeddedItemModelPanel model = panel.ItemPanel.ModelPanel!;

        model.Border.ShouldNotBeNull();
        (model.PaintBorderEnabled, model.PaintBackgroundEnabled).ShouldBe((false, false));
    }

    [Test]
    public void LoadData_WithTheClockHeldStill_LoadsOneTaskAFrameAllTheSame()
    {
        // `gpGlobals->framecount` keys the shared schedule (:3437), not the clock: a paused demo's frames still count.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        TfItemModelPanel item = panel.ItemPanel;
        List<(bool Content, string? Name)> frames = [];

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame, clockMoves: false);
            frames.Add((item.MainContent!.Visible, item.MainContent.DialogVariable("itemname")));
        }

        frames.ShouldBe(
        [
            (false, string.Empty),
            (false, string.Empty),
            (true, string.Empty),
            (true, string.Empty),
            (true, "Scattergun"),
            (true, "Scattergun"),
        ]);
    }

    [Test]
    public void SetItem_TheSameBaseItemAgain_UpdatesThePanelsAgain()
    {
        // A base item (no ID) never matches: `bMatch &=` starts from false (:2255-2264), so the panels update, the icon is
        // made again and the content hides while it is.
        (HudViewport viewport, TfFreezePanel panel) = Loaded(Unique);

        panel.ItemPanel.SetItem(TfItemView.From(Weapon(Unique, 200u, null), viewport.Items!));

        panel.ItemPanel.MainContent!.Visible.ShouldBeFalse();
    }

    [Test]
    public void SetItem_TheSameItemIdAgain_LeavesThePanelsAlone()
    {
        // The control: an item with an ID matches by it (:2228-2252), and nothing updates.
        (HudViewport viewport, TfFreezePanel panel) = Loaded(Unique);
        TfItemView owned = TfItemView.From(Weapon(Unique, 200u, null, itemId: 77u), viewport.Items!)!;

        panel.ItemPanel.SetItem(owned);

        for (int frame = 7; frame <= 12; frame++)
        {
            Frame(viewport, frame);
        }

        panel.ItemPanel.MainContent!.Visible.ShouldBeTrue("loaded again after the first set");
        panel.ItemPanel.SetItem(owned);
        panel.ItemPanel.MainContent.Visible.ShouldBeTrue();
    }

    [Test]
    public void SetItem_AnItemForOneClass_ShowsThatClasssModel()
    {
        // The first set bit of `GetClassUsability()` (:331-340) is the soldier, class 3; class 0 would copy the first
        // per-class entry, the scout's (tf_item_schema.cpp:541).
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u, definition: 202);

        panel.ItemPanel.ModelPanel!.ModelName.ShouldBe("models/soldier.mdl");
    }

    [Test]
    public void SetItem_ABlueWatcher_StillShowsTheStylesRedModel()
    {
        // `GetPlayerDisplayModel( nClass, 0 )` (:343): team 0, never the watcher's, so a style's blue model is not chosen.
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u, definition: 201, team: 3);

        panel.ItemPanel.ModelPanel!.ModelName.ShouldBe("models/red_style.mdl");
    }

    [Test]
    public void UpdatePanels_AnUnownedStrangeWarPaint_ShowsTheStatClockIcon()
    {
        // The control: with no item ID the walk reads the definition's attributes, its string module among them
        // (econ_item_view.cpp:641), so `GetStattrak` finds it (econ_item_interface.cpp:483-502).
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.SetItem(View(viewport, 203, itemId: null));

        ((VguiImagePanel)panel.ItemPanel.FindChildByName("is_strange_icon", true)!).ImageName.ShouldBe("viewmode_statclock");
    }

    [Test]
    public void UpdatePanels_AnOwnedStrangeWarPaint_ShowsTheStrangeIcon()
    {
        // With an ID the walk reads the networked demo list, which "only copies the default type and floats"
        // (econ_item_view.cpp:633): no module string, so no stat clock (item_model_panel.cpp:3327).
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.SetItem(View(viewport, 203, itemId: 77u, (834, 350), (214, 5)));

        ((VguiImagePanel)panel.ItemPanel.FindChildByName("is_strange_icon", true)!).ImageName.ShouldBe("viewmode_strange");
    }

    [Test]
    public void UpdatePanels_OneLoanerHalf_ShowsNoLoanerIcon()
    {
        // `GetAssociatedQuestID` needs both halves (tf_item_schema.cpp:1501); the control has both.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        VguiPanel icon = panel.ItemPanel.FindChildByName("is_loaner_icon", true)!;

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, (2167, 5)));
        icon.Visible.ShouldBeFalse();

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 78u, (2167, 5), (2168, 0)));
        icon.Visible.ShouldBeTrue();
    }

    [Test]
    public void UpdatePanels_OneDecalHalf_ShowsNoPaintIcon()
    {
        // `GetCustomUserTextureID` needs both halves (econ_item_view.cpp:1657); the control has both.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        VguiPanel icon = panel.ItemPanel.FindChildByName("paint_icon", true)!;

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, (152, 9)));
        icon.Visible.ShouldBeFalse();

        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 78u, (152, 9), (227, 0)));
        icon.Visible.ShouldBeTrue();
    }

    [Test]
    public void UpdatePanels_ADecalUnderTfHideCustomDecals_ShowsNoPaintIconForAnotherPlayersItem()
    {
        // `tf_hide_custom_decals` answers 0 unless the owner is the local player (econ_item_view.cpp:1642-1647).
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        Dictionary<string, string> client = new() { ["tf_hide_custom_decals"] = "1" };

        viewport.SetState(viewport.State with { ConVars = new HudConVars(null, client.GetValueOrDefault) });
        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, (152, 9), (227, 0)));

        panel.ItemPanel.FindChildByName("paint_icon", true)!.Visible.ShouldBeFalse();
    }

    [Test]
    public void UpdatePanels_ADecalUnderTfHideCustomDecals_StillShowsTheLocalPlayersOwn()
    {
        // The exemption (:1645): the owner is the local player, entity 1, account 100.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);
        Dictionary<string, string> client = new() { ["tf_hide_custom_decals"] = "1" };

        viewport.SetState(viewport.State with { ConVars = new HudConVars(null, client.GetValueOrDefault) });
        panel.ItemPanel.SetItem(View(viewport, 200, itemId: 77u, account: 100u, (152, 9), (227, 0)));

        panel.ItemPanel.FindChildByName("paint_icon", true)!.Visible.ShouldBeTrue();
    }

    [Test]
    public void LoadData_ARenderTargetIconForAModellessItem_StillCompletes()
    {
        // The copy has no model test (item_model_panel.cpp:982-1027): a powerup's empty frame is copied, and the content shows.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.SetItem(View(viewport, 204, itemId: null));

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
        }

        panel.ItemPanel.MainContent!.Visible.ShouldBeTrue();
    }

    [Test]
    public void LoadData_WhileThePanelIsHidden_StillLoadsTheDescription()
    {
        // `SetNeedsToLoad` is a tick signal (:3429), which runs whatever the panel's visibility.
        (HudViewport viewport, TfFreezePanel panel) = Shown(Unique, account: 200u);

        panel.ItemPanel.Visible = false;

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
        }

        panel.ItemPanel.MainContent!.DialogVariable("itemname").ShouldBe("Scattergun");
    }

    [Test]
    public void Show_OnA1366By768Screen_PinsThePanelWithYres12TruncatedOnce()
    {
        // `ScreenHeight() - YRES( 12 ) - tall`, a double truncated once (tf_hud_freezepanel.cpp:489): 768 - 19.2 - tall.
        (_, TfFreezePanel panel) = Shown(Unique, account: 200u, wide: 1366, tall: 768);

        panel.ItemPanel.Y.ShouldBe((int)(768 - (12 * (768 / 480.0)) - panel.ItemPanel.Tall));
    }

    /// <summary>An item as a demo carries it: with an ID, its networked list; without, the definition's attributes.</summary>
    private static TfItemView View(HudViewport viewport, int definition, ulong? itemId, params (int Index, int Bits)[] networked) =>
        View(viewport, definition, itemId, 200u, networked);

    private static TfItemView View(HudViewport viewport, int definition, ulong? itemId, uint account, params (int Index, int Bits)[] networked)
    {
        List<EconAttributeValue> values = [];

        foreach ((int index, int bits) in networked)
        {
            values.Add(new EconAttributeValue(index, bits));
        }

        SceneItem item = new(30, "CTFScatterGun", definition, new EconAttributeWire([], values, HasValidItemId: itemId is not null), IsWeapon: true)
        {
            Quality = Strange,
            Level = 1,
            AccountId = account,
            ItemId = itemId ?? SceneItem.InvalidItemId,
        };

        return TfItemView.From(item, viewport.Items!).ShouldNotBeNull();
    }

    private static (HudViewport Viewport, TfFreezePanel Panel) Loaded(int quality)
    {
        (HudViewport viewport, TfFreezePanel panel) = Shown(quality, account: 200u);

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
        }

        return (viewport, panel);
    }

    private static void Frame(HudViewport viewport, int frame, bool clockMoves = true, int quality = Unique, int definition = 200)
    {
        viewport.SetState(State(clockMoves ? 1f + (frame * 0.015f) : 1f, quality, definition: definition));
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static HudState State(float now, int quality = Unique, uint account = 200u, float? paint = null, int definition = 200, int team = 2) =>
        new(true, true, 0, 0, false, CurTime: now, RealTime: now, Team: team, LocalIndex: 1,
            Players: [Self(), Killer(quality, account, paint, definition)],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Killer" },
            AccountIds: new Dictionary<int, uint> { [1] = 100u, [2] = 200u });

    private static ScenePlayer Self() => new(1, 0f, 0f, 0f, 2, 0, 1) { LifeState = 2 };

    private static ScenePlayer Killer(int quality, uint account, float? paint, int definition = 200) =>
        new(2, 0f, 0f, 0f, 3, 75, 1)
        {
            EntityHealth = 75,
            MaxHealth = 125,
            ActiveWeapon = 30,
            Items = [Weapon(quality, account, paint, definition)],
        };

    private static SceneItem Weapon(int quality, uint account, float? paint, int definition = 200, ulong itemId = SceneItem.InvalidItemId) =>
        new(
            30,
            "CTFScatterGun",
            definition,
            paint is { } tint
                ? new EconAttributeWire([], [new EconAttributeValue(142, System.BitConverter.SingleToInt32Bits(tint))], HasValidItemId: true)
                : new EconAttributeWire([], [], HasValidItemId: false),
            IsWeapon: true)
        {
            Quality = quality,
            Level = 1,
            AccountId = account,
            ItemId = itemId,
        };

    private static (HudViewport Viewport, TfFreezePanel Panel) Shown(
        int quality, uint account, float? paint = null, int definition = 200, int team = 2, int wide = 640, int tall = 480)
    {
        (HudViewport viewport, TfFreezePanel panel) = Built(wide, tall);
        HudState state = State(1f, quality, account, paint, definition, team);
        Dictionary<int, PlayerInfo> roster = new() { [1] = new PlayerInfo("Me", 101, string.Empty, 1, false, false) };

        viewport.SetState(state);
        panel.HandleGameEvent(
            new HudGameEvent(
                new SceneGameEvent(0, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, roster),
                state.CurTime, state.Players ?? [], 1, default, string.Empty, 0),
            state);

        return (viewport, panel);
    }

    /// <summary>The freeze panel with the shipped `FreezePanel_Basic.res` `itempanel` block, keys as the game ships them.</summary>
    private static (HudViewport Viewport, TfFreezePanel Panel) Built(int wide = 640, int tall = 480)
    {
        Dictionary<string, byte[]> files = new()
        {
            [TfFreezePanel.ResFile] = Encoding.UTF8.GetBytes("""
                "Resource/UI/FreezePanel_Basic.res"
                {
                    "itempanel"
                    {
                        "ControlName" "CItemModelPanel" "fieldName" "itempanel" "xpos" "r270" "ypos" "300" "zpos" "1" "wide" "0" "tall" "0"
                        "visible" "0" "model_xpos" "10" "model_center_y" "1" "model_wide" "90" "model_tall" "55" "text_xpos" "90"
                        "text_wide" "170" "text_center" "1" "max_text_height" "90" "padding_height" "40" "resize_to_text" "1"
                        "text_forcesize" "2"
                        "itemmodelpanel" { "fieldName" "itemmodelpanel" "use_item_rendertarget" "1" "useparentbg" "1" }
                        "ItemLabel"
                        {
                            "ControlName" "Label" "fieldName" "ItemLabel" "xpos" "10" "ypos" "3" "zpos" "1" "wide" "0" "tall" "0"
                            "visible" "0" "labelText" "#FreezePanel_Item"
                        }
                        "attriblabel"
                        {
                            "ControlName" "CExLabel" "fieldName" "attriblabel" "xpos" "0" "ypos" "30" "wide" "0" "tall" "0"
                            "visible" "0" "labelText" "%attriblist%" "centerwrap" "1"
                        }
                    }
                    "FreezePanelBase"
                    {
                        "ControlName" "EditablePanel" "fieldName" "FreezePanelBase" "xpos" "10" "ypos" "300" "wide" "250" "tall" "100"
                        "FreezeLabel" { "ControlName" "CExLabel" "fieldName" "FreezeLabel" "wide" "170" "tall" "19" "labelText" "#FreezePanel_Killer" }
                        "FreezePanelHealth" { "ControlName" "CTFFreezePanelHealth" "fieldName" "FreezePanelHealth" "wide" "71" "tall" "35" }
                    }
                }
                """),
            ["Resource/UI/econ/ItemModelPanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/ItemModelPanel.res"
                {
                    "LoadingSpinner" { "ControlName" "ImagePanel" "fieldName" "LoadingSpinner" "wide" "f0" "tall" "f0" "visible" "0" }
                    "MainContentsContainer"
                    {
                        "ControlName" "EditablePanel" "fieldName" "MainContentsContainer" "wide" "f0" "tall" "f0" "visible" "1"
                        "itemmodelpanel"
                        {
                            "ControlName" "CEmbeddedItemModelPanel" "fieldName" "itemmodelpanel" "wide" "140" "tall" "100" "fov" "54"
                            "start_framed" "1" "model" { "angles_x" "10" "angles_y" "130" "angles_z" "0" "spotlight" "1" }
                        }
                        "namelabel"
                        {
                            "ControlName" "CExLabel" "fieldName" "namelabel" "wide" "140" "tall" "30" "labelText" "%itemname%"
                            "textAlignment" "south" "fgcolor" "235 226 202 255" "centerwrap" "1"
                        }
                        "attriblabel"
                        {
                            "ControlName" "CExLabel" "fieldName" "attriblabel" "ypos" "30" "wide" "140" "tall" "60"
                            "labelText" "%attriblist%" "textAlignment" "south" "fgcolor" "117 107 94 255" "centerwrap" "1"
                        }
                        "paint_icon" { "ControlName" "CItemMaterialCustomizationIconPanel" "fieldName" "paint_icon" "wide" "16" "tall" "16" "visible" "0" }
                        "is_strange_icon" { "ControlName" "ImagePanel" "fieldName" "is_strange_icon" "wide" "16" "tall" "16" "visible" "0" }
                        "is_loaner_icon" { "ControlName" "ImagePanel" "fieldName" "is_loaner_icon" "wide" "20" "tall" "24" "visible" "0" }
                    }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["FreezePanel_Killer"] = "Killed by",
            ["FreezePanel_Item"] = "%killername% is carrying:",
            ["FreezePanel_ItemOtherOwner"] = "%killername% is carrying %ownername%'s:",
            ["ItemNameFormat"] = "%s1%s2%s3%s4%s5%s6",
            ["ItemNameNormalOrUniqueQualityFormat"] = "%s1%s2%s3%s4%s5",
            ["TF_Scattergun"] = "Scattergun",
            ["TF_Weapon_Scattergun"] = "Scattergun",
            ["TF_Scattergun_Desc"] = "It scatters.",
            ["ItemTypeDesc"] = "Level %s1 %s2",
            ["Attrib_Thing"] = "+%s1 thing",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("""
            Scheme
            {
                Colors
                {
                    "QualityColorUnique" "255 215 0 255"
                    "QualityColorStrange" "207 106 50 255"
                    "ItemRarityUncommon" "75 105 255 255"
                    "ItemAttribLevel" "10 20 30 255"
                    "ItemAttribNeutral" "40 50 60 255"
                }
                Borders { "TFFatLineBorder" { } }
                Fonts { }
            }
            """), "scheme.res", _ => null);
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
                        "200"
                        {
                            "name" "TF_WEAPON_SCATTERGUN" "item_name" "#TF_Scattergun" "item_type_name" "#TF_Weapon_Scattergun"
                            "item_description" "#TF_Scattergun_Desc" "item_slot" "primary" "model_player" "models/w_scattergun.mdl"
                            "used_by_classes" { "scout" "1" }
                        }
                        "201"
                        {
                            "name" "Styled" "item_name" "#Styled" "item_slot" "primary" "used_by_classes" { "soldier" "1" }
                            "model_player_per_class" { "scout" "models/scout.mdl" "soldier" "models/soldier.mdl" }
                            "visuals"
                            {
                                "styles"
                                {
                                    "0"
                                    {
                                        "model_player_per_class" { "soldier" "models/red_style.mdl" }
                                        "model_player_per_class_blue" { "soldier" "models/blue_style.mdl" }
                                    }
                                }
                            }
                            "static_attrs" { "item style override" "0" }
                        }
                        "202"
                        {
                            "name" "Soldier Only" "item_name" "#Soldier" "item_slot" "primary" "used_by_classes" { "soldier" "1" }
                            "model_player_per_class" { "scout" "models/scout.mdl" "soldier" "models/soldier.mdl" }
                        }
                        "203"
                        {
                            "name" "Painted Strange" "item_name" "#Scattergun" "item_slot" "primary" "model_player" "models/w_scattergun.mdl"
                            "static_attrs" { "paintkit_proto_def_index" "350" "kill eater" "5" "weapon_uses_stattrak_module" "models/stattrack.mdl" }
                        }
                        "204" { "name" "Powerup" "item_name" "#Powerup" "item_slot" "action" }
                        "205"
                        {
                            "name" "Paint Tool" "item_name" "#Paint" "item_class" "tool" "tool" { "type" "paintkit" }
                            "attributes" { "paintkit_proto_def_index" { "value" "350" } }
                        }
                    }
                    "rarities" { "uncommon" { "value" "2" "loc_key" "Rarity_Uncommon" "color" "desc_uncommon" } }
                    "item_collections" { "paints" { "name" "#Paints" "items" { "uncommon" { "Paint Tool" "1" } } } }
                    "attributes"
                    {
                        "142" { "name" "set item tint RGB" "attribute_class" "set_item_tint_rgb" "hidden" "1" }
                        "152" { "name" "custom texture lo" "hidden" "1" "stored_as_integer" "1" }
                        "227" { "name" "custom texture hi" "hidden" "1" "stored_as_integer" "1" }
                        "214" { "name" "kill eater" "hidden" "1" "stored_as_integer" "1" }
                        "834" { "name" "paintkit_proto_def_index" "hidden" "1" "stored_as_integer" "1" }
                        "2025" { "name" "weapon_uses_stattrak_module" "hidden" "1" "attribute_type" "string" }
                        "2167" { "name" "quest loaner id low" "hidden" "1" "stored_as_integer" "1" }
                        "2168" { "name" "quest loaner id hi" "hidden" "1" "stored_as_integer" "1" }
                        "542" { "name" "item style override" "attribute_class" "item_style_override" "hidden" "1" }
                        "6" { "name" "additive thing" "description_string" "#Attrib_Thing" "description_format" "value_is_additive" "effect_type" "neutral" }
                    }
                    "colors"
                    {
                        "desc_level" { "color_name" "ItemAttribLevel" }
                        "desc_uncommon" { "color_name" "ItemRarityUncommon" }
                        "desc_attrib_neutral" { "color_name" "ItemAttribNeutral" }
                    }
                }
                """)),
        };
        TfFreezePanel panel = new(viewport);

        viewport.SetState(State(1f));
        VguiLayout.SolveTraverse(viewport, context, forceApplySchemeSettings: true);

        return (viewport, panel);
    }
}
