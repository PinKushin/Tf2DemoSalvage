using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What a panel tree draws and in what order, as `Panel::PaintTraverse` and `Panel::PaintBackground` do.</summary>
/// <remarks>
/// Panel.cpp:1128 — invisible panels and their children draw nothing; a first-painting border, then the background
/// without inset, then `Paint` with it, then the children in z order, then a last-painting border; the alpha multiplier is
/// the parent's times `alpha / 255`. Panel.cpp:1301 — background type 0 fills, 1 is `Texture1` stretched, 2 is a box with
/// rounded corner textures of `max( scale(8) / 2, 8 )` on a proportional panel.
/// </remarks>
public sealed class VguiPaintConformanceTests
{
    private const int ScreenTall = 1080;

    [Test]
    public void PaintTraverse_AParentAndChild_PaintsBackgroundThenChildThenBorderLast()
    {
        VguiContext context = Context();
        VguiPanel parent = new(null, "Parent") { BgColor = (1, 1, 1, 255), Border = context.Borders.Get("Edge") };
        _ = new VguiPanel(parent, "Child") { BgColor = (2, 2, 2, 255), Wide = 10, Tall = 5 };
        RecordingSurface surface = new();

        VguiLayout.InternalSolveTraverse(parent);
        parent.PaintTraverse(surface, context);

        surface.Calls.ShouldBe([
            "push Parent inset=False", "color 1 1 1 255", "fill 0 0 64 24", "pop Parent",
            "push Parent inset=True", "pop Parent",
            "push Child inset=False", "color 2 2 2 255", "fill 0 0 10 5", "pop Child",
            "push Child inset=True", "pop Child",
            "push Parent inset=False", "color 9 9 9 255", "fill 0 0 2 24", "pop Parent",
        ]);
    }

    /// <remarks>
    /// `Border::Paint( VPANEL )` (vgui2.dll 0x180002b80): a line is `scale( proportional_scalar × 1000 ) / 1000` pixels
    /// thick once that reaches 2000 — 2 at 1080 tall — and each side's lines step inward one pixel apiece, trimmed at their
    /// ends by `offset`'s two numbers times that thickness (`Paint2`, 0x1800027b0).
    /// </remarks>
    [Test]
    public void PaintBorder_EachSide_StepsInwardAndTrimsByItsOffsets()
    {
        VguiContext context = Context();
        VguiPanel panel = new(null, "Panel") { Wide = 100, Tall = 50, Border = context.Borders.Get("Sides") };
        RecordingSurface surface = new();

        panel.Border!.Paint(surface, panel, context);

        surface.Calls.ShouldBe([
            "color 1 0 0 255", "fill 0 0 2 50", "color 2 0 0 255", "fill 1 2 3 44",
            "color 3 0 0 255", "fill 0 0 100 2",
            "color 4 0 0 255", "fill 98 0 100 50",
            "color 5 0 0 255", "fill 0 48 100 50",
        ]);
    }

    /// <remarks>
    /// `ScalableImageBorder::Paint` (vgui2.dll 0x18000c290): three rows of three quads — corner, middle, corner — the
    /// middle `max( size − 2 · corner, 0 )` wide in pixels and `max( 1 − 2 · uv, 0 )` in texture space, where a corner's uv
    /// is `src_corner / texture size`. The `x` it is handed is ignored: columns start at 0.
    /// </remarks>
    [Test]
    public void PaintBorder_AScalableImage_DrawsNineSlices()
    {
        VguiContext context = Context();
        VguiPanel panel = new(null, "Panel") { Wide = 100, Tall = 50, Border = context.Borders.Get("Box") };
        RecordingSurface surface = new();

        panel.Border!.Paint(surface, panel, context);

        surface.Calls.ShouldBe([
            "color 255 255 255 255", "texture vgui/box",
            "subrect 0 0 9 18 0 0 0.25 0.125", "subrect 9 0 91 18 0.25 0 0.75 0.125", "subrect 91 0 100 18 0.75 0 1 0.125",
            "subrect 0 18 9 32 0 0.125 0.25 0.875", "subrect 9 18 91 32 0.25 0.125 0.75 0.875", "subrect 91 18 100 32 0.75 0.125 1 0.875",
            "subrect 0 32 9 50 0 0.875 0.25 1", "subrect 9 32 91 50 0.25 0.875 0.75 1", "subrect 91 32 100 50 0.75 0.875 1 1",
        ]);
    }

    [Test]
    public void PaintBorder_ATiledImage_RepeatsTheTextureFromTheOrigin()
    {
        VguiContext context = Context();
        VguiPanel panel = new(null, "Panel") { Wide = 100, Tall = 50, Border = context.Borders.Get("Tiles") };
        RecordingSurface surface = new();

        panel.Border!.Paint(surface, panel, context);

        surface.Calls.ShouldBe([
            "color 255 255 255 255", "texture vgui/tile",
            "textured 0 0 64 64", "textured 64 0 128 64",
        ]);
    }

    [Test]
    public void PaintTraverse_AnInvisiblePanel_DrawsNeitherItNorItsChildren()
    {
        VguiPanel parent = new(null, "Parent") { Visible = false };
        _ = new VguiPanel(parent, "Child");
        RecordingSurface surface = new();

        VguiLayout.InternalSolveTraverse(parent);
        parent.PaintTraverse(surface, Context());

        surface.Calls.ShouldBeEmpty();
    }

    [Test]
    public void PaintTraverse_AnEmptyClip_TraversesChildrenWithoutDrawing()
    {
        VguiPanel parent = new(null, "Parent") { Wide = 0 };
        _ = new VguiPanel(parent, "Child");
        RecordingSurface surface = new();

        VguiLayout.InternalSolveTraverse(parent);
        parent.PaintTraverse(surface, Context());

        surface.Calls.ShouldNotContain(call => call.StartsWith("fill", System.StringComparison.Ordinal));
    }

    [Test]
    public void PaintTraverse_AlphaMultiplies_DownTheTree()
    {
        VguiContext context = Context();
        VguiEditablePanel parent = new(null, "Parent");
        _ = new VguiPanel(parent, "Child");
        RecordingSurface surface = new();

        parent.LoadControlSettings(Block("""
            "Parent" { "alpha" "127.5" }
            "Child" { "alpha" "51" }
            """), context);
        VguiLayout.InternalSolveTraverse(parent);
        parent.PaintTraverse(surface, context);

        surface.Multipliers["Child"].ShouldBe(127.5f / 255f * 51f / 255f, 1e-6f);
        surface.AlphaMultiplier.ShouldBe(1f, "restored after the traverse");
    }

    [Test]
    public void PaintBackground_TypeTwo_DrawsStripsThenRoundedCorners()
    {
        VguiContext context = Context();
        VguiEditablePanel parent = new(null, "Parent") { Proportional = true };
        VguiPanel child = new(parent, "Child");
        RecordingSurface surface = new();

        parent.LoadControlSettings(Block("""
            "Child" { "PaintBackgroundType" "2" "wide" "40" "tall" "20" "bgcolor_override" "5 6 7 8" }
            """), context);
        child.PaintBackground(surface, context);

        // 40 and 20 at 1080 are 90 and 45; corners are max( scale(8) / 2, 8 ) = max( 18 / 2, 8 ) = 9.
        surface.Calls.ShouldBe([
            "color 5 6 7 8",
            "fill 9 0 81 9", "fill 0 9 90 36", "fill 9 36 81 45",
            "texture vgui/hud/8x800corner1", "textured 0 0 9 9",
            "texture vgui/hud/8x800corner2", "textured 81 0 90 9",
            "texture vgui/hud/8x800corner4", "textured 0 36 9 45",
            "texture vgui/hud/8x800corner3", "textured 81 36 90 45",
        ]);
    }

    [Test]
    public void PaintBackground_NoSettingsEverApplied_HasNoCornerTexturesSoTypeTwoDrawsNothing()
    {
        VguiPanel panel = new(null, "Bare");
        RecordingSurface surface = new();

        panel.SetAnimationValue("PaintBackgroundType", 2);
        panel.PaintBackground(surface, Context());

        surface.Calls.ShouldBeEmpty("`Init` leaves the texture ids -1; only the defaults pass sets them");
    }

    [Test]
    public void Border_Set_TakesItsInsetAndClampedBackgroundType()
    {
        VguiContext context = Context();
        VguiPanel panel = new(null, "Panel") { Border = context.Borders.Get("Line") };

        panel.Inset.ShouldBe((1, 2, 3, 4));
        panel.GetInt("PaintBackgroundType").ShouldBe(2, "backgroundtype 7, clamped to 0..2");
    }

    private static KeyValuesTree Block(string body) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null);

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    Colors { "Grey" "9 9 9 255" }
                    Borders
                    {
                        "Line" { "inset" "1 2 3 4" "backgroundtype" "7" "Left" { "1" { "color" "Grey" } } }
                        "Edge" { "Left" { "1" { "color" "Grey" } } }
                        "Box"
                        {
                            "bordertype" "scalable_image" "image" "box"
                            "src_corner_height" "8" "src_corner_width" "16" "draw_corner_height" "8" "draw_corner_width" "4"
                        }
                        "Tiles" { "bordertype" "image" "image" "tile" "tiled" "1" }
                        "Sides"
                        {
                            "Left" { "1" { "color" "1 0 0 255" } "2" { "color" "2 0 0 255" "offset" "1 3" } }
                            "Top" { "1" { "color" "3 0 0 255" } }
                            "Right" { "1" { "color" "4 0 0 255" } }
                            "Bottom" { "1" { "color" "5 0 0 255" } }
                        }
                    }
                    Fonts { }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, ScreenTall), scheme.Find("Fonts")!, 1920, ScreenTall, "english");
    }

    private sealed class RecordingSurface : IVguiSurface
    {
        private readonly Stack<string> _current = new();

        public List<string> Calls { get; } = [];

        public Dictionary<string, float> Multipliers { get; } = [];

        public float AlphaMultiplier { get; set; } = 1f;

        public void PushMakeCurrent(VguiPanel panel, bool useInset)
        {
            _current.Push(panel.Name);
            Multipliers[panel.Name] = AlphaMultiplier;
            Calls.Add($"push {panel.Name} inset={useInset}");
        }

        public void PopMakeCurrent(VguiPanel panel) => Calls.Add($"pop {_current.Pop()}");

        public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
            Calls.Add($"color {color.Red} {color.Green} {color.Blue} {color.Alpha}");

        public void DrawFilledRect(int x0, int y0, int x1, int y1) => Calls.Add(Rect("fill", x0, y0, x1, y1));

        public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal) =>
            Calls.Add(Rect("fade", x0, y0, x1, y1) + string.Create(CultureInfo.InvariantCulture, $" {alpha0} {alpha1} {horizontal}"));

        public void DrawOutlinedRect(int x0, int y0, int x1, int y1) => Calls.Add(Rect("outline", x0, y0, x1, y1));

        public void DrawSetTexture(string texture) => Calls.Add($"texture {texture}");

        public void DrawTexturedRect(int x0, int y0, int x1, int y1) => Calls.Add(Rect("textured", x0, y0, x1, y1));

        public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1) =>
            Calls.Add(Rect("subrect", x0, y0, x1, y1) + string.Create(CultureInfo.InvariantCulture, $" {s0} {t0} {s1} {t1}"));

        public void DrawTexturedQuad(float x0, float y0, float x1, float y1, float s0, float t0, float s1, float t1) =>
            Calls.Add(string.Create(CultureInfo.InvariantCulture, $"quad {x0} {y0} {x1} {y1} {s0} {t0} {s1} {t1}"));

        public (int Wide, int Tall) DrawGetTextureSize(string texture) => (64, 64);

        public void DrawSetTextFont(VguiFontAmalgam font) => Calls.Add("font");

        public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
            Calls.Add($"text color {color.Red} {color.Green} {color.Blue} {color.Alpha}");

        public void DrawSetTextPos(int x, int y) => Calls.Add(string.Create(CultureInfo.InvariantCulture, $"text pos {x} {y}"));

        public (int X, int Y) DrawGetTextPos() => (0, 0);

        public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default) => Calls.Add($"char {character}");

        public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default) => Calls.Add($"print {text}");

        public int GetFontTall(VguiFontAmalgam font) => 0;

        public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character) => (0, 0, 0);

        public int GetCharacterWidth(VguiFontAmalgam font, char character) => 0;

        private static string Rect(string kind, int x0, int y0, int x1, int y1) =>
            string.Create(CultureInfo.InvariantCulture, $"{kind} {x0} {y0} {x1} {y1}");
    }
}
