using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// One entity's pose over the whole demo, stored as the moments it changed.
/// </summary>
/// <remarks>
/// **Keyframes rather than a value per tick, and the arithmetic is why.** A 1,600-second demo is
/// around 106,000 frames; a match carries a few hundred model-bearing entities, so a pose per
/// entity per frame is tens of millions of records for a scene where most of them never move at
/// all. A health pack that sits still for the whole match costs one keyframe here.
///
/// It also matches the format. A demo sends only what changed, so the moments a track records are
/// exactly the moments the demo spoke — nothing is invented between them, and a pose asked for
/// between two keyframes is the earlier one, which is what the entity was.
/// </remarks>
public sealed class ScenePropTrackTests
{
    [Test]
    public void At_BeforeTheFirstKeyframe_IsNothing()
    {
        // An entity that enters at tick 500 did not exist at tick 100. Answering with its first
        // pose would have pickups and projectiles present from the start of every demo.
        ScenePropTrack track = new(entityIndex: 3, "models/items/medkit_small.mdl");

        track.Add(500, Pose(0f, 0f, 0f));

        track.At(100).ShouldBeNull();
    }

    [Test]
    public void At_BetweenKeyframes_IsTheOneBefore()
    {
        // Not interpolated: the demo said the entity was here and then said it was there, and
        // anything in between is invention. Players are interpolated by the engine because they
        // move every tick; a door that opened at tick 900 was shut at 899.
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(900, Pose(64f, 0f, 0f));

        track.At(500)!.Value.X.ShouldBe(0f);
        track.At(899)!.Value.X.ShouldBe(0f);
        track.At(900)!.Value.X.ShouldBe(64f);
    }

    [Test]
    public void At_AfterTheLastKeyframe_IsTheLastPose()
    {
        // A pickup that never moves again is still there. Ending the track at its last keyframe
        // would make every static entity vanish moments into the demo.
        ScenePropTrack track = new(entityIndex: 3, "models/items/medkit_small.mdl");

        track.Add(100, Pose(10f, 20f, 30f));

        track.At(90_000)!.Value.Y.ShouldBe(20f);
    }

    [Test]
    public void Add_APoseIdenticalToTheLast_IsNotStored()
    {
        // **The compression this whole type exists for.** An entity re-sends properties without
        // having moved, and storing each one would give a still health pack a keyframe per
        // snapshot - which is the per-frame cost this design was chosen to avoid.
        ScenePropTrack track = new(entityIndex: 3, "models/items/medkit_small.mdl");

        track.Add(100, Pose(10f, 20f, 30f));
        track.Add(200, Pose(10f, 20f, 30f));
        track.Add(300, Pose(10f, 20f, 30f));

        track.KeyframeCount.ShouldBe(1);
    }

    [Test]
    public void Add_APoseThatDiffersOnlyInAnimation_IsStored()
    {
        // The control for the test above: "identical" must mean the whole pose, not just the
        // position. A model playing an animation on the spot changes every frame while standing
        // still, and dropping those keyframes would freeze it.
        ScenePropTrack track = new(entityIndex: 3, "models/props/fan.mdl");

        track.Add(100, Pose(10f, 20f, 30f) with { Cycle = 0.1f });
        track.Add(200, Pose(10f, 20f, 30f) with { Cycle = 0.6f });

        track.KeyframeCount.ShouldBe(2);
    }

    [Test]
    public void At_AfterTheEntityLeft_IsNothing()
    {
        // Entities are destroyed - a health pack is picked up, a rocket explodes. Without an end
        // the model stays where it died for the rest of the demo, which reads as a scene that
        // slowly fills with rubbish rather than as a defect.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.End(400);

        track.At(399).ShouldNotBeNull();
        track.At(400).ShouldBeNull();
    }

    private static ScenePose Pose(float x, float y, float z) =>
        new() { X = x, Y = y, Z = z };
}
