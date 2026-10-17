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
/// **The first production caller of the decided spacing** (the owner's 2 s, <see cref="TimelineWindows.SpacingSeconds"/>),
/// through <see cref="SpacingTicks"/>, which owns the conversion.
/// </remarks>
public sealed class WindowCache
{
    private readonly TimelineWindows _walk;

    private readonly Lock _gate = new();

    /// <summary>Windows built or being built, by the checkpoint index they start at; at most the served one and the next.</summary>
    private readonly Dictionary<int, Task<DemoTimeline>> _windows = [];

    private WindowCache(TimelineWindows walk) => _walk = walk;

    /// <summary>The checkpoint spacing in ticks at a tick interval: the seconds TRUNCATED to whole ticks.</summary>
    /// <param name="secondsPerTick">The demo's interval, TF2's 0.015 s unless its server info says otherwise.</param>
    /// <returns>The ticks between checkpoints, at least one.</returns>
    /// <remarks>
    /// **Truncated, so checkpoints are never further apart than the owner's 2 s**: at 15 ms that is 133.33 ticks, so
    /// 133. Rounding would give a spacing longer than asked wherever the fraction passes a half.
    /// </remarks>
    public static int SpacingTicks(double secondsPerTick) =>
        Math.Max(1, (int)(TimelineWindows.SpacingSeconds / secondsPerTick));

    /// <summary>Walks a demo into checkpoints at the decided spacing. Seconds to minutes of work: call it off the UI thread.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="settings">The viewer's window settings, which bound what each checkpoint keeps (D220).</param>
    /// <returns>The cache, or null for a demo with no data tables.</returns>
    public static WindowCache? Walk(ReadOnlyMemory<byte> file, WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return TimelineWindows.Walk(file, SpacingTicks(ScenePropTrack.Tf2TickInterval), settings) is { } walked
            ? new WindowCache(walked)
            : null;
    }

    /// <summary>How many checkpoints the walk took.</summary>
    public int Count => _walk.Count;

    /// <summary>The window answering a moment, and the one after it set building on a worker.</summary>
    /// <param name="tick">The moment.</param>
    /// <returns>The window, once built.</returns>
    public Task<DemoTimeline> At(double tick)
    {
        int index = _walk.IndexAt(tick);

        lock (_gate)
        {
            // Only the served window and the next are kept: a seek elsewhere drops the rest.
            foreach (int stale in _windows.Keys.Where(key => key != index && key != index + 1).ToList())
            {
                _windows.Remove(stale);
            }

            Task<DemoTimeline> served = Ensure(index);

            if (index + 1 < _walk.Count)
            {
                Ensure(index + 1);
            }

            return served;
        }
    }

    /// <summary>How many windows are held, served or prefetched: two once a window with one after it is served.</summary>
    public int Held
    {
        get
        {
            lock (_gate)
            {
                return _windows.Count;
            }
        }
    }

    private Task<DemoTimeline> Ensure(int index)
    {
        if (!_windows.TryGetValue(index, out Task<DemoTimeline>? window))
        {
            int start = _walk.Ticks[index];
            int through = index + 1 < _walk.Count ? _walk.Ticks[index + 1] - 1 : int.MaxValue - 1;

            window = Task.Run(() => _walk.Window(start, through));
            _windows[index] = window;
        }

        return window;
    }
}
