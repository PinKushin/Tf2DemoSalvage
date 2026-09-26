using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::ImagePanel` (vgui2/vgui_controls/ImagePanel.cpp) drawing a `vgui::Bitmap` (vgui2.dll 0x180002290).</summary>
/// <remarks>
/// The panel is 100 by 50 and every texture 64 by 64 (<see cref="TextRecorder"/>). `PaintBackground` (:118) fills when the
/// fill alpha is above 0, then draws the image at the origin in the draw colour: scaled by `scaleAmount`, else to the
/// panel; tiled from the origin while inside the panel; or at the texture's size.
/// </remarks>
public sealed class VguiImagePanelConformanceTests
{
    [Test]
    public void PaintBackground_AnImage_IsTheTextureAtItsOwnSizeUnderVgui() =>
        Painted("\"image\" \"hud/x\"").ShouldBe(["color 255 255 255 255", "texture vgui/hud/x", "textured 0 0 64 64"]);

    [Test]
    public void PaintBackground_NoImageNoFill_DrawsNothing() => Painted(string.Empty).ShouldBeEmpty();

    [Test]
    public void PaintBackground_AFillColor_FillsThePanelFirst() =>
        Painted("\"image\" \"hud/x\" \"fillcolor\" \"1 2 3\"")
            .ShouldBe(["color 1 2 3 255", "fill 0 0 100 50", "color 255 255 255 255", "texture vgui/hud/x", "textured 0 0 64 64"]);

    [Test]
    public void PaintBackground_AFillColorByName_IsTheSchemeColor() =>
        Painted("\"fillcolor\" \"Orange\"").ShouldBe(["color 255 128 0 255", "fill 0 0 100 50"]);

    [Test]
    public void PaintBackground_ATransparentFill_IsNotDrawn() => Painted("\"fillcolor\" \"1 2 3 0\"").ShouldBeEmpty();

    [Test]
    public void PaintBackground_DrawColorAndItsOverride_ColourTheImageWithTheOverrideWinning()
    {
        Painted("\"image\" \"x\" \"drawcolor\" \"9 8 7 6\"")[0].ShouldBe("color 9 8 7 6");
        Painted("\"image\" \"x\" \"drawcolor\" \"9 8 7 6\" \"drawcolor_override\" \"1 1 1 1\"")[0].ShouldBe("color 1 1 1 1");
    }

    [Test]
    public void PaintBackground_ScaleImage_StretchesToThePanel() =>
        Painted("\"image\" \"x\" \"scaleImage\" \"1\"")[^1].ShouldBe("textured 0 0 100 50");

    [Test]
    public void PaintBackground_ScaleAmount_ScalesTheTexture() =>
        Painted("\"image\" \"x\" \"scaleImage\" \"1\" \"scaleAmount\" \"0.5\"")[^1].ShouldBe("textured 0 0 32 32");

    [Test]
    public void PaintBackground_ScaleProportional_ScalesTheAmountByTheScreen()
    {
        // 960 tall: 0.5 × .001 × 2000 = 1.
        Painted("\"image\" \"x\" \"scaleImage\" \"1\" \"scaleAmount\" \"0.5\" \"scaleProportional\" \"1\"", proportional: true)[^1]
            .ShouldBe("textured 0 0 64 64");
        Painted("\"image\" \"x\" \"scaleImage\" \"1\" \"scaleAmount\" \"0.5\" \"scaleProportional\" \"1\"")[^1]
            .ShouldBe("textured 0 0 32 32", "a non-proportional panel's QuickPropScale is the value");
    }

    [Test]
    public void PaintBackground_ScaledTwice_RestoresTheTextureSizeBetween()
    {
        (VguiImagePanel panel, VguiContext context) = Built("\"image\" \"x\" \"scaleImage\" \"1\" \"scaleAmount\" \"0.5\"");
        TextRecorder surface = new();

        panel.PaintBackground(surface, context);
        panel.PaintBackground(surface, context);

        surface.Calls[^1].ShouldBe("textured 0 0 32 32", "0.5 of the restored 64, not of the previous 32");
    }

    [Test]
    public void PaintBackground_TileImage_RepeatsWhileInsideThePanel() =>
        Painted("\"image\" \"x\" \"tileImage\" \"1\"").FindAll(call => call.StartsWith("textured", System.StringComparison.Ordinal))
            .ShouldBe(["textured 0 0 64 64", "textured 64 0 128 64"]);

    [Test]
    public void PaintBackground_TileVerticallyOnly_IsOneColumn() =>
        Painted("\"image\" \"x\" \"tileVertically\" \"1\" \"tall\" \"100\"")
            .FindAll(call => call.StartsWith("textured", System.StringComparison.Ordinal))
            .ShouldBe(["textured 0 0 64 64", "textured 0 64 64 128"]);

    [Test]
    public void Create_ImagePanel_IsAVguiImagePanel() => VguiControlFactory.Create("imagepanel").ShouldBeOfType<VguiImagePanel>();

    private static List<string> Painted(string keys, bool proportional = false)
    {
        (VguiImagePanel panel, VguiContext context) = Built(keys, proportional);
        TextRecorder surface = new();

        panel.PaintBackground(surface, context);

        return surface.Calls;
    }

    private static (VguiImagePanel Panel, VguiContext Context) Built(string keys, bool proportional = false)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""Scheme { Colors { "Orange" "255 128 0 255" } Borders { } Fonts { } }"""), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 960), scheme.Find("Fonts")!, 1280, 960, "english");
        VguiImagePanel panel = new(null, "Image") { Proportional = proportional };
        string body = $"\"Image\" {{ {keys} \"wide\" \"100\" \"tall\" \"50\" }}";

        panel.ApplySettings(
            KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null).Find("Image")!, context);
        panel.PerformApplySchemeSettings(context);

        return (panel, context);
    }
}
