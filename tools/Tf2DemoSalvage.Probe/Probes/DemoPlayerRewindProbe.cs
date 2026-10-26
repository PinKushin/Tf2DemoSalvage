using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a rewind of the viewer's <see cref="DemoPlayer"/> costs: milliseconds and commands re-read (batch-31 review 2).</summary>
/// <remarks>
/// The production path: one `DemoPlayer` over the timeline, played to the end, then asked a landing earlier — which is
/// `Restart` and the skip's re-read. Timed at the call; the re-read count is the player's own `CommandsRead`, carried
/// (B243). The control is a fresh player at the same landing, which must answer the same view.
/// `docs/measurements/2026-10-26-pov-recorder-seek-scan.md` (task 2).
/// </remarks>
public sealed class DemoPlayerRewindProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "demo-player-rewind";

    /// <inheritdoc/>
    public string Summary => "cost of a DemoPlayer rewind, ms and commands re-read: demo-player-rewind <demo> [rewinds]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!ProbeArguments.TryDemo(arguments, Summary, output, out string path) ||
            !ProbeArguments.TryCount(arguments, 1, 40, 2, output, out int rewinds))
        {
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int span = timeline.LastTick - timeline.FirstTick;
        double end = timeline.LastTick - 0.5d;
        int[] landings = [.. Enumerable.Range(0, rewinds).Select(at => timeline.FirstTick + (int)((long)span * ((at * 7919L) % rewinds) / rewinds))];

        DemoPlayer player = new(timeline);
        List<double> milliseconds = [];
        List<long> reads = [];
        int mismatches = 0;

        foreach (int landing in landings)
        {
            player.InterpolateViewpoint(end);
            long readBefore = player.CommandsRead;
            long at = Stopwatch.GetTimestamp();
            RecordedView? view = player.InterpolateViewpoint(landing + 0.25d);
            milliseconds.Add(Stopwatch.GetElapsedTime(at).TotalMilliseconds);
            reads.Add(player.CommandsRead - readBefore);

            // The whole state a fresh read lands in, not just the view: the parse-ahead list and m_bInterpolateView.
            DemoPlayer fresh = new(timeline);
            bool same = view == fresh.InterpolateViewpoint(landing + 0.25d) &&
                player.QueuedPackets.SequenceEqual(fresh.QueuedPackets) &&
                player.InterpolatesView == fresh.InterpolatesView;
            mismatches += same ? 0 : 1;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {rewinds} rewinds from {end} over {timeline.ViewCommands.Count} view commands, ticks {timeline.FirstTick}-{timeline.LastTick}, " +
            $"simple {(player.IsSimple ? "yes" : "no")}, recorder {(timeline.RecorderEntityIndex is null ? "none (SourceTV)" : "yes")}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"rewind: first {milliseconds[0]:0.000} ms, then mean {milliseconds.Skip(1).Average():0.000} ms, max {milliseconds.Skip(1).Max():0.000} ms; " +
            $"commands re-read: first {reads[0]}, then mean {reads.Skip(1).Average():0.0}, max {reads.Skip(1).Max()}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"control: {mismatches} of {rewinds} landings differ from a fresh player in view, queued packets or interpolate-view"));

        if (mismatches > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
