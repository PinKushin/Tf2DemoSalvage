using System.Diagnostics;
using System.Globalization;

namespace Tf2DemoSalvage.Logging;

/// <summary>What writing log lines has cost, summed, in stopwatch ticks (B595).</summary>
/// <remarks>
/// Since D230 the two readings use different fields: the CALLER's (<c>FileLogWriter.ThreadTiming</c>) has the enqueue in
/// <c>WaitTicks</c> and zero flush; the WRITER's (<c>FileLogWriter.Timing</c>) has zero wait and counts batches.
/// </remarks>
/// <param name="Writes">Lines written.</param>
/// <param name="WaitTicks">The caller's enqueue; zero on the writer's reading.</param>
/// <param name="FlushTicks">The writer's write-and-flush of each batch; zero on the caller's reading.</param>
/// <param name="SlowWrites">Enqueues, or batches, whose wait plus flush exceeded <see cref="SlowMilliseconds"/>.</param>
public readonly record struct LogWriteTiming(long Writes, long WaitTicks, long FlushTicks, long SlowWrites)
{
    /// <summary>The threshold a write must exceed to count as slow.</summary>
    public const double SlowMilliseconds = 5d;

    private static readonly long SlowTicks = (long)(SlowMilliseconds * Stopwatch.Frequency / 1000d);

    /// <summary>One write's timing.</summary>
    /// <param name="wait">Ticks spent waiting for the lock.</param>
    /// <param name="flush">Ticks spent writing and flushing.</param>
    /// <returns>A timing of one write.</returns>
    public static LogWriteTiming One(long wait, long flush) => new(1, wait, flush, wait + flush > SlowTicks ? 1 : 0);

    /// <summary>Sums two timings.</summary>
    public static LogWriteTiming operator +(LogWriteTiming a, LogWriteTiming b) =>
        new(a.Writes + b.Writes, a.WaitTicks + b.WaitTicks, a.FlushTicks + b.FlushTicks, a.SlowWrites + b.SlowWrites);

    /// <summary>What happened between an earlier reading and this one.</summary>
    public static LogWriteTiming operator -(LogWriteTiming a, LogWriteTiming b) =>
        new(a.Writes - b.Writes, a.WaitTicks - b.WaitTicks, a.FlushTicks - b.FlushTicks, a.SlowWrites - b.SlowWrites);

    /// <summary>Sums two timings.</summary>
    public static LogWriteTiming Add(LogWriteTiming left, LogWriteTiming right) => left + right;

    /// <summary>What happened between an earlier reading and this one.</summary>
    public static LogWriteTiming Subtract(LogWriteTiming left, LogWriteTiming right) => left - right;

    /// <summary>One clause: writes, wait ms, flush ms, slow writes.</summary>
    /// <returns>The clause.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Writes} writes, wait {WaitTicks * 1000d / Stopwatch.Frequency:0.0} ms, flush {FlushTicks * 1000d / Stopwatch.Frequency:0.0} ms, {SlowWrites} over {SlowMilliseconds:0} ms");
}
