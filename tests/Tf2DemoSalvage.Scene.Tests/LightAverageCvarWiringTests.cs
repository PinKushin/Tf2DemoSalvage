using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

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
        // A fraction, which a reading as a whole number would drop.
        ViewerSettings settings = ViewerSettings.Parse("r_lightaverage 0\nr_lightinterp 2.5");

        settings.LightAverage.ShouldBeFalse();
        settings.LightInterp.ShouldBe(2.5f);
    }

    /// <summary>A model is drawn each way, so this fails if the pose or the draw ignores the setting: off counts nothing, on counts it.</summary>
    [TestCase(false, 2f, 0)]
    [TestCase(true, 2f, 1)]
    [TestCase(true, 0f, 0)]
    public void Pose_AMomentWithAModelDrawn_AveragesItOnlyWithBothSwitchesOn(bool average, float interp, int counted)
    {
        SceneProp prop = EntityModelSetPrePoseTests.Prop("models/player/demo.mdl", 5);
        EntityModelSet models = new();

        models.Add([prop], static _ => EntityModelSetPrePoseTests.Skinned("root"));

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentInfo info = new(0d, 0, false, null, null, 0.015f, 54f, LightAverage: average, LightInterp: interp);

        scene.Build([], [prop], info);

        MomentPhases phases = scene.Pose(info);

        scene.Instances.Count.ShouldBe(1, "the instrument: the model was drawn");
        models.LightAverage.Enabled.ShouldBe(average);
        models.LightAverage.Rate.ShouldBe(interp);
        phases.LightAveraged.ShouldBe(counted);
    }
}
