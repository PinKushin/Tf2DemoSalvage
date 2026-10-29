using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The sample walks the entities that exist, not every track of the recording (B259).</summary>
/// <remarks>
/// **The engine's per-frame walks iterate `m_BaseEntities`** (`SimulateEntities`, `cdll_client_int.cpp:2010-2015`), which an
/// entity joins when it is created and leaves when it is deleted (`CClientEntityList::OnAddEntity`,
/// `cliententitylist.cpp:340`; `OnRemoveEntity`, `:402-403`), so a recording's dead and unborn entities cost a frame
/// nothing. `PropsAt` walked every track instead. The list that replaced the walk must answer exactly what the walk did:
/// every track that answers a prop, and none that does not, in TRACK order (ours: the engine's is creation order, and
/// keeping ours leaves every later handle and collation order as it was).
///
/// **The cast is born and dies in the reverse of its track order**, so a list that appended at birth or forgot a death
/// answers a different sequence. Paused (`interpolating: false`), so each answer is the last stated pose with no
/// interpolation delay and the expected sets can be written down from the keyframes.
/// </remarks>
public sealed class EntityListConformanceTests
{
    private static List<ScenePropTrack> Cast()
    {
        ScenePropTrack first = new(entityIndex: 1, "models/props/a.mdl");
        first.Add(60, new ScenePose { X = 1f });
        first.End(200);

        ScenePropTrack second = new(entityIndex: 2, "models/props/b.mdl");
        second.Add(40, new ScenePose { X = 2f });
        second.End(100);

        ScenePropTrack third = new(entityIndex: 3, "models/props/c.mdl");
        third.Add(20, new ScenePose { X = 3f });
        third.End(300);

        ScenePropTrack fourth = new(entityIndex: 4, "models/props/d.mdl");
        fourth.Add(0, new ScenePose { X = 4f });
        fourth.End(50);

        return [first, second, third, fourth];
    }

    /// <summary>What a stepped timeline answers at each listed tick, by entity, in answer order.</summary>
    private static readonly (double Tick, int[] Entities)[] Expected =
    [
        (10d, [4]),
        (30d, [3, 4]),
        (45d, [2, 3, 4]),
        (70d, [1, 2, 3]),
        (150d, [1, 3]),
        (250d, [3]),
        (310d, []),
    ];

    [Test]
    public void PropsAt_TracksBornAndEndedOutOfTrackOrder_AnswerExactlyTheLiveOnesInTrackOrder()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks(Cast());
        List<SceneProp> props = [];
        Dictionary<double, (int[] Entities, int Walked)> seen = [];

        for (double tick = 0d; tick <= 320d; tick += 1d)
        {
            timeline.PropsAt(tick, props, interpolating: false);
            seen[tick] = ([.. props.Select(prop => prop.EntityIndex)], timeline.TracksWalked);
        }

        foreach ((double tick, int[] entities) in Expected)
        {
            seen[tick].Entities.ShouldBe(entities, $"at tick {tick}");

            // The walk is the entity list, not the cast: a track that ended or is unborn is not visited.
            seen[tick].Walked.ShouldBe(entities.Length, $"tracks walked at tick {tick}");
        }
    }

    /// <summary>A seek back rebuilds the list from nothing and must land on the same answers (D131).</summary>
    [Test]
    public void PropsAt_AfterASeekBackAcrossBirthsAndEnds_AnswerExactlyTheLiveOnesInTrackOrder()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks(Cast());
        List<SceneProp> props = [];

        timeline.PropsAt(250d, props, interpolating: false);

        foreach ((double tick, int[] entities) in Expected.Where(expected => expected.Tick <= 70d))
        {
            timeline.PropsAt(tick, props, interpolating: false);
            props.Select(prop => prop.EntityIndex).ShouldBe(entities, $"at tick {tick}");
        }
    }

    /// <summary>A mover that goes hidden and comes back while it is mid-lerp, and two that are born and end while moving.</summary>
    /// <remarks>
    /// Keyframes every 3 ticks, inside the interpolation delay, so each track is on the lerp list for its whole life and
    /// its sample is rewritten by `AdvanceSample`'s lerp loop every call, not only at its wakes.
    /// </remarks>
    private static List<ScenePropTrack> Movers()
    {
        ScenePropTrack blinker = new(entityIndex: 1, "models/props/a.mdl");

        for (int tick = 0; tick <= 120; tick += 3)
        {
            blinker.Add(tick, new ScenePose { X = tick * 2f, Hidden = tick is >= 30 and < 60 });
        }

        ScenePropTrack late = new(entityIndex: 2, "models/props/b.mdl");

        for (int tick = 40; tick <= 90; tick += 3)
        {
            late.Add(tick, new ScenePose { Y = tick * 2f });
        }

        late.End(91);

        ScenePropTrack early = new(entityIndex: 3, "models/props/c.mdl");

        for (int tick = 0; tick <= 50; tick += 3)
        {
            early.Add(tick, new ScenePose { Z = tick * 2f });
        }

        early.End(52);

        return [blinker, late, early];
    }

    /// <remarks>
    /// **The list and the samples must agree after every call, on every path that writes a sample** (review of
    /// `eb9910a3`): a wake's `DeriveSample`, the lerp loop, and a resync. The walk skips a listed track with no sample, so
    /// a desync is invisible in the props; `LiveListMatchesSamples` is the check that sees it. Interpolating and stepped a
    /// quarter tick at a time, so the lerp loop runs on every call.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedWhileTracksLerpThroughHidingBirthAndEnd_KeepsTheListEqualToTheSamples()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks(Movers());
        List<SceneProp> props = [];
        HashSet<int> everSeen = [];

        for (double tick = 0d; tick <= 130d; tick += 0.25)
        {
            timeline.PropsAt(tick, props);
            everSeen.UnionWith(props.Select(prop => prop.EntityIndex));

            timeline.LiveListMatchesSamples().ShouldBeTrue($"at tick {tick}");
            timeline.TracksWalked.ShouldBe(props.Count, $"tracks walked at tick {tick}");
        }

        // The control: every mover was drawn at some point, so the run exercised all three.
        everSeen.ShouldBe([1, 2, 3], ignoreOrder: true);
    }

    /// <remarks>
    /// **One track, one place in the list** (review of `eb9910a3`): the old walk emitted a track listed twice twice, the
    /// list would emit it once, so a cast holding one track twice is refused when the timeline is built.
    /// </remarks>
    [Test]
    public void ForTracks_TheSameTrackTwice_Throws()
    {
        ScenePropTrack track = new(entityIndex: 1, "models/props/a.mdl");
        track.Add(0, new ScenePose());

        Should.Throw<System.ArgumentException>(() => DemoTimeline.ForTracks([track, track]));
    }
}
