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

        if (arguments.Count < 3)
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        int width;
        int height;
        byte[] rgba;

        if (arguments[0] == "vtf")
        {
            if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game ||
                GameArchives.Open(game).Read(arguments[1]) is not { Length: > 0 } file)
            {
                output.WriteLine($"'{arguments[1]}' was not read.");
                return;
            }

            VtfTexture vtf = VtfTexture.Decode(file);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{arguments[1]}: decoded {vtf.Width}x{vtf.Height}, mapping {vtf.MappingWidth}x{vtf.MappingHeight}"));

            (width, height, rgba) = (vtf.Width, vtf.Height, vtf.Pixels);
        }
        else if (arguments[0] == "crop")
        {
            GoldenCompareProbe.Image image = GoldenCompareProbe.Image.Read(arguments[1]);

            (width, height, rgba) = (image.Width, image.Height, image.Rgba);
        }
        else
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        int x = Int(arguments, 3, 0);
        int y = Int(arguments, 4, 0);
        int w = Int(arguments, 5, width - x);
        int h = Int(arguments, 6, height - y);
        int scale = Int(arguments, 7, 1);

        if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > width || y + h > height || scale <= 0)
        {
            output.WriteLine($"window {x},{y} {w}x{h} does not fit {width}x{height}");
            return;
        }

        byte[] colour = new byte[w * scale * h * scale * 4];
        byte[] alpha = new byte[colour.Length];
        long sum = 0;
        int opaque = 0;
        long[] clearRgb = new long[3];
        int clear = 0;
        int clearMin = 255;
        int clearMax = 0;

        for (int row = 0; row < h * scale; row++)
        {
            for (int column = 0; column < w * scale; column++)
            {
                int from = ((((y + (row / scale)) * width) + x + (column / scale)) * 4);
                int to = ((row * w * scale) + column) * 4;

                colour[to] = rgba[from];
                colour[to + 1] = rgba[from + 1];
                colour[to + 2] = rgba[from + 2];
                colour[to + 3] = 255;
                alpha[to] = alpha[to + 1] = alpha[to + 2] = rgba[from + 3];
                alpha[to + 3] = 255;
            }
        }

        for (int row = 0; row < h; row++)
        {
            for (int column = 0; column < w; column++)
            {
                int at = (((y + row) * width) + x + column) * 4;
                byte a = rgba[at + 3];

                sum += a;
                opaque += a > 0 ? 1 : 0;

                // The texels an alpha test of GREATER 0 would discard: what a blend without the test draws there.
                if (a == 0)
                {
                    clear++;

                    for (int channel = 0; channel < 3; channel++)
                    {
                        clearRgb[channel] += rgba[at + channel];
                        clearMin = Math.Min(clearMin, rgba[at + channel]);
                        clearMax = Math.Max(clearMax, rgba[at + channel]);
                    }
                }
            }
        }

        PngWriter.Write(arguments[2], w * scale, h * scale, colour);

        string alphaPath = Path.ChangeExtension(arguments[2], ".alpha.png");

        PngWriter.Write(alphaPath, w * scale, h * scale, alpha);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"wrote {arguments[2]} and {alphaPath}: {w}x{h} at {scale}x; alpha mean {(double)sum / (w * h):0.0}, {opaque} of {w * h} above zero"));

        if (clear > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"alpha-zero texels: {clear}, mean RGB {(double)clearRgb[0] / clear:0.0} {(double)clearRgb[1] / clear:0.0} {(double)clearRgb[2] / clear:0.0}, channel range {clearMin}-{clearMax}"));
        }
    }

    private static int Int(IReadOnlyList<string> arguments, int index, int otherwise) =>
        arguments.Count > index ? int.Parse(arguments[index], CultureInfo.InvariantCulture) : otherwise;
}
