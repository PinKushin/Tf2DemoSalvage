using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::RichText` (vgui2/vgui_controls/RichText.cpp): a text stream with a parallel stream of colour, fade and indent changes, wrapped at words.</summary>
/// <remarks>
/// The drawing half, which is all a HUD panel in a demo uses: the streams (`InsertString`, `InsertColorChange`,
/// `InsertFade`, `InsertIndentChange`, `ResetAllFades`), line breaking (`RecalculateLineBreaks`, :1221), scrolling to the
/// end as lines are added (`LayoutVerticalScrollBarSlider`, :1449) and `Paint` (:714). The scroll bar is a slider's
/// state — range, window, value — because that is what decides the first line drawn even while the bar is hidden.
/// Fades run on `system()->GetCurrentTime()`, the wall clock: <see cref="Clock"/>.
/// **Not modelled:** selection, the cursor, mouse and keyboard, the edit menu, clickable text and its underline font,
/// URL parsing, `textfile`, and drawing the scroll bar itself.
/// **Interpolated:** a character's width while breaking is `GetKernedCharWidth`, which for a Windows font is taken as
/// its A + B + C with no kerning — the surface library is closed, and this is not yet read out of its disassembly.
/// </remarks>
public class VguiRichText : VguiPanel
{
    private const int MaxBufferSize = 999999;
    private const int DrawOffsetX = 3;
    private const int DrawOffsetY = 1;
    private const int ScrollBarDefaultWidth = 17;

    private readonly List<char> _text = [];
    private readonly List<FormatItem> _format = [];
    private readonly List<int> _lineBreaks = [];
    private readonly (int Min, int Max, int Window, int Value)[] _slider = [(0, 0, 0, 0)];
    private int _maxCharCount = 64 * 1024;
    private int _drawOffsetX = DrawOffsetX;
    private int _drawOffsetY = DrawOffsetY;
    private int _pixelsIndent;
    private int _recalculateBreaksIndex;
    private bool _recalcLineBreaks = true;
    private bool _invalidateSlider;
    private bool _resetFades;
    private bool _scrollBarVisible = true;
    /// <summary>`DefaultTextColor`: `Color( 0, 0, 0, 0 )`, never changed.</summary>
    private static readonly (byte Red, byte Green, byte Blue, byte Alpha) DefaultTextColor = (0, 0, 0, 0);
    private (int Wide, int Tall) _laidOutSize;

    /// <summary>`RichText( parent, name )`: an empty stream with one format item in the default colour, at the end.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiRichText(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        GotoTextEnd();
        InvalidateLineBreakStream();
        _format.Add(new FormatItem(DefaultTextColor, new Fade(0f, 0f, -1f, 0), 0, 0));
    }

    /// <inheritdoc/>
    public override string ClassName => "RichText";

    /// <summary>`_font`: none until the scheme or <c>SetFont</c> gives one.</summary>
    public VguiFontAmalgam? TextFont { get; set; }

    /// <summary>The surface text is measured on — the scheme's, once one reaches the panel.</summary>
    public IVguiSurface? Surface { get; set; }

    /// <summary>`system()->GetCurrentTime()`: the wall clock fades run on, in seconds.</summary>
    public Func<double> Clock { get; set; } = () => HudClock;

    /// <summary>The wall clock a panel with no clock of its own reads.</summary>
    public static double HudClock { get; set; }

    /// <summary>The text stream as a string.</summary>
    public string Text => new([.. _text]);

    /// <summary>How many items the format stream holds.</summary>
    public int FormatStreamCount => _format.Count;

    /// <summary>`GetNumLines`.</summary>
    public int NumLines => _lineBreaks.Count;

    /// <summary>`IsAllTextAlphaZero`: whether the last paint drew nothing visible.</summary>
    public bool AllTextAlphaIsZero { get; private set; }

    /// <summary>`SetDrawOffsets`.</summary>
    /// <param name="x">The left inset.</param>
    /// <param name="y">The top inset and line gap.</param>
    public void SetDrawOffsets(int x, int y) => (_drawOffsetX, _drawOffsetY) = (x, y);

    /// <inheritdoc/>
    /// <remarks>
    /// The "Default" font, `RichText.TextColor` and `RichText.BgColor`, and the scheme's `RichText.InsetX`/`InsetY`. The draw
    /// offsets scale with the screen on a proportional panel, as the constructor does.
    /// </remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        Surface = context.Surface;
        TextFont = context.GetFont("Default", Proportional);

        if (Proportional)
        {
            float scale = context.ScreenWide / 640f;

            (_drawOffsetX, _drawOffsetY) = ((int)(DrawOffsetX * scale), (int)(DrawOffsetY * scale));
        }

        SetFgColor(context.Scheme.GetColor("RichText.TextColor", (255, 255, 255, 255)));
        BgColor = context.Scheme.GetColor("RichText.BgColor", (0, 0, 0, 0));

        if (context.Scheme.GetResourceString("RichText.InsetX") is { Length: > 0 } insetX)
        {
            SetDrawOffsets(PanelLayout.Atoi(insetX), PanelLayout.Atoi(context.Scheme.GetResourceString("RichText.InsetY")));
        }
    }

    /// <inheritdoc/>
    /// <remarks>`maxchars` (-1, none) and `scrollbar` (1); `text` sets the stream.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);
        SetMaximumCharCount(Int(block, "maxchars", -1));
        SetVerticalScrollbar(Int(block, "scrollbar", 1) != 0);

        if (block.Find("text")?.Value is { Length: > 0 } text)
        {
            SetText(text);
        }
    }

    /// <summary>`SetFgColor`: the first format item follows it while it is the only one, still in the old colour.</summary>
    /// <param name="color">The colour.</param>
    public void SetFgColor((byte, byte, byte, byte) color)
    {
        if (_format.Count == 1 && (_format[0].Color == DefaultTextColor || _format[0].Color == FgColor))
        {
            _format[0] = _format[0] with { Color = color };
        }

        FgColor = color;
    }

    /// <summary>`SetFont`.</summary>
    /// <param name="font">The font.</param>
    public void SetFont(VguiFontAmalgam? font)
    {
        TextFont = font;
        InvalidateLayout();
        _recalcLineBreaks = true;
    }

    /// <summary>`SetText( const wchar_t * )`: the stream replaced, one format item in the foreground colour, scrolled to the top.</summary>
    /// <param name="text">The text.</param>
    public void SetText(string? text)
    {
        _format.Clear();
        _format.Add(new FormatItem(FgColor, new Fade(0f, 0f, -1f, 0), 0, 0));
        _text.Clear();

        if (!string.IsNullOrEmpty(text))
        {
            // The terminator goes in with the text, as `textLen = V_wcslen(text) + 1`.
            _text.AddRange(text);
            _text.Add('\0');
        }

        GotoTextStart();
        InvalidateLineBreakStream();
        InvalidateLayout();
    }

    /// <summary>`SetMaximumCharCount`.</summary>
    /// <param name="maxChars">The limit; below 1 for none.</param>
    public void SetMaximumCharCount(int maxChars) => _maxCharCount = maxChars;

    /// <summary>`SetVerticalScrollbar`: a change of visibility changes the width text wraps in.</summary>
    /// <param name="visible">Whether the bar shows.</param>
    public void SetVerticalScrollbar(bool visible)
    {
        if (_scrollBarVisible != visible)
        {
            _scrollBarVisible = visible;
            InvalidateLineBreakStream();
            InvalidateLayout();
        }
    }

    /// <summary>`InsertString`: each character through `InsertChar`, then the breaks recalculated.</summary>
    /// <param name="text">The text.</param>
    public void InsertString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (char character in text)
        {
            if (character == '\0')
            {
                break;
            }

            InsertChar(character);
        }

        InvalidateLayout();
        _recalcLineBreaks = true;
    }

    /// <summary>`InsertColorChange` (:1039): the same colour ignored; at the same place, the last item changed; else a new item.</summary>
    /// <param name="color">The colour.</param>
    public void InsertColorChange((byte, byte, byte, byte) color)
    {
        FormatItem previous = _format[^1];

        if (previous.Color == color)
        {
            return;
        }

        if (previous.TextStreamIndex == _text.Count)
        {
            _format[^1] = previous with { Color = color };
        }
        else
        {
            _format.Add(previous with { Color = color, TextStreamIndex = _text.Count });
        }
    }

    /// <summary>`InsertFade` (:1065): the text since the last item fades `length` seconds before `sustain` from now runs out.</summary>
    /// <param name="sustain">Seconds the text holds.</param>
    /// <param name="length">Seconds its fade takes; -1 for no fade.</param>
    public void InsertFade(float sustain, float length)
    {
        FormatItem previous = _format[^1];
        Fade fade = new((float)Clock() + sustain, sustain, length, previous.Color.Alpha);

        if (previous.TextStreamIndex == _text.Count)
        {
            _format[^1] = previous with { Fade = fade };
        }
        else
        {
            _format[^1] = previous with { Fade = fade };
            _format.Add(previous with { TextStreamIndex = _text.Count });
        }
    }

    /// <summary>`ResetAllFades` (:1092): every item after the first starts its sustain again, unless held.</summary>
    /// <param name="hold">Whether fades stop until the next reset.</param>
    /// <param name="onlyExpired">Whether only items whose fade has begun restart.</param>
    /// <param name="newSustain">The sustain to use; -1 for each item's own — the first one's thereafter, as Valve's loop keeps it.</param>
    public void ResetAllFades(bool hold, bool onlyExpired = false, float newSustain = -1f)
    {
        _resetFades = hold;

        if (_resetFades)
        {
            return;
        }

        float now = (float)Clock();

        for (int i = 1; i < _format.Count; i++)
        {
            if (onlyExpired && _format[i].Fade.StartTime >= now)
            {
                continue;
            }

            if (IsMinusOne(newSustain))
            {
                newSustain = _format[i].Fade.Sustain;
            }

            _format[i] = _format[i] with { Fade = _format[i].Fade with { StartTime = now + newSustain } };
        }
    }

    /// <summary>`InsertIndentChange` (:1119): clamped to 0..255, else as a colour change.</summary>
    /// <param name="pixels">The indent.</param>
    public void InsertIndentChange(int pixels)
    {
        pixels = Math.Clamp(pixels, 0, 255);

        FormatItem previous = _format[^1];

        if (previous.PixelsIndent == pixels)
        {
            return;
        }

        if (previous.TextStreamIndex == _text.Count)
        {
            _format[^1] = previous with { PixelsIndent = pixels };
        }
        else
        {
            _format.Add(previous with { PixelsIndent = pixels, TextStreamIndex = _text.Count });
        }
    }

    /// <summary>`GotoTextStart`: the slider to the top.</summary>
    public void GotoTextStart()
    {
        _invalidateSlider = true;
        SetSliderValue(0);
    }

    /// <summary>`GotoTextEnd`: the slider to the bottom.</summary>
    public void GotoTextEnd()
    {
        _invalidateSlider = true;
        SetSliderValue(_slider[0].Max);
    }

    /// <summary>`OnThink` (:1010): the breaks recalculated when invalid, and the slider laid out after them.</summary>
    protected override void OnThink()
    {
        // `OnSizeChanged`, which Valve's `SetSize` calls as it happens: the breaks and the slider are recalculated.
        if ((Wide, Tall) != _laidOutSize)
        {
            _laidOutSize = (Wide, Tall);
            _invalidateSlider = true;
            InvalidateLineBreakStream();
            InvalidateLayout();
        }

        if (!_recalcLineBreaks)
        {
            return;
        }

        RecalculateLineBreaks();

        if (_invalidateSlider)
        {
            LayoutVerticalScrollBarSlider();
        }
    }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        AllTextAlphaIsZero = true;

        if (_text.Count == 0)
        {
            return;
        }

        int startIndex = GetStartDrawIndex(out int lineBreakIndex);
        RenderState state = StateAt(startIndex);

        _pixelsIndent = state.PixelsIndent;

        if (_format.Count > state.FormatIndex)
        {
            state.Color = _format[state.FormatIndex].Color;
        }

        CalculateFade(ref state);
        state.FormatIndex++;
        state.X = _drawOffsetX + _pixelsIndent;
        state.Y = _drawOffsetY;

        if (TextFont is { } font)
        {
            surface.DrawSetTextFont(font);
        }

        for (int i = startIndex; i < _text.Count && state.Y < Tall;)
        {
            UpdateRenderState(i, ref state);

            // A line break reached: the next line, skipping spaces unless a hard return ended the last.
            if (lineBreakIndex < _lineBreaks.Count && _lineBreaks[lineBreakIndex] <= i)
            {
                AddAnotherLine(ref state.X, ref state.Y, surface);
                lineBreakIndex++;

                if (i > 0 && _text[i - 1] != '\n' && _text[i - 1] != '\r')
                {
                    while (_text[i] == ' ' && i + 1 < _text.Count)
                    {
                        ++i;
                    }
                }
            }

            // The run to draw at once: up to the next format change, the next break or the next control character.
            int limit = _text.Count;

            if (state.FormatIndex < _format.Count
                && _format[state.FormatIndex].TextStreamIndex < limit
                && _format[state.FormatIndex].TextStreamIndex >= i
                && _format[state.FormatIndex].TextStreamIndex != 0)
            {
                limit = _format[state.FormatIndex].TextStreamIndex;
            }

            if (lineBreakIndex < _lineBreaks.Count && _lineBreaks[lineBreakIndex] < limit)
            {
                limit = _lineBreaks[lineBreakIndex];
            }

            for (int t = i; t < limit; t++)
            {
                if (char.IsControl(_text[t]))
                {
                    limit = t;
                    break;
                }
            }

            if (limit <= i)
            {
                if (_text[i] == '\t')
                {
                    int tab = Math.Max(1, 8 * CharacterWidth(surface, ' '));

                    state.X = tab * (1 + (state.X / tab));
                }

                i++;
            }
            else
            {
                state.X += DrawString(surface, i, limit - 1, state);
                i = limit;
            }
        }
    }

    /// <summary>`InvalidateLineBreakStream`: one break, the end of the buffer, and a full recalculation due.</summary>
    protected void InvalidateLineBreakStream()
    {
        _lineBreaks.Clear();
        _lineBreaks.Add(MaxBufferSize);
        _recalculateBreaksIndex = 0;
        _recalcLineBreaks = true;
    }

    private int LineHeight(IVguiSurface? surface) => TextFont is { } font && surface is not null ? surface.GetFontTall(font) : 0;

    private int CharacterWidth(IVguiSurface surface, char character) => TextFont is { } font ? surface.GetCharacterWidth(font, character) : 0;

    private void InsertChar(char character)
    {
        // "throw away redundant linefeed characters"
        if (character == '\r')
        {
            return;
        }

        if (_maxCharCount > 0 && _text.Count > _maxCharCount)
        {
            TruncateTextStream();
        }

        _text.Add(character);
        _recalculateBreaksIndex = _lineBreaks.Count - 2;
    }

    /// <summary>`TruncateTextStream` (:1978): the first half of the maximum dropped, the format stream moved with it.</summary>
    private void TruncateTextStream()
    {
        if (_maxCharCount < 1)
        {
            return;
        }

        int cull = _maxCharCount / 2;

        _text.RemoveRange(0, cull);

        int formatIndex = FindFormatIndex(cull);

        if (formatIndex > 0)
        {
            _format[0] = _format[formatIndex] with { TextStreamIndex = 0 };
            _format.RemoveRange(1, formatIndex);
        }

        for (int i = 1; i < _format.Count; i++)
        {
            _format[i] = _format[i] with { TextStreamIndex = _format[i].TextStreamIndex - cull };
        }

        InvalidateLineBreakStream();
        InvalidateLayout();
        _invalidateSlider = true;
    }

    /// <summary>`RecalculateLineBreaks` (:1221): whole words wrapped at the width less the offset and any scroll bar.</summary>
    private void RecalculateLineBreaks()
    {
        if (!_recalcLineBreaks || Surface is not { } surface)
        {
            return;
        }

        int wide = Wide;

        if (wide == 0)
        {
            return;
        }

        wide -= _drawOffsetX;
        _recalcLineBreaks = false;

        if (_text.Count == 0)
        {
            return;
        }

        if (_scrollBarVisible)
        {
            wide -= ScrollBarDefaultWidth;
        }

        int x = _drawOffsetX;
        int y = _drawOffsetY;
        int wordStartIndex = 0;
        int lineStartIndex = 0;
        bool hasWord = false;
        bool justStartedNewLine = true;
        bool wordStartedOnNewLine = true;
        int startChar = 0;

        if (_recalculateBreaksIndex <= 0)
        {
            _lineBreaks.Clear();
        }
        else
        {
            _lineBreaks.RemoveRange(_recalculateBreaksIndex + 1, _lineBreaks.Count - (_recalculateBreaksIndex + 1));
            startChar = _lineBreaks[_recalculateBreaksIndex];
            lineStartIndex = startChar;
            wordStartIndex = lineStartIndex;
        }

        // "handle the case where this char is a new line"
        if (startChar < _text.Count && _text[startChar] is '\r' or '\n')
        {
            startChar++;
            lineStartIndex = startChar;
        }

        RenderState state = StateAt(startChar);
        float lineWidthSoFar = 0;

        // Valve's `for`, whose increment also follows a break that went back to a word's start — so that word's first
        // character is past before the new line's width counts again.
        int i = startChar;

        while (i < _text.Count)
        {
            char character = _text[i];

            state.X = x;

            if (UpdateRenderState(i, ref state))
            {
                x = state.X;
            }

            bool isSpace = char.IsWhiteSpace(character);
            bool previousWordStartedOnNewLine = wordStartedOnNewLine;
            int previousWordStartIndex = wordStartIndex;

            if (!isSpace && character is not '\t' and not '\n' and not '\r')
            {
                if (!hasWord)
                {
                    wordStartIndex = i;
                    hasWord = true;
                    wordStartedOnNewLine = justStartedNewLine;
                }
            }
            else
            {
                hasWord = false;
            }

            // `GetKernedCharWidth`: A + B + C for a Windows font.
            if (TextFont is { } font)
            {
                (int a, int b, int c) = surface.GetCharAbcWide(font, character);

                lineWidthSoFar += a + b + c;
            }

            bool forceBreak = MathF.Floor(lineWidthSoFar + 0.6f) + x > wide;

            if (!char.IsControl(character))
            {
                justStartedNewLine = false;
            }

            if (!forceBreak && character is not '\r' and not '\n')
            {
                i++;
                continue;
            }

            AddAnotherLine(ref x, ref y, surface);

            if (character is '\r' or '\n')
            {
                lineStartIndex = i + 1;
                _lineBreaks.Add(i + 1);
            }
            else if (previousWordStartedOnNewLine || previousWordStartIndex <= lineStartIndex)
            {
                lineStartIndex = i;
                _lineBreaks.Add(i);
            }
            else
            {
                _lineBreaks.Add(previousWordStartIndex);
                lineStartIndex = previousWordStartIndex;
                i = previousWordStartIndex;
            }

            lineWidthSoFar = 0;
            justStartedNewLine = true;
            hasWord = false;
            wordStartedOnNewLine = false;
            i++;
        }

        _lineBreaks.Add(MaxBufferSize);
        _invalidateSlider = true;
    }

    /// <summary>`LayoutVerticalScrollBarSlider` (:1449): the range the lines, the window those that fit; kept at the end when it was.</summary>
    private void LayoutVerticalScrollBarSlider()
    {
        _invalidateSlider = false;

        (int min, int max, int window, int value) = _slider[0];
        bool atEnd = max != 0 && value + min + window == max;
        int displayLines = Tall / Math.Max(1, LineHeight(Surface) + _drawOffsetY);
        int numLines = _lineBreaks.Count;

        if (numLines <= displayLines)
        {
            SetSliderRange(0, numLines, numLines);
            SetSliderValue(0);
        }
        else
        {
            SetSliderRange(0, numLines, displayLines);

            if (atEnd)
            {
                SetSliderValue(numLines - displayLines);
            }
        }
    }

    private void SetSliderRange(int min, int max, int window)
    {
        max = Math.Max(max, min);
        _slider[0] = (min, max, window, _slider[0].Value);
        SetSliderValue(_slider[0].Value);
    }

    /// <summary>`ScrollBarSlider::SetValue`: clamped to the range less the window.</summary>
    private void SetSliderValue(int value)
    {
        (int min, int max, int window, _) = _slider[0];

        value = Math.Max(Math.Min(value, max - window), min);
        _slider[0] = (min, max, window, value);
    }

    /// <summary>`GetStartDrawIndex` (:2232): the text index of the line the slider is on.</summary>
    private int GetStartDrawIndex(out int lineBreakIndex)
    {
        int startLine = Math.Min(_slider[0].Value, _lineBreaks.Count - 1);

        lineBreakIndex = startLine;

        return startLine > 0 && startLine < _lineBreaks.Count ? _lineBreaks[startLine - 1] : 0;
    }

    private void AddAnotherLine(ref int x, ref int y, IVguiSurface? surface)
    {
        x = _drawOffsetX + _pixelsIndent;
        y += LineHeight(surface) + _drawOffsetY;
    }

    /// <summary>`GenerateRenderStateForTextStreamIndex`: the format item in force at an index.</summary>
    private RenderState StateAt(int textIndex)
    {
        int formatIndex = FindFormatIndex(textIndex);
        FormatItem item = _format[formatIndex];

        return new RenderState { FormatIndex = formatIndex, Color = item.Color, PixelsIndent = item.PixelsIndent };
    }

    /// <summary>`FindFormatStreamIndexForTextStreamPos` (:975): the last item starting at or before an index.</summary>
    private int FindFormatIndex(int textIndex)
    {
        int formatIndex = 0;

        while (formatIndex < _format.Count && _format[formatIndex].TextStreamIndex <= textIndex)
        {
            formatIndex++;
        }

        formatIndex--;

        return formatIndex >= 0 && formatIndex < _format.Count ? formatIndex : 0;
    }

    /// <summary>`UpdateRenderState` (:941): the next format item applied where it starts.</summary>
    private bool UpdateRenderState(int textIndex, ref RenderState state)
    {
        if (state.FormatIndex >= _format.Count || _format[state.FormatIndex].TextStreamIndex != textIndex)
        {
            return false;
        }

        FormatItem item = _format[state.FormatIndex];

        state.Color = item.Color;
        CalculateFade(ref state);

        int indentChange = item.PixelsIndent - state.PixelsIndent;

        state.PixelsIndent = item.PixelsIndent;

        if (indentChange != 0)
        {
            state.X = state.PixelsIndent + _drawOffsetX;
        }

        _pixelsIndent = state.PixelsIndent;
        state.FormatIndex++;
        return true;
    }

    /// <summary>`CalculateFade` (:692): the alpha scaled by the time left before the fade's start, over its length.</summary>
    private void CalculateFade(ref RenderState state)
    {
        if (state.FormatIndex >= _format.Count || _resetFades)
        {
            return;
        }

        Fade fade = _format[state.FormatIndex].Fade;

        if (IsMinusOne(fade.Length))
        {
            return;
        }

        float fraction = (fade.StartTime - (float)Clock()) / fade.Length;
        int alpha = Math.Clamp((int)(fraction * fade.OriginalAlpha), 0, fade.OriginalAlpha);

        state.Color = state.Color with { Alpha = (byte)alpha };
    }

    /// <summary>`DrawString` (:610): a run in one colour; nothing drawn at zero alpha. Returns its width.</summary>
    private int DrawString(IVguiSurface surface, int first, int last, RenderState state)
    {
        int wide = 0;

        for (int i = first; i <= last; i++)
        {
            wide += CharacterWidth(surface, _text[i]);
        }

        surface.DrawSetTextColor(state.Color);

        if (state.Color.Alpha != 0)
        {
            AllTextAlphaIsZero = false;
            surface.DrawSetTextPos(state.X, state.Y);

            StringBuilder run = new(last - first + 1);

            for (int i = first; i <= last; i++)
            {
                run.Append(_text[i]);
            }

            surface.DrawPrintText(run.ToString());
        }

        return wide;
    }

    /// <summary>Valve's `== -1.0f` sentinel test, kept exact: the bits of -1.</summary>
    private static bool IsMinusOne(float value) => BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(-1f);

    /// <summary>`TFormatStream::fade`.</summary>
    private readonly record struct Fade(float StartTime, float Sustain, float Length, int OriginalAlpha);

    /// <summary>`TFormatStream`: from a text index on, a colour, a fade and an indent.</summary>
    private readonly record struct FormatItem((byte Red, byte Green, byte Blue, byte Alpha) Color, Fade Fade, int PixelsIndent, int TextStreamIndex);

    /// <summary>`TRenderState`.</summary>
    private struct RenderState
    {
        public int X;
        public int Y;
        public (byte Red, byte Green, byte Blue, byte Alpha) Color;
        public int PixelsIndent;
        public int FormatIndex;
    }
}
