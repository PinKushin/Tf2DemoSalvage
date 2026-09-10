using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Resolving an entity index to the occupant of that slot at a given moment.
/// </summary>
/// <remarks>
/// **B386, and the fault it fixes is one an index-keyed lookup cannot report.** The engine recycles
/// edict slots, so a demo has far more entities than indices and
/// <see cref="DemoTimeline.TrackFor(int)"/> answers about whichever occupant was written last. It has
/// already produced two silent wrong answers: a rocket trail that drew nothing because the track it
/// found starts after the tick being replayed (B375), and a `cycle` probe that described a
/// client-side-animated player when the index at that tick held a door (B386).
///
/// **Synthetic rather than corpus, because a corpus test cannot state the answer** (D38). Asking a
/// real recording which entity occupied slot 141 at tick 8200 means reading it out of the same
/// timeline the code under test reads — two readings of one file, agreeing. Here the two occupants
/// are put in the slot by the test, so the expected track is the one the test placed there.
///
/// **The control is the third test**, and without it the first two prove nothing: a lookup that
/// ignored the tick entirely would still pass whenever the track it happened to return was the right
/// one. `TrackFor_TheIndexAlone_AnswersTheLastOccupant` pins the wrong answer explicitly, so the pair
/// can only both hold if the tick is what chose.
/// </remarks>
public sealed class TrackSelectionByTickTests
{
    /// <summary>The slot both occupants use, chosen to match the reported case.</summary>
    private const int ReusedSlot = 141;

    [Test]
    public void TrackFor_AnIndexOwningSeveralTracks_PicksTheOneAliveAtTheTick()
    {
        (ScenePropTrack door, ScenePropTrack player, DemoTimeline timeline) = ReusedSlotTimeline();

        timeline.TrackFor(ReusedSlot, tick: 150).ShouldBeSameAs(door);
        timeline.TrackFor(ReusedSlot, tick: 450).ShouldBeSameAs(player);
    }

    [Test]
    public void TrackFor_ATickInNeitherOccupantsLife_IsNull()
    {
        (_, _, DemoTimeline timeline) = ReusedSlotTimeline();

        // Before the first occupant existed, and in the gap between the two. A track returned here
        // would be one the sampler answers null for, which is how B375's replay came to run against
        // a window it was not inside.
        timeline.TrackFor(ReusedSlot, tick: 50).ShouldBeNull();
        timeline.TrackFor(ReusedSlot, tick: 350).ShouldBeNull();
    }

    [Test]
    public void TrackFor_TheIndexAlone_AnswersTheLastOccupant()
    {
        // **The control, and the reason the two tests above are not self-satisfying.** This is the
        // wrong answer stated on purpose: at tick 150 the one-argument overload names the occupant
        // that does not arrive until 400. If the tick-aware overload ever stopped consulting the
        // tick, it would agree with this — and this test says what that agreement would mean.
        (ScenePropTrack door, ScenePropTrack player, DemoTimeline timeline) = ReusedSlotTimeline();

        timeline.TrackFor(ReusedSlot).ShouldBeSameAs(player);
        timeline.TrackFor(ReusedSlot).ShouldNotBeSameAs(door);
        timeline.TrackFor(ReusedSlot, tick: 150).ShouldNotBeSameAs(timeline.TrackFor(ReusedSlot));
    }

    [Test]
    public void TrackFor_APlayerTrack_IsFoundAsWell()
    {
        // **Both populations, because a player's track is not in `Props`.** A selection scanning
        // only `Props` would silently find nothing for a player index — and `cycle`'s own documented
        // example is a player. This is the case that would make the shared selection narrower than
        // the lookup it replaces.
        ScenePropTrack track = At(new ScenePropTrack(ReusedSlot, string.Empty), 10, 90);

        DemoTimeline timeline = DemoTimeline.ForPlayerTracks([track], []);

        timeline.TrackFor(ReusedSlot, tick: 50).ShouldBeSameAs(track);
        timeline.TrackFor(ReusedSlot, tick: 5).ShouldBeNull();
    }

    [Test]
    public void TracksFor_AReusedSlot_ListsEveryOccupant()
    {
        (ScenePropTrack door, ScenePropTrack player, DemoTimeline timeline) = ReusedSlotTimeline();

        timeline.TracksFor(ReusedSlot).ShouldBe([door, player]);
    }

    [Test]
    public void TracksFor_AnIndexTheDemoNeverUsed_IsEmpty()
    {
        // The distinction a report has to make: "nothing was ever recorded for that index" and
        // "seven tracks, none covering your tick" send the reader to opposite places.
        (_, _, DemoTimeline timeline) = ReusedSlotTimeline();

        timeline.TracksFor(entityIndex: 9999).ShouldBeEmpty();
    }

    [Test]
    public void EndTick_ATrackEndedAfterItsLastKeyframe_ReportsTheEndAndNotTheKeyframe()
    {
        // **The bound a window report must print** (B243). `Alive` tests the end tick, so a
        // diagnostic printing the last keyframe instead can declare a tick outside a window the
        // selection accepted — here 290 is alive and 280 is the last thing the demo said.
        ScenePropTrack track = At(new ScenePropTrack(ReusedSlot, "a.mdl"), 100, 280);
        track.End(300);

        track.EndTick.ShouldBe(300);
        track.Keyframes[^1].Tick.ShouldBe(280);
        track.Alive(290).ShouldBeTrue();
        track.Alive(300).ShouldBeFalse();
    }

    /// <summary>One slot, two occupants, with a gap between them.</summary>
    /// <returns>The earlier track, the later track, and a timeline holding both.</returns>
    /// <remarks>
    /// **A gap on purpose.** Two abutting windows cannot separate "picked by the tick" from "picked
    /// by whichever comes first", and they cannot express the null the sampler owes for a slot that
    /// is momentarily empty.
    /// </remarks>
    private static (ScenePropTrack Door, ScenePropTrack Player, DemoTimeline Timeline)
        ReusedSlotTimeline()
    {
        ScenePropTrack door =
            At(new ScenePropTrack(ReusedSlot, "models/props_well/main_entrance_door.mdl"), 100, 280);
        door.End(300);

        ScenePropTrack player =
            At(new ScenePropTrack(ReusedSlot, "models/player/scout.mdl"), 400, 500);

        List<ScenePropTrack> tracks = [door, player];

        return (door, player, DemoTimeline.ForTracks(tracks));
    }

    /// <summary>Gives a track two keyframes, so it has a real window rather than a point.</summary>
    /// <param name="track">The track.</param>
    /// <param name="first">Its first tick.</param>
    /// <param name="last">Its last tick.</param>
    /// <returns>The same track.</returns>
    private static ScenePropTrack At(ScenePropTrack track, int first, int last)
    {
        track.Add(first, new ScenePose { X = first, Y = 0f, Z = 0f });
        track.Add(last, new ScenePose { X = last, Y = 0f, Z = 0f });

        return track;
    }
}
