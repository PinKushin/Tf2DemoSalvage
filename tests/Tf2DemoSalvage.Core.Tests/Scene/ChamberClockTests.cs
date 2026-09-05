using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The chamber's clock is stamped on the TRANSITION, not while it differs (B348).
/// </summary>
/// <remarks>
/// **`OnDataChanged` carries a remembered flag, and that flag is the whole mechanism**
/// (<c>tf_weapon_grenadelauncher.cpp:626</c>):
///
/// <code>
///   if ( m_bCurrentAndGoalTubeEqual &amp;&amp; m_iCurrentTube != m_iGoalTube )
///       m_flBarrelRotateBeginTime = gpGlobals->curtime;
///
///   m_bCurrentAndGoalTubeEqual = ( m_iCurrentTube == m_iGoalTube );
/// </code>
///
/// **Dropping the first half is the plausible mistake**, and it is the one this suite exists to
/// catch: stamping whenever the tubes differ restarts the animation on every packet that still
/// shows them apart, so the chamber never gets past the first few degrees of a 0.2666-second swing
/// and reads as almost stationary. Nothing about the pose or the bone would look wrong.
/// </remarks>
public sealed class ChamberClockTests
{
    /// <summary>Entity slot the prop occupies.</summary>
    private const int Prop = 9;

    [Test]
    public void Build_WhenTheGoalTubeFirstDiffers_StampsThatMoment()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 0),
            (Tick: 660, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 1)));

        Started(timeline, at: 660).ShouldBeGreaterThan(
            0d,
            "the rotation began when the goal changed, not when the recording opened");
    }

    /// <remarks>
    /// **The control that makes the test above about the TRANSITION.** Three snapshots, all with
    /// the tubes already apart: the clock must keep the first moment rather than being rewritten by
    /// the two that follow. Without the remembered flag every one of them restamps.
    /// </remarks>
    [Test]
    public void Build_WhileTheTubesStayApart_KeepsTheFirstStamp()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 0),
            (Tick: 660, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 1),
            (Tick: 1320, Sequence: 1, Parity: 1, FrameReset: 0, NoInterp: 0, GoalTube: 1)));

        double first = Started(timeline, at: 660);

        // **Non-zero FIRST, because equality alone passes when nothing is stamped at all.** With
        // the assignment deleted both reads are zero and `ShouldBe` holds vacuously — found by
        // sabotage, and it is the difference between catching a REWRITE and catching an absence.
        first.ShouldBeGreaterThan(0d, "the clock was stamped when the goal first differed");

        Started(timeline, at: 1320).ShouldBe(
            first, "the chamber is still turning from the same moment it set off");
    }

    /// <remarks>
    /// **A chamber that never turns has no clock**, which is what nearly every launcher in a demo
    /// reports — and zero is what an unstamped `double` also holds, so this asserts the quiet case
    /// rather than assuming it.
    /// </remarks>
    [Test]
    public void Build_WhenTheTubesNeverDiffer_NeverStamps()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 0),
            (Tick: 660, Sequence: 1, Parity: 1, FrameReset: 0, NoInterp: 0, GoalTube: 0)));

        Started(timeline, at: 660).ShouldBe(0d);
    }

    /// <remarks>
    /// **Both tubes reach the pose**, not just the clock — the base angle comes from the current
    /// one and the goal is what says an animation is running at all.
    /// </remarks>
    [Test]
    public void Build_ATurningChamber_CarriesBothTubes()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            clientSideAnimation: false,
            (Tick: 0, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 0),
            (Tick: 660, Sequence: 0, Parity: 0, FrameReset: 0, NoInterp: 0, GoalTube: 4)));

        (int Current, int Goal, double StartedSeconds) chamber =
            Chamber(timeline, at: 660).ShouldNotBeNull();

        chamber.Current.ShouldBe(0);
        chamber.Goal.ShouldBe(4);
    }

    /// <summary>When the chamber showing at that tick began turning.</summary>
    private static double Started(DemoTimeline timeline, int at) =>
        Chamber(timeline, at).ShouldNotBeNull().StartedSeconds;

    /// <summary>The chamber the pose at that tick carries.</summary>
    private static (int Current, int Goal, double StartedSeconds)? Chamber(
        DemoTimeline timeline, int at)
    {
        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.EntityIndex != Prop)
            {
                continue;
            }

            foreach ((int Tick, ScenePose Pose) frame in track.Keyframes)
            {
                if (frame.Tick == at)
                {
                    return frame.Pose.Chamber;
                }
            }
        }

        throw new InvalidOperationException($"the fixture produced no keyframe at {at}");
    }
}
