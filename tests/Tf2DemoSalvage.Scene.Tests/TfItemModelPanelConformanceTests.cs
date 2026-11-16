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
    public void UpdateDescription_TheAttributeList_IsEveryLineButTheName()
    {
        (_, TfFreezePanel panel) = Loaded(Unique);

        panel.ItemPanel.MainContent!.DialogVariable("attriblist").ShouldBe("Level 1 Scattergun\nIt scatters.");
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

    private static (HudViewport Viewport, TfFreezePanel Panel) Loaded(int quality)
    {
        (HudViewport viewport, TfFreezePanel panel) = Shown(quality, account: 200u);

        for (int frame = 1; frame <= 6; frame++)
        {
            Frame(viewport, frame);
        }

        return (viewport, panel);
    }

    private static void Frame(HudViewport viewport, int frame)
    {
        viewport.SetState(State(1f + (frame * 0.015f)));
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static HudState State(float now, int quality = Unique, uint account = 200u) =>
        new(true, true, 0, 0, false, CurTime: now, RealTime: now, Team: 2, LocalIndex: 1, Players: [Self(), Killer(quality, account)],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Killer" },
            AccountIds: new Dictionary<int, uint> { [1] = 100u, [2] = 200u });

    private static ScenePlayer Self() => new(1, 0f, 0f, 0f, 2, 0, 1) { LifeState = 2 };

    private static ScenePlayer Killer(int quality, uint account) =>
        new(2, 0f, 0f, 0f, 3, 75, 1)
        {
            EntityHealth = 75,
            MaxHealth = 125,
            ActiveWeapon = 30,
            Items = [new SceneItem(30, "CTFScatterGun", 200, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true)
            {
                Quality = quality,
                Level = 1,
                AccountId = account,
            }],
        };

    private static (HudViewport Viewport, TfFreezePanel Panel) Shown(int quality, uint account)
    {
        (HudViewport viewport, TfFreezePanel panel) = Built();
        HudState state = State(1f, quality, account);
        Dictionary<int, PlayerInfo> roster = new() { [1] = new PlayerInfo("Me", 101, string.Empty, 1, false, false) };

        viewport.SetState(state);
        panel.HandleGameEvent(
            new HudGameEvent(
                new SceneGameEvent(0, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, roster),
                state.CurTime, state.Players ?? [], 1, default, string.Empty, 0),
            state);

        return (viewport, panel);
    }

    private static (HudViewport Viewport, TfFreezePanel Panel) Built()
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
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("""
            Scheme
            {
                Colors
                {
                    "QualityColorUnique" "255 215 0 255"
                    "QualityColorStrange" "207 106 50 255"
                }
                Borders { "TFFatLineBorder" { } }
                Fonts { }
            }
            """), "scheme.res", _ => null);
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
