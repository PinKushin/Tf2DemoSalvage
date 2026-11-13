using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a rewind (D246) waits on, through the production path: `FramePresenter`'s rebuild, a pre-roll of
/// <see cref="FramePresenter.RewindPreRollTicks"/> ticks, timed by the presenter's own log line, and a backward scrub drag
/// whose every event is a jump (B727).
/// </summary>
/// <remarks>
/// **The production path, as `seek-pose` wires it**: `FramePresenter` over `MomentPresenter`, `TimelineMoments` and
/// `EntityModelSet`, the show callback being the window's (`moments.Show`), the map's geometry loaded so every entity
/// poses. No device: the pre-roll poses with `ViewFrustum.Nothing` and draws nothing in the viewer either. No sound
/// scripts, so the opening forward seek shows its tick without walking (a rewind does not read them).
///
/// The rebuild cost printed is the presenter's own (`rewind rebuilt entities from`, carried from the call, B243); the
/// wall time around each `ShowMoment` adds the target's own show. **The control** is one played step back (no jump),
/// which must log no rebuild and costs one show: the per-tick baseline the pre-roll's ticks are compared to.
///
/// <code>
///   rewind-cost f12 60000                 five rewinds of 600 ticks from 60000, then a drag of 30 events 20 ticks apart
///   rewind-cost f12 60000 600 5 30 20     the same, every number named
/// </code>
/// </remarks>
public sealed class RewindCostProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "rewind-cost";

    /// <inheritdoc/>
    public string Summary =>
        "ms a rewind's entity rebuild costs, and a backward scrub drag per event, through FramePresenter (B727): " +
        "rewind-cost <demo> <tick> [back 600] [rewinds 5] [drag events 30] [drag step 20]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        int Arg(int index, int fallback) =>
            arguments.Count > index ? int.Parse(arguments[index], CultureInfo.InvariantCulture) : fallback;

        int tick = Arg(1, 0);
        int back = Arg(2, 600);
        int rewinds = Arg(3, 5);
        int dragEvents = Arg(4, 30);
        int dragStep = Arg(5, 20);

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Core.Container.DemoHeader.Parse(file).MapName;

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
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Lighting = map.Lighting,
            Weapons = game.Weapons,
            StaticProps = assets.StaticModels,
            SequenceEra = SequenceEras.Shipped.EraFor(timeline.NetworkProtocol),
            Viewmodels = new TimelineViewmodels(timeline),
        };
        MomentPresenter moments = new(moment, new FrameLedger(), NullLogger.Instance);

        moments.Appearances = new PlayerAppearances(NullLogger.Instance) { Game = game, Timeline = timeline };
        moments.Source = new TimelineMoments(timeline);
        models.ModelResolved = (entity, looping, staticProp, sequenceLoops) =>
            moments.Source?.OnNewModel(entity, looping, staticProp, sequenceLoops);

        SoundPresenter sound = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
            new ActiveLoops(),
            static _ => null,
            NullLogger.Instance);
        LineLogger render = new();
        FramePresenter frames = new(models, moment, moments, sound, render, NullLogger.Instance, NullLogger.Instance)
        {
            RewindsAtOnce = true,
        };

        ViewFrame Frame(double shown) =>
            new(timeline, null, null, (int)shown, shown, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50);

        ViewFrame Show(double shown)
        {
            moments.Show(shown, new MomentView((int)shown, false, null, null, 54f, Playing: false));
            return Frame(shown);
        }

        int jumps = 0;

        // One move: a jump or a played step, the wall time around it and the rebuild line it logged, if any.
        (double Wall, string? Line) Move(int to, bool jump)
        {
            render.Lines.Clear();

            long began = Stopwatch.GetTimestamp();

            frames.ShowMoment(Frame(to), to, null, Show, jump ? ++jumps : jumps);
            moments.PoseNow();

            double wall = Stopwatch.GetElapsedTime(began).TotalMilliseconds;

            return (wall, render.Lines.FirstOrDefault(static line => line.StartsWith(FramePresenter.RewindRebuiltLine, StringComparison.Ordinal)));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}, ticks {timeline.FirstTick}-{timeline.LastTick}, pre-roll {FramePresenter.RewindPreRollTicks}"));

        // The opening seek: a forward jump from nothing, shown at once with no scripts to deal.
        (double opening, _) = Move(tick, jump: true);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"opening at {tick}: {opening:0.0} ms"));

        // The control: played steps back, no jump, one show each and no rebuild.
        List<double> played = [];

        for (int step = 1; step <= 10; step++)
        {
            (double wall, string? line) = Move(tick - step, jump: false);

            played.Add(wall);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  played step to {tick - step}: {wall:0.00} ms{(line is null ? ", no rebuild (control pass)" : $", CONTROL FAIL: {line}")}"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"played step back: median {Median(played):0.00} ms (one show and pose)"));

        int at = tick - 10;
        List<double> rebuilds = [];

        for (int rewind = 0; rewind < rewinds; rewind++)
        {
            at = Math.Max(timeline.FirstTick, at - back);

            (double wall, string? line) = Move(at, jump: true);
            double? rebuilt = RebuildMs(line);

            if (rebuilt is { } ms)
            {
                rebuilds.Add(ms);
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  rewind to {at}: {wall:0.0} ms in all; {line ?? "NO REBUILD LOGGED"}"));
        }

        if (rebuilds.Count > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"rewind rebuild: median {Median(rebuilds):0.0} ms, {Median(rebuilds) / FramePresenter.RewindPreRollTicks:0.000} ms a pre-roll tick, first {rebuilds[0]:0.0} ms"));
        }

        // A backward scrub drag: every event a jump (`OnScrubbed`), each its own rewind.
        List<double> drag = [];
        int rebuiltEvents = 0;

        for (int dragged = 0; dragged < dragEvents; dragged++)
        {
            at = Math.Max(timeline.FirstTick, at - dragStep);

            (double wall, string? line) = Move(at, jump: true);

            drag.Add(wall);
            rebuiltEvents += line is null ? 0 : 1;
        }

        if (drag.Count > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"drag back, in the call: {drag.Count} events {dragStep} ticks apart, {rebuiltEvents} rebuilt, median {Median(drag):0.0} ms an event, max {drag.Max():0.0}, total {drag.Sum():0} ms"));
        }

        // The same drag as the viewer runs it: the rebuild spread over drawn frames, one frame (`StepSkip`, then the window's pose)
        // after each event, and the frames the last event needs to land.
        frames.SkipsAcrossFrames = true;

        ViewFrame HostFrame() => Frame(at);

        List<double> spread = [];
        int spreadRebuilt = 0;

        for (int dragged = 0; dragged < dragEvents; dragged++)
        {
            at = Math.Max(timeline.FirstTick, at - dragStep);

            render.Lines.Clear();

            long began = Stopwatch.GetTimestamp();

            // The event (`OnScrubbed` → `ShowMoment`), then the frame the window draws after it.
            frames.ShowMoment(Frame(at), at, null, Show, ++jumps);
            frames.StepSkip(HostFrame());
            moments.PoseNow();

            spread.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            spreadRebuilt += render.Lines.Exists(static line => line.StartsWith(FramePresenter.RewindRebuiltLine, StringComparison.Ordinal)) ? 1 : 0;
        }

        List<double> landing = [];
        string? landed = null;
        string lastTarget = string.Create(CultureInfo.InvariantCulture, $" before {at} in ");

        for (int frame = 0; frame < 1000 && frames.Skipping; frame++)
        {
            render.Lines.Clear();

            long began = Stopwatch.GetTimestamp();

            frames.StepSkip(HostFrame());
            moments.PoseNow();

            landing.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            landed ??= render.Lines.FirstOrDefault(line => line.StartsWith(FramePresenter.RewindRebuiltLine, StringComparison.Ordinal) && line.Contains(lastTarget, StringComparison.Ordinal));
        }

        if (spread.Count > 0 && landing.Count > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"drag back, over frames ({FramePresenter.RewindTicksPerFrame} ticks a frame): {spread.Count} events, {spreadRebuilt} landed within their own frame, median {Median(spread):0.0} ms an event and its frame, max {spread.Max():0.0}; " +
                $"the last landed after {landing.Count} more frames, median {Median(landing):0.0} ms, max {landing.Max():0.0}; {landed ?? "NO REBUILD LOGGED"}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  each event and its frame, in order: {string.Join(" ", spread.Select(static ms => ms.ToString("0.0", CultureInfo.InvariantCulture)))}"));
        }
    }

    /// <summary>The milliseconds the presenter's rebuild line carries, or null when none was logged.</summary>
    private static double? RebuildMs(string? line)
    {
        if (line is null)
        {
            return null;
        }

        string[] words = line.Split(' ');
        int unit = Array.LastIndexOf(words, "ms");

        return unit > 0 ? double.Parse(words[unit - 1], CultureInfo.InvariantCulture) : null;
    }

    private static double Median(List<double> values)
    {
        List<double> sorted = [.. values.Order()];

        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2d;
    }

    /// <summary>Keeps every line logged, for the probe to read the presenter's own (B243).</summary>
    private sealed class LineLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add(formatter(state, exception));
        }
    }
}
