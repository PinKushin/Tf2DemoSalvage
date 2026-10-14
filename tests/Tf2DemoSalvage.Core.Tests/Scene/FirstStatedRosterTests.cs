using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The first-stated team and class table that replaced `Backfill` (D217): an addition above the engine, filled into a
/// frame when it is read so the builder never rewrites a frame it has finished.
/// </summary>
public sealed class FirstStatedRosterTests
{
    private static TimelineFrame Frame(int tick, params ScenePlayer[] players) => new(tick, new List<ScenePlayer>(players));

    private static ScenePlayer Player(int index, int? team, int? playerClass) =>
        new(index, 0f, 0f, 0f, team, 125, playerClass);

    /// <remarks>**The addition itself**: a frame before the first statement reads the first statement.</remarks>
    [Test]
    public void Fill_PlayerBeforeTheirFirstStatement_ReadsTheFirstStatement()
    {
        FirstStatedRoster roster = new();
        roster.Observe(Frame(1, Player(3, null, null)));
        roster.Observe(Frame(2, Player(3, 2, 5)));
        roster.Observe(Frame(3, Player(3, 3, 7)));

        ScenePlayer filled = roster.Fill(Frame(1, Player(3, null, null))).Players[0];

        (filled.Team, filled.PlayerClass).ShouldBe((2, 5));
    }

    /// <remarks>**Never overwrites the demo**: a stated team stays, and only the missing class is filled.</remarks>
    [Test]
    public void Fill_TeamStatedClassMissing_KeepsTheTeamAndFillsTheClass()
    {
        FirstStatedRoster roster = new();
        roster.Observe(Frame(2, Player(3, 2, 5)));

        ScenePlayer filled = roster.Fill(Frame(9, Player(3, 3, null))).Players[0];

        (filled.Team, filled.PlayerClass).ShouldBe((3, 5));
    }

    /// <remarks>
    /// **The wiring, end to end**: a player entering with no team reads the FIRST team the demo states, not the last,
    /// and each later frame keeps its own. No corpus demo exercises this — disconnecting the table left the
    /// fingerprints of f12, rgl-pug and movement-test unchanged — so this authored demo is the only test that can see
    /// the hop. The change of team on the last frame is what tells a roster fed every finished frame from one fed only
    /// the final frame.
    /// </remarks>
    [Test]
    public void Build_PlayerEntersBeforeTheirTeamIsStated_ReadsTheFirstStatedTeamAtEntry()
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfALateTeam(0.015f, (10, null), (12, SceneTeams.Blu), (14, SceneTeams.Red)));

        timeline.Frames.Select(frame => (frame.Tick, frame.Players[0].Team))
            .ShouldBe([(10, SceneTeams.Blu), (12, SceneTeams.Blu), (14, SceneTeams.Red)]);
    }

    /// <remarks>**A clone is independent**: what the original observes after the snapshot does not reach the copy.</remarks>
    [Test]
    public void Clone_ThenOriginalObserves_CopyIsUnchanged()
    {
        FirstStatedRoster roster = new();
        FirstStatedRoster copy = roster.Clone();
        roster.Observe(Frame(2, Player(3, 2, 5)));

        copy.Fill(Frame(1, Player(3, null, null))).Players[0].Team.ShouldBeNull();
    }
}
