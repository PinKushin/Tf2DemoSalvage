using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

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
public static class SpectatorTarget
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

    /// <summary>The next player to spectate, cycling forward or back from the current one.</summary>
    /// <param name="players">Everyone the timeline reports at a tick.</param>
    /// <param name="current">The entity currently followed, or <c>null</c> for none.</param>
    /// <param name="reverse">Whether to go backwards.</param>
    /// <returns>The player to follow, or <c>null</c> when nobody is playing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **Modelled on `CTFPlayer::FindNextObserverTarget` in `src/game/server/tf/tf_player.cpp`**,
    /// which TF2's own `spec_next` and `spec_prev` commands call. The parts that transfer:
    ///
    /// - **the search starts one step past the current target**, because
    ///   `GetNextObserverSearchStartPoint` does `startIndex += iDir` before looking, so a cycle
    ///   never returns the player already being watched;
    /// - **both directions wrap**, and the SDK spells each arm out — `if (currentIndex > iMax)
    ///   currentIndex = 0; else if (currentIndex &lt; 0) currentIndex = iMax;`. A one-armed wrap is
    ///   the plausible bug and it works for exactly as long as nobody cycles backwards;
    /// - **null when the search finds nothing**, which matters because of what the caller does with
    ///   it: `if ( target ) SetObserverTarget( target );`. A failed cycle leaves the camera where it
    ///   was rather than blanking the view — and the first seconds of a competitive match really
    ///   are SourceTV alone.
    ///
    /// **What is deliberately not copied.** `IsValidObserverTarget` also admits buildings, observer
    /// points and a coached student, and rejects `target == this`. Neither transfers: this viewer
    /// follows players, and "this" is the recording client, who in a POV demo is precisely who you
    /// want to watch. A rule copied without its context is the kind that gets confidently repeated.
    ///
    /// **Entity-index order, matching <see cref="Choose"/> rather than the SDK's list order.** TF2
    /// walks `m_hObservableEntities`, rebuilt per search and holding more than players. Ours has to
    /// agree with the default target or the first click would jump somewhere unrelated, and it has
    /// to be stable from tick to tick for the same reason <see cref="Choose"/> is.
    /// </remarks>
    public static ScenePlayer? Next(
        IReadOnlyList<ScenePlayer> players, int? current, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(players);

        ScenePlayer[] playing =
        [
            .. players
                .Where(player => player.Team is >= FirstPlayingTeam and <= LastPlayingTeam)
                .OrderBy(player => player.EntityIndex),
        ];

        if (playing.Length == 0)
        {
            return null;
        }

        int at = current is { } entity
            ? Array.FindIndex(playing, player => player.EntityIndex == entity)
            : -1;

        if (at < 0)
        {
            // Either nothing was followed yet, or the followed player has left, died out of the
            // list or gone to spectator. Resume from the default so a click always does something
            // explicable — the SDK's equivalent guard resets the index rather than failing.
            return reverse ? playing[^1] : playing[0];
        }

        int step = reverse ? -1 : 1;

        return playing[((at + step) % playing.Length + playing.Length) % playing.Length];
    }
}
