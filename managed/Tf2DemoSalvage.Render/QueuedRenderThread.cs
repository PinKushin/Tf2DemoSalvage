using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Tf2DemoSalvage.Render;

/// <summary>The engine's queued material system: one render thread, one frame in flight, and a full drain on `Lock` (B581, D228).</summary>
/// <remarks>
/// Read from TF2's x64 `materialsystem.dll` (finding 99): `EndFrame` (`0x180014bc0`) waits for the previous frame's job
/// only if it is still running, then hands the frame just recorded to a pool of ONE thread named `MatQueue`
/// (`0x1800130f0`, created on first use) and returns; there is no wait at `BeginFrame` and no held frame. `Lock`
/// (`0x180018000`) and forcing mode 0 (`0x180016050`) wait for the in-flight job before anything else touches the device.
///
/// **What crosses is the frame, not a call list.** The engine records each call into a `CMatQueuedRenderContext`; this
/// takes the whole draw as one job, at the granularity the device already consumes (finding 98 design C).
///
/// **Mode 0 runs the job on the calling thread**, which is today's serial path, kept as it is.
/// </remarks>
public sealed class QueuedRenderThread : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _work = new(0);
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private readonly List<(string Caller, long Ticks)> _waits = [];
    private Thread? _thread;
    private Action? _job;
    private ExceptionDispatchInfo? _failure;
    private long _renderTicks;
    private bool _stopping;
    private bool _disposed;

    /// <summary>The thread mode. Leaving <see cref="MaterialThreadMode.QueuedThreaded"/> drains first, as the engine's force-off does.</summary>
    public MaterialThreadMode Mode
    {
        get;
        set
        {
            Lock();
            field = value;
        }
    }

    /// <summary>The render thread's managed id, or -1 before it was started.</summary>
    public int RenderThreadId { get; private set; } = -1;

    /// <summary>Whether a frame is being drawn on the render thread now.</summary>
    public bool InFlight => !_idle.IsSet;

    /// <summary>Told, on the waiting thread, the moment a <see cref="Lock"/> is about to block, with its caller's name; for tests and `--measure`.</summary>
    public Action<string>? Waiting { get; set; }

    /// <summary>Draws a frame: on the render thread in mode 2, after the previous one finished; on this thread otherwise.</summary>
    /// <param name="draw">The frame.</param>
    /// <param name="queued">False to draw on this thread whatever the mode (a state the engine's `EndFrame` vetoes mode 2 in).</param>
    /// <exception cref="ArgumentNullException"><paramref name="draw"/> is null.</exception>
    public void Submit(Action draw, bool queued = true)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Mode != MaterialThreadMode.QueuedThreaded || !queued || OnRenderThread)
        {
            Lock();
            draw();

            return;
        }

        // `EndFrame`'s one wait: the previous frame's job, only if it is still pending or running.
        Lock();

        _thread ??= Start();
        _job = draw;
        _idle.Reset();
        _work.Release();
    }

    /// <summary>Waits until no frame is in flight — the engine's `Lock`, a full drain — and rethrows what the last one threw.</summary>
    /// <param name="caller">Who asked, for the wait ledger.</param>
    /// <remarks>Free when idle and on the render thread itself, so a device call the draw makes never waits on its own frame.</remarks>
    public void Lock([CallerMemberName] string caller = "")
    {
        if (OnRenderThread)
        {
            return;
        }

        if (!_idle.IsSet)
        {
            long at = Stopwatch.GetTimestamp();

            Waiting?.Invoke(caller);
            _idle.Wait();

            lock (_gate)
            {
                _waits.Add((caller, Stopwatch.GetTimestamp() - at));
            }
        }

        if (Interlocked.Exchange(ref _failure, null) is { } failure)
        {
            failure.Throw();
        }
    }

    /// <summary>The render thread's drawing time since the last call, in stopwatch ticks, carried from where it ran.</summary>
    /// <returns>The ticks.</returns>
    public long TakeRenderTicks() => Interlocked.Exchange(ref _renderTicks, 0);

    /// <summary>Every <see cref="Lock"/> that actually waited since the last call, by caller.</summary>
    /// <returns>The waits.</returns>
    public IReadOnlyList<(string Caller, long Ticks)> TakeWaits()
    {
        lock (_gate)
        {
            if (_waits.Count == 0)
            {
                return [];
            }

            (string, long)[] taken = [.. _waits];
            _waits.Clear();

            return taken;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Lock();
        }
        finally
        {
            _disposed = true;
            _stopping = true;
            _work.Release();
            _thread?.Join();
            _work.Dispose();
            _idle.Dispose();
        }
    }

    private bool OnRenderThread => Environment.CurrentManagedThreadId == RenderThreadId;

    private Thread Start()
    {
        Thread thread = new(Run) { Name = "MatQueue", IsBackground = true };

        RenderThreadId = thread.ManagedThreadId;
        thread.Start();

        return thread;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Not swallowed: every exception is captured and rethrown on the game thread at its next Lock.")]
    private void Run()
    {
        while (true)
        {
            _work.Wait();

            if (_stopping)
            {
                return;
            }

            long at = Stopwatch.GetTimestamp();

            try
            {
                _job?.Invoke();
            }
            catch (Exception failure)
            {
                // Carried to the game thread, which rethrows it at its next Lock: a thread that died here would leave
                // the game thread waiting for ever.
                _failure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                _job = null;
                Interlocked.Add(ref _renderTicks, Stopwatch.GetTimestamp() - at);
                _idle.Set();
            }
        }
    }
}
