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

    /// <summary>The track's render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <summary><c>EF_NODRAW</c>, bit 5 of <c>m_fEffects</c>.</summary>
    private const int NoDraw = 0x020;

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

        // `120 - Delay` ticks into the ten-tick span from 110 to 120, over 100 units (B267).
        shown.ShouldHaveSingleItem().X.ShouldBe(100f + (100f * ((10 - Delay) / 10f)), 1f);
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
    public void PropsAt_AnEntityHiddenByNoDraw_IsNotDrawnAndComesBackWhenTheBitClears()
    {
        // **The check that EF_NODRAW actually arrives.** A taken health pack is hidden rather than
        // deleted because it respawns, and the fix for that reads one bit of m_fEffects. If the
        // property never reaches the decoder the fix is a no-op that looks identical to working —
        // markers on the floor either way — which is why the corpus version could only count how
        // many tracks were hidden somewhere.
        //
        // The bit is set on one snapshot and cleared on the next, so all three states are checked
        // against a known answer. The coming back is the half that distinguishes a pickup from a
        // destroyed entity: a track hidden from some point onwards could be something that was
        // deleted, which proves nothing about respawning.
        //
        // **Each state is asked for ten ticks AFTER it was stated, because of the render delay.**
        // A track draws `now - 7`, so asking on the tick a change was stated draws the tick before
        // it and reports the previous state. That is not a quirk of this fixture — it is what a
        // client shows, and asking at the stated tick is what made the first version of this test
        // report a visible pickup on the tick it was taken.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEffects(
            Interval, (100, 0), (200, NoDraw), (300, 0)));

        Hidden(timeline, tick: 110).ShouldBeFalse("the pickup starts visible");
        Hidden(timeline, tick: 210).ShouldBeTrue("EF_NODRAW did not reach the timeline");
        Hidden(timeline, tick: 310).ShouldBeFalse("the pickup never came back");
    }

    [Test]
    public void PropsAt_EffectsWithoutNoDraw_DoNotHideTheEntity()
    {
        // EF_NODRAW is one bit of m_fEffects and its neighbours are ordinary things an entity
        // sets — EF_BONEMERGE is 0x001 and EF_NOSHADOW 0x010. A reader testing the field for
        // non-zero rather than masking hides every entity that sets any of them, which on a real
        // demo is most of the cosmetics.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEffects(
            Interval, (100, 0x001 | 0x010), (200, 0x001 | 0x010)));

        Hidden(timeline, tick: 210).ShouldBeFalse(
            "an effects value without EF_NODRAW must not hide the entity");
    }

    /// <summary>Whether the single prop track is withheld from the drawn set at a tick.</summary>
    /// <remarks>
    /// **Absence from what <c>PropsAt</c> returns, not the flag on the pose it returns**, and the
    /// difference is the whole sensitivity of these tests. This read
    /// <c>props.Count == 0 || props[0].Pose.Hidden</c> until 2026-08-21, which passes identically
    /// against a <c>PropsAt</c> that has stopped filtering at all: the prop comes back, its pose
    /// still carries <c>Hidden</c>, and the helper reports it as hidden. Measured by deleting the
    /// filter — this file stayed green while a corpus test went red.
    ///
    /// Wrong instrument, in the sense of CLAUDE.md's four routes to a test that cannot fail: the
    /// variable is whether the renderer is handed the entity, and the proxy was a field on the
    /// thing it was handed. That gap is what allowed a working feature to be filed as broken
    /// (B133) — nothing in the suite could tell the two states apart.
    ///
    /// An absent prop still counts as hidden, which is what makes a fixture producing no track at
    /// all fail rather than pass the "is hidden" case for the wrong reason — the failure that led
    /// to the origin-table fix in <c>SyntheticPlayer.SchemaWithProp</c>. The visible cases are the
    /// control: they require the prop to be present, so a track that never existed reddens them.
    /// </remarks>
    private static bool Hidden(DemoTimeline timeline, int tick)
    {
        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        return props.Count == 0;
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

        // The drawn moment is `140 - Delay`, that many ticks into the twenty-tick span from 120 to
        // 140, over 100 units — derived rather than written out, because a literal here is a second
        // home for the delay and it had one (B267).
        shown.ShouldHaveSingleItem().X.ShouldBe(100f + (100f * ((20 - Delay) / 20f)), 1f);
    }
}
