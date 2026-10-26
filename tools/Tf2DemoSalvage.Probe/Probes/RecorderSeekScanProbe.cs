using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a seek's activity-start scan costs for a POV demo's recorder, through prediction (B584, batch-31 review 2).</summary>
/// <remarks>
/// The production wiring: `TimelineMoments` as `DemoSystems.Open` builds it and `MomentScene.EarlierPlayer =
/// moments.ActivityPlayerAt` as `MomentPresenter` sets it. The recorder's calls are timed AT the call, inside the delegate
/// the scene holds; nothing is replayed to get the figure. Hooks and ground surfaces are left null (a lower bound).
/// `docs/measurements/2026-10-26-pov-recorder-seek-scan.md`.
/// </remarks>
public sealed class RecorderSeekScanProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "recorder-seek-scan";

    /// <inheritdoc/>
    public string Summary => "cost of the seek scan's recorder probes through prediction: recorder-seek-scan <demo> [seeks]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        int seeks = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 40;
        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(file).MapName;

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            output.WriteLine("control failed: the demo has no recorder (SourceTV?), so there is nothing to measure.");
            return;
        }

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        DemoPlayer demoPlayer = new(timeline);
        RecorderPrediction prediction = new(timeline, () => map.Level);
        TimelineMoments moments = new(timeline) { Player = demoPlayer, Prediction = prediction };
        EntityModelSet models = new() { Geometry = assets.Geometry };
        GameAppearance appearance = new(game.Classes, null);

        // The route under measurement, or the pre-review-7 one for the control; switched between runs of the same landings.
        bool predicted = true;
        long recorderTicks = 0;
        long recorderCalls = 0;
        List<int> recorderProbeTicks = [];

        ScenePlayer? Earlier(double tick, int entity)
        {
            if (entity != recorder)
            {
                return moments.ActivityPlayerAt(tick, entity);
            }

            long at = Stopwatch.GetTimestamp();
            ScenePlayer? answer = predicted ? moments.ActivityPlayerAt(tick, entity) : timeline.PlayerAt(tick, entity);
            recorderTicks += Stopwatch.GetTimestamp() - at;
            recorderCalls++;
            recorderProbeTicks.Add((int)Math.Floor(tick));
            return answer;
        }

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Appearance = appearance,
            EarlierPlayer = Earlier,
        };

        GC.KeepAlive(scene);

        List<ScenePlayer> players = [];
        List<SceneProp> drawn = [];

        // One frame's sequence pass at a tick, players as the viewer samples them; answers the pass's milliseconds.
        double Frame(double tick)
        {
            drawn.Clear();
            moments.PlayersAt(tick, players);
            PlayerProps.Add(players, drawn, appearance, models);
            models.Add(drawn, assets.Geometry);
            models.CurrentTick = tick;
            long at = Stopwatch.GetTimestamp();
            models.UpdateClientSideAnimations(drawn);
            return Stopwatch.GetElapsedTime(at).TotalMilliseconds;
        }

        int span = timeline.LastTick - timeline.FirstTick;
        int[] landings = [.. Enumerable.Range(0, seeks).Select(seek => timeline.FirstTick + (int)((long)span * ((seek * 7919L) % seeks) / seeks))];

        (List<double> Frame, List<double> Recorder, List<long> Calls) Seeks()
        {
            List<double> frameMs = [];
            List<double> recorderMs = [];
            List<long> calls = [];

            foreach (int landing in landings)
            {
                recorderTicks = 0;
                recorderCalls = 0;
                frameMs.Add(Frame(landing));
                recorderMs.Add(recorderTicks * 1000d / Stopwatch.Frequency);
                calls.Add(recorderCalls);

                // The held frame after it, so the next landing is a seek from a hold.
                Frame(landing + 0.5d);
            }

            return (frameMs, recorderMs, calls);
        }

        // The instrument's own control: at a mid-demo tick the recorder must be drawn and his view read, or nothing below
        // measures prediction.
        int middleTick = timeline.FirstTick + (span / 2);
        Frame(middleTick);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"control at {middleTick}: recorder drawn {drawn.Any(prop => prop.EntityIndex == recorder)}, " +
            $"in the players {players.Any(player => player.EntityIndex == recorder)} " +
            $"({players.FirstOrDefault(player => player.EntityIndex == recorder)}), drawn props {drawn.Count} of {players.Count} players, " +
            $"InterpolateViewpoint {(new DemoPlayer(timeline).InterpolateViewpoint(middleTick) is null ? "null" : "a view")}, " +
            $"ViewsAt {demoPlayer.ViewsAt(middleTick)}, view commands {timeline.ViewCommands.Count}, " +
            $"packets among them {timeline.ViewCommands.Count(command => command.IsPacket)}"));

        foreach (IGrouping<DemoCommandType, DemoViewCommand> kind in timeline.ViewCommands.GroupBy(command => command.Type))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {kind.Key}: {kind.Count()}, ticks {kind.Min(command => command.Tick)}-{kind.Max(command => command.Tick)}, " +
                $"IsPacket {kind.First().IsPacket}, default views {kind.Count(command => command.View.IsDefault)}, " +
                $"first index {timeline.ViewCommands.ToList().FindIndex(command => command.Type == kind.Key)}"));
        }

        (List<double> frames, List<double> recorderMs, List<long> calls) = Seeks();
        List<int> scanned = [.. recorderProbeTicks];

        static string Stats(List<double> values) => string.Create(CultureInfo.InvariantCulture,
            $"first {values[0]:0.00} ms, then mean {values.Skip(1).Average():0.00} ms, max {values.Skip(1).Max():0.00} ms");

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: recorder entity {recorder}, {seeks} seeks, ticks {timeline.FirstTick}-{timeline.LastTick}"));
        output.WriteLine("predicted route: seek frame " + Stats(frames) + "; its recorder probes " + Stats(recorderMs));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"recorder probes per seek: mean {calls.Skip(1).Average():0.0}, max {calls.Skip(1).Max()}, total {calls.Sum()}"));

        // Controls and counts over the probes the scan actually asked (the ticks, carried; the questions below are
        // characterisation, not the timed figure).
        IReadOnlyList<(int Tick, int Acknowledged)> packets = timeline.PacketAcknowledgements;
        int viewed = 0;
        int nonNull = 0;
        long pending = 0;
        HashSet<int> distinctPackets = [];

        foreach (int tick in scanned)
        {
            viewed += demoPlayer.ViewsAt(tick) ? 1 : 0;
            nonNull += prediction.VelocityThrough(tick) is not null ? 1 : 0;

            if (LastPacketIndex(packets, tick) is int at and >= 0)
            {
                distinctPackets.Add(at);
                pending += RecorderPrediction.Pending(timeline.UserCommands, packets[at].Acknowledged, tick).Count;
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"control: {scanned.Count} recorder probe ticks, {viewed} viewed ({100d * viewed / Math.Max(1, scanned.Count):0.0}%), " +
            $"{nonNull} with a predicted velocity ({100d * nonNull / Math.Max(1, scanned.Count):0.0}%)"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"counts: {distinctPackets.Count} distinct packets ({(double)scanned.Count / Math.Max(1, distinctPackets.Count):0.00} probe ticks a packet), " +
            $"{(double)pending / Math.Max(1, scanned.Count):0.00} pending commands a probe; {packets.Count} packets over {span} ticks"));

        // The control route: the same landings with the recorder answered off his track, as before review 7.
        predicted = false;
        (List<double> controlFrames, List<double> controlRecorder, List<long> controlCalls) = Seeks();
        predicted = true;
        output.WriteLine("track route (control): seek frame " + Stats(controlFrames) + "; its recorder probes " + Stats(controlRecorder) +
            string.Create(CultureInfo.InvariantCulture, $"; {controlCalls.Sum()} recorder probes"));

        // Worst case: the scan's cap, 2048 consecutive recorder probes back from a mid-demo tick where he is viewed and alive.
        int from = timeline.FirstTick + (span / 2);

        while (from < timeline.LastTick && !(demoPlayer.ViewsAt(from) && timeline.PlayerAt(from, recorder) is { IsAlive: true }))
        {
            from++;
        }

        int predictedInCap = 0;
        long capAt = Stopwatch.GetTimestamp();

        for (int tick = from; tick > from - ActivityScanTicks; tick--)
        {
            predictedInCap += moments.ActivityPlayerAt(tick, recorder) is not null ? 1 : 0;
        }

        double capMs = Stopwatch.GetElapsedTime(capAt).TotalMilliseconds;
        int velocities = Enumerable.Range(0, ActivityScanTicks).Count(back => prediction.VelocityThrough(from - back) is not null);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"worst case: {ActivityScanTicks} recorder probes back from {from}: {capMs:0.0} ms " +
            $"({capMs * 1000d / ActivityScanTicks:0.0} us a probe), {predictedInCap} answered, {velocities} with a predicted velocity"));

        // Fast playback: frames 12 ticks apart, past ContinuityTicks, so each scans its gap (HoldsAcross).
        List<double> fastFrames = [];
        List<double> fastRecorder = [];
        double fastTick = timeline.FirstTick + (span / 3);
        Frame(fastTick);

        for (int frame = 0; frame < 300 && fastTick + 12 < timeline.LastTick; frame++)
        {
            fastTick += 12;
            recorderTicks = 0;
            fastFrames.Add(Frame(fastTick));
            fastRecorder.Add(recorderTicks * 1000d / Stopwatch.Frequency);
        }

        output.WriteLine("fast playback, 12 ticks a frame: frame " + Stats(fastFrames) + "; its recorder probes " + Stats(fastRecorder));
    }

    /// <summary>`EntityModelSet.ActivityScanTicks`, internal to Scene: the scan's cap.</summary>
    private const int ActivityScanTicks = 2048;

    /// <summary>The index of the last packet at or before a tick, or -1.</summary>
    private static int LastPacketIndex(IReadOnlyList<(int Tick, int Acknowledged)> packets, int tick)
    {
        int low = 0;
        int high = packets.Count;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (packets[middle].Tick <= tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}
