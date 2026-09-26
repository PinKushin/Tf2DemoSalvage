using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CFontAmalgam`: a font handle — the fonts that draw it, each over a range of characters.</summary>
/// <remarks>vguimatsurface.dll: `AddFont` 0x18001c3b0, `GetFontForChar` 0x18001c570, `GetFontHeight` 0x18001c5c0.</remarks>
public sealed class VguiFontAmalgam
{
    private readonly List<(int Low, int High, VguiWin32Font Font)> _fonts = [];

    /// <summary>Made only by <see cref="VguiFontManager.CreateFont"/>, which keeps the list the handle is an index into.</summary>
    internal VguiFontAmalgam()
    {
    }

    /// <summary>The tallest font added.</summary>
    public int MaxHeight { get; private set; }

    /// <summary>The widest `tmMaxCharWidth` added.</summary>
    public int MaxWidth { get; private set; }

    /// <summary>`GetFontHeight`: the FIRST font's height; the maximum only when there is none.</summary>
    public int Height => _fonts.Count == 0 ? MaxHeight : _fonts[0].Font.Height;

    /// <summary>`GetFontForChar`: the first font whose range holds the character, else the first font, else null.</summary>
    /// <param name="character">The character.</param>
    /// <returns>The font.</returns>
    public VguiWin32Font? GetFontForChar(int character)
    {
        foreach ((int low, int high, VguiWin32Font font) in _fonts)
        {
            if (low <= character && character <= high)
            {
                return font;
            }
        }

        return _fonts.Count == 0 ? null : _fonts[0].Font;
    }

    /// <summary>`RemoveAll`.</summary>
    internal void Clear() => _fonts.Clear();

    /// <summary>`AddFont`.</summary>
    internal void Add(VguiWin32Font font, int low, int high)
    {
        _fonts.Add((low, high, font));
        MaxHeight = Math.Max(MaxHeight, font.Height);
        MaxWidth = Math.Max(MaxWidth, font.MaxCharWidth);
    }
}

/// <summary>`CFontManager`: font handles, and the one `CWin32Font` per face and size they share.</summary>
/// <remarks>
/// vguimatsurface.dll, renamed in `tf2vguimatsurface`.
/// <list type="bullet">
/// <item>`CFontManager_SetFontGlyphSet` (0x180017590): a foreign-capable face (the table at 0x18010d7b8 lists only
/// Marlett) or Tahoma itself covers every character. Any other face takes 0x00–0xFF — or the custom font file's range,
/// its bounds put in order — with Tahoma below and above; it is kept only when both create. A face that will not create
/// leaves Tahoma alone throughout. Failing that, `g_FallbackFonts` (0x18010d7d0) names the next face to try: Times New
/// Roman → Courier New → Courier, Verdana and Trebuchet MS → Arial, Tahoma → nothing, anything else → Tahoma.</item>
/// <item>`CreateOrFindWin32Font` (0x180016770): an existing font of the same face, tall, weight, blur, scanlines and
/// flags is reused — so its width cache is shared too. **Not modelled:** the faces `platform/resource/FontInfo.kv`
/// forces onto FreeType (the GorDIN family, used by the menus, not the HUD).</item>
/// </list>
/// </remarks>
/// <param name="gdi">GDI.</param>
public sealed class VguiFontManager(IVguiGdi gdi)
{
    private static readonly string[] ForeignCapable = ["Marlett"];

    private static readonly (string? Face, string? Fallback)[] Fallbacks =
    [
        ("Times New Roman", "Courier New"),
        ("Courier New", "Courier"),
        ("Verdana", "Arial"),
        ("Trebuchet MS", "Arial"),
        ("Tahoma", null),
        (null, "Tahoma"),
    ];

    private readonly List<VguiWin32Font> _fonts = [];
    private readonly List<VguiFontAmalgam> _handles = [];

    /// <summary>`CreateFont`: a new, empty handle.</summary>
    /// <returns>The handle.</returns>
    public VguiFontAmalgam CreateFont()
    {
        VguiFontAmalgam handle = new();

        _handles.Add(handle);

        return handle;
    }

    /// <summary>`AddCustomFontFile`'s Windows half.</summary>
    /// <param name="fullPath">The font file on disk.</param>
    /// <returns>Whether GDI added it.</returns>
    public bool AddCustomFontFile(string fullPath) => gdi.AddFontResource(fullPath);

    /// <summary>`SetFontGlyphSet`.</summary>
    /// <param name="font">The handle, emptied first.</param>
    /// <param name="glyphSet">The glyph set the scheme chose.</param>
    /// <param name="rangeMin">The custom font file's range start, or 0.</param>
    /// <param name="rangeMax">Its end, or 0.</param>
    /// <returns>Whether any face was set.</returns>
    public bool SetFontGlyphSet(VguiFontAmalgam font, VguiFont glyphSet, int rangeMin, int rangeMax)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(glyphSet);

        font.Clear();

        string? face = glyphSet.Name;

        while (face is not null)
        {
            VguiWin32Font? own = CreateOrFind(glyphSet with { Name = face });

            if (Array.Exists(ForeignCapable, name => string.Equals(name, face, StringComparison.OrdinalIgnoreCase)))
            {
                // A foreign-capable face needs no extension; if it did not create, the next face is tried directly.
                if (own is not null)
                {
                    font.Add(own, 0, 0xffff);
                    return true;
                }

                face = Fallback(face);
                continue;
            }

            if (own is not null && string.Equals(face, "Tahoma", StringComparison.OrdinalIgnoreCase))
            {
                font.Add(own, 0, 0xffff);
                return true;
            }

            VguiWin32Font? extended = CreateOrFind(glyphSet with { Name = "Tahoma" });

            if (own is not null && extended is not null)
            {
                (int low, int high) = (0, 0xff);

                if (rangeMin > 0 || rangeMax > 0)
                {
                    (low, high) = rangeMin <= rangeMax ? (rangeMin, rangeMax) : (rangeMax, rangeMin);
                }

                if (low > 0)
                {
                    font.Add(extended, 0, low - 1);
                }

                font.Add(own, low, high);

                if (high < 0xffff)
                {
                    font.Add(extended, high + 1, 0xffff);
                }

                return true;
            }

            if (own is null && extended is not null)
            {
                font.Add(extended, 0, 0xffff);
                return true;
            }

            face = Fallback(face);
        }

        return false;
    }

    /// <summary>`GetCharABCwide`: the widths from the font drawing the character; none, and <c>b</c> is the handle's widest.</summary>
    /// <param name="font">The handle.</param>
    /// <param name="character">The character.</param>
    /// <returns>Leading, glyph and trailing widths.</returns>
    public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character)
    {
        ArgumentNullException.ThrowIfNull(font);

        // 0x180016a70: a handle outside the manager's list answers zeros.
        if (!_handles.Contains(font))
        {
            return (0, 0, 0);
        }

        return font.GetFontForChar(character) is { } drawn ? drawn.GetCharAbcWidths(character) : (0, font.MaxWidth, 0);
    }

    /// <summary>`GetFontTall`.</summary>
    /// <param name="font">The handle.</param>
    /// <returns>The first font's height; 0 for a handle this manager did not make.</returns>
    public int GetFontTall(VguiFontAmalgam font)
    {
        ArgumentNullException.ThrowIfNull(font);

        return _handles.Contains(font) ? font.Height : 0;
    }

    /// <summary>`CreateOrFindWin32Font`: an equal font reused, else a new one kept only if it creates.</summary>
    private VguiWin32Font? CreateOrFind(VguiFont glyphSet)
    {
        foreach (VguiWin32Font existing in _fonts)
        {
            VguiFont held = existing.GlyphSet;

            if (string.Equals(held.Name, glyphSet.Name, StringComparison.OrdinalIgnoreCase)
                && (held.Tall, held.Weight, held.Blur, held.Scanlines, held.Flags) == (glyphSet.Tall, glyphSet.Weight, glyphSet.Blur, glyphSet.Scanlines, glyphSet.Flags))
            {
                return existing;
            }
        }

        if (VguiWin32Font.Create(gdi, glyphSet) is not { } created)
        {
            return null;
        }

        _fonts.Add(created);

        return created;
    }

    /// <summary>`GetFallbackFontName`: the listed fallback, else Tahoma.</summary>
    private static string? Fallback(string face)
    {
        foreach ((string? listed, string? fallback) in Fallbacks)
        {
            if (listed is null || string.Equals(listed, face, StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }
        }

        return null;
    }
}
