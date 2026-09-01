using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A track that can never change is derived once, not once a frame — B259 fix 3, stage A.
/// </summary>
/// <remarks>
/// **The engine is told; we can ask.** `CClientLeafSystem` learns an entity moved through
/// `RenderableChanged`, which marks it dirty, and `PreRender` then reworks only the dirty ones — so
/// its per-frame cost is proportional to what moved rather than to what exists
/// (`clientleafsystem.cpp:543`). It has to be told, because it streams and cannot see the future.
/// This project decodes the whole demo before drawing a frame, so a track already knows whether it
/// will ever change: one with a single keyframe answers the same pose at every tick of its life.
///
/// Measured on `tf2-2026-pub-pov-clean`: **677 of 1,165 tracks**, which is most of what a map
/// contains — crates, lights, doors, signs.
///
/// **What these tests must distinguish**, and it is the whole difficulty: a cache that never updates
/// and a cache that never hits produce the same output. Only the second pair below can tell them
/// apart, by asking a track that DOES change for two different answers.
/// </remarks>
public sealed class ConstantTrackTests
{
    [Test]
    public void NeverChanges_WithOneKeyframe_IsTrue()
    {
        ScenePropTrack track = new(entityIndex: 5, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 10f, Y = 20f, Z = 30f });

        track.NeverChanges.ShouldBeTrue();
    }

    /// <remarks>
    /// The control. Without it a property hard-coded to true would pass the test above, and every
    /// moving entity in the game would be frozen at its first pose.
    /// </remarks>
    [Test]
    public void NeverChanges_WithTwoKeyframes_IsFalse()
    {
        ScenePropTrack track = new(entityIndex: 5, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 10f });
        track.Add(7, new ScenePose { X = 200f });

        track.NeverChanges.ShouldBeFalse();
    }

    /// <remarks>
    /// **A constant track is still not alive everywhere**, and conflating the two is how the
    /// interpolation list shipped a defect: ended tracks held their last pose for ever and
    /// `selected` went 566 to 850. A prop that has been removed is not a prop that stopped moving.
    /// </remarks>
    [Test]
    public void Alive_OutsideTheTracksLife_IsFalse()
    {
        ScenePropTrack track = new(entityIndex: 5, "models/props/crate.mdl");

        track.Add(100, new ScenePose { X = 10f });
        track.End(200);

        track.Alive(50d).ShouldBeFalse("before its first keyframe the entity does not exist yet");
        track.Alive(150d).ShouldBeTrue();
        track.Alive(250d).ShouldBeFalse("from End the entity is gone");
    }

    /// <remarks>
    /// **The output test for a constant track**: whatever the sampling does internally, two
    /// different ticks inside one life must produce the same prop. This passes with or without a
    /// cache — it is the invariant a cache must not break, not evidence that one exists.
    /// </remarks>
    [Test]
    public void PropsAt_AConstantTrackAtTwoTicks_AnswersTheSameProp()
    {
        ScenePropTrack track = new(entityIndex: 5, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 10f, Y = 20f, Z = 30f, Yaw = 90f });

        DemoTimeline timeline = DemoTimeline.ForTracks([track]);

        List<SceneProp> first = [];
        List<SceneProp> later = [];

        timeline.PropsAt(20d, first);
        timeline.PropsAt(400d, later);

        first.Single().ShouldBe(later.Single());
    }

    /// <remarks>
    /// **The pair that catches a cache which never updates.** A moving track must answer DIFFERENTLY
    /// at two ticks — so a cache keyed on the track rather than on the tick, or one that forgets to
    /// exclude changing tracks, reddens here and nowhere else. Without this the feature could be
    /// implemented as "return the first answer for ever" and every test above would still pass.
    /// </remarks>
    [Test]
    public void PropsAt_AMovingTrackAtTwoTicks_AnswersDifferentPositions()
    {
        ScenePropTrack track = new(entityIndex: 5, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f });
        track.Add(7, new ScenePose { X = 100f });
        track.Add(60, new ScenePose { X = 800f });

        DemoTimeline timeline = DemoTimeline.ForTracks([track]);

        List<SceneProp> early = [];
        List<SceneProp> late = [];

        timeline.PropsAt(14d, early);
        timeline.PropsAt(66d, late);

        early.Single().Pose.X.ShouldNotBe(
            late.Single().Pose.X,
            "a track with three keyframes moves, and must not be served from a constant cache");
    }
}
