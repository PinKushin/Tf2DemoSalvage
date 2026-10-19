using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The roster a demo's load reports: each player as first seen, in the order first seen (finding 80).</summary>
/// <remarks>
/// **The report grouped every player of every frame** to take the first of each: 96,589 frames times 13 players on f12,
/// 1.1 GB of large-object garbage traced to <c>DecodedDemo.Report</c> during the load. One pass over the frames, keeping
/// the first of each entity, is the same answer.
/// </remarks>
public sealed class DecodedDemoRosterTests
{
    private static ScenePlayer Player(int entity, int team) =>
        new(EntityIndex: entity, X: 0f, Y: 0f, Z: 0f, Team: team, Health: 100, PlayerClass: 1, Yaw: 0f, LifeState: 0, Flags: 0);

    [Test]
    public void Roster_ThreeFramesWithPlayersComingAndGoing_IsEachPlayerAsFirstSeenInTheOrderFirstSeen()
    {
        List<IReadOnlyList<ScenePlayer>> frames =
        [
            [Player(5, 2), Player(3, 3)],
            [Player(3, 2), Player(7, 3)],
            [Player(5, 3), Player(9, 2)],
        ];

        IReadOnlyList<ScenePlayer> roster = DecodedDemo.Roster(frames);

        roster.Select(player => (player.EntityIndex, player.Team)).ShouldBe([(5, 2), (3, 3), (7, 3), (9, 2)]);
    }

    [Test]
    public void Roster_ManyFramesOfTheSamePlayers_AllocatesNothingPerFrame()
    {
        IReadOnlyList<ScenePlayer> same = [.. Enumerable.Range(1, 13).Select(entity => Player(entity, 2))];
        List<IReadOnlyList<ScenePlayer>> frames = [.. Enumerable.Repeat(same, 20_000)];

        _ = DecodedDemo.Roster(frames);
        long before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<ScenePlayer> roster = DecodedDemo.Roster(frames);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        roster.Count.ShouldBe(13);
        allocated.ShouldBeLessThan(64 * 1024, "20,000 frames of 13 must not be copied; the old grouping copied every one");
    }
}
