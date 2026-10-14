using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which field of a built timeline holds its memory, by releasing them one at a time.
/// </summary>
/// <remarks>
/// **Written for the checkpointed timeline (D217)**, because `dotnet-gcdump report` on a 3 GB timeline
/// summed to 200 MB of it (B407 already found the report unreliable at this size). This needs no
/// heap reader: it builds the timeline through `DemoTimeline.Build`, then sets each reference field
/// to null by reflection — first each field of every `ScenePropTrack` (across all tracks at once),
/// then each field of the timeline — and prints the live heap released by each step after a forced
/// compacting collection. An object shared by two fields is charged to the one released second.
///
/// **The control is the sum**: the steps must add up to the timeline's total, and the last line
/// prints what is left, which must be near zero. A step that releases nothing is a field that holds
/// nothing, not an instrument fault, only while the sum closes.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineHoldersProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-holders";

    /// <inheritdoc/>
    public string Summary =>
        "live heap each field of a built timeline holds, released one at a time: timeline-holders <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("timeline-holders <demo>");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        long before = Live();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        long total = Live() - before;
        output.WriteLine($"{Path.GetFileName(path)}: {new FileInfo(path).Length / 1048576d:0.0} MB file, timeline {Mb(total):0} MB");
        output.WriteLine(
            $"  inline sizes: ScenePlayer {System.Runtime.CompilerServices.Unsafe.SizeOf<ScenePlayer>()} B, "
            + $"TimelineFrame {System.Runtime.CompilerServices.Unsafe.SizeOf<TimelineFrame>()} B, "
            + $"ScenePose {System.Runtime.CompilerServices.Unsafe.SizeOf<ScenePose>()} B, "
            + $"SceneSoundscape {System.Runtime.CompilerServices.Unsafe.SizeOf<SceneSoundscape>()} B; "
            + $"frames {timeline.Frames.Count}, player-frames {timeline.Frames.Sum(frame => frame.Players.Count)}, "
            + $"soundscape samples {timeline.Soundscapes.Count}");

        List<ScenePropTrack> tracks = [.. timeline.Props.Concat(timeline.PlayerTracks).Distinct()];
        long released = 0;
        long last = Live();

        foreach (FieldInfo field in Fields(typeof(ScenePropTrack)))
        {
            foreach (ScenePropTrack track in tracks)
            {
                field.SetValue(track, null);
            }

            long now = Live();
            released += last - now;
            output.WriteLine($"  ScenePropTrack.{field.Name,-32} {Mb(last - now),8:0} MB  ({tracks.Count} tracks)");
            last = now;
        }

        foreach (FieldInfo field in Fields(typeof(DemoTimeline)))
        {
            field.SetValue(timeline, null);
            long now = Live();
            released += last - now;
            output.WriteLine($"  DemoTimeline.{field.Name,-34} {Mb(last - now),8:0} MB");
            last = now;
        }

        output.WriteLine($"  released {Mb(released):0} MB of {Mb(total):0} MB; left {Mb(last - before):0} MB");
        GC.KeepAlive(timeline);
        GC.KeepAlive(tracks);
    }

    // Reaching private fields is the whole instrument: it releases what the type keeps to itself.
#pragma warning disable S3011
    private static IEnumerable<FieldInfo> Fields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.FieldType.IsValueType);
#pragma warning restore S3011

    private static long Live()
    {
#pragma warning disable S1215
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
        return GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes;
    }

    private static double Mb(long bytes) => bytes / 1048576d;
}
