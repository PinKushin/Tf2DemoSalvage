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

        moments.OnNewModel(7, [false, true], staticProp: false);

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

        moments.OnNewModel(999, [true], staticProp: false);

        track.PoseParameterLoops.ShouldBeEmpty("the wrong entity's track must be left alone");
    }

    /// <remarks>
    /// **B389, and the same fault the `cycle` probe had.** An edict index does not name a track —
    /// the engine recycles slots, so `TrackFor(entityIndex)` answers about whichever occupant was
    /// written last. A model resolving during a frame at tick 150 was stamping a track that does not
    /// begin until tick 400: the entity being drawn kept the pose-parameter history of a model it
    /// never had, and one that does not exist yet got looping flags from the past. Neither threw.
    ///
    /// **The frame's own tick is what decides**, which is also what makes it Valve's rule: the engine
    /// stamps `gpGlobals->curtime` because `OnNewModel` happens to the entity being simulated NOW.
    /// </remarks>
    [Test]
    public void OnNewModel_AnIndexWhoseSlotWasReused_TeachesTheOccupantAliveAtTheFrame()
    {
        ScenePropTrack early = new(entityIndex: 7, "models/props_well/main_entrance_door.mdl");
        early.Add(100, new ScenePose());
        early.Add(280, new ScenePose());
        early.End(300);

        ScenePropTrack late = new(entityIndex: 7, "models/player/scout.mdl");
        late.Add(400, new ScenePose());
        late.Add(500, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([early, late]));

        // The frame being drawn, which is the only thing that says which occupant this is about.
        moments.PropsAt(150, new List<SceneProp>());

        moments.OnNewModel(7, [false, true], staticProp: true);

        early.PoseParameterLoops.Count.ShouldBe(2);
        early.StaticPropModel.ShouldBeTrue();

        late.PoseParameterLoops.ShouldBeEmpty(
            "the occupant that does not arrive until tick 400 was not the one whose model resolved");
        late.StaticPropModel.ShouldBeFalse();
    }

    /// <remarks>
    /// **The other direction, so the test above cannot pass by always picking the first.** The same
    /// two tracks, a later frame, and the stamp must land on the other one.
    /// </remarks>
    [Test]
    public void OnNewModel_AFrameInsideTheSecondOccupant_TeachesThatOneInstead()
    {
        ScenePropTrack early = new(entityIndex: 7, "models/props_well/main_entrance_door.mdl");
        early.Add(100, new ScenePose());
        early.End(300);

        ScenePropTrack late = new(entityIndex: 7, "models/player/scout.mdl");
        late.Add(400, new ScenePose());
        late.Add(500, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([early, late]));

        moments.PropsAt(450, new List<SceneProp>());

        moments.OnNewModel(7, [true], staticProp: false);

        late.PoseParameterLoops.Count.ShouldBe(1);
        early.PoseParameterLoops.ShouldBeEmpty();
    }
}
