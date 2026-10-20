using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Two folders of same-named captures of ours, pixel against pixel (finding 84).</summary>
/// <remarks>
/// A measurement (D38): per pair the pixels that differ, their bounding box and the largest channel delta, largest
/// first. <c>--sbs a.png b.png out.png</c> writes before | after | the difference amplified eight times.
/// </remarks>
public sealed class PairDiffProbe : IProbe
{
    private const int Amplify = 8;

    /// <inheritdoc/>
    public string Name => "pair-diff";

    /// <inheritdoc/>
    public string Summary => "two folders of our own captures, pixel against pixel: pair-diff <a-folder> <b-folder> | pair-diff --sbs <a.png> <b.png> <out.png>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 4 && arguments[0] == "--sbs")
        {
            SideBySide(arguments[1], arguments[2], arguments[3]);
            output.WriteLine($"wrote {arguments[3]}");
            return;
        }

        if (arguments.Count < 2)
        {
            output.WriteLine(Summary);
            return;
        }

        List<(string Name, int Pixels, string Box, int Max)> rows = [];

        foreach (string a in Directory.EnumerateFiles(arguments[0], "*.png").Order(StringComparer.Ordinal))
        {
            string b = Path.Combine(arguments[1], Path.GetFileName(a));

            if (!File.Exists(b))
            {
                rows.Add((Path.GetFileName(a), -1, "missing in b", 0));
                continue;
            }

            (int pixels, string box, int max) = Compare(GoldenCompareProbe.Image.Read(a), GoldenCompareProbe.Image.Read(b));
            rows.Add((Path.GetFileName(a), pixels, box, max));
        }

        foreach ((string name, int pixels, string box, int max) in rows.OrderByDescending(one => one.Pixels))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-16} {pixels,8} px  max {max,3}  {box}"));
        }
    }

    private static (int Pixels, string Box, int Max) Compare(GoldenCompareProbe.Image a, GoldenCompareProbe.Image b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return (-2, $"size {a.Width}x{a.Height} against {b.Width}x{b.Height}", 0);
        }

        int pixels = 0;
        int max = 0;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;

        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                int at = ((y * a.Width) + x) * 4;
                int delta = Math.Max(
                    Math.Abs(a.Rgba[at] - b.Rgba[at]),
                    Math.Max(Math.Abs(a.Rgba[at + 1] - b.Rgba[at + 1]), Math.Abs(a.Rgba[at + 2] - b.Rgba[at + 2])));

                if (delta == 0)
                {
                    continue;
                }

                pixels++;
                max = Math.Max(max, delta);
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }
        }

        return (pixels, pixels == 0 ? "identical" : $"box ({x0},{y0})-({x1},{y1})", max);
    }

    private static void SideBySide(string aPath, string bPath, string outPath)
    {
        GoldenCompareProbe.Image a = GoldenCompareProbe.Image.Read(aPath);
        GoldenCompareProbe.Image b = GoldenCompareProbe.Image.Read(bPath);
        int width = a.Width * 3;
        byte[] rgba = new byte[width * a.Height * 4];

        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                int from = ((y * a.Width) + x) * 4;
                int row = y * width;

                for (int channel = 0; channel < 4; channel++)
                {
                    rgba[((row + x) * 4) + channel] = a.Rgba[from + channel];
                    rgba[((row + a.Width + x) * 4) + channel] = b.Rgba[from + channel];

                    int diff = channel == 3 ? 255 : Math.Min(255, Math.Abs(a.Rgba[from + channel] - b.Rgba[from + channel]) * Amplify);
                    rgba[((row + (2 * a.Width) + x) * 4) + channel] = (byte)diff;
                }
            }
        }

        PngWriter.Write(outPath, width, a.Height, rgba);
    }
}
