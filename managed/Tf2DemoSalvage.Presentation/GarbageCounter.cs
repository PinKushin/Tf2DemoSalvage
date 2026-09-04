using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One reading of the runtime's collection counters.</summary>
/// <param name="Gen0">Gen 0 collections since the process started.</param>
/// <param name="Gen1">Gen 1 collections since the process started.</param>
/// <param name="Gen2">Gen 2 collections since the process started.</param>
/// <param name="Paused">Time spent with managed threads suspended, since the process started.</param>
/// <param name="Allocated">Bytes allocated on the managed heap since the process started.</param>
/// <remarks>
/// **Every field is a TOTAL, not a rate**, which is the trap the counter exists to avoid: printed
/// raw they grow all session and read as a leak.
/// </remarks>
public readonly record struct GarbageReading(
    int Gen0, int Gen1, int Gen2, TimeSpan Paused, long Allocated)
{
    /// <summary>Reads the counters as they stand now.</summary>
    /// <returns>The current totals.</returns>
    /// <remarks>
    /// **The only part of this file that touches <see cref="GC"/>**, and it is separated for exactly
    /// that reason: a counter that read the runtime itself could only be tested by persuading the
    /// runtime to collect on cue.
    ///
    /// <c>GC.GetTotalPauseDuration()</c> is the runtime's own accounting of time spent with threads
    /// suspended, so a pause reported here is not inferred from a gap in the log — it is the pause,
    /// reported by the thing that caused it. The same holds for the byte count: it is the runtime's
    /// own total, not a sum of allocations this code knows about, so it cannot miss a caller.
    ///
    /// **<c>precise: false</c>**, which reads each thread's own allocation context rather than
    /// suspending to total them exactly. The imprecision is bounded by one allocation buffer per
    /// thread — kilobytes against the tens of megabytes a second being measured — and the precise
    /// form would make reading the counter cost the very pause the counter beside it reports.
    /// </remarks>
    public static GarbageReading FromRuntime() =>
        new(
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalPauseDuration(),
            GC.GetTotalAllocatedBytes(precise: false));
}

/// <summary>Collections and pause time since the previous reading.</summary>
/// <remarks>
/// **The instrument for a stall that is not a frame rate drop**, which is the owner's exact
/// description of B163: *"the stutter isnt in engine fps, its stutter across the whole app, the fps
/// doesnt drop, everything freezes for a half a second to maybe a second sometimes"*.
///
/// A blocking gen2 collection does precisely that. It suspends every managed thread, so the window
/// stops pumping and nothing is drawn — and because the frames on either side are as fast as ever,
/// the AVERAGE rate barely moves. That is why a frame counter alone cannot see it, and why the pair
/// of numbers matters more than either alone.
///
/// **This was `MainForm.GarbageThisSecond` and a tuple field beside it** (B188, D90). Nothing in it
/// was ever about a window.
/// </remarks>
public sealed class GarbageCounter
{
    /// <summary>Below this, a pause is drift rather than a stall.</summary>
    /// <remarks>
    /// **A quiet second must stay one line.** Sub-millisecond pause drift happens constantly, and
    /// printing it would put a `gc 0/0/0` on nearly every line in the log — drowning the seconds
    /// where something actually happened, which are the only ones worth reading.
    /// </remarks>
    private static readonly TimeSpan Noise = TimeSpan.FromMilliseconds(1);

    /// <summary>The unit allocation is reported in.</summary>
    /// <remarks>
    /// **Megabytes because the measured rate is tens to hundreds of them a second** (B262), so bytes
    /// would print nine digits and kilobytes six, on a line already carrying six other numbers.
    /// </remarks>
    private const long Megabyte = 1024L * 1024L;

    private GarbageReading? _previous;

    /// <summary>What has happened since the last call, or empty when nothing has.</summary>
    /// <param name="now">The current totals, usually <see cref="GarbageReading.FromRuntime"/>.</param>
    /// <returns>A suffix for the frame line, or an empty string.</returns>
    /// <remarks>
    /// **Empty on the first call, deliberately.** With no previous reading the delta would be
    /// "everything since process start", so the first line of every session would attribute the
    /// startup collections to that one second — a large number in the log's most-read line, which is
    /// not so much wrong as meaningless.
    /// </remarks>
    public string Since(GarbageReading now)
    {
        GarbageReading? was = _previous;

        _previous = now;

        if (was is not { } previous)
        {
            return string.Empty;
        }

        (int Gen0, int Gen1, int Gen2, TimeSpan Paused, long Allocated) since = (
            now.Gen0 - previous.Gen0,
            now.Gen1 - previous.Gen1,
            now.Gen2 - previous.Gen2,
            now.Paused - previous.Paused,
            now.Allocated - previous.Allocated);

        // **`&&`, not `||`, and the difference is a whole class of stall.** Pause time can grow
        // without any COUNT moving — a single long gen2 that began before this second and finished
        // inside it. An `||` here would discard exactly the seconds worth reporting.
        //
        // **Allocation is deliberately NOT part of this test**, though it is reported below. Every
        // frame allocates, so a second reported because bytes moved is a second reported always, and
        // the suffix would land on every line in the log — drowning the ones that matter, which is
        // the outcome this guard exists to prevent. A second quiet enough not to provoke a collection
        // allocated less than the gen 0 budget, and the empty line already says that.
        if (since is { Gen0: 0, Gen1: 0, Gen2: 0 } && since.Paused < Noise)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"; gc {since.Gen0}/{since.Gen1}/{since.Gen2} paused {since.Paused.TotalMilliseconds:0.#} ms" +
            $", allocated {since.Allocated / (double)Megabyte:0.#} MB");
    }
}
