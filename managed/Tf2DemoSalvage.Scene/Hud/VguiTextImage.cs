using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::Image` (vgui2/vgui_controls/Image.cpp): a position, a size and a colour, painted relative to its panel.</summary>
public abstract class VguiImage
{
    /// <summary>X within the panel.</summary>
    public int X { get; private set; }

    /// <summary>Y within the panel.</summary>
    public int Y { get; private set; }

    /// <summary>Width.</summary>
    public int Wide { get; private set; }

    /// <summary>Height.</summary>
    public int Tall { get; private set; }

    /// <summary>`GetColor`: white until set.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) Color { get; set; } = (255, 255, 255, 255);

    /// <summary>`SetPos`.</summary>
    /// <param name="x">X.</param>
    /// <param name="y">Y.</param>
    public void SetPos(int x, int y) => (X, Y) = (x, y);

    /// <summary>`SetSize`.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    public virtual void SetSize(int wide, int tall) => (Wide, Tall) = (wide, tall);

    /// <summary>`GetContentSize`: the size, unless the image knows better.</summary>
    /// <param name="surface">The surface, for measuring.</param>
    /// <returns>The content's size.</returns>
    public virtual (int Wide, int Tall) GetContentSize(IVguiSurface surface) => (Wide, Tall);

    /// <summary>`Paint`.</summary>
    /// <param name="surface">The surface, made current for the owning panel.</param>
    public abstract void Paint(IVguiSurface surface);
}

/// <summary>`vgui::TextImage` (vgui2/vgui_controls/TextImage.cpp): a string drawn in one font.</summary>
/// <remarks>
/// `USE_GETKERNEDCHARWIDTH` is 0 on every platform (`public/vgui/VGUI.h:76`), so the unkerned paths are the ones ported:
/// characters placed `GetCharacterWidth` apart, text measured as a + b + c. `&amp;&amp;` is one ampersand and a lone
/// `&amp;` draws nothing (hotkey underlines are compiled out); `\r` and characters up to 8 are skipped. The colour-change
/// stream and the fallback font are not modelled yet.
/// </remarks>
public sealed class VguiTextImage : VguiImage
{
    /// <summary>`TextImage( text )`, with a font.</summary>
    /// <param name="font">The font handle, or null for none — which draws nothing.</param>
    public VguiTextImage(VguiFontAmalgam? font) => Font = font;

    private readonly List<int> _lineBreaks = [];
    private readonly List<int> _lineIndents = [];
    private int _drawWidth;
    private int? _ellipsis;
    private bool _recalculate = true;

    /// <summary>The text, localised.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>`GetFont`.</summary>
    public VguiFontAmalgam? Font { get; set; }

    /// <summary>`SetWrap`.</summary>
    public bool Wrap { get; set; }

    /// <summary>`SetCenterWrap`.</summary>
    public bool CenterWrap { get; set; }

    /// <summary>`SetAllCaps`.</summary>
    public bool AllCaps { get; set; }

    /// <summary>`SetText( const char * )`: a `#token` is looked up, and drawn literally when there is no such string.</summary>
    /// <param name="text">The text or token.</param>
    /// <param name="localize">The localisation table's lookup, or null.</param>
    public void SetText(string? text, Func<string, string?>? localize)
    {
        text ??= string.Empty;

        if (text.StartsWith('#') && localize?.Invoke(text[1..]) is { } localised)
        {
            text = localised;
        }

        Text = text;
        _lineBreaks.Clear();
        _lineIndents.Clear();
        _recalculate = true;
    }

    /// <summary>`SetDrawWidth`.</summary>
    /// <param name="width">The width text is truncated or wrapped to.</param>
    public void SetDrawWidth(int width)
    {
        _drawWidth = width;
        _recalculate = true;
    }

    /// <inheritdoc/>
    public override void SetSize(int wide, int tall)
    {
        base.SetSize(wide, tall);
        _drawWidth = wide;
        _recalculate = true;
    }

    /// <inheritdoc/>
    public override (int Wide, int Tall) GetContentSize(IVguiSurface surface) => GetTextSize(surface);

    /// <summary>`ResizeImageToContent`.</summary>
    /// <param name="surface">The surface, for measuring.</param>
    public void ResizeImageToContent(IVguiSurface surface)
    {
        (int wide, int tall) = GetContentSize(surface);

        SetSize(wide, tall);
    }

    /// <summary>`GetTextSize`: a + b + c per character (a newline's too), the widest line, a font height per line.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The text's size.</returns>
    public (int Wide, int Tall) GetTextSize(IVguiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Font is not { } font)
        {
            return (0, 0);
        }

        if (Wrap || CenterWrap)
        {
            RecalculateNewLinePositions(surface, font);
        }

        int fontHeight = surface.GetFontTall(font);
        int wide = 0;
        int tall = 0;
        int maxWide = 0;

        for (int index = 0; index < Text.Length; index++)
        {
            tall = Math.Max(tall, fontHeight);

            char character = Text[index];

            if (character == '&')
            {
                continue;
            }

            if (AllCaps)
            {
                character = char.ToUpperInvariant(character);
            }

            (int a, int b, int c) = surface.GetCharAbcWide(font, character);

            wide += a + b + c;

            if (character == '\n')
            {
                tall += fontHeight;
                maxWide = Math.Max(maxWide, wide);
                wide = 0;
            }

            if (Wrap || CenterWrap)
            {
                foreach (int lineBreak in _lineBreaks)
                {
                    if (lineBreak == index)
                    {
                        tall += fontHeight;
                        maxWide = Math.Max(maxWide, wide);
                        wide = 0;
                    }
                }
            }
        }

        return (Math.Max(wide, maxWide), tall);
    }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Font is not { } font)
        {
            return;
        }

        if (_recalculate)
        {
            if (Wrap || CenterWrap)
            {
                RecalculateNewLinePositions(surface, font);
            }

            RecalculateEllipsesPosition(surface, font);
        }

        surface.DrawSetTextColor(Color);
        surface.DrawSetTextFont(font);

        int lineHeight = surface.GetFontTall(font);
        float x = CenterWrap && _lineIndents.Count > 0 ? _lineIndents[0] : 0f;
        int y = 0;
        int indent = 0;
        int nextBreak = 0;
        int index = -1;

        // A while rather than a for: `&&` steps over its second character inside the body, as the engine's pointer does.
        while (++index < Text.Length)
        {
            char character = AllCaps ? char.ToUpperInvariant(Text[index]) : Text[index];

            if (character == '\r' || character <= 8)
            {
                continue;
            }

            if (character == '\n')
            {
                indent++;
                x = CenterWrap && indent < _lineIndents.Count ? _lineIndents[indent] : 0f;
                y += lineHeight;
                continue;
            }

            if (character == '&')
            {
                if (index + 1 < Text.Length && Text[index + 1] == '&')
                {
                    index++;
                }
                else
                {
                    continue;
                }
            }

            if (index == _ellipsis)
            {
                for (int dot = 0; dot < 3; dot++)
                {
                    surface.DrawSetTextPos((int)(x + X), y + Y);
                    surface.DrawUnicodeChar('.');
                    x += surface.GetCharacterWidth(font, '.');
                }

                break;
            }

            if (nextBreak != _lineBreaks.Count && index == _lineBreaks[nextBreak])
            {
                indent++;
                x = CenterWrap && indent < _lineIndents.Count ? _lineIndents[indent] : 0f;
                y += lineHeight;
                nextBreak++;
            }

            surface.DrawSetTextPos((int)(x + X), y + Y);
            surface.DrawUnicodeChar(character);
            x += surface.GetCharacterWidth(font, character);
        }
    }

    /// <summary>`RecalculateNewLinePositions`: break before the word that overflows, or at the character when the word started its line.</summary>
    private void RecalculateNewLinePositions(IVguiSurface surface, VguiFontAmalgam font)
    {
        int x = 0;
        int wordStart = 0;
        bool hasWord = false;
        bool justStartedNewLine = true;
        bool wordStartedOnNewLine = true;
        int index = Text.Length > 0 && Text[0] is '\r' or '\n' ? 0 : -1;

        _lineBreaks.Clear();
        _lineIndents.Clear();

        // A while rather than a for: a break before a word rewinds to it, as the engine's pointer does.
        while (++index < Text.Length)
        {
            if (Text[index] is '&' or '\u0001' or '\u0002' or '\u0003' && index + 1 < Text.Length)
            {
                index++;
            }

            char character = AllCaps ? char.ToUpperInvariant(Text[index]) : Text[index];

            if (!char.IsWhiteSpace(character))
            {
                if (!hasWord)
                {
                    wordStart = index;
                    hasWord = true;
                    wordStartedOnNewLine = justStartedNewLine;
                }
            }
            else
            {
                hasWord = false;
            }

            int width = surface.GetCharacterWidth(font, character);

            if (!char.IsControl(character))
            {
                justStartedNewLine = false;
            }

            if (x + width > _drawWidth || character is '\r' or '\n')
            {
                justStartedNewLine = true;
                hasWord = false;

                if (character is not ('\r' or '\n'))
                {
                    if (wordStartedOnNewLine)
                    {
                        _lineBreaks.Add(index);
                    }
                    else
                    {
                        _lineBreaks.Add(wordStart);
                        index = wordStart - 1;
                    }
                }

                x = 0;
                continue;
            }

            x += width;
        }

        RecalculateCenterWrapIndents(surface, font);
    }

    /// <summary>`RecalculateCenterWrapIndents`: each line's indent is half the width it leaves.</summary>
    private void RecalculateCenterWrapIndents(IVguiSurface surface, VguiFontAmalgam font)
    {
        _lineIndents.Clear();

        if (!CenterWrap)
        {
            return;
        }

        int nextBreak = 0;
        int lineWidth = 0;
        int index = -1;

        while (++index < Text.Length)
        {
            char character = AllCaps ? char.ToUpperInvariant(Text[index]) : Text[index];

            if (character == '\r')
            {
                continue;
            }

            if (character == '\n')
            {
                _lineIndents.Add((int)((_drawWidth - lineWidth) * 0.5));
                lineWidth = 0;
                continue;
            }

            if (character == '&')
            {
                if (index + 1 < Text.Length && Text[index + 1] == '&')
                {
                    index++;
                }
                else
                {
                    continue;
                }
            }

            if (nextBreak != _lineBreaks.Count && index == _lineBreaks[nextBreak])
            {
                _lineIndents.Add((int)((_drawWidth - lineWidth) * 0.5));
                lineWidth = 0;
                nextBreak++;
            }

            lineWidth += surface.GetCharacterWidth(font, character);
        }

        _lineIndents.Add((int)((_drawWidth - lineWidth) * 0.5));
    }

    /// <summary>`RecalculateEllipsesPosition`: the first character past which the rest no longer fits beside three dots.</summary>
    private void RecalculateEllipsesPosition(IVguiSurface surface, VguiFontAmalgam font)
    {
        _recalculate = false;
        _ellipsis = null;

        if (Wrap || CenterWrap || Text.Contains('\n', StringComparison.Ordinal))
        {
            return;
        }

        if (_drawWidth == 0)
        {
            _drawWidth = Wide;
        }

        int ellipsesWidth = 3 * surface.GetCharacterWidth(font, '.');
        int x = 0;
        int index = -1;

        while (++index < Text.Length)
        {
            char character = AllCaps ? char.ToUpperInvariant(Text[index]) : Text[index];

            if (character == '\r')
            {
                continue;
            }

            if (character == '&')
            {
                if (index + 1 < Text.Length && Text[index + 1] == '&')
                {
                    index++;
                }
                else
                {
                    continue;
                }
            }

            int width = surface.GetCharacterWidth(font, character);

            if (index == 0)
            {
                x += width;
                continue;
            }

            if (x + width + ellipsesWidth > _drawWidth)
            {
                int remaining = width;

                for (int rest = index + 1; rest < Text.Length; rest++)
                {
                    remaining += surface.GetCharacterWidth(font, Text[rest]);
                }

                if (x + remaining > _drawWidth)
                {
                    _ellipsis = index;
                    return;
                }
            }

            x += width;
        }
    }
}
