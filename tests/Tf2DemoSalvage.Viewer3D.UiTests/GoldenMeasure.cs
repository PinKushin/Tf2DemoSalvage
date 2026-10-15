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
/// brightness and is all any of these questions ask.
/// </remarks>
internal sealed class GoldenMeasure
{
    private readonly float[] _luma;

    private GoldenMeasure(float[] luma, int width, int height)
    {
        _luma = luma;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The width every image is resampled to before it is measured — the committed goldens' width.</summary>
    /// <remarks>
    /// **Both sides go through the same resample**, so a full-size viewer frame and a half-size golden are averaged
    /// alike; a measure taken at two resolutions would differ by the resampling alone around a small bright disc.
    /// </remarks>
    public const int MeasuredWidth = 640;

    /// <summary>Reads an image file into luma, resampled to <see cref="MeasuredWidth"/>.</summary>
    public static GoldenMeasure Load(string path)
    {
        using Bitmap rgb = Resampled(path);
        Rectangle all = new(0, 0, rgb.Width, rgb.Height);
        BitmapData data = rgb.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int[] pixels = new int[rgb.Width * rgb.Height];

            for (int y = 0; y < rgb.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rgb.Width, rgb.Width);
            }

            float[] luma = new float[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                int p = pixels[i];
                luma[i] = (0.299f * ((p >> 16) & 0xFF)) + (0.587f * ((p >> 8) & 0xFF)) + (0.114f * (p & 0xFF));
            }

            return new GoldenMeasure(luma, rgb.Width, rgb.Height);
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

        count.ShouldBeGreaterThan(0, $"no pixel reaches luma {threshold}, so there is no bright centroid.");

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
    public double[] RegionMeans(int columns, int rows)
    {
        double[] sum = new double[columns * rows];
        int[] count = new int[columns * rows];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int cell = (y * rows / Height * columns) + (x * columns / Width);

                sum[cell] += _luma[(y * Width) + x];
                count[cell]++;
            }
        }

        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] /= count[i];
        }

        return sum;
    }
}
