using System.Linq;
using System.Text;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`CFPSPanel` on the tools root, drawn through VGUI: when it shows, where it sits, where each line goes.</summary>
/// <remarks>
/// `game/client/vgui_fpspanel.cpp`: hidden until an `OnTick` (every 250 ms) finds either readout on; placed at
/// <c>wide − 300</c>, 0; the frame-rate line at panel (2, 2), position lines each <c>tall + 2</c> below, one counter across
/// both. `DefaultFixedOutline` here is 10 tall with an outline, so 12.
/// </remarks>
public sealed class FpsPanelTests
{
    private const int Wide = 1920;
    private const int Tall = 1080;
    /// <summary>600 fps, so `%3i` has no leading space and the line's first glyph sits at the pen.</summary>
    private const double Frame = 1d / 600d;

    [Test]
    public void Frame_BeforeTheFirstTick_DrawsNothing()
    {
        Rig tools = Tools();

        tools.Draw(Wide, Tall, 0.0, Frame, FpsMeter.Instantaneous, default, "cp_process_f12");

        tools.Draw(Wide, Tall, 0.1, Frame, FpsMeter.Instantaneous, default, "cp_process_f12").Quads.ShouldBeEmpty();
    }

    [Test]
    public void Frame_AfterATickWithTheMeterOn_DrawsItsLineAtTheTopOfThePanel()
    {
        Rig tools = Tools();
        VguiDrawList list = Shown(tools, FpsMeter.Instantaneous, default);

        list.Quads.ShouldNotBeEmpty();
        // Pen at panel x 2; the outline widens a to −1 after the first query, so floor( −1 + 0.6 ) puts it a pixel left.
        list.Quads.Min(quad => quad.X0).ShouldBe(Wide - FpsPanel.PanelWidth + 2 - 1);
        list.Quads.Min(quad => quad.Y0).ShouldBe(2);
        tools.Fps.Tall.ShouldBe((4 * 12) + 8);
    }

    [Test]
    public void Frame_WithEverythingOff_StaysHidden()
    {
        Rig tools = Tools();

        Shown(tools, FpsMeter.Hidden, default).Quads.ShouldBeEmpty();
        tools.Fps.Visible.ShouldBeFalse();
    }

    [Test]
    public void Frame_WithBothOn_PutsThePositionALineBelowTheFrameRate()
    {
        VguiDrawList list = Shown(Tools(), FpsMeter.Instantaneous, Somewhere);

        list.Quads.Select(quad => quad.Y0).Distinct().Order().Take(2).ShouldBe([2f, 2f + 12 + 2]);
    }

    [Test]
    public void Frame_WithOnlyThePositionOn_StartsOnTheTopLine() =>
        Shown(Tools(), FpsMeter.Hidden, Somewhere).Quads.Min(quad => quad.Y0).ShouldBe(2);

    [Test]
    public void Frame_OnAScreenNarrowerThanThePanel_PutsItOffTheLeftEdgeAsTf2Does()
    {
        Rig tools = Tools();

        tools.Draw(200, Tall, 0.0, Frame, FpsMeter.Instantaneous, default, null);

        tools.Fps.X.ShouldBe(200 - FpsPanel.PanelWidth);
    }

    private static PositionReadout Somewhere => new(PositionReadout.View, (1802f, -679f, 373f), (0f, 90f, 0f), default, default, 0f);

    /// <summary>Past the first frame (which the meter never draws) and past the first tick.</summary>
    private static VguiDrawList Shown(Rig tools, int mode, PositionReadout position)
    {
        tools.Draw(Wide, Tall, 0.0, Frame, mode, position, "cp_process_f12");
        tools.Draw(Wide, Tall, 0.3, Frame, mode, position, "cp_process_f12");

        return tools.Draw(Wide, Tall, 0.31, Frame, mode, position, "cp_process_f12");
    }

    private static Rig Tools()
    {
        byte[] scheme = Encoding.UTF8.GetBytes("""
            Scheme
            {
                Fonts
                {
                    "DefaultFixedOutline" { "1" { "name" "Lucida Console" "tall" "10" "weight" "0" "outline" "1" } }
                }
            }
            """);

        VguiSurfaceHost host = new(path => path == "resource/SourceScheme.res" ? scheme : null, _ => null, new SolidGdi(), _ => (0, 0));

        return new Rig(host, new VguiTools(host));
    }

    /// <summary>The tools panel on its own surface, a frame at a time, as `MainForm.BuildOverlay` drives it.</summary>
    private sealed class Rig(VguiSurfaceHost host, VguiTools tools)
    {
        public FpsPanel Fps => tools.Fps;

        public VguiDrawList Draw(int wide, int tall, double realtime, double frameSeconds, int mode, PositionReadout position, string? mapName)
        {
            host.BeginFrame(wide, tall);
            tools.Frame(realtime, frameSeconds, mode, position, mapName);

            return host.List;
        }
    }

    /// <summary>A GDI whose every glyph is a solid block, 6 wide in a 10-tall font: placement, not shapes, is under test.</summary>
    private sealed class SolidGdi : IVguiGdi
    {
        private (int Wide, int Tall) _bitmap;

        public bool AddFontResource(string path) => true;

        public bool FamilyExists(string family) => true;

        public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality) =>
            new(face, 10, 8, 6);

        public void CreateBitmap(VguiGdiFont font, int wide, int tall) => _bitmap = (wide, tall);

        public (int A, int B, int C)? GetCharAbcWidths(VguiGdiFont font, char character) => (0, 6, 0);

        public int? GetTextExtent(VguiGdiFont font, char character) => 6;

        public VguiGrayGlyph? GetGlyphOutlineGray8(VguiGdiFont font, int character) => null;

        public byte[] DrawGlyph(VguiGdiFont font, char character, int penX, int clearWide, int clearTall) =>
            Enumerable.Repeat((byte)255, _bitmap.Wide * _bitmap.Tall * 4).ToArray();
    }
}
