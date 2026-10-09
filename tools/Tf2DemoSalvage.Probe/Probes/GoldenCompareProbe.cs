using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Compares a real-TF2 capture with the viewer's capture of the same demo, tick and camera (B161).
/// </summary>
/// <remarks>
/// **A measurement, not a test** (D38): it prints numbers and writes pictures, and fails nothing.
/// The capture recipe, the pinned cvars and what the numbers include are in
/// <c>docs/findings/76-the-golden-comparison.md</c>.
///
/// **Regions come from the REFERENCE game, never from us.** Each optional mask is a second TF2
/// capture at the same moment with one layer switched off — <c>cl_drawhud 0</c>, then
/// <c>r_drawviewmodel 0</c>, then <c>r_drawskybox 0</c> — and the pixels that changed are that
/// layer. A mask drawn from our own render would hide exactly the divergence it should show.
/// Precedence when layers overlap: HUD, viewmodel, sky, then everything else is world.
/// </remarks>
public sealed class GoldenCompareProbe : IProbe
{
    /// <summary>Summed |ΔR|+|ΔG|+|ΔB| above which a mask pixel is "this layer".</summary>
    private const int MaskThreshold = 30;

    /// <summary>Per-channel mean error above which a pixel counts as wrong in the "bad %" column.</summary>
    private const int BadPixelThreshold = 32;

    /// <summary>Edge of the tile searched for each region's worst crop.</summary>
    private const int Tile = 64;

    /// <summary>How much the diff image amplifies an error so a small one is visible.</summary>
    private const int DiffGain = 4;

    private static readonly string[] RegionNames = ["world", "viewmodel", "hud", "sky"];

    /// <inheritdoc/>
    public string Name => "golden-compare";

    /// <inheritdoc/>
    public string Summary =>
        "TF2 capture vs viewer capture, per region (B161): golden-compare <tf2.png> <ours.png> <outdir> [--hud <tf2-nohud.png>] [--viewmodel <tf2-nohud-novm.png>] [--sky <tf2-nohud-novm-nosky.png>]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3)
        {
            output.WriteLine(Summary);
            return;
        }

        Image reference = Image.Read(arguments[0]);
        Image ours = Image.Read(arguments[1]);
        string folder = arguments[2];

        if (reference.Width != ours.Width || reference.Height != ours.Height)
        {
            output.WriteLine($"size differs: tf2 {reference.Width}x{reference.Height}, ours {ours.Width}x{ours.Height} - fix the capture, not the comparison");
            return;
        }

        Dictionary<string, string> masks = Options(arguments);
        byte[] region = Regions(reference, masks);

        Directory.CreateDirectory(folder);
        Report(output, reference, ours, region, folder);
    }

    /// <summary>The <c>--name path</c> pairs after the three positional arguments.</summary>
    private static Dictionary<string, string> Options(IReadOnlyList<string> arguments)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);

        for (int i = 3; i + 1 < arguments.Count; i += 2)
        {
            options[arguments[i].TrimStart('-')] = arguments[i + 1];
        }

        return options;
    }

    /// <summary>Region index per pixel: 0 world, 1 viewmodel, 2 HUD, 3 sky.</summary>
    /// <remarks>
    /// Each mask is differenced against the capture one layer up, so the chain is
    /// full → no HUD → no viewmodel → no sky, and a layer is what disappeared at its own step.
    /// </remarks>
    private static byte[] Regions(Image reference, Dictionary<string, string> masks)
    {
        byte[] region = new byte[reference.Width * reference.Height];
        Image above = reference;

        // Applied in reverse precedence so a later (stronger) layer overwrites an earlier one.
        List<(byte Index, Image Upper, Image Lower)> steps = [];

        foreach ((string key, byte index) in new[] { ("hud", (byte)2), ("viewmodel", (byte)1), ("sky", (byte)3) })
        {
            if (masks.TryGetValue(key, out string? path))
            {
                Image lower = Image.Read(path);
                steps.Add((index, above, lower));
                above = lower;
            }
        }

        for (int s = steps.Count - 1; s >= 0; s--)
        {
            (byte index, Image upper, Image lower) = steps[s];

            for (int p = 0; p < region.Length; p++)
            {
                if (upper.Distance(lower, p) > MaskThreshold)
                {
                    region[p] = index;
                }
            }
        }

        return region;
    }

    /// <summary>Prints the per-region table and writes the diff and the worst crops.</summary>
    private static void Report(TextWriter output, Image reference, Image ours, byte[] region, string folder)
    {
        int count = RegionNames.Length;
        long[] pixels = new long[count];
        double[] error = new double[count];
        long[] bad = new long[count];
        double[] worstPixel = new double[count];

        // Each side's mean colour per region: a brightness or tint shift reads here as two numbers somebody
        // can recognise, where the error column alone cannot tell "darker" from "misaligned".
        double[] referenceSum = new double[count * 3];
        double[] oursSum = new double[count * 3];
        byte[] diff = new byte[reference.Width * reference.Height * 4];
        double total = 0;

        for (int p = 0; p < region.Length; p++)
        {
            double e = ours.Distance(reference, p) / 3.0;
            int r = region[p];

            pixels[r]++;
            error[r] += e;
            total += e;
            bad[r] += e > BadPixelThreshold ? 1 : 0;
            worstPixel[r] = Math.Max(worstPixel[r], e);

            for (int c = 0; c < 3; c++)
            {
                referenceSum[(r * 3) + c] += reference.Rgba[(p * 4) + c];
                oursSum[(r * 3) + c] += ours.Rgba[(p * 4) + c];
            }

            for (int c = 0; c < 3; c++)
            {
                diff[(p * 4) + c] = (byte)Math.Min(255, Math.Abs(ours.Rgba[(p * 4) + c] - reference.Rgba[(p * 4) + c]) * DiffGain);
            }

            diff[(p * 4) + 3] = 255;
        }

        PngWriter.Write(Path.Combine(folder, "diff.png"), reference.Width, reference.Height, diff);

        output.WriteLine(Invariant($"summary: mean |error| {total / region.Length:0.000} /255 per channel over {region.Length} px"));
        output.WriteLine("region      px%     mean   bad%  max px   worst tile (x,y) mean   tf2 rgb      ours rgb");

        for (int r = 0; r < count; r++)
        {
            if (pixels[r] == 0)
            {
                output.WriteLine($"{RegionNames[r],-9}  absent");
                continue;
            }

            (int x, int y, double worst) = WorstTile(reference, ours, region, (byte)r);
            WriteCrop(Path.Combine(folder, $"crop-{RegionNames[r]}.png"), reference, ours, x, y);

            output.WriteLine(Invariant(
                $"{RegionNames[r],-9} {100.0 * pixels[r] / region.Length,5:0.0} {error[r] / pixels[r],8:0.000} {100.0 * bad[r] / pixels[r],6:0.0} {worstPixel[r],7:0.0}   ({x},{y}) {worst:0.00}   {Mean(referenceSum, r, pixels[r])}  {Mean(oursSum, r, pixels[r])}"));
        }
    }

    /// <summary>The <see cref="Tile"/>-pixel tile, on a half-tile grid, with the highest mean error over this region's pixels.</summary>
    /// <remarks>A tile counts only if at least a quarter of it is this region, so one stray mask pixel cannot be "the worst".</remarks>
    private static (int X, int Y, double Mean) WorstTile(Image reference, Image ours, byte[] region, byte index)
    {
        (int X, int Y, double Mean) best = (0, 0, -1);

        for (int y = 0; y + Tile <= reference.Height; y += Tile / 2)
        {
            for (int x = 0; x + Tile <= reference.Width; x += Tile / 2)
            {
                double sum = 0;
                int n = 0;

                for (int ty = y; ty < y + Tile; ty++)
                {
                    for (int tx = x; tx < x + Tile; tx++)
                    {
                        int p = (ty * reference.Width) + tx;

                        if (region[p] == index)
                        {
                            sum += ours.Distance(reference, p) / 3.0;
                            n++;
                        }
                    }
                }

                if (n >= Tile * Tile / 4 && sum / n > best.Mean)
                {
                    best = (x, y, sum / n);
                }
            }
        }

        return best;
    }

    /// <summary>Writes TF2 | ours | diff for one tile, each doubled in size so it can be read.</summary>
    private static void WriteCrop(string path, Image reference, Image ours, int x0, int y0)
    {
        const int Scale = 2;
        int edge = Tile * Scale;
        int width = edge * 3;
        byte[] rgba = new byte[width * edge * 4];

        for (int y = 0; y < edge; y++)
        {
            for (int x = 0; x < edge; x++)
            {
                int source = ((((y0 + (y / Scale)) * reference.Width) + x0 + (x / Scale)) * 4);

                for (int c = 0; c < 4; c++)
                {
                    byte a = reference.Rgba[source + c];
                    byte b = ours.Rgba[source + c];
                    byte d = c == 3 ? (byte)255 : (byte)Math.Min(255, Math.Abs(a - b) * DiffGain);

                    rgba[(((y * width) + x) * 4) + c] = c == 3 ? (byte)255 : a;
                    rgba[(((y * width) + edge + x) * 4) + c] = c == 3 ? (byte)255 : b;
                    rgba[(((y * width) + (2 * edge) + x) * 4) + c] = d;
                }
            }
        }

        PngWriter.Write(path, width, edge, rgba);
    }

    private static string Mean(double[] sums, int region, long pixels) =>
        Invariant($"{sums[region * 3] / pixels,3:0} {sums[(region * 3) + 1] / pixels,3:0} {sums[(region * 3) + 2] / pixels,3:0}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>An 8-bit RGBA picture read from a PNG.</summary>
    /// <remarks>
    /// **Only what the two producers write**: 8-bit truecolour with or without alpha, not interlaced
    /// — the viewer's <see cref="PngWriter"/> and the tf2 MCP's TGA conversion. Anything else is
    /// refused rather than half-decoded, because a picture read slightly wrong is a divergence that
    /// is not there. All five row filters are undone (RFC 2083 §6), since another encoder may use them.
    /// </remarks>
    private sealed class Image
    {
        public required int Width { get; init; }

        public required int Height { get; init; }

        public required byte[] Rgba { get; init; }

        /// <summary>Summed absolute RGB difference at one pixel.</summary>
        public int Distance(Image other, int pixel)
        {
            int i = pixel * 4;

            return Math.Abs(Rgba[i] - other.Rgba[i]) + Math.Abs(Rgba[i + 1] - other.Rgba[i + 1]) + Math.Abs(Rgba[i + 2] - other.Rgba[i + 2]);
        }

        public static Image Read(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            using MemoryStream idat = new();
            int width = 0;
            int height = 0;
            int channels = 0;

            for (int at = 8; at + 8 <= file.Length;)
            {
                int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
                string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
                ReadOnlySpan<byte> data = file.AsSpan(at + 8, length);

                if (type == "IHDR")
                {
                    width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data);
                    height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    channels = (data[8], data[9], data[12]) switch
                    {
                        (8, 2, 0) => 3,
                        (8, 6, 0) => 4,
                        _ => throw new InvalidDataException($"{path}: only 8-bit RGB/RGBA non-interlaced PNG is read"),
                    };
                }
                else if (type == "IDAT")
                {
                    idat.Write(data);
                }

                at += 12 + length;
            }

            idat.Position = 0;
            using System.IO.Compression.ZLibStream zlib = new(idat, System.IO.Compression.CompressionMode.Decompress);
            byte[] raw = new byte[height * ((width * channels) + 1)];
            zlib.ReadExactly(raw);

            return new Image { Width = width, Height = height, Rgba = Unfilter(raw, width, height, channels) };
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, int channels)
        {
            int stride = width * channels;
            byte[] current = new byte[stride];
            byte[] previous = new byte[stride];
            byte[] rgba = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                ReadOnlySpan<byte> line = raw.AsSpan((y * (stride + 1)) + 1, stride);

                for (int i = 0; i < stride; i++)
                {
                    int left = i >= channels ? current[i - channels] : 0;
                    int up = previous[i];
                    int corner = i >= channels ? previous[i - channels] : 0;

                    current[i] = (byte)(line[i] + Predict(filter, left, up, corner));
                }

                for (int x = 0; x < width; x++)
                {
                    int o = ((y * width) + x) * 4;

                    rgba[o] = current[x * channels];
                    rgba[o + 1] = current[(x * channels) + 1];
                    rgba[o + 2] = current[(x * channels) + 2];
                    rgba[o + 3] = channels == 4 ? current[(x * channels) + 3] : (byte)255;
                }

                (previous, current) = (current, previous);
            }

            return rgba;
        }

        private static int Predict(int filter, int left, int up, int corner) => filter switch
        {
            0 => 0,
            1 => left,
            2 => up,
            3 => (left + up) / 2,
            4 => Paeth(left, up, corner),
            _ => throw new InvalidDataException($"PNG row filter {filter} does not exist"),
        };

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc)
            {
                return a;
            }

            return pb <= pc ? b : c;
        }
    }
}
