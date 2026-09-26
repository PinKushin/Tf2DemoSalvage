using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>One screen-space quad for the renderer: a texture (or none, for a solid fill), its corners and colours.</summary>
/// <param name="Texture">The material, or null for a solid fill.</param>
/// <param name="X0">Left, in screen pixels.</param>
/// <param name="Y0">Top.</param>
/// <param name="X1">Right.</param>
/// <param name="Y1">Bottom.</param>
/// <param name="S0">Left texture coordinate.</param>
/// <param name="T0">Top texture coordinate.</param>
/// <param name="S1">Right texture coordinate.</param>
/// <param name="T1">Bottom texture coordinate.</param>
/// <param name="Red">Red.</param>
/// <param name="Green">Green.</param>
/// <param name="Blue">Blue.</param>
/// <param name="AlphaTopLeft">Alpha at the top-left corner.</param>
/// <param name="AlphaTopRight">Alpha at the top-right corner.</param>
/// <param name="AlphaBottomRight">Alpha at the bottom-right corner.</param>
/// <param name="AlphaBottomLeft">Alpha at the bottom-left corner.</param>
/// <param name="Additive">Whether it blends additively whatever its texture's material says — an additive font's glyph.</param>
public readonly record struct VguiQuad(
    string? Texture,
    float X0,
    float Y0,
    float X1,
    float Y1,
    float S0,
    float T0,
    float S1,
    float T1,
    byte Red,
    byte Green,
    byte Blue,
    byte AlphaTopLeft,
    byte AlphaTopRight,
    byte AlphaBottomRight,
    byte AlphaBottomLeft,
    bool Additive = false);

/// <summary>`CMatSystemSurface`'s draw calls, collected as screen-space quads in paint order — the portable half of the surface.</summary>
/// <remarks>
/// Closed code, `vguimatsurface.dll`, renamed in `tf2vguimatsurface`:
/// <list type="bullet">
/// <item>`PushMakeCurrent` (0x180011fb0): the origin is the panel's absolute position, plus its left and top inset when
/// asked; the clip is the panel's clip rectangle either way. Popping restores the panel beneath.</item>
/// <item>`DrawSetColor` (0x18000ca70): alpha is <c>(int)( a × multiplier )</c> at the moment the colour is set.</item>
/// <item>Every draw is skipped at zero alpha, translated, and clipped on the CPU (0x1800039a0): corners clamped to the
/// clip, a rectangle left inside out rejected, texture coordinates interpolated to the cut (the midpoint when the
/// rectangle has no extent).</item>
/// <item>`DrawFilledRect` (0x180009d20) carries zero texture coordinates; `DrawTexturedRect` (0x18000e070) the whole
/// texture; `DrawTexturedSubRect` (0x18000e1c0) the given part of it.</item>
/// <item>`DrawFilledRectFade` (0x18000a1f0): each alpha scaled by the colour's alpha / 255 and truncated, skipped only
/// when both are zero, top to bottom or left to right, and not re-interpolated when cut.</item>
/// <item>`DrawOutlinedRect` (0x18000b2e0): top, bottom, left, right, as filled rectangles.</item>
/// </list>
/// </remarks>
/// <param name="textureSize">Each texture's size in texels, zero when it did not load.</param>
/// <param name="fonts">The font manager text is drawn through, or null for a list that draws no text.</param>
public sealed class VguiDrawList(Func<string, (int Wide, int Tall)> textureSize, VguiFontManager? fonts = null) : IVguiSurface
{
    private readonly List<VguiQuad> _quads = [];
    private readonly VguiGlyphCache _glyphs = new();
    private VguiFontAmalgam? _textFont;
    private (byte Red, byte Green, byte Blue, byte Alpha) _textColor;
    private int _textX;
    private int _textY;
    private readonly Stack<(int X, int Y, (int X0, int Y0, int X1, int Y1) Clip)> _current = new();
    private (byte Red, byte Green, byte Blue, byte Alpha) _color;
    private string? _texture;
    private int _offsetX;
    private int _offsetY;
    private (int X0, int Y0, int X1, int Y1) _clip;

    /// <summary>The quads, in paint order.</summary>
    public IReadOnlyList<VguiQuad> Quads => _quads;

    /// <inheritdoc/>
    public float AlphaMultiplier { get; set; } = 1f;

    /// <summary>Empties the list for the next frame.</summary>
    public void Clear() => _quads.Clear();

    /// <inheritdoc/>
    public void PushMakeCurrent(VguiPanel panel, bool useInset)
    {
        ArgumentNullException.ThrowIfNull(panel);

        (int left, int top, _, _) = useInset ? panel.Inset : default;

        _current.Push((panel.AbsX + left, panel.AbsY + top, panel.ClipRect));
        (_offsetX, _offsetY, _clip) = _current.Peek();
    }

    /// <inheritdoc/>
    public void PopMakeCurrent(VguiPanel panel)
    {
        _current.Pop();

        if (_current.Count > 0)
        {
            (_offsetX, _offsetY, _clip) = _current.Peek();
        }
    }

    /// <inheritdoc/>
    public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
        _color = color with { Alpha = (byte)(int)(color.Alpha * AlphaMultiplier) };

    /// <inheritdoc/>
    public void DrawFilledRect(int x0, int y0, int x1, int y1)
    {
        if (_color.Alpha != 0)
        {
            Add(null, x0, y0, x1, y1, 0f, 0f, 0f, 0f, (_color.Alpha, _color.Alpha, _color.Alpha, _color.Alpha), interpolate: true);
        }
    }

    /// <inheritdoc/>
    public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal)
    {
        float scale = _color.Alpha * 0.003921569f;
        byte first = (byte)(uint)(long)(alpha0 * scale);
        byte second = (byte)(uint)(long)(alpha1 * scale);

        if (first == 0 && second == 0)
        {
            return;
        }

        (byte, byte, byte, byte) corners = horizontal ? (first, second, second, first) : (first, first, second, second);

        Add(null, x0, y0, x1, y1, 0f, 0f, 0f, 0f, corners, interpolate: false);
    }

    /// <inheritdoc/>
    public void DrawOutlinedRect(int x0, int y0, int x1, int y1)
    {
        if (_color.Alpha == 0)
        {
            return;
        }

        DrawFilledRect(x0, y0, x1, y0 + 1);
        DrawFilledRect(x0, y1 - 1, x1, y1);
        DrawFilledRect(x0, y0 + 1, x0 + 1, y1 - 1);
        DrawFilledRect(x1 - 1, y0 + 1, x1, y1 - 1);
    }

    /// <inheritdoc/>
    public void DrawSetTexture(string texture) => _texture = texture;

    /// <inheritdoc/>
    public void DrawTexturedRect(int x0, int y0, int x1, int y1) => DrawTexturedSubRect(x0, y0, x1, y1, 0f, 0f, 1f, 1f);

    /// <inheritdoc/>
    public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1)
    {
        if (_color.Alpha != 0)
        {
            Add(_texture, x0, y0, x1, y1, s0, t0, s1, t1, (_color.Alpha, _color.Alpha, _color.Alpha, _color.Alpha), interpolate: true);
        }
    }

    /// <inheritdoc/>
    public (int Wide, int Tall) DrawGetTextureSize(string texture) => textureSize(texture);

    /// <inheritdoc/>
    public void DrawSetTextFont(VguiFontAmalgam font) => _textFont = font;

    /// <inheritdoc/>
    public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color) =>
        _textColor = color with { Alpha = (byte)(int)(color.Alpha * AlphaMultiplier) };

    /// <inheritdoc/>
    public void DrawSetTextPos(int x, int y) => (_textX, _textY) = (x, y);

    /// <inheritdoc/>
    public (int X, int Y) DrawGetTextPos() => (_textX, _textY);

    /// <inheritdoc/>
    public int GetFontTall(VguiFontAmalgam font) => Fonts.GetFontTall(font);

    /// <inheritdoc/>
    public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character) => Fonts.GetCharAbcWide(font, character);

    /// <inheritdoc/>
    public int GetCharacterWidth(VguiFontAmalgam font, char character) => Fonts.GetCharacterWidth(font, character);

    /// <inheritdoc/>
    /// <remarks>
    /// `DrawGetUnicodeCharRenderInfo` (0x18000ab20) then `DrawRenderCharFromInfo` (0x18000c940): nothing at zero text
    /// alpha or without a font; the quad at pen + a (pen − a + … for an underlined font), `b + 2` wide (plus a + c when
    /// underlined) by `tall + 2`; the pen then moves to that x plus b + c. A glyph the cache cannot place draws nothing and
    /// does not advance.
    /// </remarks>
    public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default)
    {
        if (_textColor.Alpha == 0 || _textFont is not { } font)
        {
            return;
        }

        int tall = Fonts.GetFontTall(font);
        (int a, int b, int c) = Fonts.GetCharAbcWide(font, character);
        bool underline = IsUnderlined(font);
        int x = underline ? _textX : _textX + a;

        if (_glyphs.Get(font, character) is not (string texture, float s0, float t0, float s1, float t1))
        {
            return;
        }

        int wide = b + 2;

        if (underline)
        {
            wide += c + a;
            x -= a;
        }

        byte alpha = _textColor.Alpha;

        Add(texture, x, _textY, x + wide, _textY + tall + 2, s0, t0, s1, t1, (_textColor.Red, _textColor.Green, _textColor.Blue),
            (alpha, alpha, alpha, alpha), interpolate: true, IsAdditive(font, drawType));
        _textX = x + b + c;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `DrawPrintText` (0x18000b990): each glyph at <c>floor( a + 0.6 )</c> past the pen, `b + 2` by `tall + 2`; the pen
    /// moves <c>floor( a + b + c + 0.6 )</c>. Whitespace moves the pen and draws nothing — unless the font is underlined. An
    /// underlined font also pulls the base left by each character's a, cumulatively, as the engine does. A glyph the cache
    /// cannot place neither draws nor moves the pen. Additive glyphs are white at the text's alpha.
    /// </remarks>
    public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || _textFont is not { } font)
        {
            return;
        }

        int baseX = _textX;
        int tall = Fonts.GetFontTall(font) + 2;
        bool underline = IsUnderlined(font);
        bool additive = IsAdditive(font, drawType);
        (byte, byte, byte) colour = additive ? ((byte)255, (byte)255, (byte)255) : (_textColor.Red, _textColor.Green, _textColor.Blue);
        byte alpha = _textColor.Alpha;
        int pen = 0;

        foreach (char character in text)
        {
            (int a, int b, int c) = Fonts.GetCharAbcWide(font, character);
            int wide = b + 2;

            if (underline)
            {
                wide += a + c;
                baseX -= a;
            }

            if (!char.IsWhiteSpace(character) || underline)
            {
                if (_glyphs.Get(font, character) is not (string texture, float s0, float t0, float s1, float t1))
                {
                    continue;
                }

                int x = (int)Math.Floor(a + 0.6) + baseX + pen;

                Add(texture, x, _textY, x + wide, _textY + tall, s0, t0, s1, t1, colour, (alpha, alpha, alpha, alpha), interpolate: true, additive);
            }

            pen = (int)(Math.Floor(a + b + c + 0.6) + pen);
        }

        _textX += pen;
    }

    /// <summary>The glyph pages, for the renderer to upload.</summary>
    public IReadOnlyDictionary<string, (int Wide, int Tall, byte[] Rgba, int Version)> Pages => _glyphs.Pages;

    private VguiFontManager Fonts => fonts ?? throw new InvalidOperationException("This draw list was made without a font manager.");

    /// <summary>The underline test at 0x18001c640: the handle's first font.</summary>
    private static bool IsUnderlined(VguiFontAmalgam font) => ((font.First?.GlyphSet.Flags ?? 0) & VguiFont.Underline) != 0;

    /// <summary>`FontDrawType_t` resolved: the default is the handle's first font's `additive`.</summary>
    private static bool IsAdditive(VguiFontAmalgam font, VguiFontDrawType drawType) => drawType switch
    {
        VguiFontDrawType.Additive => true,
        VguiFontDrawType.NonAdditive => false,
        _ => ((font.First?.GlyphSet.Flags ?? 0) & VguiFont.Additive) != 0,
    };

    /// <summary>Translates, clips (0x1800039a0) and records one quad.</summary>
    private void Add(
        string? texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, (byte, byte, byte, byte) alphas, bool interpolate) =>
        Add(texture, x0, y0, x1, y1, s0, t0, s1, t1, (_color.Red, _color.Green, _color.Blue), alphas, interpolate, additive: false);

    private void Add(
        string? texture,
        int x0,
        int y0,
        int x1,
        int y1,
        float s0,
        float t0,
        float s1,
        float t1,
        (byte Red, byte Green, byte Blue) colour,
        (byte, byte, byte, byte) alphas,
        bool interpolate,
        bool additive)
    {
        float left = _offsetX + x0;
        float top = _offsetY + y0;
        float right = _offsetX + x1;
        float bottom = _offsetY + y1;

        float clippedLeft = Math.Max(_clip.X0, left);
        float clippedRight = Math.Min(_clip.X1, right);
        float clippedTop = Math.Max(_clip.Y0, top);
        float clippedBottom = Math.Min(_clip.Y1, bottom);

        if (clippedLeft > clippedRight || clippedBottom < clippedTop)
        {
            return;
        }

        if (interpolate)
        {
            (s0, s1) = (Lerp(clippedLeft, left, right, s0, s1), Lerp(clippedRight, left, right, s0, s1));
            (t0, t1) = (Lerp(clippedTop, top, bottom, t0, t1), Lerp(clippedBottom, top, bottom, t0, t1));
        }

        (byte topLeft, byte topRight, byte bottomRight, byte bottomLeft) = alphas;

        _quads.Add(new VguiQuad(
            texture, clippedLeft, clippedTop, clippedRight, clippedBottom, s0, t0, s1, t1,
            colour.Red, colour.Green, colour.Blue, topLeft, topRight, bottomRight, bottomLeft, additive));
    }

    /// <summary>0x180003ca0: where <paramref name="at"/> falls between two edges, carried to their coordinates.</summary>
    private static float Lerp(float at, float from, float to, float valueFrom, float valueTo) =>
        (to - from) is 0f ? ((valueTo - valueFrom) * 0.5f) + valueFrom : ((valueTo - valueFrom) * ((at - from) / (to - from))) + valueFrom;
}
