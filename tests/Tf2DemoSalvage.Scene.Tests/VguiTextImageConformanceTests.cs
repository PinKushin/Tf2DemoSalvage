using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::TextImage` (vgui2/vgui_controls/TextImage.cpp): where each character is drawn and how big the text is.</summary>
/// <remarks>
/// With `USE_GETKERNEDCHARWIDTH` 0 on every platform (`public/vgui/VGUI.h:76`): `Paint` places each character at the
/// running width, `GetCharacterWidth` apart; `GetTextSize` sums a + b + c — the newline's own included — and adds a font
/// height per line; a doubled ampersand is one and a lone one is dropped; a line too long for the draw width ends in three dots
/// where the rest would not fit; wrapping breaks before the word that would overflow.
/// </remarks>
public sealed class VguiTextImageConformanceTests
{
    private static readonly VguiFontAmalgam Font = new VguiFontManager(new FakeGdi()).CreateFont();

    [Test]
    public void Paint_EachCharacter_IsAtTheRunningWidthFromThePosition() =>
        Painted("ab", x: 5, y: 6).ShouldBe(["a@5,6", "b@15,6"]);

    [Test]
    public void Paint_Ampersands_PairDrawOneAndALoneOneIsDropped() =>
        Painted("a&&b&c").ShouldBe(["a@0,0", "&@10,0", "b@20,0", "c@30,0"]);

    [Test]
    public void Paint_ANewline_StartsTheNextLineAFontHeightDown() =>
        Painted("a\nb").ShouldBe(["a@0,0", "b@0,12"]);

    [Test]
    public void Paint_TooLongForTheDrawWidth_EndsInThreeDotsWhereTheRestWouldNotFit() =>
        Painted("abcd", drawWidth: 25).ShouldBe(["a@0,0", ".@10,0", ".@14,0", ".@18,0"]);

    [Test]
    public void Paint_Wrapped_BreaksAtTheCharacterThatOverflowsAndDrawsItOnTheNextLine() =>
        Painted("ab cd", drawWidth: 25, wrap: true).ShouldBe(["a@0,0", "b@10,0", " @0,12", "c@10,12", "d@20,12"]);

    [Test]
    public void GetTextSize_ANewline_CountsItsOwnWidthOnTheLineItEnds()
    {
        VguiTextImage text = new(Font);

        text.SetText("ab\nc", null);

        text.GetTextSize(new TextRecorder()).ShouldBe((30, 24), "a + b + the newline's 10, then c alone; two lines of 12");
    }

    [Test]
    public void SetText_ALocalisedToken_IsItsValueAndAnUnknownOneIsLiteral()
    {
        VguiTextImage text = new(Font);
        Dictionary<string, string> strings = new() { ["Known"] = "Hi" };

        text.SetText("#Known", strings.GetValueOrDefault);
        text.Text.ShouldBe("Hi");

        text.SetText("#Missing", strings.GetValueOrDefault);
        text.Text.ShouldBe("#Missing");
    }

    private static List<string> Painted(string value, int x = 0, int y = 0, int drawWidth = 1000, bool wrap = false)
    {
        VguiTextImage text = new(Font) { Wrap = wrap };
        TextRecorder surface = new();

        text.SetText(value, null);
        text.SetPos(x, y);
        text.SetSize(drawWidth, 100);
        text.Paint(surface);

        return surface.Glyphs;
    }
}
