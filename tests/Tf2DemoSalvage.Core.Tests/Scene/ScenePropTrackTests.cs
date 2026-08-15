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
    public void At_WithAThirdSample_FollowsTheHermiteCurve()
    {
        // **The engine's default, not an enhancement.** _Interpolate_Hermite is what the client
        // uses whenever a third sample exists; linear is the fallback for when it does not, or
        // when INTERPOLATE_LINEAR_ONLY is set. A rocket that is turning bends through its updates
        // instead of taking a corner at each one.
        //
        // Predicted exactly rather than checked for "not linear". With p0 = 0, p1 = 0, p2 = 100
        // evenly spaced, Lerp_Hermite at t = 0.5 is:
        //     p1*(2t^3-3t^2+1) + p2*(-2t^3+3t^2) + d1*(t^3-2t^2+t) + d2*(t^3-t^2)
        //   = 0*0.5 + 100*0.5 + 0*0.125 + 100*(-0.125)
        //   = 37.5
        // Linear would be 50, so the two disagree and the test can tell them apart.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(200, Pose(0f, 0f, 0f) with { Yaw = 1f });
        track.Add(300, Pose(100f, 0f, 0f) with { Yaw = 1f });

        track.At(250)!.Value.X.ShouldBe(37.5f, 0.001);
    }

    [Test]
    public void At_WithOnlyTwoSamples_IsLinear()
    {
        // The control: hermite needs three points, and the engine falls back rather than
        // fabricating one. Halfway between 0 and 100 is 50 - the value the test above is
        // deliberately not.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(100, Pose(0f, 0f, 0f));
        track.Add(200, Pose(100f, 0f, 0f));

        track.At(150)!.Value.X.ShouldBe(50f, 0.001);
    }

    [Test]
    public void At_WithUnevenlySpacedSamples_RenormalisesTheOldest()
    {
        // **TimeFixup_Hermite, and the reason it exists.** A hermite spline assumes evenly spaced
        // samples; demo updates are not, because the server sends when it sends. Valve rebuilds
        // the oldest sample at a uniform interval before splining - lerping prev towards start and
        // pretending it sits at start->changetime - dt1.
        //
        // Here p0 sits 200 ticks before p1 while p2 is only 100 after, so dt1/dt2 is 0.5 and the
        // fixup lerps p0 halfway towards p1: a synthetic sample of -50 at tick 100, in place of
        // the real -100 at tick 0.
        //
        // The spline then runs p0 = -50, p1 = 0, p2 = 100 at t = 0.5:
        //     0*0.5 + 100*0.5 + 50*0.125 + 100*(-0.125) = 43.75
        //
        // Feeding the raw -100 in instead gives 37.5, which is what the first prediction written
        // here assumed - so this test distinguishes the fixup from its absence rather than merely
        // distinguishing hermite from linear.
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/rocket.mdl");

        track.Add(0, Pose(-100f, 0f, 0f));
        track.Add(200, Pose(0f, 0f, 0f) with { Yaw = 1f });
        track.Add(300, Pose(100f, 0f, 0f) with { Yaw = 1f });

        track.At(250)!.Value.X.ShouldBe(43.75f, 0.001);
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

    [Test]
    public void EveryDiscreteFieldSurvivesInterpolation()
    {
        // **The test that was missing, and the bug it would have caught.** At rebuilds the pose
        // field by field, and m_nBody was simply not in the list — so between two keyframes the
        // body number reverted to the record's default of zero, and every capture point drew the
        // "?" sign while the demo, the model and the packer all measured correct.
        //
        // Asked BETWEEN keyframes deliberately. On a keyframe the earlier pose is returned whole
        // and every field is right by construction, so the one condition where a rebuilt pose can
        // differ from a copied one is the only condition that can fail. A test sampling on the
        // keyframe passes against the defect.
        //
        // Discrete rather than blended, all of them: there is no halfway between one sign and
        // another, and none between hidden and shown.
        ScenePropTrack track = new(entityIndex: 7, "models/effects/cappoint_hologram.mdl");

        ScenePose held = new()
        {
            Body = 3,
            Skin = 1,
            Sequence = 5,
            Hidden = true,
        };

        track.Add(0, held with { X = 0f });
        track.Add(10, held with { X = 100f });

        ScenePose? between = track.At(5d);

        between.ShouldNotBeNull();

        // The position must actually have moved, or the case is not between keyframes at all and
        // the assertions below are being made about a returned keyframe.
        between.Value.X.ShouldBeGreaterThan(0f);
        between.Value.X.ShouldBeLessThan(100f);

        between.Value.Body.ShouldBe(3, "the body number selects which alternative is drawn");
        between.Value.Skin.ShouldBe(1, "the skin family is how a team colour is carried");
        between.Value.Sequence.ShouldBe(5);
        between.Value.Hidden.ShouldBeTrue();

        // **Stated as the whole list, deliberately.** Two fields were lost from this rebuild in one
        // session — Body, then Skin — and both were found only when something looked wrong on
        // screen. The failure mode is silent by construction: a field left out takes the record's
        // default, and every default here is also a legitimate value.
        //
        // So the test asserts the pose survives WHOLE rather than field by field. A field added to
        // ScenePose and forgotten in At now fails this the moment it carries a non-default value,
        // instead of waiting for someone to notice a wrong picture.
        between.Value.ShouldBe(
            held with { X = between.Value.X },
            "every field except the interpolated position must survive interpolation");
    }

    private static ScenePose Pose(float x, float y, float z) =>
        new() { X = x, Y = y, Z = z };
}
