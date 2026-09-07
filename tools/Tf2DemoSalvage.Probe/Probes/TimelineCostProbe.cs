using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Where a demo's timeline build spends its time, column by column.
/// </summary>
/// <remarks>
/// **Written because the fast gate's largest single cost is one of these builds and nobody knew
/// which column it was.** Measured 2026-09-06: `Tf2DemoSalvage.Corpus.Tests` takes 142 seconds
/// under `TF2DEMOSALVAGE_GCOR_ONLY=1`, and a single test that asks for `z1800`'s timeline takes 68
/// of them by itself. Twelve tests report ~110 seconds each, which is not twelve builds — they run
/// in parallel and block on the one `TimelineCache` entry, so the number to attack is the build,
/// not the test count.
///
/// **The columns, because the total alone says nothing about what to fix.** `DecodedDemo` logs the
/// same split when the viewer opens a demo (B265, where splitting one number took a frame from 96
/// to 447 fps), but nothing outside the viewer could ask for it — so a question about decode cost
/// meant launching a window and reading a log.
///
/// **The values are CARRIED, not recomputed** (B243). `DemoTimeline.Build` times its own phases and
/// hands them out as <see cref="TimelinePhases"/>; this prints those. A probe that re-timed the
/// phases from outside would be timing a second route, and free to disagree with the one that runs.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineCostProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-cost";

    /// <inheritdoc/>
    public string Summary =>
        "where a demo's timeline build spends its time, by column: timeline-cost <demo> [more demos]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("timeline-cost <demo> [more demos]");
            return;
        }

        output.WriteLine(
            "demo                                        MB   total  commands   schema messages entities sampling"
            + "  (viewmodels)     rest   frames  tracks  players    us/frame-track");

        foreach (string asked in arguments)
        {
            string? path = DemoCorpus.Find(asked, output);

            if (path is null)
            {
                output.WriteLine($"No demo named '{asked}'.");
                continue;
            }

            byte[] file = File.ReadAllBytes(path);

            DemoTimeline timeline = DemoTimeline.Build(file);
            TimelinePhases phases = timeline.Phases;

            // **Bytes are the WRONG denominator here and this column is why.** The committed era
            // specimens are the owner's own SOLO recordings — one player, no worn items — while a
            // modern match carries a full roster, so entity work scales with frames × tracks and
            // not with file size. Comparing seconds per megabyte across the two makes a demo look
            // superlinear when it is only denser.
            long work = (long)timeline.Frames.Count * Math.Max(timeline.Props.Count, 1);

            double perUnit = work > 0 ? phases.Entities * 1000d / work : 0d;

            // **The rest column is the subtracted one, and it is the one worth watching.** Every
            // named column is something a stopwatch was deliberately wrapped around; whatever is
            // left is work nobody thought to time, which is exactly where an unmeasured cost hides.
            double rest = phases.Total - phases.Commands - phases.Schema - phases.Messages
                - phases.Entities - phases.Sampling;

            output.WriteLine(
                $"{Path.GetFileNameWithoutExtension(path),-40} "
                + $"{Megabytes(file.Length),5:0.0} "
                + $"{Seconds(phases.Total),6:0.00}s "
                + $"{Seconds(phases.Commands),8:0.00}s "
                + $"{Seconds(phases.Schema),7:0.00}s "
                + $"{Seconds(phases.Messages),7:0.00}s "
                + $"{Seconds(phases.Entities),7:0.00}s "
                + $"{Seconds(phases.Sampling),7:0.00}s "
                + $"{Seconds(phases.Viewmodels),12:0.00}s "
                + $"{Seconds(rest),7:0.00}s "
                + $"{timeline.Frames.Count,8} "
                + $"{timeline.Props.Count,7} "
                + $"{timeline.PlayerTracks.Count,8} "
                + $"{perUnit,17:0.00}");
        }
    }

    private static double Megabytes(int bytes) => bytes / (1024d * 1024d);

    private static double Seconds(double milliseconds) => milliseconds / 1000d;
}
