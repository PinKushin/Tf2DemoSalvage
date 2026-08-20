using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Who the first-person view spectates when the demo has no recorded camera.
/// </summary>
/// <remarks>
/// **A SourceTV recording carries a camera entity that is a player as far as the wire is
/// concerned, and it never moves.** Taking the first player in the list picked it every time, so
/// the first-person view on the corpus's only real match sat in a resupply room for fourteen
/// minutes — the same frame at ticks 2883, 20000 and 40000, with only the world around it changing.
///
/// It is entity 1 because of how a competitive server starts: SourceTV connects to an empty server
/// before any player does, so it takes the lowest slot and sorts first for ever after.
///
/// The engine's own predicate is the rule to follow — <c>tf_shareddefs.h:225</c>:
///
/// <code>
/// inline bool IsValidTFTeam( int iTeam ) { return iTeam == TF_TEAM_RED || iTeam == TF_TEAM_BLUE; }
/// </code>
///
/// with <c>TEAM_UNASSIGNED</c> 0 and <c>TEAM_SPECTATOR</c> 1 below them, so RED is 2 and BLU is 3.
/// Measured on z1800: the SourceTV camera is team 1 with no class at all, and every playing player
/// is 2 or 3.
/// </remarks>
public sealed class SpectatorTargetTests
{
    [Test]
    public void Choose_AListLedByTheSourceTvCamera_TakesAPlayingPlayer()
    {
        // **The measured shape of z1800, and the defect in one line.** The camera sorts first and
        // is on the spectator team; picking it produces a static view of wherever it was placed.
        List<ScenePlayer> players =
        [
            Player(entity: 1, team: 1, playerClass: null, health: 1),
            Player(entity: 2, team: 3, playerClass: 1, health: 125),
            Player(entity: 3, team: 2, playerClass: 9, health: 125),
        ];

        SpectatorTarget.Choose(players).ShouldNotBeNull().EntityIndex.ShouldBe(2);
    }

    [Test]
    public void Choose_AmongPlayingPlayers_TakesTheLowestEntityIndex()
    {
        // Stable rather than clever: a target that changed from tick to tick would teleport the
        // camera around the map, which is worse than following an arbitrary but consistent player.
        // Picking a subject deliberately is separate work.
        List<ScenePlayer> players =
        [
            Player(entity: 7, team: 2, playerClass: 3, health: 200),
            Player(entity: 4, team: 3, playerClass: 5, health: 150),
        ];

        SpectatorTarget.Choose(players).ShouldNotBeNull().EntityIndex.ShouldBe(4);
    }

    [Test]
    public void Choose_WhenOnlySpectatorsArePresent_IsNobody()
    {
        // The first seconds of a competitive match are exactly this: SourceTV connected and nobody
        // else. Answering with the camera would be the bug; answering with nothing lets the caller
        // fall back to the map view and say so.
        List<ScenePlayer> players =
        [
            Player(entity: 1, team: 1, playerClass: null, health: 1),
            Player(entity: 2, team: 0, playerClass: null, health: 1),
        ];

        SpectatorTarget.Choose(players).ShouldBeNull();
    }

    [Test]
    public void Choose_AnEmptyList_IsNobody()
    {
        SpectatorTarget.Choose([]).ShouldBeNull();
    }

    private static ScenePlayer Player(int entity, int team, int? playerClass, int health) =>
        new(entity, 0f, 0f, 0f, team, health, playerClass);
}
