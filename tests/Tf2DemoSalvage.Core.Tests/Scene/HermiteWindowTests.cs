using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That a spline is only used over samples close enough together to justify one.
/// </summary>
/// <remarks>
/// **B94, and the symptom was a door sinking through the floor.** A shutter on cp_process rests
/// closed at Z 640 and rises 145 units to open. Watched in the viewer it drifted upward slowly for
/// no apparent reason, opened correctly when triggered, and then on closing DESCENDED PAST its
/// resting height into the ground — where it also drew black, because a model whose illumination
/// point is inside solid geometry samples no ambient light.
///
/// **A cubic through three points leaves the range of those points.** That is what hermite is for
/// and it is fine over samples milliseconds apart; over samples seconds apart the overshoot is
/// enormous. A door's origin is a step function on the wire — it is sent while moving and not at
/// all while still — so the three nearest samples to a moving door routinely straddle a long
/// stationary stretch.
///
/// **The engine never sees that shape, and this project did.** `CInterpolatedVar` keeps a history
/// trimmed to the interpolation window and clamps its fraction (`pInfo->frac = MIN(frac, 2.0f)`),
/// so its three samples are always recent; hermite additionally requires a valid third entry with
/// `dt2 > 0.0001f`, and `INTERPOLATE_LINEAR_ONLY` disables it for variables where overshoot cannot
/// be tolerated. Our keyframe list is the whole demo, so "the third sample" could be from any point
/// in the recording.
///
/// Valve's own comment on the fixup says the quiet part: without renormalising, a spline
/// "overshoots whenever the packet spacing wobbles". Renormalising evens the spacing; it does not
/// make a multi-second span appropriate for a spline.
/// </remarks>
public sealed class HermiteWindowTests
{
    private static ScenePose At(float z) => new() { Z = z, Scale = 1f };

    /// <summary>A door: stationary for a long time, then a step to its open height.</summary>
    /// <remarks>
    /// The two stationary samples are far apart, which is what a demo actually contains — nothing
    /// is sent while a door sits still. The step then arrives ten ticks after the last of them.
    /// </remarks>
    private static ScenePropTrack Door()
    {
        ScenePropTrack track = new(entityIndex: 40, modelPath: "*132");

        track.Add(0, At(640f));
        track.Add(600, At(640f));
        track.Add(610, At(785f));

        return track;
    }

    [Test]
    public void ADoorStepping_NeverGoesBelowItsRestingHeight()
    {
        ScenePropTrack door = Door();

        // Across the whole step. Hermite over samples 0, 600 and 610 undershoots here; the engine,
        // whose history would hold none of tick 0 by then, interpolates linearly and cannot.
        for (double tick = 600; tick <= 610; tick += 0.25)
        {
            ScenePose pose = door.At(tick).ShouldNotBeNull();

            pose.Z.ShouldBeGreaterThanOrEqualTo(
                640f - 0.01f,
                $"tick {tick} put the door at {pose.Z:0.###}, below its resting height");
        }
    }

    [Test]
    public void ADoorStepping_NeverGoesAboveItsOpenHeight()
    {
        // The other side of the same overshoot, and the control: an implementation that clamped only
        // the bottom would satisfy the test above while still flying the door through the ceiling.
        ScenePropTrack door = Door();

        for (double tick = 600; tick <= 610; tick += 0.25)
        {
            ScenePose pose = door.At(tick).ShouldNotBeNull();

            pose.Z.ShouldBeLessThanOrEqualTo(
                785f + 0.01f,
                $"tick {tick} put the door at {pose.Z:0.###}, above its open height");
        }
    }

    [Test]
    public void HermiteWindow_AStationaryDoor_DoesNotDrift()
    {
        // **The other half of what was seen: a slow rise "for no reason".** Between two samples
        // holding the same value the pose must hold too. Linear interpolation between equal values
        // is flat; a spline reaching forward to a third sample is not, so this fails for the same
        // cause as the tests above and at a place nobody would think to look.
        ScenePropTrack door = Door();

        for (double tick = 100; tick <= 500; tick += 25)
        {
            ScenePose pose = door.At(tick).ShouldNotBeNull();

            pose.Z.ShouldBe(640f, 0.01f, $"tick {tick} drifted to {pose.Z:0.###}");
        }
    }

    [Test]
    public void AGapWithNoRestatement_HoldsRatherThanSliding()
    {
        // **The case the first fix missed, and the one a real demo actually contains.** Delta
        // compression means a stationary entity sends NOTHING, so there is no repeated pose to
        // collapse and nothing to record a hold with. Recording the last restatement therefore did
        // not help here at all: the owner watched the shutter still drift after that change.
        //
        // Two keyframes, 610 ticks apart, no repeats between them — exactly what the wire carries
        // for a door that opens once. A live client cannot slide toward the second because it has
        // not arrived yet; this timeline can see it, and did.
        ScenePropTrack door = new(entityIndex: 42, modelPath: "*139");

        door.Add(0, At(584f));
        door.Add(610, At(728f));

        // Most of the way through the gap, the door has not been told to move.
        door.At(100).ShouldNotBeNull().Z.ShouldBe(584f, 0.01f);
        door.At(300).ShouldNotBeNull().Z.ShouldBe(584f, 0.01f);
        door.At(600).ShouldNotBeNull().Z.ShouldBe(584f, 0.01f);

        // And it does arrive: the later keyframe is not discarded, only deferred by the
        // interpolation delay. At tick 610 the client is drawing tick 603, which is 98.8% of the way
        // through a gap whose earlier end is ancient — so it is nearly there — and it lands exactly
        // on the new value once the delay has passed.
        //
        // That near-jump IS the engine: its history holds the same two entries, and a frac of 0.988
        // is what `(targettime - older) / (newer - older)` gives. A real door never reaches this
        // shape, because a moving entity is updated every tick and its gaps are one.
        door.At(610).ShouldNotBeNull().Z.ShouldBe(726.35f, 0.1f);
        door.At(617).ShouldNotBeNull().Z.ShouldBe(728f, 0.01f);
    }

    [Test]
    public void HermiteWindow_AClosingDoor_DoesNotUndershootPastShut()
    {
        // **The other half of what was seen: it now stops at closed for a moment and then sinks
        // into the floor.** A demo states no pose below the closed height — measured across every
        // brush track on cp_process — so nothing but the interpolation can produce one.
        //
        // A door travels at a constant speed and then stops dead. That makes the last two spans
        // very different: -15 units, then 0. A cubic fitted through them carries the incoming
        // velocity past the final sample before turning round, which puts the door below shut. It is
        // the same overshoot Valve's own comment warns about, and the reason the engine exposes
        // INTERPOLATE_LINEAR_ONLY for values that cannot tolerate it.
        ScenePropTrack door = new(entityIndex: 43, modelPath: "*132");

        int tick = 0;

        // Constant-speed close, one update per tick, exactly as the wire carries it.
        for (float z = 728f; z > 584f; z -= 16f)
        {
            door.Add(tick++, At(z));
        }

        door.Add(tick, At(584f));

        // Then it sits shut. Repeats collapse, so this is one keyframe and a hold.
        door.Add(tick + 200, At(584f));

        for (double at = 0; at <= tick + 200; at += 0.25)
        {
            ScenePose pose = door.At(at).ShouldNotBeNull();

            pose.Z.ShouldBeGreaterThanOrEqualTo(
                584f - 0.01f,
                $"tick {at} put the closing door at {pose.Z:0.###}, below shut");
        }
    }

    [Test]
    public void CloselySpacedSamples_StillGetTheirSpline()
    {
        // **The control for the fix, and the reason it is a window rather than a deletion.** Hermite
        // is what makes a rocket fly a curve instead of a polyline, and samples a few ticks apart are
        // exactly what it is for. A fix that disabled it everywhere would pass every test above and
        // silently undo the interpolation work it sits in.
        ScenePropTrack rocket = new(entityIndex: 41, modelPath: "models/weapons/w_rocket.mdl");

        rocket.Add(0, At(0f));
        rocket.Add(2, At(100f));
        rocket.Add(4, At(400f));

        // Sampled inside the second span, where a spline and a straight line disagree: linear gives
        // 250 at the midpoint, and the curve through an accelerating third sample does not.
        ScenePose pose = rocket.At(3.0).ShouldNotBeNull();

        Math.Abs(pose.Z - 250f).ShouldBeGreaterThan(
            0.5f, $"the spline was not applied: {pose.Z:0.###} is the straight line");
    }
}
