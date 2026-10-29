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
}
