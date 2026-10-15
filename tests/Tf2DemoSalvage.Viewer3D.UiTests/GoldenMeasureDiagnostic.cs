using System;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>Prints every measure for each PNG in <c>GOLDEN_DIR</c>; asserts nothing.</summary>
[Explicit("a measurement, not a test")]
public sealed class GoldenMeasureDiagnostic
{
    [Test]
    public void Measure_EveryPngInGoldenDir_PrintsTheMeasures()
    {
        string folder = Environment.GetEnvironmentVariable("GOLDEN_DIR") ?? throw new InvalidOperationException("GOLDEN_DIR unset");

        foreach (string png in Directory.GetFiles(folder, "*.png").Order(StringComparer.Ordinal))
        {
            if (Environment.GetEnvironmentVariable("GOLDEN_OUT") is { } outDir)
            {
                using System.Drawing.Bitmap small = GoldenMeasure.Resampled(png);
                small.Save(Path.Combine(outDir, Path.GetFileName(png)), System.Drawing.Imaging.ImageFormat.Png);
            }

            GoldenMeasure m = GoldenMeasure.Load(png);
            (double X, double Y) c = m.BrightFraction(250) > 0 ? m.BrightCentroid(250) : (0.5, 0.28);
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(png)} {m.Width}x{m.Height} bright250={m.BrightFraction(250):0.00000} bright240={m.BrightFraction(240):0.00000} " +
                $"centroid=({c.X:0.0000},{c.Y:0.0000})");
            TestContext.Out.WriteLine("  radial " + string.Join(" ", m.RadialProfile(c, 0.02, 10).Select(v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))));
            TestContext.Out.WriteLine("  regions " + string.Join(" ", m.RegionMeans(4, 3).Select(v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))));
        }
    }
}
