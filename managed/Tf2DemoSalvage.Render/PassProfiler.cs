using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The draw's passes, in `DrawFrame` order; a pass is the work since the previous mark.</summary>
public enum RenderPass
{
    /// <summary>Back buffer and depth cleared.</summary>
    Clear,

    /// <summary>`DrawMonitors`.</summary>
    Monitors,

    /// <summary>The 2D skybox.</summary>
    Sky2D,

    /// <summary>Collation, the 3D sky room and its renderables.</summary>
    Sky3D,

    /// <summary>Water reflection and refraction views.</summary>
    WaterViews,

    /// <summary>The opaque world.</summary>
    World,

    /// <summary>Opaque renderables, size buckets: props, players, detail models.</summary>
    OpaqueModels,

    /// <summary>Brush-entity decals.</summary>
    EntityDecals,

    /// <summary>World translucents, translucent models and grass, interleaved by leaf.</summary>
    Translucent,

    /// <summary>The water intersection view.</summary>
    WaterIntersection,

    /// <summary>Pixel-visibility queries.</summary>
    Occlusion,

    /// <summary>The viewmodel pass.</summary>
    Viewmodels,

    /// <summary>Particle batches.</summary>
    Particles,

    /// <summary>The screen overlay.</summary>
    Overlay,

    /// <summary>The exposure histogram and walk.</summary>
    Exposure,

    /// <summary>Bloom.</summary>
    Bloom,

    /// <summary>Map lines and markers.</summary>
    DebugLines,

    /// <summary>VGUI.</summary>
    Hud,

    /// <summary>`Present`.</summary>
    Present,
}

/// <summary>Draws and buffer maps issued by `Render`, counted at each call; read by <see cref="PassProfiler"/> at its marks.</summary>
/// <remarks>
/// ponytail: plain static increments, one writer (the thread that draws); a second drawing thread would need Interlocked.
/// **Counted whether or not `ProfilePasses` is on**, deliberately: one `long++` per draw or map, which is cheaper than
/// the branch on a device flag that would skip it, and the profiler differences the counts so it needs no reset.
/// </remarks>
public static class SubmitCounters
{
    /// <summary>Draw calls so far.</summary>
    public static long Draws { get; private set; }

    /// <summary>`Map` and `UpdateSubresource` calls so far.</summary>
    public static long Uploads { get; private set; }

    /// <summary>One draw call.</summary>
    public static void Draw() => Draws++;

    /// <summary>One map or subresource update.</summary>
    public static void Upload() => Uploads++;
}

/// <summary>Sums of the profiled passes over a measurement; written on the drawing thread, taken on the game thread.</summary>
public sealed class PassLedger
{
    private static readonly int PassCount = Enum.GetValues<RenderPass>().Length;

    private readonly Lock _gate = new();
    private readonly long[] _cpuTicks = new long[PassCount];
    private readonly long[] _draws = new long[PassCount];
    private readonly long[] _uploads = new long[PassCount];
    private readonly double[] _gpuMs = new double[PassCount];
    private readonly ulong[] _primitives = new ulong[PassCount];
    private readonly ulong[] _pixels = new ulong[PassCount];
    private long _cpuFrames;
    private long _gpuFrames;
    private long _skipped;
    private long _disjoint;
    private long _abandoned;
    private readonly long[] _spanTicks = new long[PassCount * SubmitSpans.Count];
    private readonly long[] _spanLaps = new long[PassCount * SubmitSpans.Count];
    private readonly long[] _spanBytes = new long[PassCount * SubmitSpans.Count];

    /// <summary>One frame's model-path spans, pass-major (<see cref="SubmitSpans"/>).</summary>
    public void AddSpans(long[] ticks, long[] laps, long[] bytes)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(bytes);

        lock (_gate)
        {
            for (int at = 0; at < _spanTicks.Length; at++)
            {
                _spanTicks[at] += ticks[at];
                _spanLaps[at] += laps[at];
                _spanBytes[at] += bytes[at];
            }
        }
    }

    /// <summary>One pass's CPU side, from the frame being drawn.</summary>
    public void AddCpu(RenderPass pass, long ticks, long draws, long uploads)
    {
        lock (_gate)
        {
            _cpuTicks[(int)pass] += ticks;
            _draws[(int)pass] += draws;
            _uploads[(int)pass] += uploads;
        }
    }

    /// <summary>One frame's CPU side is complete.</summary>
    public void CpuFrame()
    {
        lock (_gate)
        {
            _cpuFrames++;
        }
    }

    /// <summary>One pass's GPU side, read back from an answered frame.</summary>
    public void AddGpu(RenderPass pass, double milliseconds, ulong primitives, ulong pixelInvocations)
    {
        lock (_gate)
        {
            _gpuMs[(int)pass] += milliseconds;
            _primitives[(int)pass] += primitives;
            _pixels[(int)pass] += pixelInvocations;
        }
    }

    /// <summary>One frame's GPU side was read.</summary>
    public void GpuFrame()
    {
        lock (_gate)
        {
            _gpuFrames++;
        }
    }

    /// <summary>A frame not answered in time, so not read.</summary>
    public void Skipped()
    {
        lock (_gate)
        {
            _skipped++;
        }
    }

    /// <summary>A frame begun and never ended — the draw threw — so dropped.</summary>
    public void Abandoned()
    {
        lock (_gate)
        {
            _abandoned++;
        }
    }

    /// <summary>A frame whose timestamps the device says are unreliable, so dropped.</summary>
    public void Disjoint()
    {
        lock (_gate)
        {
            _disjoint++;
        }
    }

    /// <summary>Mean per frame of each pass that ran, CPU over the frames drawn and GPU over the frames read.</summary>
    /// <param name="width">The back buffer's width as drawn.</param>
    /// <param name="height">Its height.</param>
    /// <returns>The report's lines.</returns>
    public string Report(int width, int height)
    {
        lock (_gate)
        {
            double cpuEach(long ticks) => _cpuFrames == 0 ? 0d : ticks * 1000d / Stopwatch.Frequency / _cpuFrames;
            double gpuEach(double ms) => _gpuFrames == 0 ? 0d : ms / _gpuFrames;
            double countEach(double n, long frames) => frames == 0 ? 0d : n / frames;

            StringBuilder text = new();
            long cpuAll = 0;
            double gpuAll = 0;
            long drawsAll = 0;
            long uploadsAll = 0;

            text.Append(CultureInfo.InvariantCulture, $"render passes, mean per frame at {width}x{height}: {_cpuFrames} frames drawn, {_gpuFrames} read on the GPU, {_skipped} skipped, {_disjoint} disjoint, {_abandoned} abandoned");

            foreach (RenderPass pass in Enum.GetValues<RenderPass>())
            {
                int at = (int)pass;

                if (_cpuTicks[at] == 0 && _gpuMs[at] == 0)
                {
                    continue;
                }

                cpuAll += _cpuTicks[at];
                gpuAll += _gpuMs[at];
                drawsAll += _draws[at];
                uploadsAll += _uploads[at];
                text.Append(CultureInfo.InvariantCulture,
                    $"\n    {pass}: cpu {cpuEach(_cpuTicks[at]):0.000} ms, gpu {gpuEach(_gpuMs[at]):0.000} ms, draws {countEach(_draws[at], _cpuFrames):0.0}, maps {countEach(_uploads[at], _cpuFrames):0.0}, primitives {countEach(_primitives[at], _gpuFrames):0}, pixels {countEach(_pixels[at], _gpuFrames):0}");

                // The model path's spans in this pass; the remainder of the pass's CPU is what no span covers.
                long spanned = 0;

                foreach (SubmitSpan span in Enum.GetValues<SubmitSpan>())
                {
                    int s = (at * SubmitSpans.Count) + (int)span;

                    if (_spanLaps[s] == 0)
                    {
                        continue;
                    }

                    spanned += _spanTicks[s];
                    text.Append(CultureInfo.InvariantCulture,
                        $"\n        span {span}: cpu {cpuEach(_spanTicks[s]):0.0000} ms, laps {countEach(_spanLaps[s], _cpuFrames):0.0}, bytes {countEach(_spanBytes[s], _cpuFrames):0}");
                }

                if (spanned != 0)
                {
                    text.Append(CultureInfo.InvariantCulture,
                        $"\n        unspanned: cpu {cpuEach(_cpuTicks[at] - spanned):0.0000} ms");
                }
            }

            text.Append(CultureInfo.InvariantCulture,
                $"\n    all: cpu {cpuEach(cpuAll):0.000} ms, gpu {gpuEach(gpuAll):0.000} ms, draws {countEach(drawsAll, _cpuFrames):0.0}, maps {countEach(uploadsAll, _cpuFrames):0.0}");

            return text.ToString();
        }
    }
}

/// <summary>A profiled frame's lifetime: leaving it before `EndFrame` — the draw threw — abandons the frame at the throw.</summary>
/// <remarks>
/// Without it a throw on a measurement's LAST frame was never counted: `TakeLedger` ran before the next `BeginFrame` closed it.
/// </remarks>
public readonly ref struct PassFrame
{
    private readonly PassProfiler? _profiler;
    private readonly ComPtr<ID3D11DeviceContext> _context;

    internal PassFrame(PassProfiler? profiler, ComPtr<ID3D11DeviceContext> context)
    {
        _profiler = profiler;
        _context = context;
    }

    /// <summary>Abandons the frame if it is still open; nothing when it ended or nothing is profiled.</summary>
    /// <remarks>
    /// **Runs while the draw's exception unwinds, so it must not throw one of its own** — and does not, so it is not
    /// wrapped: `Abandon` makes only `ID3D11DeviceContext::End` calls, which are `void` (`d3d11.h`, Silk's binding checks
    /// no HRESULT), so a removed or lost device answers later through `GetData` or `Present`, never here; and the ledger's
    /// counter takes a lock and increments. A catch here would guard nothing and could only hide a fault (batch-32 review).
    /// </remarks>
    public void Dispose() => _profiler?.Abandon(_context);
}

/// <summary>Per-pass CPU and GPU time of `DrawFrame`, for `--measure` only; every call is on the drawing thread.</summary>
/// <remarks>
/// One Stopwatch read and one timestamp query at each mark, inside one disjoint query per frame; one pipeline-statistics
/// query per pass. A frame's queries are read at the start of the frame <see cref="Ring"/> later without flushing, so the
/// drawing thread never waits on them; an unanswered frame is skipped and counted.
/// </remarks>
public sealed unsafe class PassProfiler : IDisposable
{
    /// <summary>Frames in flight before a frame's queries are read.</summary>
    public const int Ring = 5;

    private const uint DoNotFlush = 1; // D3D11_ASYNC_GETDATA_DONOTFLUSH

    private static readonly int PassCount = Enum.GetValues<RenderPass>().Length;

    private readonly Slot[] _slots;
    private Slot? _current;
    private int _frame;
    private int _pass = -1;
    private long _passStart;
    private long _draws;
    private long _uploads;
    private readonly long[] _frameCpu = new long[PassCount];
    private readonly long[] _frameDraws = new long[PassCount];
    private readonly long[] _frameUploads = new long[PassCount];

    // The model draw path's spans (SubmitSpans): totals at the previous mark, and this frame's per pass, pass-major.
    private readonly long[] _spanTicks = new long[SubmitSpans.Count];
    private readonly long[] _spanLaps = new long[SubmitSpans.Count];
    private readonly long[] _spanBytes = new long[SubmitSpans.Count];
    private readonly long[] _frameSpanTicks = new long[PassCount * SubmitSpans.Count];
    private readonly long[] _frameSpanLaps = new long[PassCount * SubmitSpans.Count];
    private readonly long[] _frameSpanBytes = new long[PassCount * SubmitSpans.Count];

    private PassProfiler(Slot[] slots) => _slots = slots;

    /// <summary>Where the sums go.</summary>
    public PassLedger Ledger { get; private set; } = new();

    /// <summary>Makes the queries.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The profiler.</returns>
    public static PassProfiler Create(ComPtr<ID3D11Device> device)
    {
        Slot[] slots = new Slot[Ring];

        for (int s = 0; s < Ring; s++)
        {
            slots[s] = new Slot(device);
        }

        return new PassProfiler(slots);
    }

    /// <summary>Starts a new ledger, so a measurement counts only its own frames.</summary>
    /// <returns>The ledger just closed.</returns>
    public PassLedger TakeLedger()
    {
        PassLedger taken = Ledger;

        Ledger = new PassLedger();

        return taken;
    }

    /// <summary>Begins a frame that is closed when the scope is left, however it is left.</summary>
    /// <param name="profiler">The profiler, or null when passes are not profiled.</param>
    /// <param name="context">The immediate context.</param>
    /// <returns>The scope; `using` it around the draw.</returns>
    public static PassFrame Begin(PassProfiler? profiler, ComPtr<ID3D11DeviceContext> context)
    {
        profiler?.BeginFrame(context);

        return new PassFrame(profiler, context);
    }

    /// <summary>The frame begins: reads the slot's previous frame if answered, then opens the slot again.</summary>
    /// <param name="context">The immediate context.</param>
    /// <remarks>
    /// **A frame left open is closed here, not read**, when no <see cref="PassFrame"/> closed it at the throw.
    /// </remarks>
    public void BeginFrame(ComPtr<ID3D11DeviceContext> context)
    {
        Abandon(context);

        Slot slot = _slots[_frame++ % Ring];

        if (slot.Issued)
        {
            Collect(context, slot);
        }

        slot.Reset();
        _current = slot;
        context.Begin(slot.Disjoint);
        context.End(slot.Start);
        _pass = -1;
        SubmitSpans.On = true;
        SnapshotSpans();
        _passStart = Stopwatch.GetTimestamp();
        _draws = SubmitCounters.Draws;
        _uploads = SubmitCounters.Uploads;
    }

    private void SnapshotSpans()
    {
        SubmitSpans.Ticks.CopyTo(_spanTicks);
        SubmitSpans.Laps.CopyTo(_spanLaps);
        SubmitSpans.Bytes.CopyTo(_spanBytes);
    }

    private void AddSpans(int pass)
    {
        for (int s = 0; s < SubmitSpans.Count; s++)
        {
            int at = (pass * SubmitSpans.Count) + s;

            _frameSpanTicks[at] += SubmitSpans.Ticks[s] - _spanTicks[s];
            _frameSpanLaps[at] += SubmitSpans.Laps[s] - _spanLaps[s];
            _frameSpanBytes[at] += SubmitSpans.Bytes[s] - _spanBytes[s];
        }

        SnapshotSpans();
    }

    /// <summary>The pass just drawn ends here; the next begins.</summary>
    /// <param name="context">The immediate context.</param>
    /// <param name="ended">The pass the work since the previous mark belongs to.</param>
    public void Mark(ComPtr<ID3D11DeviceContext> context, RenderPass ended)
    {
        // Full: a pass marked twice in one frame. Every slot of the order is taken, so the rest of the frame is unprofiled.
        if (_current is not { } slot || slot.Count >= slot.Order.Length)
        {
            return;
        }

        int at = (int)ended;
        long now = Stopwatch.GetTimestamp();

        if (_pass >= 0)
        {
            context.End(slot.Statistics[_pass]);
        }

        // Statistics opened at the previous mark (or the frame's start) belong to the pass just named.
        slot.StatisticsFor[at] = _pass;
        context.End(slot.Stamps[at]);
        slot.Order[slot.Count++] = at;

        // Held with the frame until EndFrame: an abandoned frame's passes never reach the ledger's per-frame mean.
        _frameCpu[at] += now - _passStart;
        _frameDraws[at] += SubmitCounters.Draws - _draws;
        _frameUploads[at] += SubmitCounters.Uploads - _uploads;
        AddSpans(at);

        _pass = at;
        context.Begin(slot.Statistics[at]);
        _passStart = Stopwatch.GetTimestamp();
        _draws = SubmitCounters.Draws;
        _uploads = SubmitCounters.Uploads;
    }

    /// <summary>The frame ends, after the last mark.</summary>
    /// <param name="context">The immediate context.</param>
    public void EndFrame(ComPtr<ID3D11DeviceContext> context)
    {
        if (_current is not { } slot)
        {
            return;
        }

        if (_pass >= 0)
        {
            context.End(slot.Statistics[_pass]);
        }

        context.End(slot.Disjoint);
        slot.Issued = true;
        _current = null;
        SubmitSpans.On = false;

        for (int at = 0; at < PassCount; at++)
        {
            if (_frameCpu[at] != 0 || _frameDraws[at] != 0 || _frameUploads[at] != 0)
            {
                Ledger.AddCpu((RenderPass)at, _frameCpu[at], _frameDraws[at], _frameUploads[at]);
            }
        }

        Ledger.AddSpans(_frameSpanTicks, _frameSpanLaps, _frameSpanBytes);

        ClearFrameCpu();
        Ledger.CpuFrame();
    }

    /// <summary>
    /// Closes a frame begun and never ended — `DrawFrame` threw between its marks — and counts it abandoned; nothing when
    /// no frame is open.
    /// </summary>
    /// <param name="context">The immediate context.</param>
    /// <remarks>
    /// Its queries were begun and never ended: they are ended now, the slot stays unissued so its partial GPU side is never
    /// collected, and its CPU marks are dropped, so it is counted as abandoned and in no pass's mean.
    /// </remarks>
    public void Abandon(ComPtr<ID3D11DeviceContext> context)
    {
        if (_current is not { } abandoned)
        {
            return;
        }

        if (_pass >= 0)
        {
            context.End(abandoned.Statistics[_pass]);
        }

        context.End(abandoned.Disjoint);
        _current = null;
        SubmitSpans.On = false;
        ClearFrameCpu();
        Ledger.Abandoned();
    }

    private void ClearFrameCpu()
    {
        Array.Clear(_frameCpu);
        Array.Clear(_frameDraws);
        Array.Clear(_frameUploads);
        Array.Clear(_frameSpanTicks);
        Array.Clear(_frameSpanLaps);
        Array.Clear(_frameSpanBytes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Slot slot in _slots)
        {
            slot.Dispose();
        }
    }

    private void Collect(ComPtr<ID3D11DeviceContext> context, Slot slot)
    {
        QueryDataTimestampDisjoint disjoint = default;

        if (context.GetData(slot.Disjoint, &disjoint, (uint)sizeof(QueryDataTimestampDisjoint), DoNotFlush) != 0)
        {
            Ledger.Skipped();
            return;
        }

        if (disjoint.Disjoint != 0u || disjoint.Frequency == 0)
        {
            Ledger.Disjoint();
            return;
        }

        ulong previous = 0;

        if (context.GetData(slot.Start, &previous, sizeof(ulong), DoNotFlush) != 0)
        {
            Ledger.Skipped();
            return;
        }

        for (int i = 0; i < slot.Count; i++)
        {
            int at = slot.Order[i];
            ulong stamp = 0;
            QueryDataPipelineStatistics statistics = default;

            if (context.GetData(slot.Stamps[at], &stamp, sizeof(ulong), DoNotFlush) != 0)
            {
                Ledger.Skipped();
                return;
            }

            // The pass's statistics were opened at the mark before it; the first pass's at the frame's start had none.
            int opened = slot.StatisticsFor[at];

            if (opened >= 0 &&
                context.GetData(slot.Statistics[opened], &statistics, (uint)sizeof(QueryDataPipelineStatistics), DoNotFlush) != 0)
            {
                Ledger.Skipped();
                return;
            }

            Ledger.AddGpu((RenderPass)at, (stamp - previous) * 1000d / disjoint.Frequency, statistics.IAPrimitives, statistics.PSInvocations);
            previous = stamp;
        }

        Ledger.GpuFrame();
    }

    private sealed class Slot : IDisposable
    {
        private ComPtr<ID3D11Query> _disjoint;
        private ComPtr<ID3D11Query> _start;
        private readonly ComPtr<ID3D11Query>[] _stamps = new ComPtr<ID3D11Query>[PassCount];
        private readonly ComPtr<ID3D11Query>[] _statistics = new ComPtr<ID3D11Query>[PassCount];

        public Slot(ComPtr<ID3D11Device> device)
        {
            _disjoint = Make(device, Query.TimestampDisjoint);
            _start = Make(device, Query.Timestamp);

            for (int p = 0; p < PassCount; p++)
            {
                _stamps[p] = Make(device, Query.Timestamp);
                _statistics[p] = Make(device, Query.PipelineStatistics);
            }
        }

        public ID3D11Asynchronous* Disjoint => (ID3D11Asynchronous*)_disjoint.Handle;

        public ID3D11Asynchronous* Start => (ID3D11Asynchronous*)_start.Handle;

        public Stamp Stamps => new(_stamps);

        public Stamp Statistics => new(_statistics);

        public int[] Order { get; } = new int[PassCount];

        /// <summary>For each pass, which statistics query was open over it (the previous pass's index), or -1.</summary>
        public int[] StatisticsFor { get; } = new int[PassCount];

        public int Count { get; set; }

        public bool Issued { get; set; }

        public void Reset()
        {
            Count = 0;
            Issued = false;
        }

        public void Dispose()
        {
            _disjoint.Dispose();
            _start.Dispose();

            for (int p = 0; p < PassCount; p++)
            {
                _stamps[p].Dispose();
                _statistics[p].Dispose();
            }
        }

        private static ComPtr<ID3D11Query> Make(ComPtr<ID3D11Device> device, Query kind)
        {
            ComPtr<ID3D11Query> query = default;
            QueryDesc description = new() { Query = kind };

            SilkMarshal.ThrowHResult(device.CreateQuery(in description, ref query));

            return query;
        }
    }

    private readonly struct Stamp(ComPtr<ID3D11Query>[] queries)
    {
        public ID3D11Asynchronous* this[int at] => (ID3D11Asynchronous*)queries[at].Handle;
    }
}
