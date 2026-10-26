using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a seek's activity-start scan costs on a real demo, through the production wiring (D233).</summary>
/// <remarks>
/// The scene and model set are the production ones: `MomentScene` hands `EntityModelSet.PlayerPropAt` its own builder
/// over `DemoTimeline.PlayerAt`, and the probe only counts the calls passing through it. A seek is a landing more than
/// `ContinuityTicks` from the last frame, so every drawn player's start is recovered; the frame after it is held.
/// </remarks>
public sealed class SeekScanProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "seek-scan";

    /// <inheritdoc/>
    public string Summary => "cost of a seek's activity-start scan over every drawn player: seek-scan <demo> [seeks]";

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

        EntityModelSet models = new() { Geometry = assets.Geometry };
        GameAppearance appearance = new(game.Classes, null);
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Appearance = appearance,
            EarlierPlayer = timeline.PlayerAt,
        };

        // Counted on the way through: the number production asked, not a second walk.
        Func<double, int, SceneProp?> builder = models.PlayerPropAt!;
        long probes = 0;
        List<(double Tick, int Entity)> asked = [];
        models.PlayerPropAt = (tick, entity) =>
        {
            probes++;
            asked.Add((tick, entity));
            return builder(tick, entity);
        };

        GC.KeepAlive(scene);

        List<ScenePlayer> players = [];
        List<SceneProp> drawn = [];

        // One frame's sequence choice at a tick, the props built as the scene builds them; answers how many were drawn.
        int Frame(int tick)
        {
            drawn.Clear();
            timeline.PlayersAt(tick, players);
            PlayerProps.Add(players, drawn, appearance, models);
            models.Add(drawn, assets.Geometry);
            models.CurrentTick = tick;
            models.UpdateClientSideAnimations(drawn);
            return drawn.Count;
        }
        int span = timeline.LastTick - timeline.FirstTick;
        List<double> seekMs = [];
        List<double> heldMs = [];
        List<int> counts = [];
        long heldBytes = 0;
        long secondHeldBytes = 0;
        long totalProbes = 0;

        for (int seek = 0; seek < seeks; seek++)
        {
            // Spread over the demo, alternately backwards and forwards so every landing is a seek.
            int landing = timeline.FirstTick + (int)((long)span * ((seek * 7919L) % seeks) / seeks);

            drawn.Clear();
            timeline.PlayersAt(landing, players);
            PlayerProps.Add(players, drawn, appearance, models);
            models.Add(drawn, assets.Geometry);

            List<SceneProp> landed = [.. drawn];
            models.CurrentTick = landing;
            probes = 0;
            long at = Stopwatch.GetTimestamp();
            models.UpdateClientSideAnimations(landed);
            seekMs.Add(Stopwatch.GetElapsedTime(at).TotalMilliseconds);
            totalProbes += probes;
            counts.Add(landed.Count);

            // The frame after: held, nothing scanned. The props as the seek left them, so the pre-existing `with` copy a
            // fresh prop takes when its sequence is written (B574's shape) is not counted against the hold.
            List<SceneProp> next = [.. landed];
            models.CurrentTick = landing + 0.5d;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            at = Stopwatch.GetTimestamp();
            models.UpdateClientSideAnimations(next);
            heldMs.Add(Stopwatch.GetElapsedTime(at).TotalMilliseconds);
            heldBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;

            // And a second held frame, which a one-off lazy build in the first cannot reach.
            models.CurrentTick = landing + 1d;
            bytes = GC.GetAllocatedBytesForCurrentThread();
            models.UpdateClientSideAnimations(next);
            secondHeldBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
        }

        // **A whole forward seek as the viewer makes it** (B504): a skip frame every 99 ticks, `SimulatesOnly`, then the
        // landing. Played to the first tick before it so the skip starts from a hold.
        int skipFrom = timeline.FirstTick + (span / 4);
        int skipTo = timeline.FirstTick + (3 * span / 4);
        Frame(skipFrom);
        probes = 0;
        long skipAt = Stopwatch.GetTimestamp();
        models.SimulatesOnly = true;
        int skipFrames = 0;

        foreach (int through in DemoSkip.Frames(skipFrom + 1, skipTo))
        {
            Frame(through);
            skipFrames++;
        }

        double skipMs = Stopwatch.GetElapsedTime(skipAt).TotalMilliseconds;
        long skipProbes = probes;
        models.SimulatesOnly = false;
        long landAt = Stopwatch.GetTimestamp();
        int skipLanded = Frame(skipTo);
        double landMs = Stopwatch.GetElapsedTime(landAt).TotalMilliseconds;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"forward skip {skipFrom}->{skipTo}: {skipFrames} skip frames {skipMs:0.0} ms with {skipProbes} probes; " +
            $"landing {landMs:0.00} ms for {skipLanded} players, {probes - skipProbes} probes"));

        // The first seek also pays the JIT; it is reported, and left out of the mean.
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {seeks} seeks, drawn players {counts.Min()}-{counts.Max()} (mean {counts.Average():0.0})"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"seek frame: first {seekMs[0]:0.00} ms, then mean {seekMs.Skip(1).Average():0.00} ms, max {seekMs.Skip(1).Max():0.00} ms; " +
            $"{totalProbes} earlier-tick probes ({(double)totalProbes / counts.Sum():0.0} a player)"));
        // The split: the same probes through the timeline's sampler alone, and how many found a drawable player.
        long started = Stopwatch.GetTimestamp();
        int present = 0;

        foreach ((double tick, int entity) in asked)
        {
            present += timeline.PlayerAt(tick, entity) is { IsAlive: true } ? 1 : 0;
        }

        double sampling = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();

        foreach ((double tick, int entity) in asked)
        {
            builder(tick, entity);
        }

        double whole = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // The control for `430e3ca6`, which made the speed share the pose's track reads: every alive probe's speed against
        // the formula as it was before, rebuilt from the public track — bit for bit, or the refactor changed a frame.
        int compared = 0;
        int differ = 0;

        foreach ((double tick, int entity) in asked)
        {
            if (timeline.PlayerAt(tick, entity) is not { IsAlive: true } player ||
                timeline.TrackFor(entity, tick) is not { } track ||
                track.At(tick) is not { } now ||
                track.At(Math.Max(0d, tick - (0.1d / Math.Max(0.001f, 0.015f)))) is not { } was)
            {
                continue;
            }

            float across = now.X - was.X;
            float along = now.Y - was.Y;
            float old = MathF.Sqrt((across * across) + (along * along)) / (float)0.1d;
            compared++;
            differ += BitConverter.SingleToInt32Bits(old) == BitConverter.SingleToInt32Bits(player.Speed) ? 0 : 1;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"speed against the pre-430e3ca6 formula: {compared} compared, {differ} differ"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{asked.Count} probes replayed: {whole:0.0} ms through the scene's builder, {sampling:0.0} ms of it PlayerAt alone; " +
            $"{present} found him alive"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"held frame after: mean {heldMs.Skip(1).Average():0.000} ms, max {heldMs.Skip(1).Max():0.000} ms, {heldBytes} B allocated over {seeks} frames, " +
            $"{secondHeldBytes} B over the {seeks} frames after those"));
    }
}
