using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>
/// The measures a golden-image test compares by (D219) — never pixel equality.
/// </summary>
/// <remarks>
/// **Every measure is resolution-free**: positions and radii are fractions of the image WIDTH, counts are
/// fractions of the pixel total, so a viewer frame and a TF2 screenshot of different sizes but the same
/// aspect and field of view compare directly. Luma is Rec. 601 on 0..255, which is what the eye reads as
/// brightness; <see cref="RegionChannelMeans"/> carries the hue that luma alone cannot see.
/// </remarks>
internal sealed class GoldenMeasure
{
    private readonly int[] _pixels;

    private readonly float[] _luma;

    private GoldenMeasure(int[] pixels, int width, int height)
    {
        _pixels = pixels;
        _luma = new float[pixels.Length];
        Width = width;
        Height = height;

        for (int i = 0; i < pixels.Length; i++)
        {
            _luma[i] = (0.299f * Channel(pixels[i], 0)) + (0.587f * Channel(pixels[i], 1)) + (0.114f * Channel(pixels[i], 2));
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The width every image is resampled to before it is measured — the committed goldens' width.</summary>
    /// <remarks>
    /// **Both sides go through the same resample**, so a full-size viewer frame and a half-size golden are averaged
    /// alike; a measure taken at two resolutions would differ by the resampling alone around a small bright disc.
    /// </remarks>
    public const int MeasuredWidth = 640;

    /// <summary>Reads an image file, resampled to <see cref="MeasuredWidth"/> unless it is that wide already (a golden).</summary>
    public static GoldenMeasure Load(string path)
    {
        using Bitmap source = new(path);

        if (source.Width == MeasuredWidth)
        {
            return Of(source);
        }

        using Bitmap rgb = Resampled(source);

        return Of(rgb);
    }

    /// <summary>Measures a bitmap as it is, without resampling — how a synthetic image with a known answer is read.</summary>
    public static GoldenMeasure Of(Bitmap rgb)
    {
        Rectangle all = new(0, 0, rgb.Width, rgb.Height);
        BitmapData data = rgb.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int[] pixels = new int[rgb.Width * rgb.Height];

            for (int y = 0; y < rgb.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rgb.Width, rgb.Width);
            }

            return new GoldenMeasure(pixels, rgb.Width, rgb.Height);
        }
        finally
        {
            rgb.UnlockBits(data);
        }
    }

    /// <summary>An image resampled to <see cref="MeasuredWidth"/>, keeping its aspect; how a golden is made from a capture.</summary>
    public static Bitmap Resampled(string path)
    {
        using Bitmap source = new(path);

        return Resampled(source);
    }

    private static Bitmap Resampled(Bitmap source)
    {
        int height = (int)Math.Round((double)source.Height * MeasuredWidth / source.Width);
        Bitmap target = new(MeasuredWidth, height, PixelFormat.Format32bppArgb);

        using Graphics draw = Graphics.FromImage(target);

        draw.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        draw.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        draw.DrawImage(source, new Rectangle(0, 0, MeasuredWidth, height));

        return target;
    }

    /// <summary>The fraction of pixels whose luma is at least <paramref name="threshold"/>.</summary>
    public double BrightFraction(float threshold)
    {
        int count = 0;

        foreach (float l in _luma)
        {
            if (l >= threshold)
            {
                count++;
            }
        }

        return (double)count / _luma.Length;
    }

    /// <summary>The centroid of the pixels at or above <paramref name="threshold"/>, as fractions of the width.</summary>
    public (double X, double Y) BrightCentroid(float threshold)
    {
        double sx = 0, sy = 0;
        int count = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_luma[(y * Width) + x] >= threshold)
                {
                    sx += x;
                    sy += y;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            throw new InvalidOperationException($"no pixel reaches luma {threshold}, so there is no bright centroid.");
        }

        return (sx / count / Width, sy / count / Width);
    }

    /// <summary>Mean luma in rings around a centre, each ring <paramref name="step"/> wide; all in width fractions.</summary>
    public double[] RadialProfile((double X, double Y) centre, double step, int rings)
    {
        double[] sum = new double[rings];
        int[] count = new int[rings];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double dx = ((double)x / Width) - centre.X;
                double dy = ((double)y / Width) - centre.Y;
                int ring = (int)(Math.Sqrt((dx * dx) + (dy * dy)) / step);

                if (ring < rings)
                {
                    sum[ring] += _luma[(y * Width) + x];
                    count[ring]++;
                }
            }
        }

        double[] mean = new double[rings];

        for (int i = 0; i < rings; i++)
        {
            mean[i] = count[i] == 0 ? 0 : sum[i] / count[i];
        }

        return mean;
    }

    /// <summary>Mean luma of each cell of a <paramref name="columns"/> by <paramref name="rows"/> grid, row-major.</summary>
    public double[] RegionMeans(int columns, int rows) => Regions(columns, rows, 1, (i, _) => _luma[i]);

    /// <summary>
    /// Mean red, green and blue of each cell of a <paramref name="columns"/> by <paramref name="rows"/> grid: cell-major,
    /// three values a cell, on 0..255.
    /// </summary>
    /// <remarks>**A hue shift keeps luma and moves these**, which is why a golden test reads both.</remarks>
    public double[] RegionChannelMeans(int columns, int rows) =>
        Regions(columns, rows, 3, (i, channel) => Channel(_pixels[i], channel));

    private double[] Regions(int columns, int rows, int perCell, Func<int, int, float> value)
    {
        double[] sum = new double[columns * rows * perCell];
        int[] count = new int[columns * rows];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int cell = (y * rows / Height * columns) + (x * columns / Width);

                for (int c = 0; c < perCell; c++)
                {
                    sum[(cell * perCell) + c] += value((y * Width) + x, c);
                }

                count[cell]++;
            }
        }

        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] /= count[i / perCell];
        }

        return sum;
    }

    /// <summary>Red (0), green (1) or blue (2) of a 32bpp ARGB pixel.</summary>
    private static int Channel(int argb, int channel) => (argb >> (16 - (8 * channel))) & 0xFF;
}
