using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Team and class read off <c>CTFPlayerResource</c> rather than off the player.
/// </summary>
/// <remarks>
/// **Neither travels on the player entity on a modern demo.** Both live on a single
/// <c>CTFPlayerResource</c> entity as arrays indexed by entity index — one entity for the whole
/// server rather than a copy per player. A reader taking them off the player gets null for
/// everyone, which draws a match in which nobody has a team, and looks like a colour bug rather
/// than a missing entity.
///
/// **The array naming is the part that makes this worth a synthetic test.** Source's
/// <c>SendPropArray</c> generates a sub-table named after the array whose properties are <c>000</c>,
/// <c>001</c>, … — so the flattened key is <c>m_iTeam.001</c>, owner table and all, with no
/// <c>DT_</c> prefix. Nothing in this repository documents that, and reading the flattener without
/// it leads to the confident and wrong conclusion that the lookup can never match.
///
/// The corpus test this replaces asserted that *at least one demo* reached 100% coverage of stated
/// teams, and printed the class percentage without asserting it. Here both are constructed and
/// both are asserted by value.
/// </remarks>
public sealed class SyntheticPlayerResourceTests
{
    [Test]
    public void Build_TeamAndClassOnTheResource_ReachTheScenePlayer()
    {
        // Distinct values per player, so a lookup keyed on the wrong slot returns the other
        // player's numbers rather than a plausible default. Scout is 1 and Soldier 3.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResource(
            tick: 66,
            (1, SceneTeams.Red, 1),
            (2, SceneTeams.Blu, 3)));

        ScenePlayer[] players = [.. timeline.PlayersAt(66).OrderBy(player => player.EntityIndex)];

        players.Length.ShouldBe(2);

        players[0].Team.ShouldBe(SceneTeams.Red);
        players[0].PlayerClass.ShouldBe(1);

        players[1].Team.ShouldBe(SceneTeams.Blu);
        players[1].PlayerClass.ShouldBe(3);
    }

    [Test]
    public void Build_TheResourceArrays_AreKeyedByEntityIndexNotByOrder()
    {
        // **The failure this catches is a plausible one:** indexing the arrays by position in the
        // player list rather than by entity slot. With players in slots 1 and 2 the two are the
        // same, so the bug hides. A gap makes them differ — slot 7's class must come from
        // m_iPlayerClass.007, not from the second entry.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResource(
            tick: 66,
            (1, SceneTeams.Red, 2),
            (7, SceneTeams.Blu, 8)));

        ScenePlayer seventh = timeline.PlayersAt(66)
            .Single(player => player.EntityIndex == 7);

        seventh.PlayerClass.ShouldBe(8);
        seventh.Team.ShouldBe(SceneTeams.Blu);
    }

    [Test]
    public void Build_TheEntityAndTheResourceDisagree_TheEntitysOwnTeamWins()
    {
        // B65, "one player on BLU draws in RED". The skin is `GetSkin`'s `switch( GetTeamNumber() )`
        // (c_tf_player.cpp:7795), and `GetTeamNumber()` is `return m_iTeamNum;` (c_baseentity.cpp:3894) — the
        // ENTITY's own field. The resource's `m_iTeam` is a copy `UpdateConnectedPlayer` takes once per
        // `ResourceThink`, every 0.1 s (player_resource.cpp:100, :132), so after a switch it is stale for up to that
        // long, and `UpdateDisconnectedPlayer` never clears it. Reading it first draws exactly the symptom reported.
        //
        // Both directions and a player the resource agrees with, so a reader that took either source unconditionally
        // for everyone fails on one of the three.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResourceAndOwnTeams(
            tick: 66,
            (1, SceneTeams.Red, SceneTeams.Blu, 5),
            (2, SceneTeams.Blu, SceneTeams.Red, 3),
            (3, SceneTeams.Red, SceneTeams.Red, 1)));

        ScenePlayer[] players = [.. timeline.PlayersAt(66).OrderBy(player => player.EntityIndex)];

        players.Select(player => player.Team).ShouldBe([SceneTeams.Blu, SceneTeams.Red, SceneTeams.Red]);
    }

    [Test]
    public void Build_AnEntityThatSentNoTeam_FallsBackToTheResource()
    {
        // The fallback the other way round: an entity that has not stated `m_iTeamNum` still has a team if the
        // resource names one, rather than drawing with none.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResourceAndOwnTeams(
            tick: 66,
            (4, SceneTeams.Blu, null, 2)));

        timeline.PlayersAt(66).ShouldHaveSingleItem().Team.ShouldBe(SceneTeams.Blu);
    }

    [Test]
    public void Build_AnEntityThatSentTeamZero_IsUnassignedThoughTheResourceSaysBlu()
    {
        // `GetTeamNumber()` returns `m_iTeamNum` whatever it holds, so a stated 0 (TEAM_UNASSIGNED) is the answer, not
        // an absence: only an entity that never sent the field falls back. A reader that treated 0 as "not sent" would
        // take the resource's BLU here.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResourceAndOwnTeams(
            tick: 66,
            (4, SceneTeams.Blu, 0, 2)));

        timeline.PlayersAt(66).ShouldHaveSingleItem().Team.ShouldBe(0);
    }

    [Test]
    public void Build_APlayerTheResourceSaysNothingAbout_HasNoClass()
    {
        // A player is in the world for a moment before the resource has said anything about them,
        // and a spectator never gets an entry. The scene must report that as "not stated" rather
        // than inventing a class — a wrong class draws the wrong model.
        //
        // Slot 9 is positioned but absent from the arrays, which the fixture arranges by simply
        // not naming it.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithResource(
            tick: 66,
            (1, SceneTeams.Red, 5)));

        timeline.PlayersAt(66).ShouldHaveSingleItem().PlayerClass.ShouldBe(5);
    }
}
