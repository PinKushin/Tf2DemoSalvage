using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>An integer-HDR lightmap holds light up to sixteen, not two (B514).</summary>
/// <remarks>
/// *Read in disassembly.* <c>materialsystem.dll</c> writes an integer-HDR lightmap texel as <c>(int)(light · 4096)</c>
/// clamped to 65535 (<c>0x180036450</c>, called per channel by the page writer <c>0x180028550</c>), so a texel holds
/// <c>light / 16</c>; <c>shaderapidx9.dll</c> loads <c>cLightScale.y = 16</c> under <c>HDR_TYPE_INTEGER</c>
/// (<c>0x18001be90</c>, from <c>SetToneMappingScaleLinear</c> <c>0x180023be0</c>), and <c>LIGHT_MAP_SCALE</c> is
/// <c>cLightScale.y</c> (<c>common_ps_fxc.h:51</c>). So light reaches 65535 / 4096 = 16. The atlas used to hold half
/// the light in a byte and clip at 2.
/// </remarks>
public sealed class HdrLightmapRangeConformanceTests
{
    [TestCase(510f)]
    [TestCase(765f)]
    [TestCase(1020f)]
    [TestCase(2040f)]
    public void Store_LightAboveTwo_IsKeptToSixBitsOfMantissa(float sample)
    {
        byte[] luxel = new byte[4];

        BspLightmaps.Store(luxel, sample, sample, sample);
        (float red, float green, float blue) = BspLightmaps.Load(luxel);

        red.ShouldBe(sample, sample / 64f);
        green.ShouldBe(red);
        blue.ShouldBe(sample, sample / 32f);
    }

    [Test]
    public void Store_LightAboveSixteen_IsClampedThere()
    {
        byte[] luxel = new byte[4];

        BspLightmaps.Store(luxel, 255f * 40f, 0f, 0f);

        BspLightmaps.Load(luxel).Red.ShouldBe(255f * BspLightmaps.HdrLightMapScale);
        BspLightmaps.HdrLightMapScale.ShouldBe(16f);
    }

    [Test]
    public void Store_Zero_IsAllZeroBits()
    {
        byte[] luxel = [1, 2, 3, 4];

        BspLightmaps.Store(luxel, 0f, 0f, 0f);

        luxel.ShouldBe(new byte[4]);
    }
}
