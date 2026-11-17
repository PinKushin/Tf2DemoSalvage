using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One stretch of a player's overheal pluses.</summary>
/// <param name="Entity">The player.</param>
/// <param name="Team">The team whose pluses he shows — his disguise's, to an enemy of a disguised spy.</param>
/// <param name="From">The tick the effect is stepped from: the condition's first, or two seconds before a seek met it.</param>
/// <param name="Stop">The tick emission stopped, or null while it runs.</param>
public readonly record struct OverhealRun(int Entity, int Team, int From, int? Stop);

/// <summary>Which players show overheal pluses, and since when — <c>C_TFPlayer::UpdateOverhealEffect</c> (B538).</summary>
/// <remarks>
/// The engine's function is quoted in `OverhealEffectsConformanceTests`. It runs when the condition is added and when
/// it is removed, so an effect lives exactly as long as `TF_COND_HEALTH_OVERHEALED` does and then stops emitting.
///
/// **Its start is kept, not recomputed**, because an effect is stepped from the tick it began and a start that moved
/// with the frame would rebuild it every frame. A seek lands on at most two seconds of it — the pluses' own lives, so
/// the picture is the one playing would have left (`docs/memory/a-parity-rule-may-bound-the-cost.md`).
///
/// *Not built:* the restart `UpdateSpyStateChange` gives a running effect (`c_tf_player.cpp:10016`), which stops the old
/// particles emitting and starts a new effect on the same body.
/// </remarks>
public sealed class OverhealEffects
{
    /// <summary>`TF_COND_HEALTH_OVERHEALED`.</summary>
    public const int Overhealed = 23;

    /// <summary>How long a stopped effect is still offered, and how far back a seek replays: two seconds, the pluses' lives.</summary>
    public const int FadeTicks = 132;

    /// <summary>`TF_CLASS_SPY`.</summary>
    private const int Spy = 8;

    /// <summary>`TF_TEAM_RED` and `TF_TEAM_BLUE`.</summary>
    private const int Red = 2, Blue = 3;

    /// <summary>Each player's pluses by team: a team change stops one run and starts another.</summary>
    private readonly TickRuns<(int Entity, int Team)> _runsOf = new(FadeTicks, FadeTicks);

    /// <summary>What <see cref="At"/> hands back, reused.</summary>
    private readonly List<OverhealRun> _runs = [];

    /// <summary>`CreateOverhealEffect`'s system for a team.</summary>
    /// <param name="team">The team number.</param>
    /// <returns>The particle system's name, or null for a team with none.</returns>
    public static string? EffectName(int? team) => team switch
    {
        Blue => "overhealedplayer_blue_pluses",
        Red => "overhealedplayer_red_pluses",
        _ => null,
    };

    /// <summary>`UpdateOverhealEffect`'s <c>bShow</c> and <c>iTeam</c> for one player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="localEntity">The local player's entity — the recorder — or null when the demo has none.</param>
    /// <returns>The team whose pluses he shows, or null when he shows none.</returns>
    public static int? ShownTeam(ScenePlayer player, int? localEntity)
    {
        if (!player.Conditions.Has(Overhealed) ||
            player.EntityIndex == localEntity ||
            (player.Conditions.IsStealthed && player.IsEnemy))
        {
            return null;
        }

        return player.PlayerClass == Spy && player.IsEnemy && player.Conditions.Has(PlayerConditions.Disguised)
            ? player.DisguiseTeam
            : player.Team;
    }

    /// <summary>Every overheal effect that should be running or fading at a tick.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="playersAt">The recorded players on a tick.</param>
    /// <param name="localEntity">The local player's entity, or null.</param>
    /// <returns>The effects, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="playersAt"/> is null.</exception>
    public IReadOnlyList<OverhealRun> At(int tick, Func<int, IReadOnlyList<ScenePlayer>> playersAt, int? localEntity)
    {
        ArgumentNullException.ThrowIfNull(playersAt);

        _runs.Clear();

        // A seek walks two seconds back: nothing older than the window can still have a particle alive.
        foreach (TickRun<(int Entity, int Team)> run in _runsOf.At(tick, (walked, showing) =>
        {
            foreach (ScenePlayer player in playersAt(walked))
            {
                if (ShownTeam(player, localEntity) is { } team && EffectName(team) is not null)
                {
                    // The same player under another team's colours is another key: the old effect stops, a new one starts.
                    showing.Add((player.EntityIndex, team));
                }
            }
        }))
        {
            _runs.Add(new OverhealRun(run.Key.Entity, run.Key.Team, run.From, run.Stop));
        }

        return _runs;
    }
}
