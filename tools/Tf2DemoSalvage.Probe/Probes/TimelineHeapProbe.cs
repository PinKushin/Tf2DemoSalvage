using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How much managed heap one demo's timeline holds once it is built, and a pause to census it.
/// </summary>
/// <remarks>
/// **Written for B433**: the viewer logged a 12 GB managed heap after loading `z1800`, and the log
/// line cannot say which structure holds it. This builds the timeline through the production path
/// (`DemoTimeline.Build`, as the viewer's load does), forces a full compacting
/// collection, and prints the live heap — the timeline's share, since nothing else is alive. With
/// `--wait` it then prints its process id and blocks on stdin, so `dotnet-gcdump collect -p PID`
/// can take a by-type census of exactly that state.
///
/// `--map` adds the map through `LoadedMap.Read`, the call `LevelSystems` makes. **What it still
/// misses**: the viewer's packed entity models (`EntityModelSet`, in the viewer assembly), which the
/// viewer's own log line reports separately, and GPU-side copies.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineHeapProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-heap";

    /// <inheritdoc/>
    public string Summary =>
        "live managed heap held by a built timeline, optionally paused for dotnet-gcdump: timeline-heap <demo> [--map] [--wait]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("timeline-heap <demo> [--wait]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        // The control: the heap before anything is built, so the difference is the timeline's.
        long before = Live();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        long after = Live();

        output.WriteLine(
            $"{Path.GetFileNameWithoutExtension(path)}: frames {timeline.Frames.Count}, props {timeline.Props.Count}, "
            + $"players {timeline.PlayerTracks.Count}; live heap {Mb(before):0} MB before, {Mb(after):0} MB after "
            + $"(timeline holds {Mb(after - before):0} MB)");

        // The map the viewer reads next, through the same `LoadedMap.Read` (texture quality 0, as the other probes pass).
        LoadedMap? map = null;
        string mapName = DemoHeader.Parse(File.ReadAllBytes(path)).MapName;
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Contains("--map")
            && locator.Find(mapName) is { } mapPath
            && locator.FindGameFolder() is { } folder)
        {
            GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
            map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);
            // What the viewer's "memory after load" line read before D199: the heap as the LAST collection left it, of
            // whatever kind, counting every dead object no full collection had reached. The viewer now collects first.
            GCMemoryInfo asLogged = GC.GetGCMemoryInfo();
            output.WriteLine(
                $"with {mapName}, read as the viewer's log line reads it: managed heap {Mb(asLogged.HeapSizeBytes):0} MB "
                + $"(committed {Mb(asLogged.TotalCommittedBytes):0} MB, last GC gen{asLogged.Generation})");

            long withMap = Live();
            output.WriteLine($"with {mapName}: live heap {Mb(withMap):0} MB (map and game content hold {Mb(withMap - after):0} MB)");

            // What the viewer does once the world is on the device (B407): the textures' pixels go.
            map.Assets?.ReleaseUploaded();
            long released = Live();
            output.WriteLine($"after the viewer's post-upload release: live heap {Mb(released):0} MB");
        }

        if (arguments.Contains("--wait"))
        {
            output.WriteLine($"pid {Environment.ProcessId}; census now, then press Enter");
            output.Flush();
            _ = Console.ReadLine();
        }

        GC.KeepAlive(timeline);
        GC.KeepAlive(map);
    }

    private static long Live()
    {
        // A census wants the LIVE set, and only a forced full collection makes the heap size mean that.
#pragma warning disable S1215
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
        return GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes;
    }

    private static double Mb(long bytes) => bytes / 1048576d;
}
