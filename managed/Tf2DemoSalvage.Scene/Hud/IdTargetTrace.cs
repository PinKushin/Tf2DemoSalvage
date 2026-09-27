using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>What a trace answers `C_TFPlayer::UpdateIDTarget` with: the entity a ray hit, or none.</summary>
/// <param name="Entity">The entity index hit, or null for the world (or nothing).</param>
/// <param name="IsPlayer">Whether that entity is a player — `tr.m_pEnt->IsPlayer()`.</param>
/// <param name="Team">That entity's team, meaningless when <see cref="Entity"/> is null.</param>
/// <param name="StartSolid">`trace.startsolid` — the ray began embedded in something.</param>
/// <param name="DidHitNonWorldEntity">`tr.DidHitNonWorldEntity()` — hit something other than the world/nothing.</param>
public readonly record struct IdTraceHit(int? Entity, bool IsPlayer, int Team, bool StartSolid, bool DidHitNonWorldEntity);

/// <summary>
/// `C_TFPlayer::UpdateIDTarget`'s trace (c_tf_player.cpp:7041), over `GetIDTarget()` (:7025) — free camera and an alive
/// local player's own crosshair target, as distinct from a spectator's (whose target is always the observer target;
/// see <see cref="TfSpectatorTargetId"/>). Feeds <see cref="TfMainTargetId"/> via <see cref="HudState.IdTarget"/>.
/// </summary>
/// <remarks>
/// Two rays from the same view, `MainViewOrigin()`/`MainViewForward()` (:7072): a `MASK_SOLID | CONTENTS_DEBRIS` trace
/// against everything but the shooter (or, as an observer, everything but the observer target); if that hit a player,
/// a second `MASK_SHOT` trace replicating the medigun's mask, preferred when it also hit a (possibly different) player
/// and the first trace either wasn't embedded or hit a different entity than the shot trace did. The result counts only
/// when it did not start solid (unless the hit is an enemy player, who "we sometimes press right against") and hit a
/// non-world entity that isn't the tracer's own entity.
///
/// **The revive attribute changes nothing**: `iReviveMedic` is computed from the `revive` hook and the medic check
/// (:7083) and never read — the mask is `MASK_SOLID | CONTENTS_DEBRIS` either way, which is what this ports.
/// The world and entity geometry is the caller's: the traces are delegates, and the viewer answers them with the BSP
/// sweep, the players' collision hulls for `MASK_SOLID` and their hitboxes for `MASK_SHOT`.
/// **Not modelled here:** the branches before the trace — `mp_fadetoblack`, a forced ID target, and death cam or chase
/// naming the observer target (:7051-7068) — which only the spectator's ID reads, and it takes its target itself.
/// </remarks>
public static class IdTargetTrace
{
    /// <summary>`MAX_TRACE_LENGTH` (util_shared.h) — far enough that nothing real is farther.</summary>
    public const float MaxTraceLength = 4096f * 32f;

    /// <summary>How far in front of the view the ray starts — `VectorMA( origin, 10, forward, vecStart )`.</summary>
    private const float StartOffset = 10f;

    /// <summary>`C_TFPlayer::GetIDTarget()`'s value after one `UpdateIDTarget` call.</summary>
    /// <param name="viewOrigin">`MainViewOrigin()`.</param>
    /// <param name="viewForward">`MainViewForward()`, expected normalized.</param>
    /// <param name="selfEntity">The tracing player's own entity index — ignored as a hit, and the "we hit nobody but ourselves" check.</param>
    /// <param name="selfTeam">The tracing player's team, for the enemy-player startsolid exception.</param>
    /// <param name="isObserver">`IsObserver()` — traces ignoring the observer target rather than self.</param>
    /// <param name="observerTarget">`GetObserverTarget()`'s entity index, ignored by the solid trace while observing.</param>
    /// <param name="solidTrace">`UTIL_TraceLine` with `MASK_SOLID | CONTENTS_DEBRIS` (or plain `MASK_SOLID` while observing).</param>
    /// <param name="shotTrace">`UTIL_TraceLine` with `MASK_SHOT`, always ignoring <paramref name="selfEntity"/>.</param>
    /// <returns>`m_iIDEntIndex`: the entity index found, or 0 for none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solidTrace"/> or <paramref name="shotTrace"/> is null.</exception>
    public static int GetIdTarget(
        Vector3 viewOrigin,
        Vector3 viewForward,
        int selfEntity,
        int selfTeam,
        bool isObserver,
        int observerTarget,
        Func<Vector3, Vector3, int, IdTraceHit> solidTrace,
        Func<Vector3, Vector3, int, IdTraceHit> shotTrace)
    {
        ArgumentNullException.ThrowIfNull(solidTrace);
        ArgumentNullException.ThrowIfNull(shotTrace);

        Vector3 start = viewOrigin + (viewForward * StartOffset);
        Vector3 end = viewOrigin + (viewForward * MaxTraceLength);

        // "If we're in observer mode, ignore our observer target. Otherwise, ignore ourselves."
        int ignore = isObserver ? observerTarget : selfEntity;
        IdTraceHit trace = solidTrace(start, end, ignore);

        bool isEnemyPlayer = false;

        if (trace is { Entity: not null, IsPlayer: true })
        {
            // "use the shot mask to replicate the medigun's trace" — always ignoring self, observer or not.
            IdTraceHit shot = shotTrace(start, end, selfEntity);

            if (shot.Entity is not null && shot.IsPlayer && (!trace.StartSolid || trace.Entity != shot.Entity))
            {
                trace = shot;
            }

            // "It's okay to start solid against enemies because we sometimes press right against them"
            isEnemyPlayer = selfTeam != trace.Team;
        }

        if ((!trace.StartSolid || isEnemyPlayer) && trace.DidHitNonWorldEntity && trace.Entity is { } hit && hit != selfEntity)
        {
            return hit;
        }

        return 0;
    }
}
