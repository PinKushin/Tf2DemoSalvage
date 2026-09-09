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
/// **What the engine actually refuses is a sample it has not RECEIVED, and nothing else** — which is
/// most of this file, and was worth the fix. A client's history contains only arrived entries, so it
/// cannot slide toward an update that has not been sent; this reader holds the whole recording and
/// could. That bound is now stored per entry and applied to the search
/// (<see cref="InterpolatedHistory"/>).
///
/// **The belief this file was written on was that the engine also refuses a sample that is merely OLD,
/// and reading `RemoveEntriesPreviousTo` kills it** (<c>interpolatedvar.h:782</c>). It keeps
/// `Truncate( i + 3 )` — the first entry past the cutoff PLUS TWO MORE — so a history pruned at a quiet
/// moment still holds two arbitrarily old entries, and the spline will use them. The wrong conclusion is
/// kept here deliberately, because it produced a real symptom patch: a window that refused hermite over
/// a long span, which is this project's rule and not Valve's.
///
/// Valve's own comment on the fixup says the quiet part: without renormalising, a spline
/// "overshoots whenever the packet spacing wobbles". Renormalising evens the spacing; it does not
/// stop the curve leaving the range of its samples, and `INTERPOLATE_LINEAR_ONLY` — the one switch that
/// would — is set on exactly one variable in the whole client, `m_viewtarget`
/// (<c>c_baseflex.cpp:133</c>). Not the origin.
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
        // interpolation delay. At tick 610 the client is drawing `610 - delay`, nearly the whole
        // way through a gap whose earlier end is ancient — so it is almost there — and it lands
        // exactly on the new value once the delay has passed.
        //
        // That near-jump IS the engine: its history holds the same two entries, and the fraction is
        // what `(targettime - older) / (newer - older)` gives. A real door never reaches this
        // shape, because a moving entity is updated every tick and its gaps are one.
        //
        // **Derived from the delay rather than written out** (B267). This read 726.35 with a
        // comment saying "drawing tick 603", both of which encoded a seven-tick delay; the engine's
        // is eight (`GetInterpolationAmount` adds `serverTickMultiple` after the rounding), so the
        // literal was a second place the old value lived.
        int delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

        float almost = 584f + ((728f - 584f) * ((610f - delay) / 610f));

        door.At(610).ShouldNotBeNull().Z.ShouldBe(almost, 0.1f);
        door.At(610 + delay).ShouldNotBeNull().Z.ShouldBe(728f, 0.01f);
    }

    /// <remarks>
    /// **The engine undershoots here, and this test says by how much.** It was written asserting that a
    /// closing door never dips below shut, on the belief that Valve's pruning keeps the spline's three
    /// samples close together. It does not: `RemoveEntriesPreviousTo` keeps `Truncate( i + 3 )`
    /// (<c>interpolatedvar.h:782</c>), so the two entries past the cutoff survive however old they are.
    ///
    /// **The arithmetic, all of it, because a predicted value is the only assertion worth making here.**
    /// At tick 209 the client draws <c>targettime = 209 - 8 = 201</c>. Its history holds the
    /// just-arrived entry at changetime 209 and, from the last prune, changetimes 9, 8 and 7. So
    /// `GetInterpolationInfo` gives <c>older = 9</c>, <c>newer = 209</c>, <c>oldest = 8</c>, and sets
    /// `m_bHermite` because <c>dt2 = 9 - 8 = 1 &gt; 0.0001</c> (<c>:851</c>).
    ///
    /// `TimeFixup2_Hermite` then respaces the oldest with <c>dt1 = 209 - 9 = 200</c>
    /// (<c>:1372</c>): <c>frac = 200 / 1 = 200</c>, and
    /// <c>Lerp( 1 - 200, 600, 584 ) = 600 + (-199)(584 - 600) = 3784</c>. That is an extrapolation two
    /// hundred ticks into the past, and it is what the engine feeds the curve.
    ///
    /// `Lerp_Hermite( 0.96, 3784, 584, 584 )` with <c>d1 = -3200</c> and <c>d2 = 0</c>:
    ///
    /// <code>
    /// 584 * (2t³-3t²+1)  = 584 * 0.004672 =    2.728
    /// 584 * (-2t³+3t²)   = 584 * 0.995328 =  581.272
    /// -3200 * (t³-2t²+t) = -3200 * 0.001536 = -4.915
    /// </code>
    ///
    /// — **579.085**, five units below shut. Predicted from the engine's own three functions before the
    /// value was read back, which is what makes it an experiment rather than a description.
    ///
    /// **What is NOT established:** whether a real recording contains this shape. It needs a
    /// restatement two hundred ticks after a door stops with nothing in between, and a `func_door` that
    /// has stopped also stops simulating, so its updates stop entirely. The measurement that would
    /// settle it is `jitter` on a match demo, and it is B370's, not this test's.
    /// </remarks>
    [Test]
    public void HermiteWindow_AClosingDoor_UndershootsExactlyAsTheEngineDoes()
    {
        ScenePropTrack door = new(entityIndex: 43, modelPath: "*132");

        int tick = 0;

        // Constant-speed close, one update per tick, exactly as the wire carries it.
        for (float z = 728f; z > 584f; z -= 16f)
        {
            door.Add(tick++, At(z));
        }

        door.Add(tick, At(584f));

        // Then it is restated, two hundred ticks later and unchanged. `AddToHead` is unconditional, so
        // this is a second history entry carrying the same height at a different changetime.
        door.Add(tick + 200, At(584f));

        // **Only the moment the restatement lands can reach past shut, and this pins both halves.**
        // Every earlier sample is bounded by what had arrived, so the deep sample is the last one.
        double deepest = 584f;

        for (double at = 0; at < tick + 200; at += 0.25)
        {
            ScenePose pose = door.At(at).ShouldNotBeNull();

            pose.Z.ShouldBeGreaterThanOrEqualTo(
                584f - 0.01f,
                $"tick {at} put the closing door at {pose.Z:0.###}: before the restatement arrives, " +
                "no entry the client holds is below shut");

            deepest = Math.Min(deepest, pose.Z);
        }

        deepest.ShouldBe(584f, 0.01f);

        door.At(tick + 200).ShouldNotBeNull().Z.ShouldBe(
            579.085f, 0.01f, "the value Valve's three functions predict for this history");
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
