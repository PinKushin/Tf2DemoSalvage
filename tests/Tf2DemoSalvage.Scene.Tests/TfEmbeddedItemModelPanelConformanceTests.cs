using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CEmbeddedItemModelPanel::Paint`'s inventory image branch (game/client/econ/item_model_panel.cpp:747-847, B759).</summary>
public sealed class TfEmbeddedItemModelPanelConformanceTests
{
    [Test]
    public void Paint_ASmallInventoryImageOfAnotherRatioOnA1366By768Screen_FitsItToTheHeightAroundItsShiftedCentre()
    {
        // Worked by hand from :756-797 for a 140x100 panel, image data pos (12, 0) size 120x80 (not the 128x82 defaults),
        // the texture 64x64: texW = 120/64 = 1.875, texH = 80/64 = 1.25, texX = (1 - 1.875) * 0.5 = -0.4375,
        // texY = (1 - 1.25) * 0.5 = -0.125; x = (int)(0 + XRES(12)) = (int)25.6125 = 25 (truncated, not rounded to 26); the
        // ratio 1.5 is not 1.4, so the centre is (int)(25 + 70) = 95, the width (int)(100 * 1.5f) = 150, x = (int)(95 - 75) = 20.
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes("""
            "items_game" { "items" { "300" { "name" "Hat" "item_name" "#Hat" "item_slot" "head" "image_inventory" "backpack/hat"
              "image_inventory_pos_x" "12" "image_inventory_pos_y" "0" "image_inventory_size_w" "120" "image_inventory_size_h" "80" } } }
            """));
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel") { Wide = 140, Tall = 100 };
        TextRecorder surface = new();
        SceneItem hat = new(40, "CTFWearable", 300, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: false) { Quality = 6 };

        panel.SetItem(TfItemView.From(hat, schema));
        panel.LoadInventoryImage();
        panel.Paint(surface, Context());

        surface.SubRects.ShouldBe(["20 0 170 100 uv -0.4375 -0.125 1.4375 1.125"]);
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 768), scheme.Find("Fonts")!, 1366, 768, "english")
        {
            Surface = new TextRecorder(),
        };
    }
}
