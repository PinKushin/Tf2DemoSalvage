using System;
using System.Diagnostics;

namespace Tf2DemoSalvage.Render;

/// <summary>What a stretch of the model draw path spent its CPU on, for `--measure` only (B597).</summary>
/// <remarks>Each span is the time since the previous lap, so the spans of one region are disjoint by construction.</remarks>
public enum SubmitSpan
{
    /// <summary>The draw-order and repeated-model reports before the opaque buckets.</summary>
    Diagnostics,

    /// <summary>A model draw's argument checks, refract copy and vertex-buffer lookup.</summary>
    InstanceSetup,

    /// <summary>`BindPipeline`, the model's vertex buffer and its colour stream.</summary>
    PipelineBinds,

    /// <summary>`BindFlex`: the face's deltas, uploaded or the null stream.</summary>
    Flex,

    /// <summary>`SetModel`: the model constants mapped, written and bound.</summary>
    ModelConstants,

    /// <summary>`SetBones`: the bone constants mapped, written and bound.</summary>
    Bones,

    /// <summary>The nearest cubemap and its once-per-model report.</summary>
    Cubemap,

    /// <summary>A batch's filters, raster, blend and shader-resource binds.</summary>
    BatchState,

    /// <summary>`SetMaterial`'s underlay, luxel, thumbnail and depth binds.</summary>
    MaterialBinds,

    /// <summary>`SetMaterial`'s scratch copies, proxies and modulation conversion.</summary>
    Proxies,

    /// <summary>`SetMaterial`'s checks, map, copy and binds of the material constants.</summary>
    MaterialMap,

    /// <summary>The draw call itself.</summary>
    DrawCall,

    /// <summary>The submission reports after a model's batches, and the stream unbinds.</summary>
    Bookkeeping,

    /// <summary>A renderable's body report before its draw, and its decals after.</summary>
    Renderable,

    /// <summary>Two laps back to back, once per model: what one lap of this instrument costs.</summary>
    TimerOverhead,
}

/// <summary>
/// Per-span CPU ticks, laps and bytes of the model draw path, on only inside a profiled frame (<see cref="PassProfiler"/>).
/// </summary>
/// <remarks>
/// ponytail: static and single-writer, like <see cref="SubmitCounters"/> — the drawing thread is the only caller. Off, a lap
/// is one bool read. **Carried from where the work runs** (B243): each lap reads the clock at the boundary it names; the
/// profiler differences the totals at its marks, so a span is attributed to the pass it ran in.
/// </remarks>
public static class SubmitSpans
{
    /// <summary>How many spans there are.</summary>
    public static readonly int Count = Enum.GetValues<SubmitSpan>().Length;

    private static readonly long[] TicksOf = new long[Count];
    private static readonly long[] LapsOf = new long[Count];
    private static readonly long[] BytesOf = new long[Count];
    private static long _last;

    /// <summary>Whether laps are taken; set by the profiler for the length of a profiled frame.</summary>
    /// <remarks>
    /// Not volatile, deliberately: the profiler sets it in <c>BeginFrame</c> and clears it in <c>EndFrame</c> on the drawing
    /// thread, the same thread that laps, so no other thread reads it.
    /// </remarks>
    public static bool On { get; set; }

    /// <summary>Ticks so far, per span.</summary>
    public static ReadOnlySpan<long> Ticks => TicksOf;

    /// <summary>Laps so far, per span.</summary>
    public static ReadOnlySpan<long> Laps => LapsOf;

    /// <summary>Bytes written so far, per span.</summary>
    public static ReadOnlySpan<long> Bytes => BytesOf;

    /// <summary>A region starts here; the time before it belongs to no span.</summary>
    public static void Restart()
    {
        if (On)
        {
            _last = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>The time since the previous lap or restart was <paramref name="span"/>'s.</summary>
    /// <param name="span">The span that just ran.</param>
    /// <param name="bytes">Bytes it wrote into mapped memory.</param>
    /// <param name="counted">Whether this lap counts as one more occurrence of the span (false for a span's second piece).</param>
    public static void Lap(SubmitSpan span, long bytes = 0, bool counted = true)
    {
        if (!On)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        int at = (int)span;

        TicksOf[at] += now - _last;
        BytesOf[at] += bytes;

        if (counted)
        {
            LapsOf[at]++;
        }

        _last = now;
    }
}
