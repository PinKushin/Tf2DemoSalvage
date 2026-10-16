using System;
using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A growing list stored in fixed chunks, so a copy of a later state shares every chunk an earlier copy holds (D217 step 3).</summary>
/// <remarks>
/// **Why chunks.** A checkpoint copies the builder with <c>StateCopy</c>, which shares any object still equal to its
/// earlier copy. One growing array differs at every checkpoint and is copied whole each time — quadratic in a demo's
/// length; measured as most of f12's checkpoint memory for <c>sounds</c> alone. In chunks, every chunk no write touched
/// is unchanged and shared, and only the chunks written since are copied again.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ChunkedList<T> : IReadOnlyList<T>
{
    private const int ChunkBits = 10;

    private const int ChunkSize = 1 << ChunkBits;

    private readonly List<T[]> _chunks = [];

    /// <summary>Where element 0 sits in the first chunk, after a front drop.</summary>
    private int _start;

    private int _count;

    /// <inheritdoc cref="IReadOnlyCollection{T}.Count"/>
    public int Count => _count;

    /// <summary>An element.</summary>
    /// <param name="index">Which.</param>
    /// <returns>The element.</returns>
    public T this[int index]
    {
        get => Slot(index);
        set => Slot(index) = value;
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

    /// <summary>Removes the last element.</summary>
    public void RemoveLast()
    {
        Slot(_count - 1) = default!;
        _count--;

        if (_count == 0)
        {
            _chunks.Clear();
            _start = 0;
        }
        else if (_chunks.Count > (_start + _count + ChunkSize - 1) >> ChunkBits)
        {
            _chunks.RemoveAt(_chunks.Count - 1);
        }
    }

    /// <summary>Empties the list.</summary>
    public void Clear()
    {
        _chunks.Clear();
        _start = 0;
        _count = 0;
    }

    /// <summary>Drops the first <paramref name="count"/> elements: whole chunks freed, the rest of the first cleared.</summary>
    /// <param name="count">How many.</param>
    public void DropFront(int count)
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

    /// <summary>The last index whose element matches, or −1.</summary>
    /// <param name="match">The test.</param>
    /// <returns>The index.</returns>
    public int FindLastIndex(Predicate<T> match)
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

    /// <summary>The elements as a list, and this one left empty — moved chunk by chunk, so they are never held twice.</summary>
    /// <returns>The list.</returns>
    public List<T> MoveOut()
    {
        List<T> items = new(_count);

        for (int chunk = 0; chunk < _chunks.Count; chunk++)
        {
            int from = chunk == 0 ? _start : 0;
            int to = Math.Min(ChunkSize, _start + _count - (chunk << ChunkBits));
            items.AddRange(new ArraySegment<T>(_chunks[chunk], from, to - from));
            _chunks[chunk] = [];
        }

        Clear();
        return items;
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

    private ref T Slot(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A list of {_count} elements.");
        }

        int at = _start + index;
        return ref _chunks[at >> ChunkBits][at & (ChunkSize - 1)];
    }
}
