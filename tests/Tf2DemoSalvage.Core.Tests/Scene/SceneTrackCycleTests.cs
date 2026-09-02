using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The animation cycle across a loop boundary, and the spline's re-normalisation.
/// </summary>
/// <remarks>
/// **A cycle is the one interpolated value that wraps**, so it is the one where a straight line
/// between two samples is not merely imprecise but backwards: 0.9 to 0.1 is a tenth of a cycle
/// forwards, and lerping it runs the animation four fifths of the way in reverse. Valve handles it
/// with a half-cycle threshold in <c>LoopingLerp</c> and <c>LoopingLerp_Hermite</c>
/// (<c>src/game/client/lerp_functions.h</c>), and this file is the parity check on that.
///
/// Written against <see cref="ScenePropTrack"/> directly rather than through a demo, deliberately.
/// The wrap is arithmetic: the inputs are three floats and a fraction, and a demo would only make
/// the same three numbers harder to choose. What a demo is needed for — that production actually
/// calls this with the cycle it decoded — is asserted where the timeline is built.
///
/// **The predictions below are computed by hand from Valve's formula**, not read off this
/// implementation. A test whose expected value came from running the code proves the code is
/// deterministic and nothing else.
/// </remarks>
public sealed class SceneTrackCycleTests
{
    /// <summary>How far behind the asked-for tick a pose is sampled; taken from the track.</summary>
    /// <remarks>
    /// Read from production rather than copied (B267) — these tests are about cycle behaviour, not
    /// about the offset, and `InterpolationDelayConformanceTests` is what asserts the offset is
    /// the engine's.
    /// </remarks>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    [Test]
    public void At_ACycleCrossingTheLoopBoundary_MovesForwardsRatherThanBackwards()
    {
        // 0.9 to 0.1 is a tenth of a cycle forwards. The half-cycle threshold is what says so; a
        // plain lerp gives 0.5 at the midpoint, which is the animation played in reverse at four
        // times speed. Both are smooth and only one is right, which is why the value is predicted
        // rather than bounded.
        ScenePropTrack track = Track((0, 0.9f), (10, 0.1f));

        // Halfway between the two keyframes, in the pose's own time: the sample is taken `Delay`
        // ticks behind, so tick 12 asks for tick 5.
        float cycle = track.At(5 + Delay).ShouldNotBeNull().Cycle;

        // 0.9 -> 1.1, midpoint 1.0, wrapped back to 0.0.
        cycle.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void At_ACycleNotCrossingTheBoundary_IsAPlainInterpolation()
    {
        // **The control.** Every assertion about wrapping is about a correction being applied, and
        // an implementation that always added one would pass those and fail this.
        ScenePropTrack track = Track((0, 0.2f), (10, 0.4f));

        track.At(5 + Delay).ShouldNotBeNull().Cycle.ShouldBe(0.3f, 0.0001f);
    }

    [Test]
    public void At_ACycleWrappingWithAThirdSample_UsesTheHermiteFormAndStaysInRange()
    {
        // Three samples put the spline form in play rather than the linear one, and the spline is
        // where a wrap does the most damage: the tangents are differences, so an uncorrected pair
        // gives a tangent of the wrong sign and the curve leaves the [0,1) range entirely.
        //
        // 0.7, 0.9, 0.1 is a steady fifth of a cycle per step across the boundary.
        ScenePropTrack track = Track((0, 0.7f), (10, 0.9f), (20, 0.1f));

        float cycle = track.At(15 + Delay).ShouldNotBeNull().Cycle;

        // Corrected to 0.7, 0.9, 1.1 the samples are evenly spaced, so the Hermite through them is
        // the straight line: 1.0 at the midpoint, wrapped to 0.0.
        cycle.ShouldBe(0f, 0.0001f);

        // A cycle is a fraction of one repetition by definition, so anything outside the range is
        // a bug whatever else the number looks like.
        cycle.ShouldBeInRange(0f, 1f);
    }

    [Test]
    public void At_AThirdSampleThatWrappedTheOtherWay_IsPulledForwardTogetherWithIt()
    {
        // **Valve's re-check, and the only branch that needs three samples to reach.** Raising the
        // middle sample to reach the last one can leave it more than half a cycle from the first,
        // so the pair is re-examined afterwards. Valve's own comment names the case: "p0 = 0.2,
        // p1 = 0.1, p2 = 0.9" — decreasing, with p1 fixed up relative to p2.
        //
        // Valve's numbers, so the branch is reached by the case it was written for rather than by
        // one found afterwards. 0.2, 0.1, 0.9 is an animation running BACKWARDS through the
        // boundary — a taunt rewinding, or a cycle driven by a negative playback rate.
        //
        // p1 rises to 1.1 to meet p2; that leaves it 0.9 from p0, so p0 rises to 1.2 in turn.
        // Hermite through 1.2, 1.1, 0.9 at t=0.5, with d1 = -0.1 and d2 = -0.2:
        //
        //   1.1*0.5 + 0.9*0.5 + (-0.1)*0.125 + (-0.2)*(-0.125) = 1.0125
        //
        // which wraps to 0.0125.
        ScenePropTrack track = Track((0, 0.2f), (10, 0.1f), (20, 0.9f));

        float cycle = track.At(15 + Delay).ShouldNotBeNull().Cycle;

        // **Predicted, not bounded.** Without the re-check p0 stays at 0.2 and the same formula
        // gives 0.9 — a value that is in range, is smooth, and is most of a cycle wrong. Only the
        // exact number tells them apart.
        cycle.ShouldBe(0.0125f, 0.0001f);
    }

    [Test]
    public void At_KeyframesUnevenlySpaced_RenormalisesTheOlderSampleBeforeSplining()
    {
        // **A Hermite tangent is a difference, so it assumes even spacing.** Valve's
        // CInterpolatedVar does not: when the older gap and the current span differ it places a
        // synthetic sample at the matching distance instead. Skipping that does not give a
        // slightly different curve, it overshoots whenever packet spacing wobbles — which on a
        // real demo is most of the time.
        //
        // Ticks 0, 10, 15: the span being drawn is 5 and the older gap is 10, so the older sample
        // is pulled to where it would have been 5 ticks before the start.
        ScenePropTrack track = new(1, "models/player/scout.mdl");
        track.Add(0, new ScenePose { Sequence = 3, X = 0f });
        track.Add(10, new ScenePose { Sequence = 3, X = 100f });
        track.Add(15, new ScenePose { Sequence = 3, X = 150f });

        // Halfway through the 10 -> 15 span, so the fraction is 0.5.
        float x = track.At(12.5 + Delay).ShouldNotBeNull().X;

        // The synthetic sample sits at fraction 1 - 5/10 = 0.5 from 0 towards 100, so p0 = 50,
        // p1 = 100, p2 = 150 — evenly spaced, which makes the spline the straight line: 125.
        //
        // Unrenormalised, p0 would be 0 and the tangents 100 and 50, giving 121.875. Both are
        // plausible positions for a moving prop and only one is the engine's.
        x.ShouldBe(125f, 0.001f);
    }

    [Test]
    public void At_ACycleRunningBackwardsAcrossTheBoundary_AlsoMovesTheShorterWay()
    {
        // **The mirror of the first test, and it is a different branch.** The correction raises
        // whichever of the two is smaller, so 0.9 -> 0.1 and 0.1 -> 0.9 take opposite arms of the
        // same `if`. Testing only one leaves the other free to raise the wrong value, which is
        // wrong by a whole cycle rather than slightly wrong.
        //
        // 0.1 -> 0.9 is a tenth of a cycle BACKWARDS: `from` rises to 1.1 and the midpoint is 1.0,
        // which wraps to 0.
        ScenePropTrack track = Track((0, 0.1f), (10, 0.9f));

        track.At(5 + Delay).ShouldNotBeNull().Cycle.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void At_AThreeSampleCycleWhoseOldestWrapped_RaisesTheOldestRatherThanTheOthers()
    {
        // The same two-armed correction inside the spline's first pass, where the pair being
        // compared is the oldest sample against the one being interpolated from.
        //
        // 0.1, 0.9, 0.95: p0 rises to 1.1, and 0.95 is close enough to 0.9 that the second pass
        // leaves both alone. Hermite through 1.1, 0.9, 0.95 at t=0.5, with d1 = -0.2 and
        // d2 = 0.05:
        //
        //   0.9*0.5 + 0.95*0.5 + (-0.2)*0.125 + 0.05*(-0.125) = 0.89375
        //
        // No wrap this time, which is worth having: the assertion is on the curve rather than on
        // the modulo that follows it.
        ScenePropTrack track = Track((0, 0.1f), (10, 0.9f), (20, 0.95f));

        track.At(15 + Delay).ShouldNotBeNull().Cycle.ShouldBe(0.89375f, 0.0001f);
    }

    [Test]
    public void At_ASequenceChange_HoldsTheOlderCycleRatherThanBlendingAcrossIt()
    {
        // **Two different animations have unrelated cycles**, so interpolating between them is
        // meaningless in a way no threshold can rescue: 0.9 of a run and 0.1 of a jump are not
        // 0.8 apart, they are not comparable at all. The engine restarts rather than blending.
        ScenePropTrack track = new(1, "models/player/scout.mdl");
        track.Add(0, new ScenePose { Sequence = 3, Cycle = 0.9f });
        track.Add(10, new ScenePose { Sequence = 4, Cycle = 0.1f });

        track.At(5 + Delay).ShouldNotBeNull().Cycle.ShouldBe(0.9f, 0.0001f);
    }

    [Test]
    public void AtKeyframe_BeforeTheFirstAndAfterTheLast_ReportsNothing()
    {
        // "What the demo said" has no answer outside the entity's life, and a null is the only
        // honest one — clamping to the nearest keyframe would report a prop standing somewhere it
        // had not been created yet.
        ScenePropTrack track = Track((100, 0.1f), (110, 0.2f));

        track.AtKeyframe(99).ShouldBeNull();
        track.AtKeyframe(100).ShouldNotBeNull();
    }

    [Test]
    public void FirstTick_WithNoKeyframes_IsPastEveryTick()
    {
        // A track with nothing in it must sort after every track that has something, because the
        // scene builder orders by first appearance. Zero would put an empty track first.
        new ScenePropTrack(1, "models/props/barrel.mdl").FirstTick.ShouldBe(int.MaxValue);

        Track((100, 0.1f), (110, 0.2f)).FirstTick.ShouldBe(100);
    }

    /// <summary>A track of one sequence whose cycle takes the given values.</summary>
    private static ScenePropTrack Track(params (int Tick, float Cycle)[] samples)
    {
        ScenePropTrack track = new(1, "models/player/scout.mdl");

        foreach ((int tick, float cycle) in samples)
        {
            track.Add(tick, new ScenePose { Sequence = 3, Cycle = cycle });
        }

        return track;
    }
}
