using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A model's radius as <c>GetModelRadius</c> answers it (<c>model_t +0x40</c>, engine.dll 0x1801cc9a9), B724.</summary>
public sealed class SoundOriginsRadiusConformanceTests
{
    [Test]
    public void StudioRadius_AHull_IsItsLargestComponentMagnitude()
    {
        // FUN_1801060b0: radius 0, then each of hull_min and hull_max's six components, taken when its magnitude is larger.
        SoundOrigins.StudioRadius(new StudioBox(-24f, -24f, 0f, 24f, 24f, 82f)).ShouldBe(82f);
        SoundOrigins.StudioRadius(new StudioBox(-30f, -5f, -2f, 4f, 5f, 2f)).ShouldBe(30f);
        SoundOrigins.StudioRadius(StudioBox.Empty).ShouldBe(0f);
    }

    [Test]
    public void BrushRadius_ABrush_IsTheCornerOfItsWidenedBounds()
    {
        // Mod_LoadSubmodels (FUN_180104c80): mins − 1, maxs + 1, then per axis the larger magnitude: 3, 4 and 12, a length of 13.
        SoundOrigins.BrushRadius(new BspModel((-2f, -3f, 0f), (1f, 2f, 11f), default, 0, 0, 0)).ShouldBe(13f);
    }
}
