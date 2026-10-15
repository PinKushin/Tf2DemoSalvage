using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
        "snapshot the timeline build part-way, resume, and compare with a straight build: timeline-resume <demo> [fraction, default 0.5]";

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

        TimelineFingerprint straight = TimelineFingerprint.Of(DemoTimeline.Build(demo));
        TimelineFingerprint resumed = TimelineFingerprint.Of(DemoTimeline.BuildThroughSnapshot(demo, at));

        output.WriteLine($"{Path.GetFileName(path)}: snapshot after {at} of {commands} commands; {straight.Values} values straight, {resumed.Values} resumed");
        output.WriteLine($"  frames      {Same(straight.Frames, resumed.Frames)}");
        output.WriteLine($"  soundscapes {Same(straight.Soundscapes, resumed.Soundscapes)}");
        output.WriteLine($"  props       {Same(straight.Props, resumed.Props)}");
        output.WriteLine(straight == resumed ? "  IDENTICAL" : "  DIFFERENT");
    }

    private static string Same(string straight, string resumed) =>
        straight == resumed ? $"same {straight[..16]}" : $"DIFFERS {straight[..16]} vs {resumed[..16]}";
}
