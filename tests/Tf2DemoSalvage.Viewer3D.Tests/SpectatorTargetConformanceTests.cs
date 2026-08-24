using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Who the engine will let you spectate, and who it refuses.
/// </summary>
/// <remarks>
/// **`CBasePlayer::IsValidObserverTarget`, `player.cpp`** — the whole rule, and this suite exists
/// because only its first clause was implemented:
///
/// <code>
/// if ( !target->IsPlayer() )                        return false;  // only track players
/// if ( player == this )                             return false;  // not ourselves
/// if ( player->IsEffectActive( EF_NODRAW ) )        return false;  // don't watch invisible players
/// if ( player->m_lifeState == LIFE_RESPAWNABLE )    return false;  // dead, waiting for respawn
/// if ( player->m_lifeState == LIFE_DEAD || player->m_lifeState == LIFE_DYING )
/// {
///     if ( (player->m_flDeathTime + DEATH_ANIMATION_TIME) &lt; gpGlobals->curtime )
///         return false;                                            // 3s of death cam, then no
/// }
/// </code>
///
/// **The defect it describes was reported before it was understood** (B171). The owner: *"some
/// players when you switch to them its not actually going into 1st person, no viewmodel is drawn
/// and you can see the player model sometimes, depending on where the cam is, its like its a 3rd
/// person cam, but bad and not the right one"* — and then the hypothesis that turned out to be
/// right: *"b171 might be the pov cam grabbing spectators/players who are dead"*.
///
/// Spectators were already excluded, by team. **Dead players were not, because a dead player is
/// still on RED or BLU**, and cycling onto one produces all three symptoms at once: the camera goes
/// to a corpse's stale position, the viewmodel is gone because a dead player has no active weapon
/// to draw, and the world looks like a bad third-person view because that is exactly what it is.
///
/// **TF2 makes the first clause do the work.** Death here is `EF_NODRAW` and a separate
/// `CTFRagdoll`, not an animation on the player — see
/// `docs/memory/death-is-ef-nodraw-not-an-animation.md` — so a dead TF2 player fails the
/// invisible-player check before the lifeState clauses are ever reached.
///
/// **The three-second death-cam window is deliberately NOT implemented**, and this suite says so
/// rather than leaving it to be discovered. It needs `m_flDeathTime`, which nothing here decodes,
/// and its absence makes a spectate target disappear at the instant of death rather than after the
/// death animation. That is a smaller wrongness than following a corpse for the rest of the round.
/// </remarks>
public sealed class SpectatorTargetConformanceTests
{
    private static ScenePlayer Player(
        int entity, int team, int? lifeState = null, bool drawn = true) =>
        new(entity, 0f, 0f, 0f, team, Health: 100, PlayerClass: 1,
            LifeState: lifeState, Drawn: drawn);

    /// <summary>RED and BLU, from <c>IsValidTFTeam</c>.</summary>
    private const int Red = 2;

    private const int Blu = 3;

    /// <summary>Team 1 — the spectator team, which was already excluded.</summary>
    private const int Spectator = 1;

    [Test]
    public void Choose_ADeadPlayer_IsNotAValidTarget()
    {
        // LIFE_DEAD. The engine refuses this outright once the death-cam window has passed, and
        // TF2 refuses it immediately because the player is EF_NODRAW.
        ScenePlayer? chosen = SpectatorTarget.Choose(
            [Player(2, Red, lifeState: 2, drawn: false)]);

        chosen.ShouldBeNull("a dead player is not a valid observer target");
    }

    [Test]
    public void Choose_AnInvisiblePlayer_IsNotAValidTarget()
    {
        // The EF_NODRAW clause on its own, with a life state that says alive — so this fails only
        // if the invisible check is missing, which no other case here can show.
        SpectatorTarget.Choose([Player(2, Red, lifeState: 0, drawn: false)])
            .ShouldBeNull("IsEffectActive( EF_NODRAW ) refuses a player you cannot see");
    }

    [Test]
    public void Choose_ALivingPlayer_IsChosenOverADeadOne()
    {
        // **The control, and the case that separates "filters correctly" from "returns null".** A
        // filter that refused everyone would pass both tests above and break the viewer entirely.
        ScenePlayer? chosen = SpectatorTarget.Choose(
        [
            Player(2, Red, lifeState: 2, drawn: false),
            Player(3, Blu, lifeState: 0),
        ]);

        chosen.ShouldNotBeNull();
        chosen.Value.EntityIndex.ShouldBe(3, "the living player is the only valid target");
    }

    [Test]
    public void Next_CyclingPastADeadPlayer_SkipsThem()
    {
        // Cycling forward from 2 should reach 4, not stop on the corpse at 3. This is the exact
        // action the owner performed: MOUSE1 cycles the target.
        ScenePlayer? next = SpectatorTarget.Next(
            [
                Player(2, Red, lifeState: 0),
                Player(3, Red, lifeState: 2, drawn: false),
                Player(4, Red, lifeState: 0),
            ],
            current: 2,
            reverse: false);

        next.ShouldNotBeNull();
        next.Value.EntityIndex.ShouldBe(4, "cycling should step over a dead player, not onto them");
    }

    [Test]
    public void Next_WhenEveryoneElseIsDead_StaysOnTheLivingPlayer()
    {
        // The degenerate end of the same rule. Answering null here would be correct too, but it
        // must not answer a corpse — which is what makes this worth asserting rather than assuming.
        ScenePlayer? next = SpectatorTarget.Next(
            [
                Player(2, Red, lifeState: 0),
                Player(3, Red, lifeState: 2, drawn: false),
            ],
            current: 2,
            reverse: false);

        (next is null || next.Value.EntityIndex == 2).ShouldBeTrue(
            "with only one living player, cycling must not land on the dead one");
    }

    [Test]
    public void Choose_ASpectator_IsStillExcluded()
    {
        // Already true before this change, and asserted so that tightening the liveness filter
        // cannot quietly loosen the team one.
        SpectatorTarget.Choose([Player(2, Spectator, lifeState: 0)])
            .ShouldBeNull("team 1 is the spectator team and was never a valid target");
    }
}
