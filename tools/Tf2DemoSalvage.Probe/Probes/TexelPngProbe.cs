using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Texels and pixels to a PNG, nearest-neighbour: a shipped VTF's window, or a crop of a capture.</summary>
/// <remarks>
/// **The VTF is decoded by `VtfTexture.Decode`, the same decoder the viewer's CPU path uses**, at its full mip 0, and
/// the alpha is written out as a second image beside it (grey), since a PNG viewer composites alpha over white and
/// hides what the decal's alpha actually is. A capture crop is copied pixel for pixel and scaled by repetition only.
/// </remarks>
public sealed class TexelPngProbe : IProbe
{
    /// <summary>The largest output this writes, in bytes of RGBA: a guard on the window times the scale.</summary>
    private const long MaximumOutputBytes = 1L << 30;

    /// <inheritdoc/>
    public string Name => "texel-png";

    /// <inheritdoc/>
    public string Summary =>
        "a VTF window or a capture crop to PNG, nearest-neighbour: texel-png vtf <path.vtf> <out.png> [x y w h] [scale] | texel-png crop <in.png> <out.png> <x y w h> [scale]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        bool crop = arguments.Count > 0 && arguments[0] == "crop";

        if (arguments.Count < 3 || (arguments[0] != "vtf" && !crop) || (crop && arguments.Count < 7))
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        if (Read(output, crop, arguments[1]) is not { } image)
        {
            return;
        }

        (int width, int height, byte[] rgba) = image;

        if (!TryInt(arguments, 3, 0, out int x) || !TryInt(arguments, 4, 0, out int y) ||
            !TryInt(arguments, 5, width - x, out int w) || !TryInt(arguments, 6, height - y, out int h) ||
            !TryInt(arguments, 7, 1, out int scale))
        {
            output.WriteLine("x, y, w, h and scale are whole numbers. Usage: " + Summary);
            return;
        }

        if (x < 0 || y < 0 || w <= 0 || h <= 0 || (long)x + w > width || (long)y + h > height || scale <= 0)
        {
            output.WriteLine($"window {x},{y} {w}x{h} at {scale}x does not fit {width}x{height}");
            return;
        }

        long bytes = (long)w * scale * h * scale * 4;

        if (bytes > MaximumOutputBytes)
        {
            output.WriteLine($"window {w}x{h} at {scale}x is {bytes} bytes, over the {MaximumOutputBytes}-byte limit");
            return;
        }

        int outWidth = w * scale;
        int outHeight = h * scale;
        byte[] colour = new byte[bytes];
        byte[] alpha = new byte[bytes];
        long sum = 0;
        int opaque = 0;
        long[] clearRgb = new long[3];
        int clear = 0;
        int clearMin = 255;
        int clearMax = 0;

        // One pass over the output; each source texel is counted once, at its first output pixel.
        for (int row = 0; row < outHeight; row++)
        {
            for (int column = 0; column < outWidth; column++)
            {
                int from = (((y + (row / scale)) * width) + x + (column / scale)) * 4;
                int to = ((row * outWidth) + column) * 4;
                byte a = rgba[from + 3];

                colour[to] = rgba[from];
                colour[to + 1] = rgba[from + 1];
                colour[to + 2] = rgba[from + 2];
                colour[to + 3] = 255;
                alpha[to] = alpha[to + 1] = alpha[to + 2] = a;
                alpha[to + 3] = 255;

                if (row % scale != 0 || column % scale != 0)
                {
                    continue;
                }

                sum += a;
                opaque += a > 0 ? 1 : 0;

                // The texels an alpha test of GREATER 0 would discard: what a blend without the test draws there.
                if (a == 0)
                {
                    clear++;

                    for (int channel = 0; channel < 3; channel++)
                    {
                        clearRgb[channel] += rgba[from + channel];
                        clearMin = Math.Min(clearMin, rgba[from + channel]);
                        clearMax = Math.Max(clearMax, rgba[from + channel]);
                    }
                }
            }
        }

        PngWriter.Write(arguments[2], outWidth, outHeight, colour);

        string alphaPath = Path.ChangeExtension(arguments[2], ".alpha.png");

        PngWriter.Write(alphaPath, outWidth, outHeight, alpha);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"wrote {arguments[2]} and {alphaPath}: {w}x{h} at {scale}x; alpha mean {(double)sum / ((long)w * h):0.0}, {opaque} of {(long)w * h} above zero"));

        if (clear > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"alpha-zero texels: {clear}, mean RGB {(double)clearRgb[0] / clear:0.0} {(double)clearRgb[1] / clear:0.0} {(double)clearRgb[2] / clear:0.0}, channel range {clearMin}-{clearMax}"));
        }
    }

    /// <summary>The source image: a VTF out of the game at mip 0, or a PNG capture; null, with the reason written, when unread.</summary>
    private static (int Width, int Height, byte[] Rgba)? Read(TextWriter output, bool crop, string path)
    {
        if (crop)
        {
            if (!File.Exists(path))
            {
                output.WriteLine($"'{path}' does not exist.");
                return null;
            }

            GoldenCompareProbe.Image image = GoldenCompareProbe.Image.Read(path);

            return (image.Width, image.Height, image.Rgba);
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game ||
            GameArchives.Open(game).Read(path) is not { Length: > 0 } file)
        {
            output.WriteLine($"'{path}' was not read.");
            return null;
        }

        VtfTexture vtf = VtfTexture.Decode(file);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{path}: decoded {vtf.Width}x{vtf.Height}, mapping {vtf.MappingWidth}x{vtf.MappingHeight}"));

        return (vtf.Width, vtf.Height, vtf.Pixels);
    }

    private static bool TryInt(IReadOnlyList<string> arguments, int index, int otherwise, out int value)
    {
        if (arguments.Count <= index)
        {
            value = otherwise;
            return true;
        }

        return int.TryParse(arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
