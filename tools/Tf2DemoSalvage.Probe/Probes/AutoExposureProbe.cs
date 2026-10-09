using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The exposure a demo's tone-map controllers allow, and the scale TF2's auto-exposure would settle at on a picture (B514).
/// </summary>
/// <remarks>
/// <code>
///   autoexposure &lt;demo&gt; [tick]                   every controller change, and the range in force at the tick
///   autoexposure --png &lt;shot.png&gt; [min max]       the settled scale for a frame drawn at scale 1
/// </code>
/// The settled scale replays <see cref="AutoExposure"/> — the production class — on the SAME frame for 2,000 updates,
/// re-scaling it each time as the shaders would (clamped per channel, as an 8-bit target is). It reads the frame in
/// linear light and, as the control, in gamma: the golden comparison's measured ratio decides between them.
/// </remarks>
public sealed class AutoExposureProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "autoexposure";

    /// <inheritdoc/>
    public string Summary =>
        "tone-map controller changes, or the auto-exposure a scale-1 frame settles at (B514): autoexposure <demo> [tick] | autoexposure --png <shot.png> [min max]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        if (arguments[0] == "--bloom" && arguments.Count > 3)
        {
            GoldenCompareProbe.Image frame = GoldenCompareProbe.Image.Read(arguments[1]);
            float amount = float.Parse(arguments[2], CultureInfo.InvariantCulture);
            PngWriter.Write(arguments[3], frame.Width, frame.Height, Bloomed(frame, amount));
            output.WriteLine(Invariant($"wrote {arguments[3]}: the frame plus Valve's 8-bit bloom at {amount}"));
            return;
        }

        if (arguments[0] == "--png")
        {
            float min = arguments.Count > 3 ? float.Parse(arguments[2], CultureInfo.InvariantCulture) : AutoExposure.DefaultMin;
            float max = arguments.Count > 3 ? float.Parse(arguments[3], CultureInfo.InvariantCulture) : AutoExposure.DefaultMax;
            GoldenCompareProbe.Image image = GoldenCompareProbe.Image.Read(arguments[1]);

            // The frame times k before the scale: what TF2 would settle at if its unscaled light were k times ours, and
            // the brightness it would then show relative to ours, k times the scale.
            foreach (float k in new[] { 1f, 1.5f, 2f, 2.5f, 3f, 4f })
            {
                foreach (bool linear in new[] { true, false })
                {
                    float scale = Settle(image, (min, max), linear, k);
                    output.WriteLine(Invariant(
                        $"k {k,3:0.0} {(linear ? "linear" : "gamma ")} read: settles at {scale:0.000} in [{min}, {max}], shown {k * scale:0.000} x ours"));
                }
            }

            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        foreach ((int tick, SceneTonemap tonemap) in timeline.Tonemap.Samples)
        {
            output.WriteLine(Invariant($"tick {tick,7}: {tonemap}"));
        }

        if (arguments.Count > 1)
        {
            int at = int.Parse(arguments[1], CultureInfo.InvariantCulture);
            SceneTonemap held = timeline.Tonemap.At(at);
            output.WriteLine(Invariant($"at {at}: range {AutoExposure.Range(held.UseMin, held.Min, held.UseMax, held.Max)}"));
        }
    }

    /// <summary>
    /// <c>Generate8BitBloomTexture</c> on the CPU, then added in gamma: a PREDICTION to hold against the stored TF2 capture,
    /// not the renderer's pass. Each downsample tap is a 2x2 box, which is what a bilinear fetch half-way between texel
    /// centres reads.
    /// </summary>
    private static byte[] Bloomed(GoldenCompareProbe.Image frame, float amount)
    {
        int w = frame.Width / 4;
        int h = frame.Height / 4;
        float[] down = new float[w * h * 3];

        for (int qy = 0; qy < h; qy++)
        {
            for (int qx = 0; qx < w; qx++)
            {
                for (int tap = 0; tap < 4; tap++)
                {
                    int x0 = (qx * 4) + ((tap & 1) * 2);
                    int y0 = (qy * 4) + ((tap >> 1) * 2);
                    float[] p = new float[3];

                    for (int c = 0; c < 3; c++)
                    {
                        p[c] = (Texel(frame, x0, y0, c) + Texel(frame, x0 + 1, y0, c) + Texel(frame, x0, y0 + 1, c) + Texel(frame, x0 + 1, y0 + 1, c)) / 4f;
                    }

                    float lum = (0.3f * p[0]) + (0.59f * p[1]) + (0.11f * p[2]);

                    for (int c = 0; c < 3; c++)
                    {
                        down[(((qy * w) + qx) * 3) + c] += MathF.Pow(p[c], 2.2f) * lum * 0.25f;
                    }
                }
            }
        }

        float[] across = Blur(down, w, h, 1f, 0f, 1f);
        float[] bloom = Blur(across, w, h, 0f, h / (float)w, amount);
        byte[] rgba = new byte[frame.Width * frame.Height * 4];

        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                int i = ((y * frame.Width) + x) * 4;

                for (int c = 0; c < 3; c++)
                {
                    float added = Bilinear(bloom, w, h, ((x + 0.5f) / 4f) - 0.5f, ((y + 0.5f) / 4f) - 0.5f, c);
                    rgba[i + c] = (byte)Math.Clamp(MathF.Round((frame.Rgba[i + c] / 255f + added) * 255f), 0f, 255f);
                }

                rgba[i + 3] = 255;
            }
        }

        return rgba;
    }

    private static readonly (float Offset, float Weight)[] BlurTaps =
    [
        (1.3366f, 0.2185f), (3.4295f, 0.0821f), (5.4264f, 0.0461f), (7.4359f, 0.0262f), (9.4436f, 0.0162f), (11.4401f, 0.0102f),
    ];

    /// <summary><c>BlurFilter_ps2x.fxc</c>'s thirteen taps along (dx, dy) texels, times the scale.</summary>
    private static float[] Blur(float[] source, int w, int h, float dx, float dy, float scale)
    {
        float[] result = new float[source.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float sum = 0.2013f * source[(((y * w) + x) * 3) + c];

                    foreach ((float offset, float weight) in BlurTaps)
                    {
                        sum += weight * (Bilinear(source, w, h, x + (dx * offset), y + (dy * offset), c) +
                                         Bilinear(source, w, h, x - (dx * offset), y - (dy * offset), c));
                    }

                    result[(((y * w) + x) * 3) + c] = sum * scale;
                }
            }
        }

        return result;
    }

    private static float Bilinear(float[] source, int w, int h, float x, float y, int c)
    {
        x = Math.Clamp(x, 0f, w - 1);
        y = Math.Clamp(y, 0f, h - 1);
        int x0 = (int)x;
        int y0 = (int)y;
        int x1 = Math.Min(x0 + 1, w - 1);
        int y1 = Math.Min(y0 + 1, h - 1);
        float fx = x - x0;
        float fy = y - y0;
        float top = (source[(((y0 * w) + x0) * 3) + c] * (1 - fx)) + (source[(((y0 * w) + x1) * 3) + c] * fx);
        float bottom = (source[(((y1 * w) + x0) * 3) + c] * (1 - fx)) + (source[(((y1 * w) + x1) * 3) + c] * fx);

        return (top * (1 - fy)) + (bottom * fy);
    }

    private static float Texel(GoldenCompareProbe.Image frame, int x, int y, int c) =>
        frame.Rgba[(((Math.Min(y, frame.Height - 1) * frame.Width) + Math.Min(x, frame.Width - 1)) * 4) + c] / 255f;

    private static float Settle(GoldenCompareProbe.Image image, (float Min, float Max) range, bool linear, float k)
    {
        AutoExposure exposure = new();
        int[] counts = new int[AutoExposure.Bins];

        for (int frame = 0; frame < 2000; frame++)
        {
            Array.Clear(counts);
            Histogram(image, exposure.Current * k, linear, counts);
            exposure.Update(counts, range, 1f / 60f);
        }

        return exposure.Current;
    }

    /// <summary>The centre region's histogram of the frame re-drawn at <paramref name="scale"/>.</summary>
    private static void Histogram(GoldenCompareProbe.Image image, float scale, bool linear, Span<int> counts)
    {
        int skipX = (int)(image.Width * 0.5f * (1f - AutoExposure.CentreRegionX));
        int skipY = (int)(image.Height * 0.5f * (1f - AutoExposure.CentreRegionY));

        // ponytail: every 4th pixel each way; the histogram is a ratio of counts, so subsampling keeps it.
        for (int y = skipY; y < image.Height - skipY; y += 4)
        {
            for (int x = skipX; x < image.Width - skipX; x += 4)
            {
                int i = ((y * image.Width) + x) * 4;
                float r = Channel(image.Rgba[i], scale, linear);
                float g = Channel(image.Rgba[i + 1], scale, linear);
                float b = Channel(image.Rgba[i + 2], scale, linear);

                AutoExposure.Count(counts, (0.2125f * r) + (0.7154f * g) + (0.0721f * b));
            }
        }
    }

    private static float Channel(byte value, float scale, bool linear)
    {
        float c = value / 255f;
        float light = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        float drawn = MathF.Min(1f, light * scale);

        if (linear)
        {
            return drawn;
        }

        return drawn <= 0.0031308f ? drawn * 12.92f : (1.055f * MathF.Pow(drawn, 1f / 2.4f)) - 0.055f;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
