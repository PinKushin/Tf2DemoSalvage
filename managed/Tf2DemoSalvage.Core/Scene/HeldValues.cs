using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A value as the entity HELD it at each tick: the latest one received at or before it.</summary>
/// <typeparam name="T">The value; compared with its own equality, so only a change is kept.</typeparam>
/// <remarks>
/// **The engine's plain networked member, read at a moment.** A member the entity does not interpolate is
/// assigned on receipt and holds until the next update says otherwise — `ValidateModelIndex` re-applies
/// `m_nModelIndex` on every `PostDataUpdate` (<c>c_baseentity.cpp:2531</c>). A timeline that has read the
/// whole demo must therefore answer "as of the tick", never "as of the end" (B548).
/// </remarks>
internal sealed class HeldValues<T>
{
    private readonly List<int> _ticks = [];

    private readonly List<T> _values = [];

    /// <summary>How many distinct values were stated.</summary>
    public int Count => _values.Count;

    /// <summary>Every value stated, in order.</summary>
    public IReadOnlyList<T> Values => _values;

    /// <summary>Records the value received at a tick; a repeat of the last is not kept.</summary>
    /// <param name="tick">When it was received; never before the last.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// **A tick earlier than the last is taken AS the last, never refused.** Nothing in the builder guarantees packet
    /// ticks only rise, and one regression must not end the whole demo's build (decoding is total). Treating it as
    /// arriving at the latest tick keeps the list ordered for the binary search, and is the answer a client that
    /// received it last would hold from then on. Keyframes have the same exposure and do not order-check either.
    /// </remarks>
    public void Set(int tick, T value)
    {
        if (_ticks.Count > 0 && tick < _ticks[^1])
        {
            tick = _ticks[^1];
            Clamped++;
        }

        if (_ticks.Count > 0 && _ticks[^1] == tick)
        {
            // A second update at one tick replaces the first — and one that reverts to the value before it
            // leaves no change at all, so the entry goes rather than restating its predecessor.
            if (_values.Count > 1 && EqualityComparer<T>.Default.Equals(_values[^2], value))
            {
                _ticks.RemoveAt(_ticks.Count - 1);
                _values.RemoveAt(_values.Count - 1);
            }
            else
            {
                _values[^1] = value;
            }

            return;
        }

        if (_values.Count > 0 && EqualityComparer<T>.Default.Equals(_values[^1], value))
        {
            return;
        }

        _ticks.Add(tick);
        _values.Add(value);
    }

    /// <summary>How many values arrived with a tick before the last, and were taken at the last.</summary>
    public int Clamped { get; private set; }

    /// <summary>The tick each value in <see cref="Values"/> was set at.</summary>
    public IReadOnlyList<int> Ticks => _ticks;

    /// <summary>The index of the value held at a tick, or -1 before the first.</summary>
    private int IndexAt(double tick)
    {
        int low = 0;
        int high = _ticks.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = (low + high) >>> 1;

            if (_ticks[middle] <= tick)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>The value held at a tick.</summary>
    /// <param name="tick">The moment.</param>
    /// <param name="value">The value, or default before the first.</param>
    /// <returns>Whether anything had been stated by then.</returns>
    public bool TryAt(double tick, out T value)
    {
        int index = IndexAt(tick);
        value = index < 0 ? default! : _values[index];

        return index >= 0;
    }

    /// <summary>Drops every value superseded before a tick, keeping the one in force there (D217 step 3).</summary>
    /// <param name="cut">The earliest tick still to be answered.</param>
    public void DropBefore(int cut)
    {
        int keep = IndexAt(cut);

        if (keep > 0)
        {
            _ticks.RemoveRange(0, keep);
            _values.RemoveRange(0, keep);
            // A copy copies a list's capacity, not its count; this runs before every checkpoint (D217 step 4).
            _ticks.TrimIfSparse();
            _values.TrimIfSparse();
        }
    }

    /// <summary>The next tick after this one at which the value changes, or infinity.</summary>
    /// <param name="tick">The moment.</param>
    /// <returns>The tick.</returns>
    public double ChangesAfter(double tick)
    {
        int next = IndexAt(tick) + 1;

        return next < _ticks.Count ? _ticks[next] : double.PositiveInfinity;
    }
}
