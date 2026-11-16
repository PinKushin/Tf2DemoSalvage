using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A real point-of-view demo drives the spectator GUI's item panel, through the production state (B756).</summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the HUD's state for each
/// tick, as <see cref="HudStates.For"/> builds it, into <see cref="TfSpectatorGui.Update"/>, and the panel read back.
/// `rgl-pug-2026-08-10-pov`: the recorder is dead and spectating slugmundo from about tick 23600, where TF2 drew
/// "slugmundo is carrying:" (demoui tick 23632). The `.res` block is the shipped `Spectator.res`'s; the strings are
/// `tf_english.txt`'s. lcor and the game; skips without either.
/// </remarks>
public sealed class CorpusSpectatorItemPanelTests
{
    [Test]
    public void Update_ARecorderSpectatingATeammate_ShowsOneOfTheTargetsItemsNamingHim()
    {
        string? schemaPath = SdkReference.GameInstall.Find("scripts/items/items_game.txt");

        if (schemaPath is null || !System.IO.File.Exists(schemaPath))
        {
            Assert.Ignore("the game is not present");
        }

        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));
        (HudViewport viewport, TfSpectatorGui gui) = Built(ItemSchema.Read(System.IO.File.ReadAllBytes(schemaPath)));
        (int Tick, int? Definition, string Label)? seen = null;
        HudState shownAt = default;

        for (int tick = 23500; tick <= 24300; tick++)
        {
            HudState state = HudStates.For(timeline, tick);

            viewport.SetState(state);
            gui.Update(state);
            VguiLayout.SolveTraverse(viewport, viewport.Context!);

            if (gui.Visible && gui.ItemPanel.Visible)
            {
                seen = (tick, gui.ItemPanel.ModelPanel?.Item?.DefinitionIndex, ((VguiLabel)gui.ItemPanel.FindChildByName("ItemLabel", false)!).Text);
                shownAt = state;
                break;
            }
        }

        (int shownTick, int? definition, string label) = seen.ShouldNotBeNull("the panel shows within the spectating window");
        ScenePlayer target = shownAt.Player(shownAt.ObserverTarget).ShouldNotBeNull($"tick {shownTick}'s target is a player");

        label.ShouldBe("slugmundo is carrying:");
        target.Items.ShouldNotBeNull().Select(item => item.DefinitionIndex).ShouldContain(definition, "an item the target carries");
    }

    private static (HudViewport Viewport, TfSpectatorGui Gui) Built(ItemSchema schema)
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
                        "tall" "100" "visible" "0" "model_ypos" "10" "model_center_x" "1" "model_wide" "90" "model_tall" "60"
                        "text_xpos" "10" "text_ypos" "10" "text_wide" "170" "text_center" "1" "max_text_height" "100"
                        "padding_height" "10" "resize_to_text" "1" "text_forcesize" "2"
                        "itemmodelpanel" { "fieldName" "itemmodelpanel" "use_item_rendertarget" "0" "useparentbg" "1" }
                        "ItemLabel" { "ControlName" "Label" "fieldName" "ItemLabel" "xpos" "10" "ypos" "3" "wide" "270" "tall" "9" "labelText" "#FreezePanel_Item" }
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
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new VguiDrawList(_ => (0, 0)),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context, Items = schema };
        TfSpectatorGui gui = new(viewport);

        VguiLayout.SolveTraverse(viewport, context, forceApplySchemeSettings: true);
        return (viewport, gui);
    }
}
