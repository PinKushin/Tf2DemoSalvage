using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the interpolating accessor does with a player who is dead.
/// </summary>
/// <remarks>
/// **A dead player's entity follows whoever they are spectating.** The engine keeps the slot alive
/// and moves it with the camera, so interpolating that track would walk the corpse across the map
/// and stand it inside a living player — which looks like a rendering bug and is actually the
/// timeline believing the entity.
///
/// So the position recorded for a dead player is held rather than interpolated: it is where they
/// fell. The distinction is invisible on a stationary player and invisible again on a demo where
/// nobody dies, which is why it needs a demo written to contain both — one player alive and
/// moving, one dead and moving, at the same ticks.
///
/// <c>docs/memory/death-is-ef-nodraw-not-an-animation.md</c> is the other half of this: TF2 never
/// animates a dying player, and the body is a separate <c>CTFRagdoll</c> entity. What this covers
/// is the live slot, which keeps existing and keeps moving.
/// </remarks>
public sealed class SyntheticDeadPlayerTests
{
    /// <summary>Two-thirds of a millisecond off 66 tick, so it cannot be confused with a default.</summary>
    private const float Interval = 1f / 66.67f;

    /// <summary>Slot of the player who dies.</summary>
    private const int Dead = 1;

    /// <summary>Slot of the player who does not — the bystander.</summary>
    private const int Alive = 2;

    [Test]
    public void PlayersAt_ADeadPlayer_KeepsTheStatedPositionRatherThanInterpolating()
    {
        // Both players are stated at the same two ticks and move the same distance. The living one
        // is interpolated between them; the dead one is not, and holds what the demo last said.
        List<ScenePlayer> drawn = [];
        Timeline().PlayersAt(200d, drawn);

        ScenePlayer dead = drawn.Single(player => player.EntityIndex == Dead);

        // The second snapshot's position exactly. Interpolated it would be short of it, because
        // the sample is taken an interpolation window behind the tick asked for.
        dead.X.ShouldBe(512f, 0.5f);
    }

    [Test]
    public void PlayersAt_ALivingPlayerAtTheSameTick_IsInterpolatedShortOfIt()
    {
        // **The bystander, and it is what makes the assertion above mean anything.** Without it,
        // "the dead player is at 512" could be true because nothing is interpolated at all — and
        // an accessor that had stopped interpolating everything would look identical.
        List<ScenePlayer> drawn = [];
        Timeline().PlayersAt(200d, drawn);

        ScenePlayer alive = drawn.Single(player => player.EntityIndex == Alive);

        // **Predicted rather than bounded.** The sample is taken `Delay` ticks behind the tick
        // asked for — `GetInterpolationAmount`'s `TIME_TO_TICKS(cl_interp) + serverTickMultiple` —
        // so tick 200 draws `200 - Delay`, that far through the span from 100 to 200, on a 0-to-512
        // move. Derived because the literal 476.16 encoded a seven-tick delay and the engine's is
        // eight (B267).
        int delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

        alive.X.ShouldBe(512f * ((100f - delay) / 100f), 0.5f);
    }

    [Test]
    public void PlayersAt_ADeadPlayer_IsStillReported()
    {
        // Held, not dropped. A viewer draws the slot — the scoreboard entry, the spectator target
        // — so removing them would be a different behaviour that this test's sibling could not
        // tell apart from holding.
        List<ScenePlayer> drawn = [];
        Timeline().PlayersAt(200d, drawn);

        drawn.Select(player => player.EntityIndex).OrderBy(index => index)
            .ShouldBe([Dead, Alive]);
    }

    /// <summary>A demo where one of two identically-moving players is dead throughout.</summary>
    /// <remarks>
    /// Two snapshots, because one gives interpolation nothing to work with and the whole
    /// distinction being tested is between interpolating and holding. Both players travel the same
    /// distance over the same ticks, so the only difference between them is the life state.
    /// </remarks>
    private static DemoTimeline Timeline() => DemoTimeline.Build(
        SyntheticPlayer.DemoOfPlayersOverTicks(
            Interval,
            (100, [(Dead, 0f, 2), (Alive, 0f, 0)]),
            (200, [(Dead, 512f, 2), (Alive, 512f, 0)])));
}
