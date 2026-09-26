using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A surface that records text calls, where every glyph is 10 wide (a 1, b 8, c 1), '.' 4, and a font 12 tall.</summary>
internal sealed class TextRecorder : IVguiSurface
{
    public List<string> Calls { get; } = [];

    public float AlphaMultiplier { get; set; } = 1f;

    /// <summary>Just the characters drawn with where each went: "a@5,6".</summary>
    public List<string> Glyphs { get; } = [];

    private (int X, int Y) _pen;

    public void PushMakeCurrent(VguiPanel panel, bool useInset) => Calls.Add($"push {panel.Name} {useInset}");

    public void PopMakeCurrent(VguiPanel panel) => Calls.Add($"pop {panel.Name}");

    public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
        Calls.Add($"color {color.Red} {color.Green} {color.Blue} {color.Alpha}");

    public void DrawFilledRect(int x0, int y0, int x1, int y1) => Calls.Add(Text($"fill {x0} {y0} {x1} {y1}"));

    public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal) => Calls.Add("fade");

    public void DrawOutlinedRect(int x0, int y0, int x1, int y1) => Calls.Add(Text($"outline {x0} {y0} {x1} {y1}"));

    public void DrawSetTexture(string texture) => Calls.Add($"texture {texture}");

    public void DrawTexturedRect(int x0, int y0, int x1, int y1) => Calls.Add(Text($"textured {x0} {y0} {x1} {y1}"));

    public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1)
    {
        Calls.Add(Text($"subrect {x0} {y0} {x1} {y1}"));
        SubRects.Add(Text($"{x0} {y0} {x1} {y1} uv {s0} {t0} {s1} {t1}"));
    }

    public void DrawTexturedQuad(float x0, float y0, float x1, float y1, float s0, float t0, float s1, float t1)
    {
        Calls.Add(Text($"quad {x0} {y0} {x1} {y1}"));
        SubRects.Add(Text($"{x0} {y0} {x1} {y1} uv {s0} {t0} {s1} {t1}"));
    }

    /// <summary>Each textured sub-rectangle or quad with its texture coordinates: "0 0 8 8 uv 0 0 0.25 0.25".</summary>
    public List<string> SubRects { get; } = [];

    public (int Wide, int Tall) DrawGetTextureSize(string texture) => (64, 64);

    public void DrawSetTextFont(VguiFontAmalgam font) => Calls.Add("font");

    public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
        Calls.Add($"text color {color.Red} {color.Green} {color.Blue} {color.Alpha}");

    public void DrawSetTextPos(int x, int y) => _pen = (x, y);

    public (int X, int Y) DrawGetTextPos() => _pen;

    public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default)
    {
        Glyphs.Add(Text($"{character}@{_pen.X},{_pen.Y}"));
        _pen = (_pen.X + GetCharacterWidth(null!, character), _pen.Y);
    }

    public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default)
    {
        foreach (char character in text)
        {
            DrawUnicodeChar(character, drawType);
        }
    }

    public int GetFontTall(VguiFontAmalgam font) => 12;

    public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character) => character == '.' ? (0, 4, 0) : (1, 8, 1);

    public int GetCharacterWidth(VguiFontAmalgam font, char character)
    {
        if (char.IsControl(character))
        {
            return 0;
        }

        (int a, int b, int c) = GetCharAbcWide(font, character);

        return a + b + c;
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
