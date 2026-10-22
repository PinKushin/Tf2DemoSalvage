namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The exposure histogram's occlusion queries through the real device, with exact predicted counts (B575).</summary>
/// <remarks>
/// A 64×64 frame: <c>mat_exposure_center_region_x/y</c> 0.9 / 0.85 skip <c>(int)(64 · 0.05)</c> = 3 columns and
/// <c>(int)(64 · 0.075)</c> = 4 rows at each edge (`viewpostprocess.cpp:449-453`, `:485-488`), so 58 × 56 = 3248 pixels.
/// Twenty frames is every entry issued once and read three frames later.
/// </remarks>
public sealed class ExposureHistogramRenderTests
{
    private const int Region = 58 * 56;

    [Test]
    public void Measure_AFlatGreyFrame_CountsTheWholeRegionInItsOneBin()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        // Linear 0.3, stored through the sRGB curve and read back through it: bin 7 runs 0.2894 to 0.3536.
        target.Clear(0.3f, 0.3f, 0.3f);
        LuminanceHistogramSystem system = target.MeasureExposure(20);

        for (int bin = 0; bin < AutoExposure.Bins; bin++)
        {
            system.IsValid(bin).ShouldBeTrue();
            system.Counts[bin].ShouldBe(bin == 7 ? Region : 0, $"bin {bin}");
        }

        system.Counts[AutoExposure.Bins].ShouldBe(Region, "the seventeenth entry counts 0 to 100000: every pixel");
    }

    /// <remarks>A white row across black: only the 58 of its pixels inside the region count, in bin 15.</remarks>
    [Test]
    public void Measure_AWhiteRowOnBlack_CountsOnlyTheRegionsPixelsOfIt()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        float y = 1f - (32.5f * 2f / 64f);

        target.Clear(0f, 0f, 0f);
        target.DrawLines([((-1f, y), (1f, y))]);
        target.PixelAt(1, 32).Red.ShouldBe(255, "the control: the row is drawn outside the region too");

        LuminanceHistogramSystem system = target.MeasureExposure(20);

        system.Counts[15].ShouldBe(58);
        system.Counts[0].ShouldBe(Region - 58);
        system.Counts[8].ShouldBe(0);
    }
}
