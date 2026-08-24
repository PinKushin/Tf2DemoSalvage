using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace Tf2DemoSalvage.Fonts;

/// <summary>
/// Rasterises glyphs with GDI, which is what VGUI does on Windows.
/// </summary>
/// <remarks>
/// **Windows pixel parity is the target, on the owner's direction** (D84): *"we will go for windows
/// pixel parity, and just know we cant really test it on linux, which is fine because the viewer
/// cant go opn linux either"*. A SkiaSharp implementation was recommended and reversed, because the
/// consumer — Direct3D 11 behind a WinForms host — cannot run anywhere else, so a cross-platform
/// rasteriser bought protection against a divergence that cannot occur.
///
/// **This is an approximation and it says so rather than claiming parity it has not earned.**
/// `vguimatsurface` is not in `source-sdk-2013`, so Valve's own outline, blur, dropshadow and
/// scanline post-processing cannot be read. What is reproduced here is the OBSERVABLE requirement,
/// which published code does settle:
///
/// - `ISurface.h` declares <c>DrawSetTextureRGBA</c>, so the font texture is RGBA rather than a mask.
/// - `CFPSPanel::Paint` draws one font handle at three different colours and the outline is black in
///   all of them.
///
/// **The outline needs no special handling in the draw, which is the neat part.** The owner's
/// observation: *"black doesnt exactly do anything but maybe get darker when you add more color to
/// it"*. Exactly — black is a fixed point of multiplication, so a baked black outline survives any
/// tint under non-additive blending, and under additive blending it contributes nothing and simply
/// disappears. `FontDrawType_t` names both modes and `FONT_DRAW_DEFAULT` takes the scheme's
/// <c>additive</c> value; `DefaultFixedOutline` declares none, so it is the ordinary path.
/// </remarks>
public sealed class GdiGlyphRasteriser : IGlyphRasteriser, IDisposable
{
    /// <summary>Fonts built so far, so a face is created once rather than per glyph.</summary>
    private readonly Dictionary<string, Font> _fonts = [];

    /// <summary>How strings are measured and drawn.</summary>
    /// <remarks>
    /// **`GenericTypographic` plus `MeasureTrailingSpaces`, and the second half was a real bug found
    /// by a test.** Typographic format alone does not measure trailing spaces — and a lone space is
    /// ENTIRELY trailing, so <c>MeasureString(" ")</c> returns zero width. Every space in every HUD
    /// string would have had a zero advance, rendering the frame rate meter as
    /// <c>90fps(50,100)20.0mson cp_process_f12.bsp</c> with the gaps closed up.
    ///
    /// It would have been invisible to every other test here, because they all measure glyphs that
    /// have ink. The control that caught it asserts the opposite pair: a space has no ink and still
    /// moves the pen.
    ///
    /// `GenericDefault` is still wrong for the other half of the reason — it pads either side of a
    /// string for selection highlighting, which overstates every advance and is not constant, so a
    /// monospaced face stops measuring as monospaced.
    /// </remarks>
    private readonly StringFormat _format = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormat.GenericTypographic.FormatFlags | StringFormatFlags.MeasureTrailingSpaces,
    };

    private bool _disposed;

    /// <summary>GDI's weight for bold. Below this the regular face is used.</summary>
    /// <remarks>
    /// **A known parity gap, named rather than hidden.** GDI's <c>CreateFont</c> takes a weight from
    /// 0 to 1000 and picks the nearest face; <c>System.Drawing.Font</c> offers only
    /// <see cref="FontStyle.Regular"/> and <see cref="FontStyle.Bold"/>. So a scheme asking for 500
    /// (<c>FW_MEDIUM</c>) gets regular here and a medium face in the game, wherever the family has
    /// one. `DefaultFixedOutline` asks for 0, so the meter this was written for is unaffected.
    /// </remarks>
    public const int BoldWeight = 550;

    /// <inheritdoc />
    public int LineHeight(SchemeFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        return (int)Math.Ceiling(FaceFor(font).GetHeight()) + Padding(font) * 2;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The rasteriser has been disposed.</exception>
    public RasterisedGlyph Rasterise(SchemeFont font, char character)
    {
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Font face = FaceFor(font);
        int pad = Padding(font);

        string text = character.ToString(CultureInfo.InvariantCulture);

        int advance = Advance(face, text, _format);

        // The box is the advance plus room for the outline on each side. Generous rather than
        // tight: a glyph that overhangs its advance - which italics and many script faces do - would
        // otherwise be clipped, and a clipped glyph looks like a font bug rather than a box bug.
        int width = Math.Max(1, advance + (pad * 2));
        int height = Math.Max(1, (int)Math.Ceiling(face.GetHeight()) + (pad * 2));

        byte[] body = DrawBody(face, font, text, width, height, pad, _format);
        byte[] pixels = Compose(body, width, height, font.Outline);

        return new RasterisedGlyph
        {
            // **The bearings put the glyph back where the pen expects it.** The body is drawn `pad`
            // inside its box so the outline has room, so the box must be placed `pad` back.
            Metrics = new GlyphMetrics(width, height, -pad, -pad, advance),
            Pixels = pixels,
        };
    }

    /// <summary>How far the pen moves for a string, in whole pixels.</summary>
    /// <remarks>
    /// **`GenericTypographic` rather than the default format, and the difference is not subtle.**
    /// `StringFormat.GenericDefault` adds padding either side of a string for the benefit of
    /// selection highlighting, so measuring with it overstates every advance by a few pixels — which
    /// in a HUD reads as text that is mysteriously too wide and too loosely spaced.
    /// </remarks>
    private static int Advance(Font face, string text, StringFormat format)
    {
        using Bitmap scratch = new(1, 1, PixelFormat.Format32bppArgb);
        using Graphics measuring = Graphics.FromImage(scratch);

        SizeF size = measuring.MeasureString(text, face, PointF.Empty, format);

        return (int)Math.Ceiling(size.Width);
    }

    /// <summary>Draws the glyph white on transparent and returns its alpha.</summary>
    private static byte[] DrawBody(
        Font face, SchemeFont font, string text, int width, int height, int pad, StringFormat format)
    {
        using Bitmap surface = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics drawing = Graphics.FromImage(surface))
        {
            drawing.Clear(Color.Transparent);

            // **ClearType is unavailable here and that is not a limitation to work around.**
            // Subpixel rendering needs to know the colour behind the text, and there is nothing
            // behind a transparent bitmap - GDI+ silently falls back. Grey antialiasing is also
            // what a font texture wants, since the same texture is drawn over whatever the HUD
            // happens to be over.
            drawing.TextRenderingHint = font.Antialias
                ? TextRenderingHint.AntiAliasGridFit
                : TextRenderingHint.SingleBitPerPixelGridFit;

            using SolidBrush ink = new(Color.White);

            drawing.DrawString(text, face, ink, pad, pad, format);
        }

        BitmapData locked = surface.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte[] alpha = new byte[width * height];

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    // GDI+'s 32bppArgb is B, G, R, A in memory, so alpha is the fourth byte.
                    int at = (row * locked.Stride) + (column * 4) + 3;

                    alpha[(row * width) + column] = System.Runtime.InteropServices.Marshal.ReadByte(
                        locked.Scan0, at);
                }
            }

            return alpha;
        }
        finally
        {
            surface.UnlockBits(locked);
        }
    }

    /// <summary>Turns a body alpha into RGBA, adding a black outline when the scheme asks.</summary>
    /// <remarks>
    /// The outline is the body dilated by one pixel in all eight directions, which is what a
    /// one-pixel border is. Where the body covers, the pixel is white; where only the dilation
    /// covers, it is black; and the alpha is whichever is greater, so the outline's own edge is as
    /// smooth as the glyph's.
    /// </remarks>
    private static byte[] Compose(byte[] body, int width, int height, bool outline)
    {
        byte[] pixels = new byte[width * height * GlyphAtlas.BytesPerPixel];

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int index = (row * width) + column;
                byte inside = body[index];
                byte around = outline ? Dilated(body, width, height, row, column) : (byte)0;

                // **The colour IS the body coverage, which is the whole trick and looks like a
                // mistake until it is spelled out.** Fully covered pixels come out white, pixels
                // only the dilation reaches come out black, and a half-covered antialiased edge
                // comes out mid-grey — which is exactly white composited over the black outline at
                // that coverage. So the edge blends into the outline instead of stepping onto it,
                // with no separate blend pass.
                //
                // Alpha is whichever mask reaches further, so the outline's own outer edge is as
                // smooth as the glyph's.
                int at = index * GlyphAtlas.BytesPerPixel;

                pixels[at] = inside;
                pixels[at + 1] = inside;
                pixels[at + 2] = inside;
                pixels[at + 3] = Math.Max(inside, around);
            }
        }

        return pixels;
    }

    /// <summary>The greatest body alpha in the eight pixels around one.</summary>
    private static byte Dilated(byte[] body, int width, int height, int row, int column)
    {
        byte most = 0;

        for (int down = -1; down <= 1; down++)
        {
            for (int across = -1; across <= 1; across++)
            {
                int y = row + down;
                int x = column + across;

                if (y < 0 || y >= height || x < 0 || x >= width)
                {
                    continue;
                }

                most = Math.Max(most, body[(y * width) + x]);
            }
        }

        return most;
    }

    /// <summary>Pixels of room the scheme's effects need around a glyph.</summary>
    private static int Padding(SchemeFont font) =>
        (font.Outline ? 1 : 0) + (font.DropShadow ? 1 : 0) + font.Blur;

    /// <summary>Builds the face for a scheme font, or returns the one already built.</summary>
    private Font FaceFor(SchemeFont font)
    {
        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"{font.Name}|{font.Tall}|{font.Weight}|{font.Italic}");

        if (_fonts.TryGetValue(key, out Font? existing))
        {
            return existing;
        }

        FontStyle style = FontStyle.Regular;

        if (font.Weight >= BoldWeight)
        {
            style |= FontStyle.Bold;
        }

        if (font.Italic)
        {
            style |= FontStyle.Italic;
        }

        // **Pixels, because `tall` is a pixel height.** The default unit is points, which would
        // scale the font by the display's DPI and make every HUD a different size on every machine
        // - the one thing a parity target cannot tolerate.
        //
        // A family the machine does not have falls back to the platform's default rather than
        // throwing, which is what GDI does too: a missing font is a worse-looking HUD, not a dead
        // viewer.
        Font face = new(font.Name, font.Tall, style, GraphicsUnit.Pixel);

        _fonts[key] = face;

        return face;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (Font face in _fonts.Values)
        {
            face.Dispose();
        }

        _fonts.Clear();
        _format.Dispose();
        _disposed = true;
    }
}
