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
              "image_inventory_pos_x" "12" "image_inventory_pos_y" "5" "image_inventory_size_w" "120" "image_inventory_size_h" "80" } } }
            """));
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel") { Wide = 140, Tall = 100 };
        TextRecorder surface = new();
        SceneItem hat = new(40, "CTFWearable", 300, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: false) { Quality = 6 };

        panel.SetItem(TfItemView.From(hat, schema));
        panel.LoadInventoryImage();
        panel.Paint(surface, Context());

        // y = (int)(0 + YRES(5)) = (int)8.0 = 8. The small type's own material, drawn white (not greyed out).
        surface.SubRects.ShouldBe(["20 8 170 108 uv -0.4375 -0.125 1.4375 1.125"]);
        surface.SizesAsked.ShouldBe(["backpack/hat"]);
        surface.Calls.ShouldContain("texture backpack/hat");
    }

    /// <remarks>
    /// The other branches of :747-847, each worked by hand for the same 140x100 panel, image data pos (12, 5) — so x starts
    /// at 25 and y at 8 — and a 64x64 texture.
    /// <list type="bullet">
    /// <item>Equal ratio, 140x100: 140/100 is the panel's own 1.4, so no fit (:792); texW 2.1875, texH 1.5625.</item>
    /// <item>Large (`IMAGETYPE_LARGE`), 120x80: "Normal is 128*128, large is 512x512", so 480x320 (:808-812), the
    /// `_large` material; texW 7.5, texH 5; ratio 1.5, so the fit as the small case: 20 to 170.</item>
    /// <item>`force_square_image`, 120x80: MAX( 120, 80 ) both ways (:760-764), ratio 1, width (int)(100 * 1f) = 100,
    /// x = (int)(95 - 50) = 45.</item>
    /// <item>Zero size: the material's own mapping size, 64x64 (:765-769), whole texture, ratio 1: 45 to 145.</item>
    /// </list>
    /// </remarks>
    [TestCase(140, 100, false, false, "backpack/hat", "25 8 165 108 uv -0.59375 -0.28125 1.59375 1.28125")]
    [TestCase(120, 80, true, false, "backpack/hat_large", "20 8 170 108 uv -3.25 -2 4.25 3")]
    [TestCase(120, 80, false, true, "backpack/hat", "45 8 145 108 uv -0.4375 -0.4375 1.4375 1.4375")]
    [TestCase(0, 0, false, false, "backpack/hat", "45 8 145 108 uv 0 0 1 1")]
    public void Paint_AnInventoryImageBranch_DrawsWhatTheEngineDraws(int sizeW, int sizeH, bool large, bool square, string material, string expected)
    {
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(sizeW, sizeH);

        panel.InventoryImageType = large ? TfEmbeddedItemModelPanel.ImageTypeLarge : TfEmbeddedItemModelPanel.ImageTypeSmall;
        panel.SetAnimationValue("force_square_image", square);
        panel.Paint(surface, Context());

        surface.SizesAsked.ShouldBe([material]);
        surface.SubRects.ShouldBe([expected]);
    }

    [Test]
    public void Paint_AGreyedOutInventoryImage_DrawsIt96Grey()
    {
        // `m_bGreyedOut`: DrawSetColor( 96, 96, 96, 255 ) (:819-822); the control, not greyed, is white.
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(120, 80);
        (TfEmbeddedItemModelPanel white, TextRecorder whiteSurface) = Panel(120, 80);

        panel.GreyedOut = true;
        panel.Paint(surface, Context());
        white.Paint(whiteSurface, Context());

        (surface.Calls.Contains("color 96 96 96 255"), whiteSurface.Calls.Contains("color 255 255 255 255")).ShouldBe((true, true));
    }

    /// <summary>The 140x100 panel at 1366x768 holding the hat, image data pos (12, 5), its image loaded.</summary>
    private static (TfEmbeddedItemModelPanel Panel, TextRecorder Surface) Panel(int sizeW, int sizeH)
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes($$"""
            "items_game" { "items" { "300" { "name" "Hat" "item_name" "#Hat" "item_slot" "head" "image_inventory" "backpack/hat"
              "image_inventory_pos_x" "12" "image_inventory_pos_y" "5" "image_inventory_size_w" "{{sizeW}}" "image_inventory_size_h" "{{sizeH}}" } } }
            """));
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel") { Wide = 140, Tall = 100 };
        SceneItem hat = new(40, "CTFWearable", 300, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: false) { Quality = 6 };

        panel.SetItem(TfItemView.From(hat, schema));
        panel.LoadInventoryImage();
        return (panel, new TextRecorder());
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
