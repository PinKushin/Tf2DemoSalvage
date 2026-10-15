using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>
/// <see cref="GoldenMeasure"/> on synthetic images whose answer the test put there (D219, D38).
/// </summary>
/// <remarks>
/// **An instrument is proved before it is believed**, so every measure is asked for a value that MUST be present
/// (a known cell, a known square, a known disc) and, as the control, for one that must be absent or uniform.
/// No viewer is launched; the namespace keeps `ViewerSession` out.
/// </remarks>
public sealed class GoldenMeasureTests
{
    [Test]
    public void RegionMeans_OfAGridWithOneGreyPerCell_IsThatGreyPerCell()
    {
        // 4x3 cells of 2x2 pixels, cell k grey 10k: grey's luma is itself, since the Rec. 601 weights sum to 1.
        using Bitmap image = Paint(8, 6, (x, y) => Grey(10 * ((y / 2 * 4) + (x / 2))));

        double[] means = GoldenMeasure.Of(image).RegionMeans(4, 3);

        means.Length.ShouldBe(12);

        for (int k = 0; k < 12; k++)
        {
            means[k].ShouldBe(10 * k, 0.01, $"cell {k}");
        }
    }

    [Test]
    public void RegionMeans_OfAUniformImage_IsEqualEverywhere()
    {
        using Bitmap image = Paint(9, 7, (_, _) => Grey(77));

        GoldenMeasure.Of(image).RegionMeans(4, 3).ShouldAllBe(m => Math.Abs(m - 77) < 0.01);
    }

    [Test]
    public void RegionChannelMeans_OfRedBesideBlue_SeparatesTheHuesLumaCannot()
    {
        using Bitmap image = Paint(4, 2, (x, _) => x < 2 ? Color.FromArgb(255, 0, 0) : Color.FromArgb(0, 0, 255));
        GoldenMeasure measure = GoldenMeasure.Of(image);

        Assert.Multiple(() =>
        {
            measure.RegionChannelMeans(2, 1).ShouldBe([255, 0, 0, 0, 0, 255]);

            // The control: luma reads the two halves as 76.2 and 29.1, so it sees a difference but not which hue.
            measure.RegionMeans(2, 1)[0].ShouldBe(0.299 * 255, 0.01);
            measure.RegionMeans(2, 1)[1].ShouldBe(0.114 * 255, 0.01);
        });
    }

    [Test]
    public void RegionChannelMeans_OfAHueRotationAtEqualLumaOrder_Moves()
    {
        // Every channel swapped round: a hue shift. The channel means must move by the full channel difference.
        using Bitmap before = Paint(4, 4, (_, _) => Color.FromArgb(200, 100, 50));
        using Bitmap after = Paint(4, 4, (_, _) => Color.FromArgb(50, 200, 100));

        double[] a = GoldenMeasure.Of(before).RegionChannelMeans(1, 1);
        double[] b = GoldenMeasure.Of(after).RegionChannelMeans(1, 1);

        a.Zip(b, (p, q) => Math.Abs(p - q)).Max().ShouldBe(150);
    }

    [Test]
    public void BrightFraction_OfThreeWhitePixelsInAHundred_IsThreeHundredths()
    {
        using Bitmap image = Paint(10, 10, (x, y) => y == 4 && x < 3 ? Grey(255) : Grey(0));
        GoldenMeasure measure = GoldenMeasure.Of(image);

        Assert.Multiple(() =>
        {
            measure.BrightFraction(250).ShouldBe(0.03, 1e-12);

            // Controls at both ends: nothing is below 0, and nothing reaches past white.
            measure.BrightFraction(0).ShouldBe(1.0);
            measure.BrightFraction(256).ShouldBe(0.0);
        });
    }

    [Test]
    public void BrightCentroid_OfAWhiteSquare_IsItsCentreInWidthFractions()
    {
        // A 10x10 square at x 20..29, y 10..19 in a 100x50 frame: centre (24.5, 14.5), both over the WIDTH.
        using Bitmap image = Paint(100, 50, (x, y) => x is >= 20 and < 30 && y is >= 10 and < 20 ? Grey(255) : Grey(30));

        (double X, double Y) centre = GoldenMeasure.Of(image).BrightCentroid(250);

        Assert.Multiple(() =>
        {
            centre.X.ShouldBe(0.245, 1e-12);
            centre.Y.ShouldBe(0.145, 1e-12);
        });
    }

    [Test]
    public void BrightCentroid_WithNothingBright_FailsSayingSo()
    {
        using Bitmap image = Paint(10, 10, (_, _) => Grey(30));

        Should.Throw<Exception>(() => GoldenMeasure.Of(image).BrightCentroid(250))
            .Message.ShouldContain("no pixel reaches luma 250");
    }

    [Test]
    public void RadialProfile_OfAWhiteDisc_IsWhiteInsideAndBlackOutside()
    {
        // A disc of radius 7.5 px around (50, 50) in 100x100, and rings 7.5 px (0.075) wide: on integer pixels d < 7.5 is
        // d² ≤ 56, so the first ring is exactly the disc and no pixel sits on a ring boundary.
        using Bitmap image = Paint(100, 100, (x, y) => ((x - 50) * (x - 50)) + ((y - 50) * (y - 50)) <= 56 ? Grey(255) : Grey(0));

        double[] rings = GoldenMeasure.Of(image).RadialProfile((0.5, 0.5), 0.075, 4);

        rings.Select(r => Math.Round(r, 3)).ShouldBe([255, 0, 0, 0]);
    }

    [Test]
    public void RadialProfile_OfAUniformImage_IsFlat()
    {
        using Bitmap image = Paint(40, 40, (_, _) => Grey(100));

        GoldenMeasure.Of(image).RadialProfile((0.5, 0.5), 0.05, 5).ShouldAllBe(r => Math.Abs(r - 100) < 0.01);
    }

    /// <summary>Every committed golden is <see cref="GoldenMeasure.MeasuredWidth"/> wide and 16:9, as TF2's 1280x720 resampled.</summary>
    [Test]
    public void Goldens_EveryCommittedPng_Is640By360()
    {
        string[] pngs = Directory.GetFiles(Path.Combine(TestContext.CurrentContext.TestDirectory, "Goldens"), "*.png");

        pngs.ShouldNotBeEmpty("no golden was copied to the test output, so this checked nothing.");

        Assert.Multiple(() =>
        {
            foreach (string png in pngs)
            {
                using Bitmap image = new(png);

                (image.Width, image.Height).ShouldBe((GoldenMeasure.MeasuredWidth, 360), Path.GetFileName(png));
            }
        });
    }

    [Test]
    public void Granary_AtALevelWithNoGolden_FailsNamingTheLevelsThatExist()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Golden.Granary(1))
            .Message.ShouldContain("only levels 0 and 2");
    }

    private static Color Grey(int v) => Color.FromArgb(v, v, v);

    private static Bitmap Paint(int width, int height, Func<int, int, Color> colour)
    {
        Bitmap image = new(width, height, PixelFormat.Format32bppArgb);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.SetPixel(x, y, colour(x, y));
            }
        }

        return image;
    }
}
