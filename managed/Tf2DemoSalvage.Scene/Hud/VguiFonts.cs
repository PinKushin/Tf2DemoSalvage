using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>A scheme font's glyph set — what `ReloadFontGlyphs` hands `ISurface::SetFontGlyphSet`.</summary>
/// <param name="Name">The typeface.</param>
/// <param name="Tall">Height in pixels, after scaling and clamping.</param>
/// <param name="Weight">GDI weight.</param>
/// <param name="Blur">Blur, scaled with the tall.</param>
/// <param name="Scanlines">Scanlines, scaled with the tall.</param>
/// <param name="Flags">The surface's font flags: <see cref="Italic"/> and the rest.</param>
/// <param name="ScaleX">`scalex`, for a bitmap font.</param>
/// <param name="ScaleY">`scaley`, for a bitmap font.</param>
public sealed record VguiFont(string Name, int Tall, int Weight, int Blur, int Scanlines, int Flags, float ScaleX, float ScaleY)
{
    /// <summary>`FONTFLAG_ITALIC`.</summary>
    public const int Italic = 0x001;

    /// <summary>`FONTFLAG_UNDERLINE`.</summary>
    public const int Underline = 0x002;

    /// <summary>`FONTFLAG_STRIKEOUT`.</summary>
    public const int Strikeout = 0x004;

    /// <summary>`FONTFLAG_SYMBOL`.</summary>
    public const int Symbol = 0x008;

    /// <summary>`FONTFLAG_ANTIALIAS`.</summary>
    public const int Antialias = 0x010;

    /// <summary>`FONTFLAG_ROTARY`.</summary>
    public const int Rotary = 0x040;

    /// <summary>`FONTFLAG_DROPSHADOW`.</summary>
    public const int DropShadow = 0x080;

    /// <summary>`FONTFLAG_ADDITIVE`.</summary>
    public const int Additive = 0x100;

    /// <summary>`FONTFLAG_OUTLINE`.</summary>
    public const int Outline = 0x200;

    /// <summary>`FONTFLAG_CUSTOM`.</summary>
    public const int Custom = 0x400;

    /// <summary>`FONTFLAG_BITMAP`.</summary>
    public const int Bitmap = 0x800;
}

/// <summary>Chooses a scheme font's glyph set as `vgui2.dll`'s `ReloadFontGlyphs` (0x18000ebf0) does.</summary>
/// <remarks>
/// **Closed code, read from the disassembly.** For each registered font (`LoadFonts`, 0x18000e040, makes a plain handle and
/// a proportional one unless `isproportional` is `only`, which makes the proportional alone):
/// <list type="bullet">
/// <item>The numbered entries are walked in order, `isproportional` skipped; `yres` is scanned as `%d %d` (0x18005cc98). A
/// minimum of 0 means any screen; otherwise a maximum of 0 becomes the minimum, and the entry is skipped unless the screen
/// tall lies within. **The first entry that passes wins, and the walk stops** — nothing here replaces a font that fails to
/// load, whatever the comment in Valve's schemes says.</item>
/// <item>The flags: `italic` 1, `underline` 2, `strikeout` 4, `symbol` 8, `antialias` 0x10, `rotary` 0x40, `dropshadow`
/// 0x80, `additive` 0x100, `outline` 0x200, `custom` 0x400, `bitmap` 0x800 — the three effects only when the surface
/// supports them, which `CMatSystemSurface` does.</item>
/// <item>On a proportional handle whose entry named no `yres`, `tall`, `blur` and `scanlines` go through the proportional
/// scale, and `scalex`/`scaley` as ten-thousandths; when that changed `tall`, antialias is forced on.</item>
/// <item>`tall` is clamped to 255, then raised to the language's minimum: 13 for korean, tchinese, schinese and japanese,
/// 18 for thai, else none.</item>
/// </list>
/// </remarks>
public static class VguiFonts
{
    /// <summary>`ReloadFontGlyphs`'s cap on a glyph set's height.</summary>
    private const int MaximumTall = 255;

    /// <summary>A font's glyph set at a screen tall.</summary>
    /// <param name="fonts">The scheme's `Fonts` block.</param>
    /// <param name="name">The font's name.</param>
    /// <param name="proportional">Whether the proportional handle is asked for.</param>
    /// <param name="screenTall">The screen's (or sizing panel's) height.</param>
    /// <param name="language">The game's language, as the registry names it.</param>
    /// <returns>The glyph set, or null when the font is absent or no entry covers the screen.</returns>
    public static VguiFont? Resolve(KeyValuesTree fonts, string name, bool proportional, int screenTall, string language)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(name);

        if (fonts.Find(name) is not { } font)
        {
            return null;
        }

        foreach (KeyValuesTree entry in font.Children)
        {
            if (string.Equals(entry.Name, "isproportional", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (int low, int high) = Range(entry.Find("yres")?.Value);

            if (low != 0)
            {
                high = high == 0 ? low : high;

                if (screenTall < low || screenTall > high)
                {
                    continue;
                }
            }

            return Build(entry, proportional && low == 0 && high == 0, screenTall, language);
        }

        return null;
    }

    private static VguiFont Build(KeyValuesTree entry, bool scaled, int screenTall, string language)
    {
        int flags = 0;

        flags |= Int(entry, "italic") != 0 ? VguiFont.Italic : 0;
        flags |= Int(entry, "underline") != 0 ? VguiFont.Underline : 0;
        flags |= Int(entry, "strikeout") != 0 ? VguiFont.Strikeout : 0;
        flags |= Int(entry, "symbol") != 0 ? VguiFont.Symbol : 0;
        flags |= Int(entry, "antialias") != 0 ? VguiFont.Antialias : 0;
        flags |= Int(entry, "dropshadow") != 0 ? VguiFont.DropShadow : 0;
        flags |= Int(entry, "outline") != 0 ? VguiFont.Outline : 0;
        flags |= Int(entry, "custom") != 0 ? VguiFont.Custom : 0;
        flags |= Int(entry, "bitmap") != 0 ? VguiFont.Bitmap : 0;
        flags |= Int(entry, "rotary") != 0 ? VguiFont.Rotary : 0;
        flags |= Int(entry, "additive") != 0 ? VguiFont.Additive : 0;

        int authored = Int(entry, "tall");
        int tall = authored;
        int blur = Int(entry, "blur");
        int scanlines = Int(entry, "scanlines");
        float scaleX = Float(entry, "scalex", 1f);
        float scaleY = Float(entry, "scaley", 1f);

        if (scaled)
        {
            tall = PanelLayout.ProportionalScaled(authored, screenTall);
            blur = PanelLayout.ProportionalScaled(blur, screenTall);
            scanlines = PanelLayout.ProportionalScaled(scanlines, screenTall);
            scaleX = PanelLayout.ProportionalScaled((int)(scaleX * 10000f), screenTall) * 0.0001f;
            scaleY = PanelLayout.ProportionalScaled((int)(scaleY * 10000f), screenTall) * 0.0001f;

            if (tall != authored)
            {
                flags |= VguiFont.Antialias;
            }
        }

        tall = Math.Max(Math.Min(tall, MaximumTall), LanguageMinimum(language));

        return new VguiFont(entry.Find("name")?.Value ?? string.Empty, tall, Int(entry, "weight"), blur, scanlines, flags, scaleX, scaleY);
    }

    private static int LanguageMinimum(string language) =>
        language.ToUpperInvariant() switch
        {
            "KOREAN" or "TCHINESE" or "SCHINESE" or "JAPANESE" => 13,
            "THAI" => 18,
            _ => 0,
        };

    /// <summary>`sscanf( yres, "%d %d" )` into zeroed ints.</summary>
    private static (int Low, int High) Range(string? text)
    {
        if (text is null)
        {
            return (0, 0);
        }

        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return (parts.Length > 0 ? PanelLayout.Atoi(parts[0]) : 0, parts.Length > 1 ? PanelLayout.Atoi(parts[1]) : 0);
    }

    /// <summary>`KeyValues::GetInt` on a string value: `atoi`.</summary>
    private static int Int(KeyValuesTree entry, string key) =>
        entry.Find(key)?.Value is { } value ? PanelLayout.Atoi(value) : 0;

    /// <summary>`KeyValues::GetFloat` on a string value: `atof`.</summary>
    private static float Float(KeyValuesTree entry, string key, float fallback) =>
        entry.Find(key)?.Value is { } value ? PanelLayout.Atof(value) : fallback;
}
