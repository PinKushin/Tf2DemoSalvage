using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The smaller of the level and view screen fades (B717), in the assembly that holds it.</summary>
public sealed class ScreenFadeRangeLowestTests
{
    /// <summary>A lens down +X from the origin, <c>[1][1]</c> 1, a 1000-pixel viewport.</summary>
    private static readonly ScreenFadeView View = new((0f, 0f, 0f), (1f, 0f, 0f), (0f, 0f, 1f), 1f, 1000f);

    /// <remarks>
    /// A sphere of radius 0.75 at 100 units is 15 pixels wide. Under 10..20 that is 127; under 12.5..22.5,
    /// (15 − 12.5) × 25.5 = 63. The smaller wins whichever range is the level's (`cdll_util.cpp:1137-1153`).
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Lowest_TwoRangesDisagreeing_IsTheSmaller(bool narrowerIsLevel)
    {
        ScreenFadeRange wide = ScreenFadeRange.Set(10f, 20f);
        ScreenFadeRange narrower = ScreenFadeRange.Set(12.5f, 22.5f);

        ScreenFadeRange.Lowest(View, (100f, 0f, 0f), 0.75f, 1f, narrowerIsLevel ? narrower : wide, narrowerIsLevel ? wide : narrower)
            .ShouldBe((byte)63);
    }

    /// <remarks>No lens, or a scale of 0, measures nothing: 255 (the early return of each fade).</remarks>
    [Test]
    public void Lowest_NoLensOrNoScale_IsOpaque()
    {
        ScreenFadeRange.Lowest(null, (100f, 0f, 0f), 0.75f, 1f, ScreenFadeRange.Set(10f, 20f), default).ShouldBe((byte)255);
        ScreenFadeRange.Lowest(View, (100f, 0f, 0f), 0.75f, 0f, ScreenFadeRange.Set(10f, 20f), default).ShouldBe((byte)255);
        ScreenFadeRange.Lowest(View, (100f, 0f, 0f), 0.75f, 1f, ScreenFadeRange.Set(10f, 20f), default)
            .ShouldBe((byte)127, "the control: the same sphere measured");
    }
}
