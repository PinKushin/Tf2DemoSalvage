using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFTeam` (`DT_TFTeam`/`DT_Team`, `team.cpp:40-44`), decoded per frame.</summary>
public sealed class SceneTeamTests
{
    [Test]
    public void TeamsAt_RedAndBlue_ReadsScoreAndRoundsWon()
    {
        System.Collections.Generic.IReadOnlyList<SceneTeam> teams =
            DemoTimeline.Build(SyntheticPlayer.DemoWithTeams(redScore: 3, blueScore: 5, redRoundsWon: 1, blueRoundsWon: 2))
                .TeamsAt(100);

        teams.ShouldContain(new SceneTeam(2) { Score = 3, RoundsWon = 1, Name = "Red" });
        teams.ShouldContain(new SceneTeam(3) { Score = 5, RoundsWon = 2, Name = "Blue" });
    }

    [Test]
    public void TeamsAt_NoTeamEntity_IsEmpty() =>
        DemoTimeline.Build(SyntheticPlayer.Demo(new System.Collections.Generic.Dictionary<string, Tf2DemoSalvage.Core.Schema.PropertyValue>()))
            .TeamsAt(66).ShouldBeEmpty();
}
