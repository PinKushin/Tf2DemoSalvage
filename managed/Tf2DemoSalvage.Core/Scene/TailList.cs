using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A list that can only grow at its end or have its LAST element replaced or removed (D217).</summary>
/// <remarks>
/// **The invariant the builder's snapshot marks stand on, enforced rather than commented.** A snapshot keeps an output
/// list's count and last element instead of copying it, which is sound only while nothing earlier in the list is ever
/// written again. That was first a remark, and `choreography` broke it for a whole review round — a run's stop written
/// behind later runs. Any edit away from the tail now throws where it is made.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// **Stored in fixed chunks** (D217 step 3): a checkpoint copies the builder with <c>StateCopy</c>, which shares any object
/// still equal to its earlier copy. One growing array would differ at every checkpoint and be copied whole each time —
/// quadratic in a demo's length, and measured as most of f12's checkpoint memory for <c>sounds</c> alone. In chunks, every
/// full chunk is unchanged and shared; only the last is copied again.
/// </remarks>
internal sealed class TailList<T> : IReadOnlyList<T>, ITailList
{
    private const int ChunkBits = 10;

    private const int ChunkSize = 1 << ChunkBits;

    private readonly List<T[]> _chunks = [];

    /// <summary>Where element 0 sits in the first chunk, after a front trim.</summary>
    private int _start;

    private int _count;

    /// <inheritdoc cref="IReadOnlyCollection{T}.Count"/>
    public int Count => _count;

    /// <summary>An element; only the last may be replaced.</summary>
    /// <param name="index">Which.</param>
    /// <returns>The element.</returns>
    /// <exception cref="InvalidOperationException">A write to any element but the last.</exception>
    public T this[int index]
    {
        get => Slot(index);
        set
        {
            Tail(index, "replace");
            Slot(index) = value;
        }
    }

    private ref T Slot(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A tail list of {_count} elements.");
        }

        int at = _start + index;
        return ref _chunks[at >> ChunkBits][at & (ChunkSize - 1)];
    }

    /// <summary>Appends.</summary>
    /// <param name="item">The element.</param>
    public void Add(T item)
    {
        int at = _start + _count;

        if ((at >> ChunkBits) == _chunks.Count)
        {
            _chunks.Add(new T[ChunkSize]);
        }

        _chunks[at >> ChunkBits][at & (ChunkSize - 1)] = item;
        _count++;
    }

    /// <summary>Removes the last element; any other index throws.</summary>
    /// <param name="index">Must be the last.</param>
    public void RemoveAt(int index)
    {
        Tail(index, "remove");
        RemoveLast();
    }

    private void RemoveLast()
    {
        Slot(_count - 1) = default!;
        _count--;
        int used = (_start + _count + ChunkSize - 1) >> ChunkBits;

        if (_count == 0)
        {
            _chunks.Clear();
            _start = 0;
        }
        else if (_chunks.Count > used)
        {
            _chunks.RemoveAt(_chunks.Count - 1);
        }
    }

    /// <summary>The elements as a list for the timeline that is handed out; the tail list is left empty.</summary>
    /// <returns>The list, which no builder may write once it is out (the builder's lease).</returns>
    /// <remarks>Moved chunk by chunk, so a whole build's frames are never held twice.</remarks>
    public List<T> HandOut()
    {
        List<T> items = new(_count);

        for (int chunk = 0; chunk < _chunks.Count; chunk++)
        {
            int from = chunk == 0 ? _start : 0;
            int to = Math.Min(ChunkSize, _start + _count - (chunk << ChunkBits));
            items.AddRange(new ArraySegment<T>(_chunks[chunk], from, to - from));
            _chunks[chunk] = [];
        }

        _chunks.Clear();
        _start = 0;
        _count = 0;
        return items;
    }

    /// <summary>Drops the first <paramref name="count"/> elements, whole chunks freed and the rest cleared.</summary>
    private void DropFront(int count)
    {
        for (int index = 0; index < count; index++)
        {
            Slot(index) = default!;
        }

        _start += count;
        _count -= count;
        int whole = _start >> ChunkBits;
        _chunks.RemoveRange(0, whole);
        _start -= whole << ChunkBits;
    }

    private int FindLastIndex(Predicate<T> match)
    {
        for (int index = _count - 1; index >= 0; index--)
        {
            if (match(Slot(index)))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// As <see cref="DropBefore(Func{T, int}, int)"/>, but for a list that interleaves several streams written only on
    /// change: before the cut, each stream's LAST element is kept, since it is what is in force at the cut.
    /// </summary>
    /// <typeparam name="TKey">What names a stream.</typeparam>
    /// <param name="tickOf">An element's tick; the list is in tick order.</param>
    /// <param name="streamOf">An element's stream.</param>
    /// <param name="cut">The earliest tick still to be answered.</param>
    public void DropBefore<TKey>(Func<T, int> tickOf, Func<T, TKey> streamOf, int cut)
    {
        HashSet<TKey> seen = [];
        List<T> kept = [];

        // Walked backwards from the cut: the first of each stream met is its last before the cut.
        int before = FindLastIndex(item => tickOf(item) < cut);

        for (int index = before; index >= 0; index--)
        {
            if (seen.Add(streamOf(Slot(index))))
            {
                kept.Add(Slot(index));
            }
        }

        if (before < 0 || kept.Count == before + 1)
        {
            return;
        }

        // Rebuilt rather than spliced: the kept records go in front of what follows the cut, in their order.
        List<T> after = [.. this.Skip(before + 1)];
        kept.Reverse();
        _chunks.Clear();
        _start = 0;
        _count = 0;

        foreach (T item in kept.Concat(after))
        {
            Add(item);
        }
    }

    /// <summary>
    /// Drops the front of the list up to the last element stamped before <paramref name="cut"/>, which is kept as the
    /// value in force there (D217 step 3). The last element is never dropped.
    /// </summary>
    /// <param name="tickOf">An element's tick; the list is in tick order.</param>
    /// <param name="cut">The earliest tick still to be answered.</param>
    public void DropBefore(Func<T, int> tickOf, int cut)
    {
        int inForce = FindLastIndex(item => tickOf(item) < cut);

        if (inForce > 0)
        {
            DropFront(Math.Min(inForce, _count - 1));
        }
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        for (int index = 0; index < _count; index++)
        {
            yield return Slot(index);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? ITailList.Last => _count > 0 ? Slot(_count - 1) : null;

    void ITailList.RemoveLast() => RemoveLast();

    void ITailList.AddBoxed(object? item) => Add((T)item!);

    private void Tail(int index, string what)
    {
        if (index != _count - 1)
        {
            throw new InvalidOperationException(
                $"A tail list may only {what} its last element ({_count - 1}), not {index} (D217).");
        }
    }
}

/// <summary>What a builder snapshot needs of a <see cref="TailList{T}"/> without knowing its element type.</summary>
internal interface ITailList
{
    /// <summary>How many elements.</summary>
    public int Count { get; }

    /// <summary>The last element, or null when empty.</summary>
    public object? Last { get; }

    /// <summary>Removes the last element.</summary>
    public void RemoveLast();

    /// <summary>Appends an element given as an object.</summary>
    /// <param name="item">The element, of the list's type.</param>
    public void AddBoxed(object? item);
}
