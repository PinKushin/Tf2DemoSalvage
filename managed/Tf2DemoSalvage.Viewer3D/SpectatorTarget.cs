using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Picks whose eyes to borrow when a demo carries no recorded camera.
/// </summary>
/// <remarks>
/// **A SourceTV camera is a player as far as the wire is concerned, and it does not move.** The
/// first version of the first-person view took the first player the timeline reported, and on the
/// corpus's only real match that is the SourceTV camera every time: the view sat in a resupply room
/// for the whole fourteen minutes, producing the identical frame at ticks 2883, 20000 and 40000
/// while the world around it changed. Nothing failed; the picture was simply of the wrong subject.
///
/// It is entity 1 for a reason worth knowing: a competitive server starts empty, SourceTV connects
/// before any player does, so it takes the lowest slot and sorts first for the rest of the match.
///
/// The rule is the engine's own, <c>tf_shareddefs.h:225</c>:
///
/// <code>
/// inline bool IsValidTFTeam( int iTeam ) { return iTeam == TF_TEAM_RED || iTeam == TF_TEAM_BLUE; }
/// </code>
///
/// **Not yet a chosen subject, and TF2 says what that should look like when it is built**: space
/// cycles the camera mode, mouse one moves to the next player. Until then this is a default rather
/// than a choice, and the two callers that need to agree on it — the camera and the entity being
/// hidden from its own view — both come here rather than each picking.
/// </remarks>
internal static class SpectatorTarget
{
    /// <summary><c>TEAM_SPECTATOR</c> is 1, and <c>TEAM_UNASSIGNED</c> 0, so play starts at 2.</summary>
    private const int FirstPlayingTeam = 2;

    /// <summary><c>TF_TEAM_BLUE</c>, the highest team that plays.</summary>
    private const int LastPlayingTeam = 3;

    /// <summary>The player to spectate, or <c>null</c> when nobody is playing.</summary>
    /// <param name="players">Everyone the timeline reports at a tick.</param>
    /// <returns>A playing player, or <c>null</c>.</returns>
    /// <remarks>
    /// **Lowest entity index rather than any other order**, because the answer has to be the same
    /// from tick to tick. A target chosen by something that varies — nearest, most recently hurt,
    /// highest health — teleports the camera across the map between frames, which is a worse
    /// picture than following an arbitrary but consistent player.
    ///
    /// Null when nobody qualifies rather than falling back to a spectator: the first seconds of a
    /// competitive match really are SourceTV alone, and that is a state to report rather than to
    /// paper over. See <c>docs/memory/fallbacks-do-not-make-guesses-safe.md</c>.
    /// </remarks>
    public static ScenePlayer? Choose(IReadOnlyList<ScenePlayer> players)
    {
        ScenePlayer[] playing =
        [
            .. players.Where(player =>
                player.Team is >= FirstPlayingTeam and <= LastPlayingTeam),
        ];

        if (playing.Length == 0)
        {
            return null;
        }

        return playing.MinBy(player => player.EntityIndex);
    }
}
