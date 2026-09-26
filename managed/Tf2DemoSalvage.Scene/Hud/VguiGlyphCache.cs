using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CFontTextureCache`: every glyph drawn so far, rasterised once into a 1024-square page.</summary>
/// <remarks>
/// vguimatsurface.dll, renamed in `tf2vguimatsurface`: `CFontTextureCache_GetTextureForChars` (0x180005d70) sizes a cell
/// <c>max( b, 1 ) + 2</c> wide — plus a + c for an underlined font — by <c>max( tall, 1 ) + 2</c>, and fills it with
/// `GetCharRGBA`; the allocator (0x180005610) keeps one open page per height bucket (the table at 0x18010d038: 16, 32,
/// 64, 128, 256, 512 — the first taller than the cell), packs cells left to right a texel apart, starts a row when the
/// width runs out and a page when the height does. A glyph is keyed by its font HANDLE and character.
/// </remarks>
public sealed class VguiGlyphCache
{
    private const int PageSize = 1024;

    private static readonly int[] Buckets = [16, 32, 64, 128, 256, 512];

    private readonly int[] _openPage = [-1, -1, -1, -1, -1, -1];
    private readonly List<Page> _pages = [];
    private readonly Dictionary<string, (int Wide, int Tall, byte[] Rgba, int Version)> _published = new(StringComparer.Ordinal);
    private readonly Dictionary<(VguiFontAmalgam Font, char Character), (string Texture, float S0, float T0, float S1, float T1)?> _glyphs = [];

    /// <summary>Each page's RGBA and a version that moves when a glyph is added, keyed by the name the draw list uses.</summary>
    public IReadOnlyDictionary<string, (int Wide, int Tall, byte[] Rgba, int Version)> Pages => _published;

    /// <summary>`GetTextureForChar`: the page and texture coordinates of a glyph, rasterising it on first use.</summary>
    /// <param name="font">The handle.</param>
    /// <param name="character">The character.</param>
    /// <returns>Where it is, or null when no font draws it or no page can hold it.</returns>
    public (string Texture, float S0, float T0, float S1, float T1)? Get(VguiFontAmalgam font, char character)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (_glyphs.TryGetValue((font, character), out (string, float, float, float, float)? known))
        {
            return known;
        }

        if (font.GetFontForChar(character) is not { } drawing)
        {
            return null;
        }

        (int a, int b, int c) = drawing.GetCharAbcWidths(character);
        int wide = Math.Max(b, 1) + 2;
        int tall = Math.Max(drawing.Height, 1) + 2;

        if ((drawing.GlyphSet.Flags & VguiFont.Underline) != 0)
        {
            wide += c + a;
        }

        if (Allocate(wide, tall) is not (int index, int x, int y))
        {
            return null;
        }

        byte[] cell = new byte[wide * tall * 4];

        drawing.GetCharRgba(character, wide, tall, cell);

        Page page = _pages[index];

        for (int row = 0; row < tall; row++)
        {
            Buffer.BlockCopy(cell, row * wide * 4, page.Rgba, (((y + row) * PageSize) + x) * 4, wide * 4);
        }

        page.Version++;
        _published[page.Name] = (PageSize, PageSize, page.Rgba, page.Version);

        (string, float, float, float, float) placed =
            (page.Name, x / (float)PageSize, y / (float)PageSize, (x + wide) / (float)PageSize, (y + tall) / (float)PageSize);

        _glyphs[(font, character)] = placed;

        return placed;
    }

    /// <summary>0x180005610.</summary>
    private (int Page, int X, int Y)? Allocate(int wide, int tall)
    {
        int bucket = Array.FindIndex(Buckets, height => height > tall);

        if (bucket < 0)
        {
            return null;
        }

        if (_openPage[bucket] >= 0)
        {
            Page open = _pages[_openPage[bucket]];
            int end = open.X + wide;

            if (end > PageSize)
            {
                open.Y += open.RowTall;
                open.X = 0;
                open.RowTall = tall;
                end = wide;
            }

            open.RowTall = Math.Max(open.RowTall, tall);

            if (open.RowTall + open.Y <= PageSize)
            {
                (int x, int y) = (open.X, open.Y);

                open.X = end + 1;

                return (_openPage[bucket], x, y);
            }
        }

        Page page = new(string.Create(CultureInfo.InvariantCulture, $"__vgui_glyphs{_pages.Count}")) { RowTall = tall, X = wide + 1 };

        _pages.Add(page);
        _openPage[bucket] = _pages.Count - 1;
        _published[page.Name] = (PageSize, PageSize, page.Rgba, page.Version);

        return (_pages.Count - 1, 0, 0);
    }

    private sealed class Page(string name)
    {
        public string Name { get; } = name;

        public byte[] Rgba { get; } = new byte[PageSize * PageSize * 4];

        public int X { get; set; }

        public int Y { get; set; }

        public int RowTall { get; set; }

        public int Version { get; set; }
    }
}
