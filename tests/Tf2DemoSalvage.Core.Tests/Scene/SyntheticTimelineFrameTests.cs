using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Frames, movement, tick rate and scrubbing — the timeline as a viewer uses it.
/// </summary>
/// <remarks>
/// **Converted from <c>DemoTimelineTests</c>, which was the most expensive file in the corpus
/// suite.** Its five tests each rebuilt a real timeline, and every assertion was a shape rather
/// than a value: frames advance, somebody moved, the tick rate is between 10 and 200 per second.
/// Those are what you assert when the input is found — nobody knows where the players were or what
/// rate the server ran at, so the test asks whether the answer is plausible.
///
/// A written demo states all three. The player is at chosen coordinates on chosen ticks at a chosen
/// interval, so "somebody moved" becomes "the player was here, then there", and a range check on
/// the tick rate becomes the number that went in.
/// </remarks>
public sealed class SyntheticTimelineFrameTests
{
    /// <summary>Two-thirds of a millisecond off 66 tick, so it cannot be confused with a default.</summary>
    private const float Interval = 1f / 66.67f;

    [Test]
    public void Build_SeveralSnapshots_ProduceFramesInAscendingTickOrder()
    {
        // Out-of-order frames play the demo backwards in places, which looks like players
        // teleporting rather than like a bug. The corpus version could only assert the ordering;
        // here the ticks themselves are known.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (66, 0f, 0f), (132, 64f, 0f), (198, 128f, 0f)));

        timeline.Frames.Select(frame => frame.Tick).ShouldBe([66, 132, 198]);
        timeline.FirstTick.ShouldBe(66);
        timeline.LastTick.ShouldBe(198);
    }

    [Test]
    public void Build_APlayerThatMoves_RecordsEachPositionOnItsOwnFrame()
    {
        // **The measurement a static scene cannot pass**, which is what the corpus test was for:
        // a timeline whose frames all carry the same positions satisfies every structural
        // assertion while showing a map full of statues.
        //
        // The corpus version counted players occupying more than one position. This asserts which
        // positions, which also catches a timeline that moves the player to the wrong place.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (66, 0f, 0f), (132, 256f, -128f)));

        timeline.PlayersAt(66).ShouldHaveSingleItem().X.ShouldBe(0f, 0.5f);

        ScenePlayer moved = timeline.PlayersAt(132).ShouldHaveSingleItem();
        moved.X.ShouldBe(256f, 0.5f);
        moved.Y.ShouldBe(-128f, 0.5f);
    }

    [Test]
    public void Build_APropertyOnlyTheFirstSnapshotSent_IsRetainedAcrossDeltas()
    {
        // The delta path's whole point: an update carries what changed, and everything else stays.
        // Team rides on the entering snapshot only, so a timeline that rebuilt each frame from its
        // own update would report a team on the first frame and null afterwards.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (66, 0f, 0f), (132, 64f, 0f), (198, 128f, 0f)));

        timeline.PlayersAt(198).ShouldHaveSingleItem().Team.ShouldBe(SceneTeams.Red);
    }

    [Test]
    public void Build_TheTickRate_IsTheOneServerInfoStated()
    {
        // **Never a constant.** TF2's usual interval is 0.015, early servers ran 33 tick, and
        // LoadedDemo once had 66.667 hardcoded. A demo replayed at the wrong rate reads as a slow
        // or fast server rather than as a defect.
        //
        // The corpus version asserted 10..200 per second, which any plausible misread passes. Two
        // demos at two rates assert the value, and the second is the one no modern recording in the
        // corpus could supply.
        DemoTimeline modern = DemoTimeline.Build(
            SyntheticPlayer.DemoOverTicks(Interval, (66, 0f, 0f)));

        modern.IntervalPerTick.ShouldBe(Interval, 0.00001f);

        DemoTimeline early = DemoTimeline.Build(
            SyntheticPlayer.DemoOverTicks(1f / 33f, (66, 0f, 0f)));

        early.IntervalPerTick.ShouldBe(1f / 33f, 0.00001f);
    }

    [Test]
    public void PlayersAt_ATickBetweenFrames_ReturnsTheLastFrameAtOrBeforeIt()
    {
        // Scrubbing lands on arbitrary ticks and positions arrive with packets rather than on a
        // fixed cadence, so the viewer must get the most recent known positions or the map blinks
        // empty between updates.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
            Interval, (66, 0f, 0f), (132, 256f, 0f)));

        // On the frame.
        timeline.PlayersAt(132).ShouldHaveSingleItem().X.ShouldBe(256f, 0.5f);

        // One past it, and well past it: both still show that frame, because no later one exists.
        timeline.PlayersAt(133).ShouldHaveSingleItem().X.ShouldBe(256f, 0.5f);
        timeline.PlayersAt(10_000).ShouldHaveSingleItem().X.ShouldBe(256f, 0.5f);

        // Between the two frames the earlier one stands, rather than the nearer one winning.
        timeline.PlayersAt(131).ShouldHaveSingleItem().X.ShouldBe(0f, 0.5f);

        // Before the first frame there is genuinely nothing to show, and an empty list says so
        // without the caller having to special-case it.
        timeline.PlayersAt(65).ShouldBeEmpty();
    }
}
