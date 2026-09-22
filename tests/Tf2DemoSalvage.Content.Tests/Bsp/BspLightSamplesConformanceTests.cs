using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <see cref="BspLightSamples.Average"/> against `engine.dll` `0x1800d3990`'s `r_avglight` branch, the light
/// `R_LightVec` reports for a face by default (B415).
/// </summary>
/// <remarks>
/// <code>
/// for each style slot k until 255 (at most 4 when styles are asked for, else 1):
///     average = samples − 4 · (k + 1)
///     colour += average.rgb · power2_n[ average.exponent + 128 ] · d_lightstylevalue[ style ] / 264
/// </code>
/// </remarks>
public sealed class BspLightSamplesConformanceTests
{
    /// <summary>A face whose samples begin at byte 8, so its two averages sit at 4 (style slot 0) and 0 (slot 1).</summary>
    private static BspLightSamples Samples()
    {
        byte[] lump = new byte[16];

        // Slot 0's average: 255, 128, 0 at exponent -1.
        lump[4] = 255;
        lump[5] = 128;
        lump[6] = 0;
        lump[7] = unchecked((byte)(sbyte)-1);

        // Slot 1's: 51 at exponent 0.
        lump[0] = 51;
        lump[1] = 51;
        lump[2] = 51;

        return new BspLightSamples(lump, [new BspFaceLightLayout(8, (0, 5, 255, 255))]);
    }

    [Test]
    public void Average_OneStyle_IsTheAverageTimesTwoToTheExponentOver255()
    {
        (float r, float g, float b) = Samples().Average(0, styles: false, static _ => 1f);

        r.ShouldBe(255f * 0.5f / 255f, 1e-6f);
        g.ShouldBe(128f * 0.5f / 255f, 1e-6f);
        b.ShouldBe(0f);
    }

    [Test]
    public void Average_WithStyles_AddsEachStyleByItsValue()
    {
        (float r, _, _) = Samples().Average(0, styles: true, style => style == 5 ? 0.5f : 1f);

        r.ShouldBe((255f * 0.5f / 255f) + (51f / 255f * 0.5f), 1e-6f);
    }

    [Test]
    public void Average_AnUnlitFace_IsBlack()
    {
        new BspLightSamples(new byte[16], [new BspFaceLightLayout(-1, (0, 255, 255, 255))])
            .Average(0, styles: true, static _ => 1f)
            .ShouldBe((0f, 0f, 0f));
    }
}
