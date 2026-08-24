using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Where one glyph's box sits relative to the pen, and how far the pen then moves.</summary>
/// <param name="Width">Width of the glyph's box in pixels.</param>
/// <param name="Height">Height of the glyph's box in pixels.</param>
/// <param name="LeftBearing">Pixels right of the pen at which the box starts.</param>
/// <param name="TopBearing">Pixels below the line's top at which the box starts.</param>
/// <param name="Advance">Pixels the pen moves after drawing, which is NOT the width.</param>
/// <remarks>
/// **Advance and width are separate and conflating them is the classic layout bug.** The pen moves
/// by <paramref name="Advance"/>; the box is drawn at the bearings. A character with a negative
/// left bearing — an italic <c>f</c> — overhangs the character before it, and a layout that used
/// the width would push it right and accumulate the error along the line.
/// </remarks>
public readonly record struct GlyphMetrics(
    int Width,
    int Height,
    int LeftBearing,
    int TopBearing,
    int Advance);

/// <summary>One glyph as a rasteriser produced it.</summary>
/// <remarks>
/// **RGBA, four bytes a pixel, row-major — and it is not single-channel alpha for a reason worth
/// stating, because alpha is the obvious choice and it cannot work.** A one-channel mask carries
/// shape only, so a HUD that tints it tints everything in it. TF2's outlined text has a BLACK
/// outline around a coloured body, at every colour the HUD uses, and no tint of a single mask
/// produces two colours.
///
/// Storing colour beside coverage solves it with no extra pass: the body is written white and the
/// outline black, and the draw multiplies RGB by the requested colour. White becomes the colour and
/// black stays black, because <c>0 × c = 0</c>.
///
/// **The REQUIREMENT is established from published code; only the storage is ours.** Two facts
/// settle it without a decompile:
///
/// - `ISurface.h:171` declares <c>DrawSetTextureRGBA(int id, const unsigned char *rgba, ...)</c>, so
///   VGUI's texture path is RGBA rather than an alpha mask.
/// - `CFPSPanel::Paint` calls
///   <c>DrawColoredText( m_hFont, x, 2, ucColor[0], ucColor[1], ucColor[2], 255, ... )</c> — ONE
///   font handle, and `GetFPSColor` hands it red, yellow or green depending on the rate. The
///   outline is black at all three.
///
/// So a single font texture must draw at a varying colour with an outline that does not vary. That
/// is a fact about the engine, not a guess. Whether `vguimatsurface` achieves it exactly this way
/// is unknown and does not matter — it is closed, and any arrangement meeting the requirement is
/// correct. If the decompile ever happens (D84) it can confirm the mechanism; it cannot change the
/// requirement.
/// </remarks>
public sealed record RasterisedGlyph
{
    /// <summary>Where the glyph sits and how far it advances.</summary>
    public required GlyphMetrics Metrics { get; init; }

    /// <summary>RGBA, <c>Width * Height * 4</c> bytes, row-major.</summary>
    /// <remarks>
    /// <see cref="ReadOnlyMemory{T}"/> rather than an array, which is CA1819: an array property
    /// hands every caller a writable window onto the object's own state. A <c>byte[]</c> converts
    /// implicitly at the construction site, so this costs the producer nothing.
    /// </remarks>
    public required ReadOnlyMemory<byte> Pixels { get; init; }
}

/// <summary>Turns a font and a character into pixels.</summary>
/// <remarks>
/// **The seam D84 exists to place, and it is here rather than in <c>Render</c> on purpose.** The
/// abstraction lives in the portable project and the Windows implementation lives in the
/// Direct3D one, which is the direction dependency inversion asks for.
///
/// **It returns the FINISHED glyph, outline included.** In VGUI an outline is not a font feature —
/// it is Valve's own post-process over the GDI bitmap, as are <c>blur</c>, <c>dropshadow</c> and
/// <c>scanlines</c>. So an implementation is handed the whole <see cref="SchemeFont"/> and owes a
/// glyph that already looks the way the scheme asked for.
/// </remarks>
public interface IGlyphRasteriser
{
    /// <summary>Distance between the tops of two consecutive lines, in pixels.</summary>
    /// <param name="font">The font being drawn with.</param>
    /// <returns>The line height.</returns>
    public int LineHeight(SchemeFont font);

    /// <summary>Rasterises one character.</summary>
    /// <param name="font">The font being drawn with.</param>
    /// <param name="character">The character to draw.</param>
    /// <returns>The glyph's pixels and metrics.</returns>
    public RasterisedGlyph Rasterise(SchemeFont font, char character);
}

/// <summary>One glyph's place in a packed atlas.</summary>
/// <param name="X">Left edge in the atlas.</param>
/// <param name="Y">Top edge in the atlas.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="LeftBearing">Pixels right of the pen at which to draw.</param>
/// <param name="TopBearing">Pixels below the line top at which to draw.</param>
/// <param name="Advance">Pixels the pen moves after drawing.</param>
public readonly record struct AtlasGlyph(
    int X,
    int Y,
    int Width,
    int Height,
    int LeftBearing,
    int TopBearing,
    int Advance);

/// <summary>Every glyph a HUD font needs, packed into one 8-bit image.</summary>
/// <remarks>
/// **One texture rather than one per glyph, because the alternative is a draw call per character.**
/// A frame rate meter is thirty characters and a scoreboard is several hundred; binding a texture
/// each time would cost more than everything else the HUD does.
///
/// **Shelf packing, not a general rectangle packer.** Glyphs of one font are all much the same
/// height, so rows of uniform height waste very little and the packer stays a dozen lines. A
/// skyline packer would be measurably better for mixed sizes and this has none.
/// </remarks>
public sealed class GlyphAtlas
{
    private readonly Dictionary<char, AtlasGlyph> _glyphs;

    /// <summary>Bytes per pixel: RGBA. See <see cref="RasterisedGlyph"/> for why not one.</summary>
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    private GlyphAtlas(
        Dictionary<char, AtlasGlyph> glyphs,
        byte[] pixels,
        int width,
        int height,
        int lineHeight)
    {
        _glyphs = glyphs;
        _pixels = pixels;
        Width = width;
        Height = height;
        LineHeight = lineHeight;
    }

    /// <summary>RGBA for the whole atlas, <c>Width * Height * 4</c> bytes, row-major.</summary>
    /// <remarks>
    /// A span rather than the array, which is CA1819 — the caller uploads these bytes to a texture
    /// and has no business writing to them.
    /// </remarks>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Atlas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Atlas height in pixels.</summary>
    public int Height { get; }

    /// <summary>Distance between the tops of two consecutive lines.</summary>
    public int LineHeight { get; }

    /// <summary>Finds a glyph.</summary>
    /// <param name="character">The character wanted.</param>
    /// <param name="glyph">Its place in the atlas, when present.</param>
    /// <returns>Whether the atlas carries it.</returns>
    /// <remarks>
    /// **False rather than a blank glyph for a character nobody asked for.** A zero-width
    /// substitute would draw nothing and silently swallow everything outside the built set, so a
    /// HUD meeting an unexpected character would look like a HUD with a gap rather than one with a
    /// bug.
    /// </remarks>
    public bool TryGlyph(char character, out AtlasGlyph glyph) =>
        _glyphs.TryGetValue(character, out glyph);

    /// <summary>Rasterises a set of characters and packs them.</summary>
    /// <param name="rasteriser">Turns characters into pixels.</param>
    /// <param name="font">The font to rasterise with.</param>
    /// <param name="characters">Every character the atlas must carry.</param>
    /// <param name="maximumWidth">Widest the atlas may be, in pixels.</param>
    /// <returns>The packed atlas.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumWidth"/> is not positive.</exception>
    public static GlyphAtlas Build(
        IGlyphRasteriser rasteriser,
        SchemeFont font,
        string characters,
        int maximumWidth = 512)
    {
        ArgumentNullException.ThrowIfNull(rasteriser);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);

        // Rasterised once and held, because a glyph is needed twice: to measure the shelf it goes
        // on, and to copy into the atlas once the atlas size is known.
        Dictionary<char, RasterisedGlyph> drawn = [];

        foreach (char character in characters)
        {
            if (!drawn.ContainsKey(character))
            {
                drawn[character] = rasteriser.Rasterise(font, character);
            }
        }

        Dictionary<char, AtlasGlyph> placed = [];

        int penX = 0;
        int penY = 0;
        int shelfHeight = 0;
        int usedWidth = 0;

        foreach ((char character, RasterisedGlyph glyph) in drawn)
        {
            GlyphMetrics metrics = glyph.Metrics;

            // A glyph wider than the whole atlas would loop for ever if it were allowed to wrap,
            // so the shelf takes it and the atlas simply becomes that wide.
            if (penX > 0 && penX + metrics.Width > maximumWidth)
            {
                penY += shelfHeight;
                penX = 0;
                shelfHeight = 0;
            }

            placed[character] = new AtlasGlyph(
                penX,
                penY,
                metrics.Width,
                metrics.Height,
                metrics.LeftBearing,
                metrics.TopBearing,
                metrics.Advance);

            penX += metrics.Width;
            usedWidth = Math.Max(usedWidth, penX);
            shelfHeight = Math.Max(shelfHeight, metrics.Height);
        }

        int width = Math.Max(1, usedWidth);
        int height = Math.Max(1, penY + shelfHeight);

        byte[] pixels = new byte[width * height * BytesPerPixel];

        foreach ((char character, AtlasGlyph slot) in placed)
        {
            RasterisedGlyph glyph = drawn[character];

            for (int row = 0; row < slot.Height; row++)
            {
                ReadOnlySpan<byte> source = glyph.Pixels.Span.Slice(
                    row * slot.Width * BytesPerPixel,
                    slot.Width * BytesPerPixel);

                source.CopyTo(pixels.AsSpan(
                    ((((slot.Y + row) * width) + slot.X) * BytesPerPixel),
                    slot.Width * BytesPerPixel));
            }
        }

        return new GlyphAtlas(placed, pixels, width, height, rasteriser.LineHeight(font));
    }
}

/// <summary>One glyph, positioned on screen.</summary>
/// <param name="Glyph">Where to read it from in the atlas.</param>
/// <param name="X">Left edge on screen, bearing already applied.</param>
/// <param name="Y">Top edge on screen, bearing already applied.</param>
public readonly record struct PlacedGlyph(AtlasGlyph Glyph, int X, int Y);

/// <summary>Walks a string across an atlas.</summary>
/// <remarks>
/// **Single line and left to right, because that is what a HUD line is.** No wrapping, no
/// bidirectional handling and no shaping: VGUI's own <c>DrawColoredText</c> does none of those
/// either, and a HUD that needed them would need a different instrument rather than a flag on this
/// one.
/// </remarks>
public static class TextLayout
{
    /// <summary>How wide a string will be drawn, in pixels.</summary>
    /// <param name="atlas">The font's atlas.</param>
    /// <param name="text">The string to measure.</param>
    /// <returns>The sum of the advances.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static int Measure(GlyphAtlas atlas, string text)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(text);

        int width = 0;

        foreach (char character in text)
        {
            if (atlas.TryGlyph(character, out AtlasGlyph glyph))
            {
                width += glyph.Advance;
            }
        }

        return width;
    }

    /// <summary>Places each glyph of a string.</summary>
    /// <param name="atlas">The font's atlas.</param>
    /// <param name="text">The string to place.</param>
    /// <param name="x">Left edge of the line.</param>
    /// <param name="y">Top edge of the line.</param>
    /// <returns>Each glyph with its screen position.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A character the atlas does not carry is skipped rather than substituted or thrown on.** A
    /// HUD line is built from live data — a player's name, a map name — and one unexpected
    /// character must not take the frame down; a substitute glyph would be a lie about what the
    /// data said.
    /// </remarks>
    public static IEnumerable<PlacedGlyph> Place(GlyphAtlas atlas, string text, int x, int y)
    {
        // **Checked here and walked in a separate method, which is S4456.** An iterator's body does
        // not run until the first MoveNext, so a guard inside one throws at the foreach rather than
        // at the call — far from the argument that was wrong.
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(text);

        return Walk(atlas, text, x, y);
    }

    /// <summary>Walks the string, once someone enumerates.</summary>
    private static IEnumerable<PlacedGlyph> Walk(GlyphAtlas atlas, string text, int x, int y)
    {
        int pen = x;

        foreach (char character in text)
        {
            if (!atlas.TryGlyph(character, out AtlasGlyph glyph))
            {
                continue;
            }

            yield return new PlacedGlyph(glyph, pen + glyph.LeftBearing, y + glyph.TopBearing);

            // The pen moves by the advance alone. Adding the bearing here is the bug this is
            // written to avoid, and it accumulates rather than showing on one character.
            pen += glyph.Advance;
        }
    }
}
