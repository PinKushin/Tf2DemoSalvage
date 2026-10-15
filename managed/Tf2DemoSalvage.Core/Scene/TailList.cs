using System;
using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A list that can only grow at its end or have its LAST element replaced or removed (D217).</summary>
/// <remarks>
/// **The invariant the builder's snapshot marks stand on, enforced rather than commented.** A snapshot keeps an output
/// list's count and last element instead of copying it, which is sound only while nothing earlier in the list is ever
/// written again. That was first a remark, and `choreography` broke it for a whole review round — a run's stop written
/// behind later runs. Any edit away from the tail now throws where it is made.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class TailList<T> : IReadOnlyList<T>, ITailList
{
    private readonly List<T> _items = [];

    /// <inheritdoc cref="IReadOnlyCollection{T}.Count"/>
    public int Count => _items.Count;

    /// <summary>An element; only the last may be replaced.</summary>
    /// <param name="index">Which.</param>
    /// <returns>The element.</returns>
    /// <exception cref="InvalidOperationException">A write to any element but the last.</exception>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            Tail(index, "replace");
            _items[index] = value;
        }
    }

    /// <summary>Appends.</summary>
    /// <param name="item">The element.</param>
    public void Add(T item) => _items.Add(item);

    /// <summary>Removes the last element; any other index throws.</summary>
    /// <param name="index">Must be the last.</param>
    public void RemoveAt(int index)
    {
        Tail(index, "remove");
        _items.RemoveAt(index);
    }

    /// <summary>The list itself, for the timeline that is handed out.</summary>
    /// <returns>The backing list, which no builder may write once it is out (the builder's lease).</returns>
    public List<T> HandOut() => _items;

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
        int before = _items.FindLastIndex(item => tickOf(item) < cut);

        for (int index = before; index >= 0; index--)
        {
            if (seen.Add(streamOf(_items[index])))
            {
                kept.Add(_items[index]);
            }
        }

        if (before < 0 || kept.Count == before + 1)
        {
            return;
        }

        kept.Reverse();
        _items.RemoveRange(0, before + 1);
        _items.InsertRange(0, kept);
    }

    /// <summary>
    /// Drops the front of the list up to the last element stamped before <paramref name="cut"/>, which is kept as the
    /// value in force there (D217 step 3). The last element is never dropped.
    /// </summary>
    /// <param name="tickOf">An element's tick; the list is in tick order.</param>
    /// <param name="cut">The earliest tick still to be answered.</param>
    public void DropBefore(Func<T, int> tickOf, int cut)
    {
        int inForce = _items.FindLastIndex(item => tickOf(item) < cut);

        if (inForce > 0)
        {
            _items.RemoveRange(0, Math.Min(inForce, _items.Count - 1));
        }
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? ITailList.Last => _items.Count > 0 ? _items[^1] : null;

    void ITailList.RemoveLast() => _items.RemoveAt(_items.Count - 1);

    void ITailList.AddBoxed(object? item) => _items.Add((T)item!);

    private void Tail(int index, string what)
    {
        if (index != _items.Count - 1)
        {
            throw new InvalidOperationException(
                $"A tail list may only {what} its last element ({_items.Count - 1}), not {index} (D217).");
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
