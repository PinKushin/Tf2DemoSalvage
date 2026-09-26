using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::ScalableImagePanel` (vgui2/vgui_controls/ScalableImagePanel.cpp): a nine-slice of one texture.</summary>
/// <remarks>
/// `PerformLayout` (:228) turns `src_corner_*` into a fraction of the texture's size; `PaintBackground` (:78) draws three
/// rows of three quads, the corners `draw_corner_*` (scaled on a proportional panel) and the middle what is left, at least
/// 0, in `drawcolor` with the panel's alpha. The panel is 100 by 50 and the texture 64 by 64 (<see cref="TextRecorder"/>).
/// </remarks>
public sealed class VguiScalableImagePanelConformanceTests
{
    private const string Corners = "\"image\" \"hud/x\" \"src_corner_width\" \"16\" \"src_corner_height\" \"8\" \"draw_corner_width\" \"10\" \"draw_corner_height\" \"5\"";

    [Test]
    public void PaintBackground_NineSlice_CornersAtTheDrawSizeAndTheMiddleStretched() =>
        Painted(Corners).SubRects.ShouldBe(
        [
            "0 0 10 5 uv 0 0 0.25 0.125", "10 0 90 5 uv 0.25 0 0.75 0.125", "90 0 100 5 uv 0.75 0 1 0.125",
            "0 5 10 45 uv 0 0.125 0.25 0.875", "10 5 90 45 uv 0.25 0.125 0.75 0.875", "90 5 100 45 uv 0.75 0.125 1 0.875",
            "0 45 10 50 uv 0 0.875 0.25 1", "10 45 90 50 uv 0.25 0.875 0.75 1", "90 45 100 50 uv 0.75 0.875 1 1",
        ]);

    [Test]
    public void PaintBackground_ImageAndColor_AreTheVguiTextureInTheDrawColorWithThePanelAlpha()
    {
        TextRecorder surface = Painted(Corners + " \"drawcolor\" \"9 8 7 6\" \"alpha\" \"100\"");

        surface.Calls[0].ShouldBe("color 9 8 7 100");
        surface.Calls[1].ShouldBe("texture vgui/hud/x");
    }

    [Test]
    public void PaintBackground_CornersWiderThanThePanel_LeaveAnEmptyMiddle() =>
        Painted(Corners.Replace("\"draw_corner_width\" \"10\"", "\"draw_corner_width\" \"60\"", System.StringComparison.Ordinal))
            .SubRects[1].ShouldBe("60 0 60 5 uv 0.25 0 0.75 0.125");

    [Test]
    public void ApplySettings_Proportional_ScalesTheDrawCornersOnly() =>
        Painted(Corners, proportional: true).SubRects[0].ShouldBe("0 0 20 10 uv 0 0 0.25 0.125", "960 tall doubles the draw corner, not the source");

    [Test]
    public void Create_ScalableImagePanel_IsAVguiScalableImagePanel() =>
        VguiControlFactory.Create("ScalableImagePanel").ShouldBeOfType<VguiScalableImagePanel>();

    private static TextRecorder Painted(string keys, bool proportional = false)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        TextRecorder surface = new();
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 960), scheme.Find("Fonts")!, 1280, 960, "english") { Surface = surface };
        VguiScalableImagePanel panel = new(null, "Image") { Proportional = proportional };
        string body = $"\"Image\" {{ {keys} \"wide\" \"100\" \"tall\" \"50\" }}";

        panel.ApplySettings(
            KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null).Find("Image")!, context);
        panel.PerformApplySchemeSettings(context);
        panel.Think();
        panel.PaintBackground(surface, context);

        return surface;
    }
}
