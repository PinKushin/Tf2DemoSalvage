using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <see cref="BspLightSamples.Linear"/> against `engine.dll` `0x1800d37d0`, the light `R_LightVec` reads at one luxel (B415).
/// </summary>
/// <remarks>
/// <code>
/// sample = samples + ( (extents[0] + 1) * t + s ) * 4
/// for each style until 255 (at most 4 when styles are asked for, else 1):
///     scale = d_lightstylevalue[ style ] / 264
///     colour += sample.rgb * power2_n[ sample.exponent + 128 ] * scale     // 2^e / 255
///     sample += luxels * 4 * ( bumped ? 4 : 1 )
/// </code>
/// </remarks>
public sealed class BspLightSamplesConformanceTests
{
    /// <summary>A 2×2-luxel face (extents 1) with two styles, one bump set, at offset 0.</summary>
    private static BspLightSamples Samples(bool bumped = false)
    {
        int sets = bumped ? 4 : 1;
        byte[] lump = new byte[2 * sets * 4 * 4];

        // Style 0, luxel (1, 1): 255 at exponent -1.
        lump[(3 * 4) + 0] = 255;
        lump[(3 * 4) + 1] = 128;
        lump[(3 * 4) + 2] = 0;
        lump[(3 * 4) + 3] = unchecked((byte)(sbyte)-1);

        // Style 1, luxel (1, 1), one whole style (every bump set) further on: 51 at exponent 0.
        int second = (sets * 4 * 4) + (3 * 4);

        lump[second + 0] = 51;
        lump[second + 1] = 51;
        lump[second + 2] = 51;

        return new BspLightSamples(lump, [new BspFaceLightLayout(0, 1, 1, (0, 5, 255, 255), bumped)]);
    }

    [Test]
    public void Linear_OneStyle_IsTheSampleTimesTwoToTheExponentOver255()
    {
        (float r, float g, float b) = Samples().Linear(0, 1, 1, styles: false, static _ => 1f);

        r.ShouldBe(255f * 0.5f / 255f, 1e-6f);
        g.ShouldBe(128f * 0.5f / 255f, 1e-6f);
        b.ShouldBe(0f);
    }

    [Test]
    public void Linear_WithStyles_AddsEachStyleByItsValue()
    {
        (float r, _, _) = Samples().Linear(0, 1, 1, styles: true, style => style == 5 ? 0.5f : 1f);

        r.ShouldBe((255f * 0.5f / 255f) + (51f / 255f * 0.5f), 1e-6f);
    }

    [Test]
    public void Linear_ABumpedFace_StepsAWholeStylePastItsFourSets()
    {
        (float r, _, _) = Samples(bumped: true).Linear(0, 1, 1, styles: true, static _ => 1f);

        r.ShouldBe((255f * 0.5f / 255f) + (51f / 255f), 1e-6f);
    }

    [Test]
    public void Linear_AnUnlitFace_IsBlack()
    {
        new BspLightSamples(new byte[16], [new BspFaceLightLayout(-1, 1, 1, (0, 255, 255, 255), false)])
            .Linear(0, 0, 0, styles: true, static _ => 1f)
            .ShouldBe((0f, 0f, 0f));
    }
}
