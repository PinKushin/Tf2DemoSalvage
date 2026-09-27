using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFPlayerResource`/`DT_PlayerResource`, read the way `UpdatePlayerList` reads it (`tf_clientscoreboard.cpp:1284-1621`).</summary>
public sealed class SceneScoreboardPlayerTests
{
    [Test]
    public void ScoreboardPlayersAt_AConnectedSlot_ReadsEveryField()
    {
        SceneScoreboardPlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithScoreboard(
                (EntityIndex: 3, Connected: true, Valid: true, Team: SceneTeams.Blu, Alive: true,
                    Score: 12, TotalScore: 34, Deaths: 5, Ping: 67, PlayerClass: 3, ActiveDominations: 2)))
            .ScoreboardPlayersAt(100)
            .ShouldHaveSingleItem();

        player.ShouldBe(new SceneScoreboardPlayer(3)
        {
            Connected = true,
            Valid = true,
            Team = SceneTeams.Blu,
            Alive = true,
            Score = 12,
            TotalScore = 34,
            Deaths = 5,
            Ping = 67,
            PlayerClass = 3,
            ActiveDominations = 2,
        });
    }

    [Test]
    public void ScoreboardPlayersAt_ASlotThatIsNeitherConnectedNorValid_IsExcluded()
    {
        IReadOnlyList<SceneScoreboardPlayer> players = DemoTimeline.Build(SyntheticPlayer.DemoWithScoreboard(
                (EntityIndex: 3, Connected: false, Valid: false, Team: SceneTeams.Blu, Alive: true,
                    Score: 12, TotalScore: 34, Deaths: 5, Ping: 67, PlayerClass: 3, ActiveDominations: 2)))
            .ScoreboardPlayersAt(100);

        players.ShouldBeEmpty();
    }

    [Test]
    public void ScoreboardPlayersAt_TwoSlots_KeepsBothInEntityIndexOrder()
    {
        IReadOnlyList<SceneScoreboardPlayer> players = DemoTimeline.Build(SyntheticPlayer.DemoWithScoreboard(
                (EntityIndex: 2, Connected: true, Valid: true, Team: SceneTeams.Red, Alive: true,
                    Score: 1, TotalScore: 1, Deaths: 0, Ping: 20, PlayerClass: 1, ActiveDominations: 0),
                (EntityIndex: 5, Connected: true, Valid: true, Team: SceneTeams.Blu, Alive: false,
                    Score: 2, TotalScore: 2, Deaths: 1, Ping: 40, PlayerClass: 2, ActiveDominations: 0)))
            .ScoreboardPlayersAt(100);

        players.Select(player => player.EntityIndex).ToArray().ShouldBe([2, 5]);
    }
}
