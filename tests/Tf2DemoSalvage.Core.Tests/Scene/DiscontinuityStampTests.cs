using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The timeline stamps WHEN an entity jumped, from the no-interp parity (B346).
/// </summary>
/// <remarks>
/// **The same shape as <see cref="SequenceParityConformanceTests"/> and for the same reason.** The
/// wire carries a counter whose value means nothing; the timeline turns the CHANGE into a time, and
/// everything downstream compares times rather than re-deriving the signal.
///
/// **They are separate signals and this suite's controls are what say so.** A sequence parity
/// creates a transition; a no-interp parity destroys the queue
/// (<c>sequence_Transitioner.cpp:41</c>). Folding them would make every teleport restart the
/// animation, which the engine does not do — <c>IncrementInterpolationFrame</c> touches no sequence
/// state at all (<c>baseentity.cpp:8471</c>).
/// </remarks>
public sealed class DiscontinuityStampTests
{
    /// <summary>Entity slot the prop occupies.</summary>
    private const int Prop = 9;

    [Test]
    public void Build_APropWhoseNoInterpParityChanges_StampsWhenItJumped()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0),
            (Tick: 660, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 1)));

        Jumped(Single(timeline), at: 660).ShouldBeGreaterThan(
            0d,
            "the entity was teleported at 660, so the stamp is when the server said so");
    }

    /// <remarks>
    /// **The control, and it is the one that matters most here.** The parity spends nearly all of a
    /// match unchanged — 12,830 of 13,261 sends measured at zero — so a reader that stamped on
    /// every update would report every entity as having just jumped, and the queue would never
    /// hold a transition at all.
    /// </remarks>
    [Test]
    public void Build_APropWhoseNoInterpParityNeverChanges_NeverStamps()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 2),
            (Tick: 660, Sequence: 1, Parity: 1, FrameReset: 0, NoInterp: 2)));

        Last(Single(timeline)).ShouldBe(
            0d,
            "nothing jumped, so there is no discontinuity to report");
    }

    /// <remarks>
    /// **The FIRST value seen is not a change.** An entity entering the world already carries
    /// whatever parity it had, and treating that as a jump would clear the queue for every entity
    /// on the frame it appears — which is the same mistake as reading the value instead of the
    /// change, one hop earlier.
    ///
    /// **The entity enters at tick 600 and not at zero, and that is the whole test.** Written with
    /// its first frame at tick 0 this assertion CANNOT FAIL: the stamp is `tick * interval`, so a
    /// reader that wrongly counted the first sighting as a jump would write `0 * interval` — zero,
    /// bit-identical to "never jumped". Found by sabotage, which is exactly the case the
    /// `{Wrong condition}` entry in `CLAUDE.md` describes: an input for which correct and broken
    /// predict the same observation. The fix is the input, not the assertion.
    /// </remarks>
    [Test]
    public void Build_APropEnteringWithANonZeroParity_DoesNotCountAsAJump()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 600, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 3),
            (Tick: 1260, Sequence: 1, Parity: 1, FrameReset: 0, NoInterp: 3)));

        Last(Single(timeline)).ShouldBe(
            0d,
            "three was the value it arrived with, not a value it changed to");
    }

    /// <remarks>
    /// **It WRAPS, so a change back to zero is still a change** —
    /// `(m_ubInterpolationFrame + 1) % NOINTERP_PARITY_MAX` (<c>baseentity.cpp:8473</c>). A reader
    /// treating zero as "absent" would miss every fourth teleport, which is the failure that looks
    /// like an intermittent bug rather than a wrong rule.
    /// </remarks>
    [Test]
    public void Build_APropWhoseParityWrapsBackToZero_StillCountsAsAJump()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 3),
            (Tick: 660, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0)));

        Jumped(Single(timeline), at: 660).ShouldBeGreaterThan(
            0d,
            "three wrapping to zero is the fourth teleport, not a return to a resting value");
    }

    /// <remarks>
    /// **The two signals are independent, asserted in the direction that would hide a coupling.**
    /// A sequence restart must NOT stamp a discontinuity: if it did, every cabinet opening would
    /// clear its own transition queue and no sequence would ever cross-fade.
    /// </remarks>
    [Test]
    public void Build_APropWhoseSequenceRestarts_DoesNotStampADiscontinuity()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0),
            (Tick: 660, Sequence: 1, Parity: 1, FrameReset: 1, NoInterp: 0)));

        Last(Single(timeline)).ShouldBe(
            0d,
            "the sequence restarted, which is a different event from the entity jumping");
    }

    /// <summary>The discontinuity stamp on the track's final keyframe.</summary>
    private static double Last(ScenePropTrack track) =>
        track.Keyframes[^1].Pose.DiscontinuitySeconds;

    /// <summary>The discontinuity stamp showing at that tick.</summary>
    private static double Jumped(ScenePropTrack track, int at)
    {
        foreach ((int Tick, ScenePose Pose) frame in track.Keyframes)
        {
            if (frame.Tick == at)
            {
                return frame.Pose.DiscontinuitySeconds;
            }
        }

        throw new InvalidOperationException($"the fixture produced no keyframe at {at}");
    }

    private static ScenePropTrack Single(DemoTimeline timeline)
    {
        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.EntityIndex == Prop)
            {
                return track;
            }
        }

        throw new InvalidOperationException("the fixture produced no prop track");
    }
}
