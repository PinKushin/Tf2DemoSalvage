using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>A scheme's fonts as handles: `vgui2.dll`'s `CScheme_LoadFonts` (0x18000e040) and `ReloadFontGlyphs` (0x18000ebf0).</summary>
/// <remarks>
/// <list type="bullet">
/// <item>`CustomFontFiles`: an entry with a value is a font file; an entry that is a block names its file in `font`, its
/// face in `name`, and — under a sub-block named for the current language — a `range` scanned as <c>%x %x</c> and put in
/// order. Every named file goes to `AddCustomFontFile` (the surface resolves it on disk through the GAME, then PLATFORM,
/// search paths); a range is registered against the face only when one was found.</item>
/// <item>`Fonts`: each entry makes a `-no` handle unless `isproportional` is `only`, then a `-p` handle, keyed without case
/// (`GetFont`, 0x18000d0c0).</item>
/// <item>`ReloadFontGlyphs`: each handle gets the glyph set <see cref="VguiFonts.Resolve"/> chooses and its face's range; a
/// handle nothing covers is left empty and draws nothing. Bitmap fonts are not modelled.</item>
/// </list>
/// </remarks>
public sealed class VguiSchemeFonts
{
    private readonly Dictionary<(string Name, bool Proportional), VguiFontAmalgam> _handles = new(Comparer.Instance);
    private readonly Dictionary<string, (int Min, int Max)> _ranges = new(StringComparer.OrdinalIgnoreCase);

    private VguiSchemeFonts()
    {
    }

    /// <summary>Loads the fonts of a scheme file.</summary>
    /// <param name="root">The scheme file's root.</param>
    /// <param name="manager">The font manager.</param>
    /// <param name="fullPath">A game path to the file on disk, or null when there is none.</param>
    /// <param name="language">The game's language.</param>
    /// <param name="screenTall">The screen tall, for glyph set choice and proportional scaling.</param>
    /// <returns>The handles.</returns>
    public static VguiSchemeFonts Load(KeyValuesTree root, VguiFontManager manager, Func<string, string?> fullPath, string language, int screenTall)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(fullPath);

        VguiSchemeFonts fonts = new();

        foreach (KeyValuesTree entry in root.FindOrCreate("CustomFontFiles").Children)
        {
            fonts.AddCustomFontFile(entry, manager, fullPath, language);
        }

        KeyValuesTree block = root.FindOrCreate("Fonts");

        foreach (KeyValuesTree font in block.Children)
        {
            if (!string.Equals(font.Find("isproportional")?.Value, "only", StringComparison.Ordinal))
            {
                fonts._handles[(font.Name, false)] = manager.CreateFont();
            }

            fonts._handles[(font.Name, true)] = manager.CreateFont();
        }

        foreach (((string name, bool proportional), VguiFontAmalgam handle) in fonts._handles)
        {
            if (VguiFonts.Resolve(block, name, proportional, screenTall, language) is not { } glyphSet)
            {
                continue;
            }

            (int min, int max) = fonts._ranges.TryGetValue(glyphSet.Name, out (int, int) range) ? range : (0, 0);

            manager.SetFontGlyphSet(handle, glyphSet, min, max);
        }

        return fonts;
    }

    /// <summary>`GetFont`.</summary>
    /// <param name="name">The scheme font's name.</param>
    /// <param name="proportional">Whether the proportional handle is asked for.</param>
    /// <returns>The handle, or null — handle 0 — when there is none.</returns>
    public VguiFontAmalgam? GetFont(string name, bool proportional) =>
        _handles.TryGetValue((name, proportional), out VguiFontAmalgam? handle) ? handle : null;

    private void AddCustomFontFile(KeyValuesTree entry, VguiFontManager manager, Func<string, string?> fullPath, string language)
    {
        if (entry.Value is { Length: > 0 } file)
        {
            Add(file);
            return;
        }

        string? font = null;
        string? face = null;
        (int Min, int Max)? range = null;

        foreach (KeyValuesTree child in entry.Children)
        {
            if (string.Equals(child.Name, "font", StringComparison.OrdinalIgnoreCase))
            {
                font = child.Value;
            }
            else if (string.Equals(child.Name, "name", StringComparison.OrdinalIgnoreCase))
            {
                face = child.Value;
            }
            else if (string.Equals(child.Name, language, StringComparison.OrdinalIgnoreCase) && child.Find("range")?.Value is { } text)
            {
                (int low, int high) = ScanHexPair(text);

                range = low <= high ? (low, high) : (high, low);
            }
        }

        if (font is not { Length: > 0 })
        {
            return;
        }

        Add(font);

        if (range is { } found && face is not null)
        {
            _ranges[face] = found;
        }

        void Add(string path)
        {
            if (fullPath(path) is { } onDisk)
            {
                manager.AddCustomFontFile(onDisk);
            }
        }
    }

    /// <summary>`sscanf( "%x %x" )` into zeroed ints; a `0x` prefix is accepted, as `%x` accepts it.</summary>
    private static (int Low, int High) ScanHexPair(string text)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return (Hex(parts, 0), Hex(parts, 1));

        static int Hex(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return 0;
            }

            ReadOnlySpan<char> digits = parts[index];

            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                digits = digits[2..];
            }

            int length = 0;

            while (length < digits.Length && char.IsAsciiHexDigit(digits[length]))
            {
                length++;
            }

            return int.TryParse(digits[..length], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }
    }

    /// <summary>The handle key compared as the scheme's dictionary compares it: name without case, then proportionality.</summary>
    private sealed class Comparer : IEqualityComparer<(string Name, bool Proportional)>
    {
        public static Comparer Instance { get; } = new();

        public bool Equals((string Name, bool Proportional) x, (string Name, bool Proportional) y) =>
            x.Proportional == y.Proportional && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, bool Proportional) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Proportional);
    }
}
