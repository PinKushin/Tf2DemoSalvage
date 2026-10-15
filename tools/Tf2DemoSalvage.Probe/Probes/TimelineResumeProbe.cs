using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The timeline builder snapshotted part-way through a real demo and resumed, fingerprinted beside a straight build.
/// </summary>
/// <remarks>
/// **Written for D217.** `TimelineBuilderSnapshotTests` proves the snapshot on authored demos at every command; a real
/// match is what has every feed, every track and every entity class in play at the moment of the cut. The two
/// fingerprints must match; the probe prints both and says which sections differ.
///
/// **Its control** is the value count: a fingerprint over nothing matches anything.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineResumeProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-resume";

    /// <inheritdoc/>
    public string Summary =>
        "snapshot the timeline build part-way, resume, compare with a straight build, and say what a snapshot costs: timeline-resume <demo> [fraction, default 0.5] [cost]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("timeline-resume <demo> [fraction]");
            return;
        }

        double fraction = arguments.Count > 1 ? double.Parse(arguments[1], CultureInfo.InvariantCulture) : 0.5;
        byte[] demo = File.ReadAllBytes(path);
        int commands = DemoTimeline.CommandCount(demo);
        int at = (int)(commands * fraction);

        // "cost" as a third argument measures one snapshot and skips the two full builds.
        if (arguments.Count < 3 || arguments[2] != "cost")
        {
            TimelineFingerprint straight = TimelineFingerprint.Of(DemoTimeline.Build(demo));
            TimelineFingerprint resumed = TimelineFingerprint.Of(DemoTimeline.BuildThroughSnapshot(demo, at));

            output.WriteLine($"{Path.GetFileName(path)}: snapshot after {at} of {commands} commands; {straight.Values} values straight, {resumed.Values} resumed");
            output.WriteLine($"  frames      {Same(straight.Frames, resumed.Frames)}");
            output.WriteLine($"  soundscapes {Same(straight.Soundscapes, resumed.Soundscapes)}");
            output.WriteLine($"  props       {Same(straight.Props, resumed.Props)}");
            output.WriteLine($"  collections {Same(straight.Collections, resumed.Collections)}");
            output.WriteLine(straight == resumed ? "  IDENTICAL" : "  DIFFERENT");
        }
        else
        {
            output.WriteLine($"{Path.GetFileName(path)}: snapshot after {at} of {commands} commands ({demo.Length / 1048576d:0.0} MB file)");
        }

        (long total, IReadOnlyList<(string Field, long Bytes)> fields) = DemoTimeline.MeasureSnapshot(demo, at);
        output.WriteLine($"  one snapshot allocates {total / 1048576d:0.0} MB; by field, each copied alone:");

        foreach ((string field, long bytes) in fields.Take(12))
        {
            output.WriteLine($"    {field,-32} {bytes / 1048576d,8:0.0} MB");
        }
    }

    private static string Same(string straight, string resumed) =>
        straight == resumed ? $"same {straight[..16]}" : $"DIFFERS {straight[..16]} vs {resumed[..16]}";
}
