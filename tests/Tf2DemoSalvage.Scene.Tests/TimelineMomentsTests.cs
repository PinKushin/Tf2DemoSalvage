using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The adapter that lets the scene ask a demo for a moment — and tell it one thing back.
/// </summary>
/// <remarks>
/// **Every other member here is a pass-through and this file does not test those.** It exists for
/// <c>OnNewModel</c>, which is the only call that travels from the scene INTO the demo: the model
/// says which pose parameters wrap, and the interpolator that needs to know sits a layer below
/// models (B269). Nothing else in the chain can fail silently, and this hop can — an adapter that
/// dropped the fact would leave a sentry sweeping the long way round with every test still green.
/// </remarks>
public sealed class TimelineMomentsTests
{
    [Test]
    public void OnNewModel_ForAnEntityWithATrack_TeachesItWhichParametersWrap()
    {
        ScenePropTrack track = new(entityIndex: 7, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([track]));

        moments.OnNewModel(7, [false, true]);

        track.PoseParameterLoops.Count.ShouldBe(2);
        track.PoseParameterLoops[0].ShouldBeFalse();
        track.PoseParameterLoops[1].ShouldBeTrue();
    }

    /// <remarks>
    /// **The control, and it is not a defensive nicety.** The model set knows about entities the
    /// timeline may not have a track for — a viewmodel, an entity resolved from the item schema —
    /// and it tells the source about every one of them. Throwing there would take out the frame.
    /// </remarks>
    [Test]
    public void OnNewModel_ForAnEntityWithNoTrack_DoesNothing()
    {
        ScenePropTrack track = new(entityIndex: 7, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([track]));

        moments.OnNewModel(999, [true]);

        track.PoseParameterLoops.ShouldBeEmpty("the wrong entity's track must be left alone");
    }
}
