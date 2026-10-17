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
    /// <param name="settings">The settings the windows were walked under, whose feed span the replayed events fall in;
    /// the defaults when not given.</param>
    /// <returns>A list of named answers.</returns>
    public static List<(string Question, object? Answer)> At(DemoTimeline timeline, double tick, WindowSettings? settings = null) =>
        [.. Each(timeline, tick, settings)];

    /// <summary>The same answers, each computed only when it is asked for — so a caller can spread them over frames.</summary>
    /// <param name="timeline">The timeline asked.</param>
    /// <param name="tick">The moment.</param>
    /// <param name="settings">As <see cref="At"/>.</param>
    /// <returns>The named answers, in <see cref="At"/>'s order.</returns>
    public static IEnumerable<(string Question, object? Answer)> Each(DemoTimeline timeline, double tick, WindowSettings? settings = null)
    {
        int whole = (int)System.Math.Floor(tick);

        // The two samplings first, in the order the whole list always made them: the sampler keeps state between calls.
        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        yield return ("players", players);
        yield return ("frame players", timeline.PlayersAt(whole));
        yield return ("props", props.OrderBy(prop => prop.EntityIndex).ToList());
        yield return ("tracks", Tracks(timeline, tick));
        yield return ("soundscape", timeline.SoundscapeAt(whole));
        yield return ("fog", timeline.FogAt(whole));
        yield return ("sky fog", timeline.SkyFogAt(whole));
        yield return ("director", timeline.DirectorAt(whole));
        yield return ("recorded view", timeline.RecordedViewAt(whole));
        yield return ("convars", timeline.ServerConVarsAt(whole));
        yield return ("spectator gui", timeline.SpectatorGuiShownAt(whole));
        yield return ("round state", timeline.RoundStateAt(tick));
        yield return ("rules", timeline.RulesAt(whole));
        yield return ("round timers", timeline.RoundTimersAt(whole));
        yield return ("buildings", timeline.BuildingsAt(whole));
        yield return ("teams", timeline.TeamsAt(whole));
        yield return ("scoreboard", timeline.ScoreboardPlayersAt(whole));
        yield return ("id entities", timeline.IdEntitiesAt(whole));
        yield return ("recorder team", timeline.RecorderTeamAt(whole));
        yield return ("server tick", timeline.ServerTickAt(whole));
        yield return ("viewmodels", players.Select(player => (timeline.ViewmodelAt(whole, player.EntityIndex),
            timeline.OffHandViewmodelAt(whole, player.EntityIndex))).ToList());
        yield return ("sounds", timeline.Sounds.Where(sound => sound.Tick == whole).ToList());
        yield return ("events", timeline.GameEvents.Where(fired => fired.Tick == whole).ToList());
        yield return ("messages", timeline.UserMessages.Where(message => message.Tick == whole).ToList());
        yield return ("events' world", EventsWorld(timeline, whole, settings ?? new WindowSettings()));
    }

    /// <summary>
    /// What <c>HudEventFeed.Resolve</c> asks after a seek: every event the feed replays — those within the settings'
    /// feed span, converted as the feed converts it — with the players and rules at the event's OWN tick.
    /// </summary>
    private static List<(int, IReadOnlyList<ScenePlayer>, SceneGameRules)> EventsWorld(DemoTimeline timeline, int whole, WindowSettings settings)
    {
        int span = WindowReach.FeedTicks(settings, timeline.IntervalPerTick);

        return
        [
            .. timeline.GameEvents
                .Where(fired => fired.Tick > whole - span && fired.Tick <= whole)
                .Select(fired => (fired.Tick, timeline.PlayersAt(fired.Tick), timeline.RulesAt(fired.Tick))),
        ];
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
