using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>One stretch of ticks over which a key held.</summary>
/// <typeparam name="TKey">What held: a player and his team's pluses, a player and an icon.</typeparam>
/// <param name="Key">The key.</param>
/// <param name="From">The tick it began, or the window's start when a seek met it already holding.</param>
/// <param name="Stop">The tick it stopped holding, or null while it holds.</param>
public readonly record struct TickRun<TKey>(TKey Key, int From, int? Stop);

/// <summary>
/// The stretches over which keys hold, walked tick by tick, each kept with its first tick so an effect is stepped from
/// where it began rather than rebuilt every frame (B538, B765).
/// </summary>
/// <typeparam name="TKey">What holds.</typeparam>
/// <remarks>
/// A seek, a step backwards or a jump past the window forgets everything and walks the window again, so a key met
/// already holding starts at the window's edge (`docs/memory/a-parity-rule-may-bound-the-cost.md`). A stopped run is still
/// reported for <c>fadeTicks</c> after it stops, for an effect that fades; zero drops it the tick it stops.
/// </remarks>
/// <param name="windowTicks">How far back a seek walks.</param>
/// <param name="fadeTicks">How long a stopped run is still reported.</param>
public sealed class TickRuns<TKey>(int windowTicks, int fadeTicks)
    where TKey : notnull
{
    private readonly Dictionary<TKey, int> _running = [];
    private readonly List<TickRun<TKey>> _stopped = [];
    private readonly HashSet<TKey> _seen = [];
    private readonly List<TKey> _ended = [];
    private readonly List<TickRun<TKey>> _runs = [];
    private int? _tick;

    /// <summary>Every run holding or still fading at a tick.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="holdingAt">Adds to the set every key that holds at a tick.</param>
    /// <returns>The stopped runs still fading, then the holding ones; valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="holdingAt"/> is null.</exception>
    public IReadOnlyList<TickRun<TKey>> At(int tick, Action<int, ISet<TKey>> holdingAt)
    {
        ArgumentNullException.ThrowIfNull(holdingAt);

        int from;

        if (_tick is { } last && tick >= last && tick - last <= windowTicks)
        {
            from = last + 1;
        }
        else
        {
            _running.Clear();
            _stopped.Clear();
            from = tick - windowTicks;
        }

        for (int walked = from; walked <= tick; walked++)
        {
            Walk(walked, holdingAt);
        }

        _tick = tick;
        _stopped.RemoveAll(run => run.Stop is { } stop && tick >= stop + fadeTicks);

        _runs.Clear();
        _runs.AddRange(_stopped);

        foreach ((TKey key, int began) in _running)
        {
            _runs.Add(new TickRun<TKey>(key, began, null));
        }

        return _runs;
    }

    /// <summary>The tick a holding key began, after <see cref="At"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its start, or null when it does not hold.</returns>
    public int? From(TKey key) => _running.TryGetValue(key, out int began) ? began : null;

    /// <summary>One tick: each key not holding before starts here; each that no longer holds stops.</summary>
    private void Walk(int tick, Action<int, ISet<TKey>> holdingAt)
    {
        _seen.Clear();
        holdingAt(tick, _seen);

        foreach (TKey key in _seen)
        {
            _running.TryAdd(key, tick);
        }

        _ended.Clear();

        foreach (TKey key in _running.Keys)
        {
            if (!_seen.Contains(key))
            {
                _ended.Add(key);
            }
        }

        foreach (TKey key in _ended)
        {
            _stopped.Add(new TickRun<TKey>(key, _running[key], tick));
            _running.Remove(key);
        }
    }
}
