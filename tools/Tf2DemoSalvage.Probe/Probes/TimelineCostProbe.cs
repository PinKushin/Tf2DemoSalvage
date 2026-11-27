using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

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
/// same columns when the viewer opens a demo (B265, where splitting one number took a frame from 96
/// to 447 fps), but nothing outside the viewer could ask for it — so a question about decode cost
/// meant launching a window and reading a log.
///
/// **The rows are WARM and the viewer's first open is not** (B874 reviews 3 and 4): each row is a
/// second build of its demo, after an uncounted one, so no row pays the model facts' first reads, the
/// first-run static initializers or JIT, all of which the viewer's first open of a demo pays. That
/// cold build is printed once per run, on its own labelled line: the first demo's first build, the
/// figure to set beside the viewer's log line.
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
            + "  (viewmodels) building     rest   frames  tracks  players    us/frame-track  gc-paused  allocated"
            + "  (every row WARM: a second build of its demo; the cold first build is its own line)");

        // The install opened and the watcher's interp read before any count or clock, its class scripts and model facts
        // object with it, as the viewer's are before a demo loads (B874), so neither the GC pauses nor the bytes include
        // opening it or running the configs.
        GameContent game = ProbeInstall.Game(output);
        _ = ProbeInstall.Interp();
        bool coldSaid = false;

        foreach (string asked in arguments)
        {
            string? path = DemoCorpus.Find(asked, output);

            if (path is null)
            {
                output.WriteLine($"No demo named '{asked}'.");
                continue;
            }

            byte[] file = File.ReadAllBytes(path);

            // **Every row is a WARM build** (review 3): a throwaway build of the same demo first, uncounted. The model facts
            // cache what each model tells `OnNewModel` (B870) the first time any build in this process asks, so without it
            // the first demo listed paid every model's read and a later demo sharing those models paid none — rows that
            // differed by their order on the command line. So no row counts the facts' first reads, which the viewer's
            // first open of a demo does pay (`timeline-heap` prints the heap they leave), nor the build's first-run static
            // initializers and JIT. **The first demo's throwaway is the viewer's cold open** (review 4), so it is measured as
            // a row is and printed once, labelled, apart from the rows.
            (double coldTotal, long coldAllocated, TimeSpan coldGcPaused) = Throwaway(file, game);

            if (!coldSaid)
            {
                coldSaid = true;
                output.WriteLine(
                    $"COLD first build, {Path.GetFileNameWithoutExtension(path)}: {Seconds(coldTotal):0.00}s, "
                    + $"gc-paused {coldGcPaused.TotalSeconds:0.00}s, allocated {coldAllocated / (1024d * 1024d):0}MB: this process's "
                    + "first build, paying the model facts' first reads, JIT and static initializers, as the viewer's first open does; "
                    + "not a row");
            }

            // The throwaway's garbage collected before the row's counts start (review 4), so no row's gc-paused is
            // collecting what the throwaway left: a full blocking collection, as the heap probes read the heap. The
            // throwaway timeline is unreachable here, having lived only inside `Throwaway`.
            _ = DemoCensus.LiveHeap();

            // GC pauses land in whichever column allocates; this says how much of the total they are.
            TimeSpan gcBefore = GC.GetTotalPauseDuration();

            // **Bytes allocated are the build's steadiest cost** (B613): seconds move with whatever else the machine is
            // doing, while bytes move only with the code. Counted on THIS thread, because the build runs on the calling
            // thread and Core starts none of its own, so the runtime's background threads (tiered JIT, finalizer) stay out
            // of it. Still in it: a few MB that move with timing — the process-wide count read 30,163 to 30,182 MB for one
            // binary (measurements/2026-11-14-f12-decode-allocation.md). The first-run static initializers no longer are:
            // the warming build above ran them.
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            DemoTimeline timeline = ProbeInstall.Timeline(file, game);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            TimeSpan gcPaused = GC.GetTotalPauseDuration() - gcBefore;
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
                - phases.Entities - phases.Sampling - phases.Frames;

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
                + $"{Seconds(phases.Frames),7:0.00}s "
                + $"{Seconds(rest),7:0.00}s "
                + $"{timeline.Frames.Count,8} "
                + $"{timeline.Props.Count,7} "
                + $"{timeline.PlayerTracks.Count,8} "
                + $"{perUnit,17:0.00} "
                + $"{gcPaused.TotalSeconds,9:0.00}s "
                + $"{allocated / (1024d * 1024d),9:0}MB");
        }
    }

    /// <summary>The uncounted build before a row, measured as a row is: its own total, carried, and this thread's bytes and the GC's pauses across it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (double TotalMilliseconds, long Allocated, TimeSpan GcPaused) Throwaway(byte[] file, GameContent game)
    {
        TimeSpan gcBefore = GC.GetTotalPauseDuration();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        DemoTimeline timeline = ProbeInstall.Timeline(file, game);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        return (timeline.Phases.Total, allocated, GC.GetTotalPauseDuration() - gcBefore);
    }

    private static double Megabytes(int bytes) => bytes / (1024d * 1024d);

    private static double Seconds(double milliseconds) => milliseconds / 1000d;
}
