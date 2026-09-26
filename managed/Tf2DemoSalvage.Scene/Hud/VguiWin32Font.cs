using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CWin32Font`: one glyph set as GDI draws it — the rules; <see cref="IVguiGdi"/> is the calls.</summary>
/// <remarks>
/// **Closed code, `vguimatsurface.dll`, renamed in `tf2vguimatsurface`.**
/// <list type="bullet">
/// <item>`CWin32Font_Create` (0x180017b70): fails when `EnumFontFamiliesExA` does not know the family;
/// `CreateFontA( tall, …, weight, italic, underline, strikeout, SYMBOL_CHARSET for a symbol font else ANSI, …,
/// ANTIALIASED_QUALITY or NONANTIALIASED_QUALITY, … )`. The height kept is `tmHeight` plus one for a dropshadow and two
/// for an outline; the drawing bitmap is `tmMaxCharWidth` plus two for an outline, by that height.</item>
/// <item>`CWin32Font_GetCharABCWidths` (0x180017ec0): GDI's widths — `GetCharABCWidthsW`, then `…A`, then the text extent
/// as `b`, then `tmMaxCharWidth` — cached WIDENED by blur and effects. **The call that fills the cache returns GDI's own
/// numbers**; every later call returns the widened ones.</item>
/// <item>`CWin32Font_GetCharRGBA` (0x1800181e0): an antialiased font draws a character up to 0xFF (any, when `custom`)
/// from `GGO_GRAY8_BITMAP`, white with alpha level / 64; anything else — and a glyph with no outline, like a space — is
/// drawn white on black with `ExtTextOutW`, its colour kept and its alpha weighed 0.34 / 0.55 / 0.11 across the bitmap's
/// bytes in memory order. A tab has no colour. Then dropshadow, outline, blur, scanlines, rotary
/// (<see cref="VguiFontEffects"/>). The engine's version-check flag is taken as set: it is `GetVersionExA` major &gt; 4,
/// which every Windows since 2000 is.</item>
/// </list>
/// </remarks>
public sealed class VguiWin32Font
{
    private readonly IVguiGdi _gdi;
    private readonly VguiGdiFont _font;
    private readonly Dictionary<char, (int A, int B, int C)> _widths = [];

    private VguiWin32Font(IVguiGdi gdi, VguiGdiFont font, VguiFont glyphSet)
    {
        _gdi = gdi;
        _font = font;
        GlyphSet = glyphSet;
    }

    /// <summary>What it was made from.</summary>
    public VguiFont GlyphSet { get; }

    /// <summary>`GetHeight`: `tmHeight`, plus 1 for a dropshadow and 2 for an outline.</summary>
    public int Height { get; private init; }

    /// <summary>`GetAscent`: `tmAscent`.</summary>
    public int Ascent { get; private init; }

    /// <summary>`GetMaxCharWidth`: `tmMaxCharWidth`.</summary>
    public int MaxCharWidth { get; private init; }

    private int BitmapWide { get; init; }

    private int BitmapTall { get; init; }

    private int OutlineSize => (GlyphSet.Flags & VguiFont.Outline) != 0 ? 1 : 0;

    private int DropShadowOffset => (GlyphSet.Flags & VguiFont.DropShadow) != 0 ? 1 : 0;

    private bool Underline => (GlyphSet.Flags & VguiFont.Underline) != 0;

    /// <summary>`CWin32Font::Create`.</summary>
    /// <param name="gdi">GDI.</param>
    /// <param name="glyphSet">The scheme's glyph set.</param>
    /// <returns>The font, or null when GDI does not know the family or will not make it.</returns>
    public static VguiWin32Font? Create(IVguiGdi gdi, VguiFont glyphSet)
    {
        ArgumentNullException.ThrowIfNull(gdi);
        ArgumentNullException.ThrowIfNull(glyphSet);

        int flags = glyphSet.Flags;
        int charset = (flags >> 2) & 2;
        string family = glyphSet.Name;

        // The one special case in `Create`: this name enumerates Tahoma in the Japanese charset.
        if (string.Equals(glyphSet.Name, "win98japanese", StringComparison.OrdinalIgnoreCase))
        {
            charset = 0x80;
            family = "Tahoma";
        }

        if (!gdi.FamilyExists(family))
        {
            return null;
        }

        int quality = (flags & VguiFont.Antialias) != 0 ? 4 : 3;

        if (gdi.CreateFont(
                glyphSet.Name,
                glyphSet.Tall,
                glyphSet.Weight,
                (flags & VguiFont.Italic) != 0,
                (flags & VguiFont.Underline) != 0,
                (flags & VguiFont.Strikeout) != 0,
                charset,
                quality) is not { } font)
        {
            return null;
        }

        int outline = (flags & VguiFont.Outline) != 0 ? 1 : 0;
        int shadow = (flags & VguiFont.DropShadow) != 0 ? 1 : 0;
        int height = font.Height + shadow + (outline * 2);
        int bitmapWide = font.MaxCharWidth + (outline * 2);

        gdi.CreateBitmap(font, bitmapWide, height);

        return new VguiWin32Font(gdi, font, glyphSet)
        {
            Height = height,
            Ascent = font.Ascent,
            MaxCharWidth = font.MaxCharWidth,
            BitmapWide = bitmapWide,
            BitmapTall = height,
        };
    }

    /// <summary>`GetCharABCWidths`.</summary>
    /// <param name="character">The character.</param>
    /// <returns>Leading, glyph and trailing widths.</returns>
    public (int A, int B, int C) GetCharAbcWidths(char character)
    {
        if (_widths.TryGetValue(character, out (int A, int B, int C) cached))
        {
            return cached;
        }

        (int a, int b, int c) = _gdi.GetCharAbcWidths(_font, character)
            ?? (0, _gdi.GetTextExtent(_font, character) ?? MaxCharWidth, 0);

        int blur = GlyphSet.Blur;
        int outline = OutlineSize;
        int shadow = DropShadowOffset;

        _widths[character] = (a - blur - outline, b + ((outline + blur) * 2) + shadow, c - blur - shadow - outline);

        return (a, b, c);
    }

    /// <summary>`GetCharRGBA`: one character's cell, effects applied.</summary>
    /// <param name="character">The character.</param>
    /// <param name="rgbaWide">The cell's width.</param>
    /// <param name="rgbaTall">The cell's height.</param>
    /// <param name="rgba">The cell, <c>rgbaWide × rgbaTall × 4</c> bytes, written where the glyph falls.</param>
    public void GetCharRgba(char character, int rgbaWide, int rgbaTall, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        (int a, int b, int c) = GetCharAbcWidths(character);
        int wide = Underline ? a + b + c : b;
        bool antialias = (GlyphSet.Flags & VguiFont.Antialias) != 0
            && (character <= 0xff || (GlyphSet.Flags & VguiFont.Custom) != 0);

        if (antialias && _gdi.GetGlyphOutlineGray8(_font, character) is { } glyph)
        {
            CopyGray(glyph, character, b, rgbaWide, rgbaTall, rgba);
        }
        else
        {
            CopyDrawn(character, Underline ? 0 : -a, wide, rgbaWide, rgbaTall, rgba);
        }

        VguiFontEffects.DropShadow(rgbaWide, rgbaTall, rgba, DropShadowOffset);
        VguiFontEffects.Outline(rgbaWide, rgbaTall, rgba, OutlineSize);
        VguiFontEffects.GaussianBlur(rgbaWide, rgbaTall, rgba, GlyphSet.Blur);
        VguiFontEffects.Scanlines(rgbaWide, rgbaTall, rgba, GlyphSet.Scanlines);

        if ((GlyphSet.Flags & VguiFont.Rotary) != 0)
        {
            VguiFontEffects.Rotary(rgbaWide, rgbaTall, rgba);
        }
    }

    /// <summary>The `GGO_GRAY8_BITMAP` path: rows padded to four bytes, centred when wider than b + 2.</summary>
    private void CopyGray(VguiGrayGlyph glyph, char character, int b, int rgbaWide, int rgbaTall, byte[] rgba)
    {
        int pitch = (glyph.BlackBoxX + 3) & ~3;
        int top = Ascent - glyph.OriginY;
        int first = glyph.BlackBoxX >= b + 2 ? (glyph.BlackBoxX - b) >> 1 : 0;
        int left = OutlineSize + GlyphSet.Blur - first;

        for (int row = 0; row < glyph.BlackBoxY; row++)
        {
            for (int column = first; column < glyph.BlackBoxX; column++)
            {
                int x = left + column;
                int y = top + row;

                // The engine checks only the far edges; a glyph above its ascent would write before the cell.
                if (x >= rgbaWide || y >= rgbaTall || y < 0)
                {
                    continue;
                }

                byte level = glyph.Levels[(row * pitch) + column];
                float alpha = level == 0 ? 0f : Math.Min(level * 0.015625f, 1f);
                byte colour = level != 0 && character != '\t' ? (byte)255 : (byte)0;
                int at = ((y * rgbaWide) + x) * 4;

                rgba[at] = colour;
                rgba[at + 1] = colour;
                rgba[at + 2] = colour;
                rgba[at + 3] = (byte)(int)(alpha * 255f);
            }
        }
    }

    /// <summary>The `ExtTextOutW` path: the bitmap's bytes copied inside the outline margin, alpha weighed from them.</summary>
    private void CopyDrawn(char character, int penX, int wide, int rgbaWide, int rgbaTall, byte[] rgba)
    {
        byte[] bitmap = _gdi.DrawGlyph(_font, character, penX, wide, Height);
        int outline = OutlineSize;
        int shadow = DropShadowOffset;
        int right = Math.Min(wide, BitmapWide);
        int bottom = Math.Min(Height, BitmapTall);

        for (int y = outline; y < bottom - outline; y++)
        {
            for (int x = outline; x < right - shadow - outline; x++)
            {
                if (x >= rgbaWide || y >= rgbaTall)
                {
                    continue;
                }

                int from = ((BitmapWide * y) + x) * 4;
                int to = ((y * rgbaWide) + x) * 4;
                (byte first, byte second, byte third) = character == '\t' ? ((byte)0, (byte)0, (byte)0) : (bitmap[from], bitmap[from + 1], bitmap[from + 2]);

                rgba[to] = first;
                rgba[to + 1] = second;
                rgba[to + 2] = third;
                rgba[to + 3] = (byte)(int)((first * 0.34f) + (second * 0.55f) + (third * 0.11f));
            }
        }

        // A dropshadow's font clears the cell's last row across the drawn width.
        if (shadow != 0 && Height - 1 < rgbaTall)
        {
            Array.Clear(rgba, (Height - 1) * rgbaWide * 4, Math.Min(right, rgbaWide) * 4);
        }
    }
}
