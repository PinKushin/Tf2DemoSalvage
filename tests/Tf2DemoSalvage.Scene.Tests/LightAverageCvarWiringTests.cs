using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`r_lightaverage` and `r_lightinterp` from a pasted config to the average a pose uses (B676).</summary>
/// <remarks>
/// Defaults as `engine.dll` registers them: `FUN_180006ed0` "r_lightaverage", `FUN_180006f50` "r_lightinterp", flags 0x4000. The last
/// link, `ViewerPresenter.Show` to the view, is held by `--measure`'s `light averaged` count going to 0 under `+r_lightaverage 0`
/// (`docs/measurements/2026-11-04-b676-the-switch-and-the-count.md`).
/// </remarks>
public sealed class LightAverageCvarWiringTests
{
    [Test]
    public void Parse_NothingSet_IsTheEnginesDefaults()
    {
        ViewerSettings settings = new();

        settings.LightAverage.ShouldBeTrue();
        settings.LightInterp.ShouldBe(5f);
        EngineConVars.ByName(ViewerSettings.LightInterpCommand).Cheat.ShouldBeTrue();
    }

    [Test]
    public void Parse_AConfigSettingBoth_ReadsBoth()
    {
        ViewerSettings settings = ViewerSettings.Parse("r_lightaverage 0\nr_lightinterp 2");

        settings.LightAverage.ShouldBeFalse();
        settings.LightInterp.ShouldBe(2f);
    }

    [Test]
    public void Pose_AMomentWithBothSet_HandsThemToTheAverageAndCountsNothingWithNoModel()
    {
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentInfo info = new(0d, 0, false, null, null, 0.015f, 54f, LightAverage: false, LightInterp: 2f);

        scene.Build([], [], info);

        MomentPhases phases = scene.Pose(info);

        models.LightAverage.Enabled.ShouldBeFalse();
        models.LightAverage.Rate.ShouldBe(2f);
        phases.LightAveraged.ShouldBe(0);
    }
}
