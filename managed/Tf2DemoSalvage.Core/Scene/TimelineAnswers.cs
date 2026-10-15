using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Everything a timeline SAYS at one moment, through its public questions — what a window must answer exactly as the
/// whole timeline does (D217 step 3).
/// </summary>
/// <remarks>
/// **Answers, not storage.** A window holds less than a whole timeline by design, so comparing the lists themselves
/// would only measure the trim. What has to be equal is what every reader is told: the players, every live track's
/// interpolated and held pose, the props as sampled, and the per-tick state the HUD, camera, fog and sound read.
///
/// **One walk for the probe and the test**, so the two cannot disagree about what was compared.
/// </remarks>
internal static class TimelineAnswers
{
    /// <summary>What <paramref name="timeline"/> answers at <paramref name="tick"/>, in a hashable shape.</summary>
    /// <param name="timeline">The timeline asked.</param>
    /// <param name="tick">The moment; fractional ticks exercise interpolation.</param>
    /// <returns>A list of named answers.</returns>
    public static List<(string Question, object? Answer)> At(DemoTimeline timeline, double tick)
    {
        int whole = (int)System.Math.Floor(tick);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<(string, object?)> answers =
        [
            ("players", players),
            ("frame players", timeline.PlayersAt(whole)),
            ("props", props.OrderBy(prop => prop.EntityIndex).ToList()),
            ("tracks", Tracks(timeline, tick)),
            ("soundscape", timeline.SoundscapeAt(whole)),
            ("fog", timeline.FogAt(whole)),
            ("sky fog", timeline.SkyFogAt(whole)),
            ("director", timeline.DirectorAt(whole)),
            ("recorded view", timeline.RecordedViewAt(whole)),
            ("convars", timeline.ServerConVarsAt(whole)),
            ("spectator gui", timeline.SpectatorGuiShownAt(whole)),
            ("round state", timeline.RoundStateAt(tick)),
            ("rules", timeline.RulesAt(whole)),
            ("round timers", timeline.RoundTimersAt(whole)),
            ("buildings", timeline.BuildingsAt(whole)),
            ("teams", timeline.TeamsAt(whole)),
            ("scoreboard", timeline.ScoreboardPlayersAt(whole)),
            ("id entities", timeline.IdEntitiesAt(whole)),
            ("recorder team", timeline.RecorderTeamAt(whole)),
            ("server tick", timeline.ServerTickAt(whole)),
            ("viewmodels", players.Select(player => (timeline.ViewmodelAt(whole, player.EntityIndex),
                timeline.OffHandViewmodelAt(whole, player.EntityIndex))).ToList()),
            ("sounds", timeline.Sounds.Where(sound => sound.Tick == whole).ToList()),
            ("events", timeline.GameEvents.Where(fired => fired.Tick == whole).ToList()),
            ("messages", timeline.UserMessages.Where(message => message.Tick == whole).ToList()),
        ];

        return answers;
    }

    private static List<(int, int, int, ScenePose?, ScenePose?)> Tracks(DemoTimeline timeline, double tick) =>
    [
        .. timeline.Props.Concat(timeline.PlayerTracks)
            .Distinct()
            .Where(track => track.Alive(tick))
            .OrderBy(track => track.EntityIndex).ThenBy(track => track.SerialNumber).ThenBy(track => track.FirstTick)
            .Select(track => (track.EntityIndex, track.SerialNumber, track.FirstTick, track.At(tick), track.Held(tick))),
    ];
}
