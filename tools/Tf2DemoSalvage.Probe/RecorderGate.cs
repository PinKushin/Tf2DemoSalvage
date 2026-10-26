using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Probe;

/// <summary>How the seek scan answers the viewed recorder: the routes the B602 hybrid is measured among.</summary>
public enum RecorderRoute
{
    /// <summary>Prediction on every probe — the route before the hybrid, the reference.</summary>
    Exact,

    /// <summary>The networked velocity on every probe — the hybrid's cheap half alone.</summary>
    Networked,

    /// <summary>His track's differenced positions — the pre-review-7 route.</summary>
    Track,

    /// <summary>
    /// The owner's hybrid, emulated here (not shipped, B602): networked unless within <see cref="RecorderGate.Margin"/> of
    /// `MOVING_MINIMUM_SPEED`, then exact.
    /// </summary>
    Hybrid,
}

/// <summary>The recorder's speed on each route at one probed tick.</summary>
/// <param name="Tick">The whole tick the scan probed.</param>
/// <param name="Exact">Prediction's speed (or the networked one where prediction declines), as before the hybrid.</param>
/// <param name="Networked">The networked velocity's speed.</param>
/// <param name="Track">His track's speed.</param>
public readonly record struct RecorderSpeeds(int Tick, float Exact, float Networked, float Track);

/// <summary>One route over a set of seek landings.</summary>
/// <param name="Sequences">The recorder's main sequence each landing chose, or null where he was not drawn.</param>
/// <param name="RecorderMs">Milliseconds of each landing's recorder probes, timed at the call.</param>
/// <param name="ProbeTicks">Every whole tick the scan asked of the recorder, in order.</param>
public sealed record RouteRun(IReadOnlyList<int?> Sequences, IReadOnlyList<double> RecorderMs, IReadOnlyList<int> ProbeTicks);

/// <summary>Runs the seek scan over a POV match by any <see cref="RecorderRoute"/>, as the viewer runs it (B602).</summary>
/// <remarks>
/// The production wiring of `RecorderSeekScanProbe`: `TimelineMoments` as `DemoSystems.Open` builds it and the scene's
/// `EarlierPlayer` as `MomentPresenter` sets it. Each route gets its own model set, so no main-sequence cache crosses.
/// <see cref="RecorderRoute.Exact"/> is production's route (`TimelineMoments.ActivityPlayerAt`) recomposed from the
/// timeline's public pieces, so the others can be put beside it in one build.
/// </remarks>
public sealed class RecorderGate
{
    private readonly PovMatch _match;
    private readonly TimelineMoments _moments;
    private readonly DemoPlayer _player;
    private readonly RecorderPrediction _prediction;
    private readonly GameAppearance _appearance;

    /// <summary>Over one match, its prediction wired with the viewer's item hooks and ground surfaces (B450).</summary>
    public RecorderGate(PovMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        _match = match;
        _moments = match.Moments(out _player, out _prediction, wired: true);
        _appearance = new GameAppearance(match.Game.Classes, null);
    }

    /// <summary>`seek-scan`'s landings: <paramref name="count"/> spread over the demo, shifted by a fraction of a stride.</summary>
    /// <param name="count">How many.</param>
    /// <param name="shift">0 for `seek-scan`'s own; another fraction for a set disjoint from it.</param>
    public int[] Landings(int count, double shift)
    {
        DemoTimeline timeline = _match.Timeline;
        double span = timeline.LastTick - timeline.FirstTick;

        return [.. Enumerable.Range(0, count).Select(seek => timeline.FirstTick + (int)(span * ((((seek * 7919L) % count) + shift) / count)))];
    }

    /// <summary>The recorder on one route at one moment, as the scan asks.</summary>
    public ScenePlayer? Recorder(RecorderRoute route, double tick)
    {
        DemoTimeline timeline = _match.Timeline;
        int entity = _match.Recorder;
        int whole = (int)Math.Floor(tick);
        bool viewed = _player.ViewsAt(whole);

        return route switch
        {
            RecorderRoute.Hybrid => Recorder(RecorderRoute.Networked, tick) is { IsAlive: true, Speed: var speed } && !Decides(speed, Margin)
                ? Recorder(RecorderRoute.Exact, tick)
                : Recorder(RecorderRoute.Networked, tick),
            RecorderRoute.Track => timeline.PlayerAt(tick, entity),
            RecorderRoute.Networked => viewed ? timeline.PlayerAt(tick, entity, recorderViewed: true, null) : timeline.PlayerAt(tick, entity),
            _ => viewed ? timeline.PlayerAt(tick, entity, recorderViewed: true, _prediction.VelocityThrough(whole)) : timeline.PlayerAt(tick, entity),
        };
    }

    /// <summary>The recorder's speed on the three underlying routes at a probed tick, or null where he is dead, absent or unviewed.</summary>
    public RecorderSpeeds? Speeds(int tick) =>
        _player.ViewsAt(tick) &&
        Recorder(RecorderRoute.Exact, tick) is { IsAlive: true } exact &&
        Recorder(RecorderRoute.Networked, tick) is { } networked &&
        Recorder(RecorderRoute.Track, tick) is { } track
            ? new RecorderSpeeds(tick, exact.Speed, networked.Speed, track.Speed)
            : null;

    /// <summary>The recorder's speed as the FRAME draws him at whole ticks, asked in ascending order (`PlayersAt`, its own source).</summary>
    public IReadOnlyDictionary<int, float> FrameSpeeds(IEnumerable<int> ticks)
    {
        TimelineMoments frame = _match.Moments(out _, out _, wired: true);
        List<ScenePlayer> players = [];
        Dictionary<int, float> speeds = [];

        foreach (int tick in ticks.Distinct().Order())
        {
            frame.PlayersAt(tick, players);

            if (players.FirstOrDefault(player => player.EntityIndex == _match.Recorder) is { IsAlive: true } recorder)
            {
                speeds[tick] = recorder.Speed;
            }
        }

        return speeds;
    }

    /// <summary>The recorder's networked speed at the first packet after <paramref name="tick"/> — the server's answer — or null.</summary>
    public float? NextPacketSpeed(int tick)
    {
        IReadOnlyList<(int Tick, int Acknowledged)> packets = _match.Timeline.PacketAcknowledgements;
        int next = packets.Select(packet => packet.Tick).FirstOrDefault(at => at > tick, -1);

        return next >= 0 && Recorder(RecorderRoute.Networked, next) is { IsAlive: true } player ? player.Speed : null;
    }

    /// <summary>The seek frame at each landing, then its held frame, by one route.</summary>
    public RouteRun Run(RecorderRoute route, IReadOnlyList<int> landings)
    {
        ArgumentNullException.ThrowIfNull(landings);

        int recorder = _match.Recorder;
        long recorderTicks = 0;
        List<int> probeTicks = [];

        ScenePlayer? Earlier(double tick, int entity)
        {
            if (entity != recorder)
            {
                return _moments.ActivityPlayerAt(tick, entity);
            }

            long at = Stopwatch.GetTimestamp();
            ScenePlayer? answer = Recorder(route, tick);
            recorderTicks += Stopwatch.GetTimestamp() - at;
            probeTicks.Add((int)Math.Floor(tick));
            return answer;
        }

        EntityModelSet models = new() { Geometry = _match.Assets.Geometry };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Appearance = _appearance, EarlierPlayer = Earlier };
        GC.KeepAlive(scene);

        List<ScenePlayer> players = [];
        List<SceneProp> drawn = [];
        List<int?> sequences = [];
        List<double> recorderMs = [];

        void Frame(double tick)
        {
            drawn.Clear();
            _moments.PlayersAt(tick, players);
            PlayerProps.Add(players, drawn, _appearance, models);
            models.Add(drawn, _match.Assets.Geometry);
            models.CurrentTick = tick;
            models.UpdateClientSideAnimations(drawn);
        }

        foreach (int landing in landings)
        {
            recorderTicks = 0;
            Frame(landing);
            recorderMs.Add(recorderTicks * 1000d / Stopwatch.Frequency);
            sequences.Add(drawn.FirstOrDefault(prop => prop.EntityIndex == recorder)?.Pose.Sequence);
            Frame(landing + 0.5d);
        }

        return new RouteRun(sequences, recorderMs, probeTicks);
    }

    /// <summary>The emulated hybrid's band half-width around `MOVING_MINIMUM_SPEED`, in units a second.</summary>
    public float Margin { get; set; }

    /// <summary>
    /// Whether a cheap speed decides the activity alone: farther than <paramref name="margin"/> from `MOVING_MINIMUM_SPEED`
    /// 0.5 (`multiplayer_animstate.cpp:33`), the one threshold speed meets on the way to the main activity — `&gt;` in
    /// `HandleMoving` (`:938`; TF `tf_playeranimstate.cpp:1302`, `:1314`), `&lt;` in `HandleDucking` (`:1351`); TF2 has no walk
    /// (`multiplayer_animstate.cpp:935`).
    /// </summary>
    public static bool Decides(float cheapSpeed, float margin) =>
        MathF.Abs(cheapSpeed - PlayerActivityState.MovingMinimumSpeed) > margin;

    /// <summary>Whether two speeds pick the same activity: the same side of `MOVING_MINIMUM_SPEED` for `&gt;` and for `&lt;`.</summary>
    public static bool SameSide(float a, float b) =>
        (a > PlayerActivityState.MovingMinimumSpeed) == (b > PlayerActivityState.MovingMinimumSpeed) &&
        (a < PlayerActivityState.MovingMinimumSpeed) == (b < PlayerActivityState.MovingMinimumSpeed);
}
