using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// A demo as checkpoints, answered a window at a time around the playhead, the next one built ahead off the UI thread
/// (D217 step 4).
/// </summary>
/// <remarks>
/// **A window spans one checkpoint to the next**, so a moment is answered by the window of the checkpoint at or before it,
/// built by decoding forward from there. The window after it is prefetched on a worker as soon as one is served, which is
/// the one place two threads use the walk at once — a window writes nothing it shares, and a frozen property bag's read
/// cache publishes by compare-and-swap.
///
/// **Latest wins: at most the target's window and, once it is built, the next are held; every other is CANCELLED**, so a
/// scrub that crosses many windows builds only where it ends (the owner's rule, <see cref="At"/>). A window that faulted
/// or was cancelled is dropped, and the next ask builds it again. Nothing here waits: a caller shows what it has until the
/// window is ready.
///
/// **The first production caller of the decided spacing** (the owner's 2 s, <see cref="TimelineWindows.SpacingSeconds"/>),
/// through <see cref="SpacingTicks"/>, which owns the conversion.
/// </remarks>
public sealed class WindowCache : IDisposable
{
    private readonly IReadOnlyList<int> _starts;

    private readonly Func<int, int, CancellationToken, DemoTimeline> _build;

    private readonly Lock _gate = new();

    /// <summary>Windows built or being built, by the checkpoint index they start at, each with what cancels it.</summary>
    private readonly Dictionary<int, (Task<DemoTimeline> Window, CancellationTokenSource Cancel)> _windows = [];

    /// <summary>A cache over checkpoint ticks and a window builder: the walk's in production, any for a test.</summary>
    /// <param name="starts">The checkpoint ticks, in order.</param>
    /// <param name="build">Builds the window from one tick through another.</param>
    internal WindowCache(IReadOnlyList<int> starts, Func<int, int, CancellationToken, DemoTimeline> build)
    {
        _starts = starts;
        _build = build;
    }

    /// <summary>The checkpoint spacing in ticks at a tick interval: the seconds TRUNCATED to whole ticks.</summary>
    /// <param name="secondsPerTick">The demo's own interval, from its server info.</param>
    /// <returns>The ticks between checkpoints, at least one.</returns>
    /// <remarks>
    /// **Truncated, so checkpoints are never further apart than the owner's 2 s**: at 15 ms that is 133.33 ticks, so
    /// 133. Rounding would give a spacing longer than asked wherever the fraction passes a half.
    /// </remarks>
    public static int SpacingTicks(double secondsPerTick) =>
        Math.Max(1, (int)(TimelineWindows.SpacingSeconds / secondsPerTick));

    /// <summary>Walks a demo into checkpoints at the decided spacing. Seconds to minutes of work: call it off the UI thread.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="secondsPerTick">The demo's own tick interval.</param>
    /// <param name="settings">The viewer's window settings, which bound what each checkpoint keeps (D220).</param>
    /// <param name="walk">The walk, for what it keeps; null for a demo with no data tables.</param>
    /// <param name="cancel">Stops the walk between two packets.</param>
    /// <returns>The cache, or null for a demo with no data tables.</returns>
    public static WindowCache? Walk(
        ReadOnlyMemory<byte> file, double secondsPerTick, WindowSettings settings, out TimelineWindows? walk, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        walk = TimelineWindows.Walk(file, SpacingTicks(secondsPerTick), settings, cancel);
        TimelineWindows? walked = walk;

        return walked is null ? null : new WindowCache(walked.Ticks, (tick, through, cancel) => walked.Window(tick, through, cancel));
    }

    /// <summary>How many checkpoints the walk took.</summary>
    public int Count => _starts.Count;

    /// <summary>The checkpoint indices of the windows held, served or prefetched.</summary>
    internal IReadOnlyCollection<int> Held
    {
        get
        {
            lock (_gate)
            {
                return [.. _windows.Keys];
            }
        }
    }

    /// <summary>The window answering a moment; once it is built, and no newer seek has come, the next is prefetched.</summary>
    /// <param name="tick">The moment; before the first checkpoint, the first window answers.</param>
    /// <returns>The window, once built — cancelled if a newer seek overtakes it.</returns>
    /// <remarks>
    /// **Latest wins, nothing queues** — the owner, 2026-10-17: *"builds probably chouldnt qeue at all? when people scrub
    /// they care about where they end up, not what they hit before"*. Every held window but the target is dropped and its
    /// build cancelled, the prefetch included; the prefetch starts only after the target's window is built, and only if
    /// this is still the latest seek. A window already built for the target is served as it is.
    /// </remarks>
    internal Task<DemoTimeline> At(double tick)
    {
        int index = TimelineWindows.IndexAt(_starts, tick);

        lock (_gate)
        {
            int seek = ++_seeks;

            foreach (int stale in _windows.Keys.Where(key => key != index).ToList())
            {
                Drop(stale);
            }

            Task<DemoTimeline> served = Ensure(index);

            if (index + 1 < _starts.Count)
            {
                served.ContinueWith(
                    built =>
                    {
                        lock (_gate)
                        {
                            // Only behind the latest seek's finished window: a newer seek has its own build to finish first.
                            if (built.IsCompletedSuccessfully && seek == _seeks && _windows.ContainsKey(index))
                            {
                                Ensure(index + 1);
                            }
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return served;
        }
    }

    /// <summary>How many seeks have been asked; the prefetch belongs only to the latest.</summary>
    private int _seeks;

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (int held in _windows.Keys.ToList())
            {
                Drop(held);
            }
        }
    }

    private void Drop(int index)
    {
        (Task<DemoTimeline> window, CancellationTokenSource cancel) = _windows[index];
        _windows.Remove(index);

        // Cancelled now; disposed only when the build has ended, since it may still be reading the token.
        cancel.Cancel();
        window.ContinueWith(_ => cancel.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private Task<DemoTimeline> Ensure(int index)
    {
        // A window that faulted or was cancelled is never served again: the ask builds it afresh.
        if (_windows.TryGetValue(index, out (Task<DemoTimeline> Window, CancellationTokenSource Cancel) held)
            && (held.Window.IsFaulted || held.Window.IsCanceled))
        {
            Drop(index);
        }

        if (!_windows.TryGetValue(index, out held))
        {
            int start = _starts[index];
            int through = index + 1 < _starts.Count ? _starts[index + 1] - 1 : int.MaxValue - 1;
            CancellationTokenSource cancel = new();
            CancellationToken token = cancel.Token;

            held = (Task.Run(() => _build(start, through, token), token), cancel);
            _windows[index] = held;
        }

        return held.Window;
    }
}
