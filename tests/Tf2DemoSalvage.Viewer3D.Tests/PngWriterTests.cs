using System;
using System.Drawing;
using System.IO;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The PNG encoder written to get <c>System.Drawing</c> out of the render layer.
/// </summary>
/// <remarks>
/// **Verified against an INDEPENDENT decoder rather than against a fixture.** Every byte this
/// encoder writes could be checked against a hand-built expected array, and that would prove only
/// that the array and the encoder agree — both written by the same person, from the same reading of
/// the spec, at the same time. `docs/memory/differential-beats-fixtures.md` is explicit that a
/// fixture cannot falsify your own reading of a specification.
///
/// So the real test decodes what we wrote with `System.Drawing`, which is Microsoft's implementation
/// of RFC 2083 and knows nothing about this code. If our CRC is computed over the wrong bytes, our
/// IDAT is raw deflate instead of zlib, or our scanlines are missing their filter byte, it rejects
/// the file or returns different pixels.
///
/// **This suite lives here rather than in a Render test project on purpose**: it needs
/// `System.Drawing`, which is Windows-only, and putting it beside the portable code would drag that
/// dependency back into the layer the encoder exists to free.
/// </remarks>
public sealed class PngWriterTests
{
    [Test]
    public void Write_APngWeEncoded_IsDecodedIdenticallyByAnIndependentDecoder()
    {
        // **Every pixel differs from every other**, which is what makes a stride, row-order or
        // channel-order mistake visible. A solid-colour image would survive all three.
        const int Width = 5;
        const int Height = 3;

        byte[] rgba = new byte[Width * Height * 4];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = ((y * Width) + x) * 4;

                rgba[at] = (byte)(20 + (x * 40));       // red varies across
                rgba[at + 1] = (byte)(70 + (y * 60));   // green varies down
                rgba[at + 2] = (byte)(200 - (x * 10) - (y * 30));
                rgba[at + 3] = 255;
            }
        }

        string path = Path.Combine(Path.GetTempPath(), $"pngwriter-{Guid.NewGuid():N}.png");

        try
        {
            PngWriter.Write(path, Width, Height, rgba);

            using Bitmap decoded = new(path);

            decoded.Width.ShouldBe(Width);
            decoded.Height.ShouldBe(Height);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int at = ((y * Width) + x) * 4;
                    Color pixel = decoded.GetPixel(x, y);

                    pixel.R.ShouldBe(rgba[at], $"red at {x},{y}");
                    pixel.G.ShouldBe(rgba[at + 1], $"green at {x},{y}");
                    pixel.B.ShouldBe(rgba[at + 2], $"blue at {x},{y}");
                    pixel.A.ShouldBe(rgba[at + 3], $"alpha at {x},{y}");
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Write_Transparency_SurvivesTheRoundTrip()
    {
        // Colour type 6 carries alpha, and a decoder reading our header as type 2 would drop it
        // while still producing a plausible image.
        string path = Path.Combine(Path.GetTempPath(), $"pngwriter-{Guid.NewGuid():N}.png");

        try
        {
            PngWriter.Write(path, 2, 1, [255, 0, 0, 0, 0, 255, 0, 128]);

            using Bitmap decoded = new(path);

            decoded.GetPixel(0, 0).A.ShouldBe((byte)0, "fully transparent");
            decoded.GetPixel(1, 0).A.ShouldBe((byte)128, "half transparent");
            decoded.GetPixel(1, 0).G.ShouldBe((byte)255);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Write_TheFirstEightBytes_AreTheSignatureRfc2083Specifies()
    {
        // RFC 2083 section 3.1. The high bit catches a transfer that stripped it; the CR LF and the
        // lone LF catch one that converted line endings.
        string path = Path.Combine(Path.GetTempPath(), $"pngwriter-{Guid.NewGuid():N}.png");

        try
        {
            PngWriter.Write(path, 1, 1, [1, 2, 3, 4]);

            byte[] written = File.ReadAllBytes(path);

            written[..8].ShouldBe([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            // IHDR must be the first chunk, and its length is always 13.
            written[8..12].ShouldBe([0, 0, 0, 13]);
            written[12..16].ShouldBe("IHDR"u8.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Write_AOnePixelImage_IsValid()
    {
        // The smallest input, where an off-by-one in the scanline loop has nowhere to hide.
        string path = Path.Combine(Path.GetTempPath(), $"pngwriter-{Guid.NewGuid():N}.png");

        try
        {
            PngWriter.Write(path, 1, 1, [11, 22, 33, 255]);

            using Bitmap decoded = new(path);

            decoded.Width.ShouldBe(1);
            decoded.Height.ShouldBe(1);
            decoded.GetPixel(0, 0).R.ShouldBe((byte)11);
            decoded.GetPixel(0, 0).B.ShouldBe((byte)33);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Write_TheIdatPayload_IsAZlibStreamAndNotRawDeflate()
    {
        // **This test exists because the round-trip above could not catch the bug it was written
        // for.** Swapping ZLibStream for DeflateStream — writing raw deflate with no zlib header or
        // Adler-32 trailer, which PNG forbids — was sabotaged deliberately and ALL EIGHT round-trip
        // tests still passed. System.Drawing's decoder accepts it.
        //
        // So the independent decoder is more lenient than the specification, which makes it the
        // wrong instrument for this particular claim: `docs/memory/a-faithful-fixture-can-be-blind.md`
        // is the same shape, and the remedy is the same — measure the thing directly rather than
        // strengthening an assertion that was never sensitive.
        //
        // RFC 1950 §2.2: the first byte's low nibble is the compression method, 8 for deflate, and
        // the first two bytes read as a big-endian number must be a multiple of 31.
        string path = Path.Combine(Path.GetTempPath(), $"pngwriter-{Guid.NewGuid():N}.png");

        try
        {
            PngWriter.Write(path, 4, 4, new byte[64]);

            byte[] written = File.ReadAllBytes(path);

            // Walk to IDAT rather than assuming an offset: IHDR is fixed at 13 bytes today, but a
            // test that hardcodes the position would silently move off the payload if a chunk were
            // ever added ahead of it.
            int at = 8;
            int idat = -1;

            while (at + 8 <= written.Length)
            {
                int length = (written[at] << 24) | (written[at + 1] << 16)
                           | (written[at + 2] << 8) | written[at + 3];

                string type = System.Text.Encoding.ASCII.GetString(written, at + 4, 4);

                if (type == "IDAT")
                {
                    idat = at + 8;
                    break;
                }

                at += 12 + length;
            }

            idat.ShouldBeGreaterThan(0, "no IDAT chunk was found at all");

            byte cmf = written[idat];
            byte flg = written[idat + 1];

            (cmf & 0x0F).ShouldBe(8, "compression method must be deflate");
            (((cmf << 8) | flg) % 31).ShouldBe(0, "the zlib header check bits must be valid");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Write_ABufferOfTheWrongSize_IsRefusedRatherThanWritten()
    {
        // A buffer one row short writes a file that opens and shows garbage in its last rows, which
        // is worse than refusing: it looks like a rendering bug.
        Should.Throw<ArgumentException>(
            () => PngWriter.Write(Path.Combine(Path.GetTempPath(), "never.png"), 4, 4, new byte[60]));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 4)]
    public void Write_ANonPositiveDimension_IsRefused(int width, int height)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => PngWriter.Write(
                Path.Combine(Path.GetTempPath(), "never.png"), width, height, new byte[16]));
    }
}
