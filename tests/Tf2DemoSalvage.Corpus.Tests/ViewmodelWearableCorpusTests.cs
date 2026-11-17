using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>A real viewmodel wearable reaches the first-person pass, on its own viewmodel, and only there (B771).</summary>
/// <remarks>
/// **Only real bytes say whether the viewmodel's entity is the wearable's move parent**: on the f12 match the demoman (4)
/// holds sticky launcher 360 at tick 60000, and its Botkiller's viewmodel twin 362 hangs from his viewmodel. Built as the
/// viewer builds it — `MomentScene.Build` in his eyes, then `ViewmodelScene.Build` from the timeline's viewmodels — 362 is
/// out of the draw list and merged onto the arms. At 20000 he holds his grenade launcher: the twin's weapon is holstered
/// and it is in neither, the control.
/// </remarks>
public sealed class ViewmodelWearableCorpusTests
{
    private const string Match = "demostf-cp_process_f12-2026-08-08-2207";
    private const int Demoman = 4;
    private const int Twin = 362;

    [TestCase(60000, true, TestName = "Build_OnF12WithTheLauncherOut_DrawsItsViewmodelWearableOnTheArms")]
    [TestCase(20000, false, TestName = "Build_OnF12WithTheLauncherHolstered_DrawsItsViewmodelWearableNowhere")]
    public void Build_OnF12_PlacesTheBotkillersViewmodelTwinOnlyOnTheArms(int tick, bool onArms)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));
        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);
        MomentInfo info = new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: true,
            Followed: Demoman,
            EyeCamera: null,
            IntervalPerTick: timeline.IntervalPerTick,
            ViewmodelFieldOfView: 54f) { Recorder = timeline.RecorderEntityIndex };

        scene.Build(timeline.PlayersAt(tick), props, info);
        ViewmodelSceneResult firstPerson = new ViewmodelScene().Build(
            new TimelineViewmodels(timeline),
            tick,
            Demoman,
            new ViewmodelPlacement(0f, 0f, 0f, 0f, 0f, 0f),
            hands: null,
            heldWeapon: null,
            wearables: scene.ViewmodelWearables);

        (scene.Drawn.Any(prop => prop.EntityIndex == Twin),
         firstPerson.Props.Any(prop => prop.EntityIndex == Twin && prop.AttachedTo == ViewmodelScene.ArmsEntityIndex))
            .ShouldBe((false, onArms));
    }
}
