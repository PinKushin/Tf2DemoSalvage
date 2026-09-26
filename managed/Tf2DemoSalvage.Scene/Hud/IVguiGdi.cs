namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>A GDI font `CWin32Font::Create` made, with the `GetTextMetricsA` numbers it keeps.</summary>
/// <param name="Handle">The adapter's own handle.</param>
/// <param name="Height">`tmHeight`.</param>
/// <param name="Ascent">`tmAscent`.</param>
/// <param name="MaxCharWidth">`tmMaxCharWidth`.</param>
public sealed record VguiGdiFont(object Handle, int Height, int Ascent, int MaxCharWidth);

/// <summary>A `GGO_GRAY8_BITMAP` glyph: levels 0–64, rows padded to four bytes.</summary>
/// <param name="Levels">The bitmap.</param>
/// <param name="BlackBoxX">`gmBlackBoxX`.</param>
/// <param name="BlackBoxY">`gmBlackBoxY`.</param>
/// <param name="OriginY">`gmptGlyphOrigin.y`.</param>
public sealed record VguiGrayGlyph(byte[] Levels, int BlackBoxX, int BlackBoxY, int OriginY);

/// <summary>The GDI calls `CWin32Font` makes — the adapter half; everything the font DOES with them is <see cref="VguiWin32Font"/>.</summary>
public interface IVguiGdi
{
    /// <summary>`CMatSystemSurface::AddCustomFontFile`'s Windows half: `AddFontResourceExA( path, FR_PRIVATE )`.</summary>
    /// <param name="path">The font file's full path on disk.</param>
    /// <returns>Whether GDI added any font from it.</returns>
    public bool AddFontResource(string path);

    /// <summary>`EnumFontFamiliesExA` with `DEFAULT_CHARSET`: whether the family is installed or privately added.</summary>
    /// <param name="family">The face name.</param>
    /// <returns>Whether it was enumerated.</returns>
    public bool FamilyExists(string family);

    /// <summary>`CreateFontA`, selected into the font's own DC with `MM_TEXT` and `TA_UPDATECP`, then `GetTextMetricsA`.</summary>
    /// <param name="face">The face name, as the scheme wrote it.</param>
    /// <param name="tall">`cHeight`.</param>
    /// <param name="weight">`cWeight`.</param>
    /// <param name="italic">`bItalic`.</param>
    /// <param name="underline">`bUnderline`.</param>
    /// <param name="strikeout">`bStrikeOut`.</param>
    /// <param name="charset">`iCharSet`.</param>
    /// <param name="quality">`iQuality`: 4 antialiased, 3 not.</param>
    /// <returns>The font, or null when GDI made none or its metrics would not read.</returns>
    public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality);

    /// <summary>`CreateDIBSection`: the font's 32-bit top-down drawing surface, selected into its DC.</summary>
    /// <param name="font">The font.</param>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    public void CreateBitmap(VguiGdiFont font, int wide, int tall);

    /// <summary>`GetCharABCWidthsW`, else `GetCharABCWidthsA`, for one character.</summary>
    /// <param name="font">The font.</param>
    /// <param name="character">The character.</param>
    /// <returns>The widths, or null when both fail.</returns>
    public (int A, int B, int C)? GetCharAbcWidths(VguiGdiFont font, char character);

    /// <summary>`GetTextExtentPoint32A` of the character converted to the ANSI code page.</summary>
    /// <param name="font">The font.</param>
    /// <param name="character">The character.</param>
    /// <returns>`cx`, or null when it fails.</returns>
    public int? GetTextExtent(VguiGdiFont font, char character);

    /// <summary>`GetGlyphOutlineA( ch, GGO_GRAY8_BITMAP, identity )`.</summary>
    /// <param name="font">The font.</param>
    /// <param name="character">The character code, passed as it is.</param>
    /// <returns>The glyph, or null when the buffer size is not positive.</returns>
    public VguiGrayGlyph? GetGlyphOutlineGray8(VguiGdiFont font, int character);

    /// <summary>White on opaque black into the font's bitmap: `MoveToEx( x, 0 )`, clear `(0, 0, wide, tall)`, `ExtTextOutW` one character.</summary>
    /// <param name="font">The font.</param>
    /// <param name="character">The character.</param>
    /// <param name="penX">Where the pen starts.</param>
    /// <param name="clearWide">The cleared rectangle's width.</param>
    /// <param name="clearTall">Its height.</param>
    /// <returns>The bitmap's bytes in memory order — blue, green, red, pad — rows of its full width.</returns>
    public byte[] DrawGlyph(VguiGdiFont font, char character, int penX, int clearWide, int clearTall);
}
