using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A rocket's drawn position end to end, through the full decode-and-interpolate pipeline,
/// checked against Valve's own arithmetic (B397).
/// </summary>
/// <remarks>
/// **Written because three specific hypotheses for B397 were each checked and ruled out with real
/// evidence** — the reused-index track lookup (already fixed for both the trail and model
/// resolution, B375/B389), the interpolation formula itself (confirmed bit-for-bit against Valve's
/// own compiled `Lerp_Hermite`/`TimeFixup2_Hermite`), and an accidental attachment transform (the
/// real demo's own `props` probe reports the rocket as attached to nothing). None of those explain
/// the owner's measured 185-unit gap between a rocket's spawn and its real, decoded owner.
///
/// **This is the fourth check, and it uses a rocket-shaped schema rather than a player's** —
/// `SyntheticRocket.Schema` declares `m_vecOrigin` on `DT_TFBaseRocket`, exactly as
/// `EntityStateTableTests` established for B372, run through `DemoTimeline.Build` rather than
/// `EntityStateTable` in isolation. `SyntheticInterpolationTests` already proves this arithmetic
/// correct for a PLAYER; if a rocket's own class shape diverges anywhere between the wire and the
/// drawn pose, this is where it would show.
/// </remarks>
public sealed class SyntheticRocketPositionTests
{
    private const float Interval = 1f / 66.67f;

    /// <summary>The track's render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    [Test]
    public void PropsAt_ARocketBetweenTwoKeyframes_IsTheBlendTheDelayLandsOn()
    {
        // The same arithmetic SyntheticInterpolationTests already proves for a player: asking for
        // tick 120 draws tick 112, two ticks into the ten-tick span from 110 to 120. The rocket
        // covers 200 units of X in that span (1,100 u/s at 66.67 ticks/s), so it is 40 units past
        // the 110 keyframe.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
            Interval,
            (100, -2478f, -2452f, 699f),
            (110, -2278f, -2452f, 699f),
            (120, -2078f, -2452f, 699f)));

        List<SceneProp> shown = [];
        timeline.PropsAt(120.0, shown);

        SceneProp rocket = shown.ShouldHaveSingleItem();

        rocket.Pose.X.ShouldBe(-2278f + (200f * ((10 - Delay) / 10f)), 1f);
        rocket.Pose.Y.ShouldBe(-2452f, 1f);
    }

    [Test]
    public void PropsAt_ARocket_ReportsItsRealDecodedOwner()
    {
        // **The check B397 actually needs.** Ownership is resolved through the same
        // EntityState.Owner() path B372 fixed for origin and angle, not asserted here as a value
        // that merely happens to appear — a wrong table would answer null just as it did for
        // origin before that fix, and a track with no owner is indistinguishable from one whose
        // owner property was never read at all.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
            Interval, (100, -2478f, -2452f, 699f), (110, -2278f, -2452f, 699f)));

        List<SceneProp> shown = [];
        timeline.PropsAt(110.0, shown);

        shown.ShouldHaveSingleItem().OwnedBy.ShouldBe(SyntheticRocket.OwnerEntityIndex);
    }

    [Test]
    public void PropsAt_ARocketAtItsFirstTick_HoldsTheFirstStatedPositionRatherThanExtrapolating()
    {
        // **The exact case the owner's own real demo sat in at tick 51093** — asked before the
        // interpolation delay has elapsed, the first stated pose is all there is, per the third
        // branch `SyntheticInterpolationTests.PlayersAt_BeforeTheDelayHasElapsed_ShowsTheFirstStatedPose`
        // already proves for a player. If a rocket answered differently here, that difference —
        // not the general formula — would be B397's cause.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
            Interval,
            (100, -2478f, -2452f, 699f),
            (110, -2278f, -2452f, 699f),
            (120, -2078f, -2452f, 699f)));

        (100 - Delay).ShouldBeLessThan(105 - Delay, "the fixture must ask after the first keyframe");

        List<SceneProp> shown = [];
        timeline.PropsAt(105.0, shown);

        shown.ShouldHaveSingleItem().Pose.X.ShouldBe(-2478f, 0.5f);
    }
}
