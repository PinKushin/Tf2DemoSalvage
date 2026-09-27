using System.Linq;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::RichText` (vgui2/vgui_controls/RichText.cpp): its streams, line breaks, scrolling and paint.</summary>
/// <remarks>
/// Against <see cref="TextRecorder"/>: every glyph 10 wide ('.' 4), a font 12 tall. Draw offsets 3 and 1, so a line is
/// 13 tall and a 100-wide panel has 97 across.
/// </remarks>
public sealed class VguiRichTextConformanceTests
{
    [Test]
    public void Paint_OneLine_StartsAtTheDrawOffsets()
    {
        TextRecorder surface = Painted(text => text.InsertString("ab"));

        surface.Glyphs.ShouldBe(["a@3,1", "b@13,1"]);
    }

    [Test]
    public void RecalculateLineBreaks_AWordPastTheEdge_WrapsTheWholeWord()
    {
        // The space after "bbbb" brings the line to 100, past 97 less the offset; the break goes back to where "bbbb"
        // began, since that word did not start the line.
        TextRecorder surface = Painted(text => text.InsertString("aaaa bbbb cc"));

        string.Join(" ", surface.Glyphs).ShouldBe("a@3,1 a@13,1 a@23,1 a@33,1  @43,1 b@3,14 b@13,14 b@23,14 b@33,14  @43,14 c@53,14 c@63,14");
    }

    [Test]
    public void Paint_ABreakOnASpace_SkipsTheSpaceAtTheLineStart()
    {
        // Nine a's are 90; the space makes 100 and breaks there, as the word began the line. The next line starts at "bb".
        TextRecorder surface = Painted(text => text.InsertString("aaaaaaaaa bb"));

        (surface.Glyphs[^2], surface.Glyphs[^1], surface.Glyphs.Count).ShouldBe(("b@3,14", "b@13,14", 11));
    }

    [Test]
    public void RecalculateLineBreaks_ANewline_BreaksThere()
    {
        TextRecorder surface = Painted(text => text.InsertString("a\nb"));

        surface.Glyphs.ShouldBe(["a@3,1", "b@3,14"]);
    }

    [Test]
    public void InsertColorChange_TwoColours_DrawsEachRunInItsColour()
    {
        TextRecorder surface = Painted(text =>
        {
            text.InsertColorChange((255, 0, 0, 255));
            text.InsertString("ab");
            text.InsertColorChange((0, 0, 255, 255));
            text.InsertString("cd");
        });

        surface.Calls.Where(call => call.StartsWith("text color", System.StringComparison.Ordinal))
            .ShouldBe(["text color 255 0 0 255", "text color 0 0 255 255"]);
    }

    [Test]
    public void InsertColorChange_TheSameColourTwice_AddsNothing()
    {
        VguiRichText text = new(null, "t");

        // At the start the first item takes the colour in place; after text, the same colour again adds nothing.
        text.InsertColorChange((255, 0, 0, 255));
        text.InsertString("ab");
        text.InsertColorChange((255, 0, 0, 255));
        text.FormatStreamCount.ShouldBe(1);

        text.InsertColorChange((0, 0, 255, 255));
        text.FormatStreamCount.ShouldBe(2, "the control");
    }

    [Test]
    public void CalculateFade_InTheLastLengthBeforeTheSustainEnds_FadesTheAlpha()
    {
        // Fade start = now + sustain; alpha is ( start - now ) / length of the original, clamped.
        double clock = 100;
        TextRecorder surface = Painted(
            text =>
            {
                text.InsertColorChange((255, 255, 255, 255));
                text.InsertString("ab");
                text.InsertFade(10f, 2f);
            },
            () => clock + 9);

        surface.Calls.ShouldContain("text color 255 255 255 127");
    }

    [Test]
    public void CalculateFade_PastTheSustain_DrawsNothing()
    {
        TextRecorder surface = Painted(
            text =>
            {
                text.InsertColorChange((255, 255, 255, 255));
                text.InsertString("ab");
                text.InsertFade(10f, 2f);
            },
            () => 100 + 11);

        (surface.Glyphs.Count, surface.Calls.Contains("text color 255 255 255 0")).ShouldBe((0, true));
    }

    [Test]
    public void LayoutVerticalScrollBarSlider_AtTheEnd_FollowsNewLines()
    {
        // 30 tall shows two 13-tall lines. The first think leaves the slider at the end; later lines keep it there.
        VguiRichText text = new(null, "t") { Wide = 100, Tall = 30, Clock = () => 0, TextFont = new VguiFontAmalgam(), Surface = new TextRecorder() };
        text.SetVerticalScrollbar(false);
        text.SetFgColor((255, 255, 255, 255));

        text.InsertString("a\n");
        text.Think();
        text.InsertString("b\nc\nd");
        text.Think();

        TextRecorder surface = new();
        text.Paint(surface, null!);

        string.Concat(surface.Glyphs.Select(glyph => glyph[0])).ShouldBe("cd");
    }

    [Test]
    public void InsertChar_PastTheMaximum_CullsHalf()
    {
        VguiRichText text = new(null, "t");

        text.SetMaximumCharCount(10);
        text.InsertString("0123456789AB");

        text.Text.ShouldBe("56789AB");
    }

    [Test]
    public void InsertString_ACarriageReturn_IsThrownAway()
    {
        VguiRichText text = new(null, "t");

        text.InsertString("a\r\nb");

        text.Text.ShouldBe("a\nb");
    }

    private static TextRecorder Painted(System.Action<VguiRichText> fill, System.Func<double>? clock = null)
    {
        double start = 100;
        double now = start;
        VguiRichText text = new(null, "t") { Wide = 100, Tall = 100, Clock = () => now, TextFont = new VguiFontAmalgam(), Surface = new TextRecorder() };

        text.SetVerticalScrollbar(false);

        // The first format item is `Color( 0, 0, 0, 0 )` until the foreground colour is set, as the scheme would.
        text.SetFgColor((255, 255, 255, 255));
        fill(text);
        now = clock?.Invoke() ?? start;
        text.Think();

        TextRecorder surface = new();
        text.Paint(surface, null!);
        return surface;
    }
}
