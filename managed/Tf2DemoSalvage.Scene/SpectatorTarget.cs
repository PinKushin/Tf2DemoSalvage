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

    /// <summary>Whether the engine would let you observe this player.</summary>
    /// <param name="player">A player the timeline reports.</param>
    /// <returns>Whether they are a valid observer target.</returns>
    /// <remarks>
    /// **`CBasePlayer::IsValidObserverTarget`, and the team check was only its first clause.**
    ///
    /// <code>
    /// if ( player->IsEffectActive( EF_NODRAW ) )     return false;  // don't watch invisible players
    /// if ( player->m_lifeState == LIFE_RESPAWNABLE ) return false;  // dead, waiting for respawn
    /// if ( player->m_lifeState == LIFE_DEAD || player->m_lifeState == LIFE_DYING ) { ... }
    /// </code>
    ///
    /// **A dead player is still on RED or BLU**, so filtering by team alone left corpses in the
    /// cycle — and landing on one produced every symptom of B171 at once: the camera at a stale
    /// position, no viewmodel because a dead player has no active weapon, and a view that reads as
    /// a bad third-person camera because that is exactly what it is.
    ///
    /// TF2 makes the invisible check do most of the work — death here is `EF_NODRAW` plus a
    /// separate `CTFRagdoll` rather than an animation — but both are tested, because
    /// <see cref="ScenePlayer.Drawn"/> and <see cref="ScenePlayer.IsAlive"/> are decoded from
    /// different properties and either can be absent from a given demo.
    ///
    /// **The three-second death-cam window is not implemented.** The engine keeps a target valid
    /// until `m_flDeathTime + DEATH_ANIMATION_TIME`, which nothing here decodes; the effect is that
    /// a target is dropped at the instant of death rather than after the death animation. A smaller
    /// wrongness than following a corpse for the rest of the round, and stated rather than hidden.
    /// </remarks>
    public static bool CanObserve(ScenePlayer player) =>
        player.Team is >= FirstPlayingTeam and <= LastPlayingTeam &&
        player.Drawn &&
        player.IsAlive;

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
        IReadOnlyList<ScenePlayer> playing = Observable(players);

        return playing.Count == 0 ? null : playing[0];
    }

    /// <summary>Everyone a spectator can actually reach, in cycling order.</summary>
    /// <param name="players">The roster at a tick, as the timeline holds it.</param>
    /// <returns>The observable players, ordered by entity index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **Public because a caller needs to be able to say HOW MANY, and one lied about it.** The
    /// viewer logged "following entity 7 of 12" from the raw roster while the cycle was choosing
    /// from this set — which on a POV demo was a single player, since everyone outside the
    /// recorder's PVS fails <see cref="CanObserve"/>. Clicking then returned the same player every
    /// time, because `(at + 1) % 1` is 0, and the line claimed twelve candidates. A log must name
    /// what it measured.
    ///
    /// **One definition of "reachable", used by every caller.** `Choose`, <see cref="Next"/> and the
    /// viewer's report each filtered separately before this existed; three copies of a predicate are
    /// three places for it to drift, and the drift would show as a cycle that skips somebody the
    /// chooser was happy to pick.
    /// </remarks>
    public static IReadOnlyList<ScenePlayer> Observable(IReadOnlyList<ScenePlayer> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        return
        [
            .. players
                .Where(CanObserve)
                .OrderBy(player => player.EntityIndex),
        ];
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
        IReadOnlyList<ScenePlayer> playing = Observable(players);

        if (playing.Count == 0)
        {
            return null;
        }

        int at = current is { } entity
            ? IndexOfEntity(playing, entity)
            : -1;

        if (at < 0)
        {
            // Either nothing was followed yet, or the followed player has left, died out of the
            // list or gone to spectator. Resume from the default so a click always does something
            // explicable — the SDK's equivalent guard resets the index rather than failing.
            return reverse ? playing[^1] : playing[0];
        }

        int step = reverse ? -1 : 1;

        return playing[((at + step) % playing.Count + playing.Count) % playing.Count];
    }

    /// <summary>Where an entity sits in the observable list, or −1.</summary>
    private static int IndexOfEntity(IReadOnlyList<ScenePlayer> playing, int entity)
    {
        for (int at = 0; at < playing.Count; at++)
        {
            if (playing[at].EntityIndex == entity)
            {
                return at;
            }
        }

        return -1;
    }
}
