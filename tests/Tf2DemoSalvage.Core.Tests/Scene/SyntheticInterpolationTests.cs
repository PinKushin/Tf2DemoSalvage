using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Interpolation between keyframes, including the delay and the causality rule.
/// </summary>
/// <remarks>
/// **Converted from <c>DemoTimelinePropsTests</c>, whose assertion was a count.** "More than zero
/// player samples sat off a stated position" says the path was reached; it says nothing about
/// whether the position is right, and on found data there is nothing better available — nobody
/// knows where a player should be part-way between two packets.
///
/// A written demo makes it arithmetic. The engine draws <c>targettime = now - interp</c>, this
/// track uses a seven-tick delay, and a linear blend between two keyframes is a number that can be
/// worked out on paper before the test is run.
///
/// **The delay is why the first version of this fixture measured nothing.** Two keyframes a hundred
/// ticks apart never interpolate, because the later one is stated after the tick being asked for
/// and the causality rule refuses to be pulled toward an update that has not arrived. That looked
/// like broken interpolation and is the feature working; both branches are asserted below.
/// </remarks>
public sealed class SyntheticInterpolationTests
{
    private const float Interval = 1f / 66.67f;

    /// <summary>The track's fixed render delay, <c>InterpolationDelayTicks</c>.</summary>
    private const int Delay = 7;

    [Test]
    public void PlayersAt_BetweenTwoKeyframes_IsTheBlendTheDelayLandsOn()
    {
        // **The measurement that players go through the interpolator at all, and it measured zero
        // when first written — that was B47.** A player's model is not networked;
        // CTFPlayerClassShared::GetModelName resolves it locally from m_iClass, so a CTFPlayer
        // sends no m_nModelIndex, got no track, and PlayersAt silently fell back to the stated
        // frame position.
        //
        // The arithmetic, stated so a failure says which part is wrong: asking for tick 120 draws
        // tick 113, which sits three ticks into the ten-tick span from 110 to 120. The player
        // covers 100 units in that span, so it is 30 units past the 110 keyframe.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (100, 0f, 0f), (110, 100f, 0f), (120, 200f, 0f)));

        List<ScenePlayer> shown = [];
        timeline.PlayersAt(120.0, shown);

        shown.ShouldHaveSingleItem().X.ShouldBe(130f, 1f);
    }

    [Test]
    public void PlayersAt_AKeyframeStatedAfterTheTickAsked_DoesNotPullThePosition()
    {
        // **The causality rule, and it is the whole reason the delay exists.** A client at tick
        // 100 cannot be pulled toward an update stated at tick 200, because it has not received
        // one; a reader holding the whole demo can see it and would slide toward it for the entire
        // gap. That was B94 — a shutter drifting open on its own for ten seconds.
        //
        // Keyframes a hundred ticks apart, asked in the middle: the later one is in the future
        // relative to the question, so the earlier pose stands unchanged rather than blending.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (100, 0f, 0f), (200, 256f, 0f)));

        List<ScenePlayer> shown = [];
        timeline.PlayersAt(150.0, shown);

        shown.ShouldHaveSingleItem().X.ShouldBe(0f, 0.5f);
    }

    [Test]
    public void PlayersAt_BeforeTheDelayHasElapsed_ShowsTheFirstStatedPose()
    {
        // The third branch: the drawn moment is earlier than anything received, so the first
        // stated pose is all a client would have. Asked at tick 105, the target is 98 — before the
        // first keyframe at 100.
        //
        // Worth its own case because it returns the same value as a held pose would, so a test
        // that only checked "not interpolated" could not tell the two apart.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (100, 0f, 0f), (110, 100f, 0f), (120, 200f, 0f)));

        (100 - Delay).ShouldBeLessThan(105 - Delay, "the fixture must ask after the first keyframe");

        List<ScenePlayer> shown = [];
        timeline.PlayersAt(105.0, shown);

        shown.ShouldHaveSingleItem().X.ShouldBe(0f, 0.5f);
    }

    [Test]
    public void PlayersAt_TheBlendFraction_TracksWhereInTheSpanTheDelayLands()
    {
        // The control for the first test. A halfway sample is the one value an averaging bug and a
        // correct blend agree on, and 30% is not halfway — but a single fraction still cannot
        // distinguish "uses the delay" from "uses a different constant that happens to match here".
        //
        // Two spans of different lengths pin it: the delay is fixed in ticks, so a twenty-tick span
        // puts the drawn moment 35% along rather than 30%, and the distance covered scales with it.
        DemoTimeline wide = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (100, 0f, 0f), (120, 100f, 0f), (140, 200f, 0f)));

        List<ScenePlayer> shown = [];
        wide.PlayersAt(140.0, shown);

        // Target 133, thirteen ticks into the twenty-tick span from 120 to 140, over 100 units.
        shown.ShouldHaveSingleItem().X.ShouldBe(165f, 1f);
    }
}
