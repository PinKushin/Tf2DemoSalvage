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

        track.AtKeyframe(100).ShouldBeNull();
    }

    [Test]
    public void AtKeyframe_BetweenKeyframes_IsTheOneBefore()
    {
        // The raw stored value, with nothing added. Kept alongside the interpolating overload
        // because "what did the demo actually say" and "what should be drawn" are different
        // questions, and only one of them is evidence.
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(900, Pose(64f, 0f, 0f));

        track.AtKeyframe(500)!.Value.X.ShouldBe(0f);
        track.AtKeyframe(899)!.Value.X.ShouldBe(0f);
        track.AtKeyframe(900)!.Value.X.ShouldBe(64f);
    }

    [Test]
    public void At_BetweenKeyframes_Interpolates()
    {
        // **Parity with the engine, which does not snap.** A client stores a history of
        // value-plus-changetime entries - CInterpolatedVarEntryBase - and calls Interpolate() for
        // the moment being drawn. Snapping to the earlier keyframe makes a rocket jump between
        // updates instead of flying, which is most obvious on a 33-tick server where updates are
        // twice as far apart.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(200, Pose(100f, 0f, 0f));

        track.At(150)!.Value.X.ShouldBe(50f, 0.001);
        track.At(175)!.Value.X.ShouldBe(75f, 0.001);
    }

    [Test]
    public void At_OutsideTheKeyframes_DoesNotExtrapolate()
    {
        // Before the first and after the last, the value holds. Extrapolating would send a rocket
        // on for ever after its last update, which is a plausible-looking trajectory and entirely
        // invented.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(200, Pose(100f, 0f, 0f));

        track.At(500)!.Value.X.ShouldBe(100f);
    }

    [Test]
    public void At_OnCycle_WrapsRatherThanRunningBackwards()
    {
        // **Valve's LoopingLerp, from lerp_functions.h.** A looping animation goes 0.9 -> 0.1 by
        // passing through 1.0, not by running backwards through 0.5. The rule is the engine's: if
        // the two differ by half a cycle or more, raise the lower by one before interpolating and
        // take the fractional part.
        //
        // Without it a looping model plays smoothly forwards and then rewinds through its whole
        // animation at every loop point, which reads as a broken animation rather than as a
        // broken interpolation.
        ScenePropTrack track = new(entityIndex: 3, "models/props/fan.mdl");

        track.Add(100, Pose(0f, 0f, 0f) with { Cycle = 0.9f, Sequence = 1 });
        track.Add(200, Pose(0f, 0f, 0f) with { Cycle = 0.1f, Sequence = 1 });

        // Halfway is 1.0, which wraps to 0.0 - not 0.5, which is where a plain lerp lands.
        track.At(150)!.Value.Cycle.ShouldBe(0f, 0.001);
    }

    [Test]
    public void At_OnCycleWithinOneLoop_IsAPlainLerp()
    {
        // The control for the test above: the wrap only applies when the gap is half a cycle or
        // more. A rule applied everywhere would corrupt ordinary playback, and both tests pass
        // against code that always wraps unless one of them pins the ordinary case.
        ScenePropTrack track = new(entityIndex: 3, "models/props/fan.mdl");

        track.Add(100, Pose(0f, 0f, 0f) with { Cycle = 0.2f, Sequence = 1 });
        track.Add(200, Pose(0f, 0f, 0f) with { Cycle = 0.6f, Sequence = 1 });

        track.At(150)!.Value.Cycle.ShouldBe(0.4f, 0.001);
    }

    [Test]
    public void At_AcrossASequenceChange_DoesNotBlendTheCycle()
    {
        // Two different animations have no common timeline, so a cycle of 0.9 in one and 0.1 in
        // the next are not two points on one curve. Blending them produces a pose from neither
        // animation - and it is the loop case that makes this visible, since that is when the
        // wrap rule would otherwise fire on unrelated numbers.
        ScenePropTrack track = new(entityIndex: 3, "models/player/scout.mdl");

        track.Add(100, Pose(0f, 0f, 0f) with { Cycle = 0.9f, Sequence = 1 });
        track.Add(200, Pose(0f, 0f, 0f) with { Cycle = 0.1f, Sequence = 2 });

        ScenePose shown = track.At(150)!.Value;

        shown.Sequence.ShouldBe(1, "the new animation has not started yet");
        shown.Cycle.ShouldBe(0.9f, "held, not blended into an animation it does not belong to");
    }

    [Test]
    public void At_OnYaw_TakesTheShortWayRound()
    {
        // 350 degrees to 10 degrees is a 20 degree turn through north, not a 340 degree turn the
        // other way. A plain lerp spins the model almost all the way round between two updates,
        // which looks like a model that cannot decide which way it faces.
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");

        track.Add(100, Pose(0f, 0f, 0f) with { Yaw = 350f });
        track.Add(200, Pose(0f, 0f, 0f) with { Yaw = 10f });

        track.At(150)!.Value.Yaw.ShouldBe(0f, 0.01);
    }

    [Test]
    public void At_AfterTheLastKeyframe_IsTheLastPose()
    {
        // A pickup that never moves again is still there. Ending the track at its last keyframe
        // would make every static entity vanish moments into the demo.
        ScenePropTrack track = new(entityIndex: 3, "models/items/medkit_small.mdl");

        track.Add(100, Pose(10f, 20f, 30f));

        track.AtKeyframe(90_000)!.Value.Y.ShouldBe(20f);
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

        track.AtKeyframe(399).ShouldNotBeNull();
        track.AtKeyframe(400).ShouldBeNull();
    }

    [Test]
    [TestCase("models/items/medkit_small.mdl", SceneModelKind.Studio)]
    [TestCase("*3", SceneModelKind.Brush)]
    [TestCase("sprites/light_glow02_noz.vmt", SceneModelKind.Sprite)]
    [TestCase("sprites/glow06.spr", SceneModelKind.Sprite)]
    [TestCase("", SceneModelKind.Unknown)]
    [TestCase("something/unexpected.txt", SceneModelKind.Unknown)]
    public void Classify_TellsTheThreeKindsApart(string modelPath, SceneModelKind expected)
    {
        // **Every one of these came from the corpus refusing to be classified**, in this order:
        // "*3" on the 2007 demo, then "sprites/light_glow02_noz.vmt" on the 2008 one, then
        // "sprites/glow06.spr" on a 2026 one. Valve's modtype_t had all of it the whole time.
        //
        // Written as a unit test so the knowledge does not depend on a five-minute corpus run to
        // be checked, and so a fourth kind arriving is a fast failure rather than a slow one.
        ScenePropTrack.Classify(modelPath).ShouldBe(expected);
    }

    private static ScenePose Pose(float x, float y, float z) =>
        new() { X = x, Y = y, Z = z };
}
