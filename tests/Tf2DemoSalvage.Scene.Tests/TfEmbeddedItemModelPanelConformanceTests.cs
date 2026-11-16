using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CEmbeddedItemModelPanel::Paint`'s inventory image branch (game/client/econ/item_model_panel.cpp:747-847, B759).</summary>
public sealed class TfEmbeddedItemModelPanelConformanceTests
{
    [Test]
    public void Paint_AnInventoryImageOnA1366By768Screen_FitsItToTheHeightAroundItsShiftedCentre()
    {
        // Worked by hand from :756-797 for a 140x100 panel, image data pos (10, 0) size 128x82, the texture 64x64:
        // texW = 128/64 = 2, texH = 82/64 = 1.28125, texX = (1 - 2) * 0.5 = -0.5, texY = (1 - 1.28125) * 0.5 = -0.140625;
        // x = (int)(0 + XRES(10)) = (int)21.34375 = 21; the ratio 128/82 is not 140/100, so the centre is
        // (int)(21 + 140 * 0.5) = 91, the width (int)(100 * 1.5609756f) = 156, and x = (int)(91 - 156 * 0.5) = 13.
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes("""
            "items_game" { "items" { "300" { "name" "Hat" "item_name" "#Hat" "item_slot" "head" "image_inventory" "backpack/hat"
              "image_inventory_pos_x" "10" "image_inventory_pos_y" "0" "image_inventory_size_w" "128" "image_inventory_size_h" "82" } } }
            """));
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel") { Wide = 140, Tall = 100 };
        TextRecorder surface = new();
        SceneItem hat = new(40, "CTFWearable", 300, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: false) { Quality = 6 };

        panel.SetItem(TfItemView.From(hat, schema));
        panel.LoadInventoryImage();
        panel.Paint(surface, Context());

        surface.SubRects.ShouldBe(["13 0 169 100 uv -0.5 -0.140625 1.5 1.140625"]);
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
