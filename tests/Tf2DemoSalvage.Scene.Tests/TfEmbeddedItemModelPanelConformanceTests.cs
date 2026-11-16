using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CEmbeddedItemModelPanel::Paint`'s inventory image branch (game/client/econ/item_model_panel.cpp:747-870, B759).</summary>
public sealed class TfEmbeddedItemModelPanelConformanceTests
{
    [Test]
    public void Paint_ASmallInventoryImageOfAnotherRatioOnA1366By768Screen_FitsItToTheHeightAroundItsShiftedCentre()
    {
        // Worked by hand from :752-799 for a 140x100 panel, image data pos (12, 5) size 120x80 (not the 128x82 defaults),
        // the texture 64x64: texW = 120/64 = 1.875, texH = 80/64 = 1.25, texX = (1 - 1.875) * 0.5 = -0.4375,
        // texY = (1 - 1.25) * 0.5 = -0.125; x = (int)(0 + XRES(12)) = (int)25.6125 = 25 (truncated, not rounded to 26); the
        // ratio 1.5 is not 1.4, so the centre is (int)(25 + 70) = 95, the width (int)(100 * 1.5f) = 150, x = (int)(95 - 75) = 20.
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(120, 80);

        panel.Paint(surface, Context());

        // y = (int)(0 + YRES(5)) = (int)8.0 = 8. The small type's own material, drawn white (not greyed out).
        surface.SubRects.ShouldBe(["20 8 170 108 uv -0.4375 -0.125 1.4375 1.125"]);
        surface.SizesAsked.ShouldBe(["backpack/hat"]);
        surface.Calls.ShouldContain("texture backpack/hat");
    }

    /// <remarks>
    /// Each worked by hand for the same 140x100 panel, image data pos (12, 5) — so x starts at 25 and y at 8.
    /// <list type="bullet">
    /// <item>Equal ratio, 140x100, a 64x64 texture: 140/100 is the panel's own 1.4, so no fit (:792-793); texW 2.1875, texH
    /// 1.5625. A fit at the panel's own ratio draws the same rectangle, so this case pins the small type's UV, not the fit.</item>
    /// <item>Large (`IMAGETYPE_LARGE`), 120x80, a 64x32 texture: "Normal is 128*128, large is 512x512", so 480x320
    /// (:774-779), the `_large` material; texW 480/64 = 7.5, texH 320/32 = 10, so texX -3.25, texY -4.5 (a swapped mapping
    /// would give 15 and 5); ratio 1.5, so the fit as the small case: 20 to 170.</item>
    /// <item>`force_square_image`, 120x80: MAX( 120, 80 ) both ways (:761-765), ratio 1, width (int)(100 * 1f) = 100,
    /// x = (int)(95 - 50) = 45.</item>
    /// <item>Zero size, a 64x32 texture: the material's own mapping size (:766-770), the whole texture, ratio 2: width 200,
    /// x = (int)(95 - 100) = -5. Swapping the mapping's width and height would give ratio 0.5, 70 to 120.</item>
    /// </list>
    /// A large zero-size image cannot be told apart from a small one by its rectangle: `*4` runs only on a stated size, and
    /// moving it before or after the zero check still leaves 0 or a whole-texture UV. Square-then-large is the same: MAX
    /// and `*4` commute.
    /// </remarks>
    [TestCase(140, 100, 64, false, false, "backpack/hat", "25 8 165 108 uv -0.59375 -0.28125 1.59375 1.28125")]
    [TestCase(120, 80, 32, true, false, "backpack/hat_large", "20 8 170 108 uv -3.25 -4.5 4.25 5.5")]
    [TestCase(120, 80, 64, false, true, "backpack/hat", "45 8 145 108 uv -0.4375 -0.4375 1.4375 1.4375")]
    [TestCase(0, 0, 32, false, false, "backpack/hat", "-5 8 195 108 uv 0 0 1 1")]
    public void Paint_AnImageSizeAndTypeOnA1366By768Screen_DrawsTheRectangleAndUvWorkedFromTheEngine(
        int sizeW, int sizeH, int mappingTall, bool large, bool square, string material, string expected)
    {
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(sizeW, sizeH, large);

        surface.TextureSize = (64, mappingTall);
        panel.SetAnimationValue("force_square_image", square);
        panel.Paint(surface, Context());

        surface.SizesAsked.ShouldBe([material]);
        surface.SubRects.ShouldBe([expected]);
    }

    [Test]
    public void Paint_AGreyedOutInventoryImage_DrawsIt96Grey()
    {
        // `m_bGreyedOut`: DrawSetColor( 96, 96, 96, 255 ) between the texture and the draw (:821-831); the control, not
        // greyed, is white.
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(120, 80);
        (TfEmbeddedItemModelPanel white, TextRecorder whiteSurface) = Panel(120, 80);

        panel.GreyedOut = true;
        panel.Paint(surface, Context());
        white.Paint(whiteSurface, Context());

        surface.Calls.ShouldBe(["texture backpack/hat", "color 96 96 96 255", "subrect 20 8 170 108"]);
        whiteSurface.Calls.ShouldBe(["texture backpack/hat", "color 255 255 255 255", "subrect 20 8 170 108"]);
    }

    /// <remarks>
    /// The overlays (:833-870), over the image in the same rectangle, each tinted by `GetModifiedRGBValue( i == 0 )`: the
    /// FIRST takes the alternate (BLU, `set item tint rgb 2`) colour 0x2D2D24 = 45 45 36, the second the primary 0xE7B53B =
    /// 231 181 59. Greyed out, each channel is truncated after `* 96/255`: 45 is (int)16.94 = 16, 36 is 13; 231 is 86, 181
    /// is 68, 59 is 22. Large, each overlay is its `_large` material (`GetMaterialForImage`). Every overlay takes the image's
    /// own UV (:869): small -0.4375 -0.125 1.4375 1.125, large (480x320 on 64x64) -3.25 -2 4.25 3.
    /// </remarks>
    [TestCase(false, false, "overlay_a", "overlay_b", "45 45 36", "231 181 59", "-0.4375 -0.125 1.4375 1.125")]
    [TestCase(true, false, "overlay_a", "overlay_b", "16 16 13", "86 68 22", "-0.4375 -0.125 1.4375 1.125")]
    [TestCase(false, true, "overlay_a_large", "overlay_b_large", "45 45 36", "231 181 59", "-3.25 -2 4.25 3")]
    [TestCase(true, true, "overlay_a_large", "overlay_b_large", "16 16 13", "86 68 22", "-3.25 -2 4.25 3")]
    public void Paint_TwoOverlaysOnATwoTonePaintedItem_TintTheFirstAlternateAndTheSecondPrimary(
        bool greyed, bool large, string first, string second, string firstColor, string secondColor, string uv)
    {
        (TfEmbeddedItemModelPanel panel, TextRecorder surface) = Panel(120, 80, large, painted: true);

        panel.GreyedOut = greyed;
        panel.Paint(surface, Context());

        // Each in the image's own rectangle: ratio 1.5, 20 to 170, the same at either size.
        surface.Calls.ShouldBe([
            large ? "texture backpack/hat_large" : "texture backpack/hat", greyed ? "color 96 96 96 255" : "color 255 255 255 255", "subrect 20 8 170 108",
            "texture " + first, $"color {firstColor} 255", "subrect 20 8 170 108",
            "texture " + second, $"color {secondColor} 255", "subrect 20 8 170 108",
        ]);
        surface.SubRects.ShouldBe(["20 8 170 108 uv " + uv, "20 8 170 108 uv " + uv, "20 8 170 108 uv " + uv]);
    }

    /// <summary>The 140x100 panel at 1366x768 holding the hat, image data pos (12, 5), its image loaded.</summary>
    /// <param name="sizeW">`image_inventory_size_w`.</param>
    /// <param name="sizeH">`image_inventory_size_h`.</param>
    /// <param name="large">Whether the panel draws the large image.</param>
    /// <param name="painted">Whether the hat has two overlays and carries a two-tone paint.</param>
    private static (TfEmbeddedItemModelPanel Panel, TextRecorder Surface) Panel(int sizeW, int sizeH, bool large = false, bool painted = false)
    {
        string overlays = painted ? "\"image_inventory_overlay\" \"overlay_a\" \"image_inventory_overlay2\" \"overlay_b\"" : string.Empty;
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes($$"""
            "items_game"
            {
                "items" { "300" { "name" "Hat" "item_name" "#Hat" "item_slot" "head" "image_inventory" "backpack/hat" {{overlays}}
                  "image_inventory_pos_x" "12" "image_inventory_pos_y" "5" "image_inventory_size_w" "{{sizeW}}" "image_inventory_size_h" "{{sizeH}}" } }
                "attributes" { "142" { "name" "set item tint rgb" } "261" { "name" "set item tint rgb 2" } }
            }
            """));
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel")
        {
            Wide = 140,
            Tall = 100,
            InventoryImageType = large ? TfEmbeddedItemModelPanel.ImageTypeLarge : TfEmbeddedItemModelPanel.ImageTypeSmall,
        };
        EconAttributeValue[] paint = painted
            ? [new(142, System.BitConverter.SingleToInt32Bits(0xE7B53B)), new(261, System.BitConverter.SingleToInt32Bits(0x2D2D24))]
            : [];
        SceneItem hat = new(40, "CTFWearable", 300, new EconAttributeWire([], paint, HasValidItemId: painted), IsWeapon: false)
        {
            Quality = 6,
            ItemId = painted ? 77ul : SceneItem.InvalidItemId,
        };

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
