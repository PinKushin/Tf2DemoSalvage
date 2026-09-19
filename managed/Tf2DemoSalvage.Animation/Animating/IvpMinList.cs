using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's event queue: elements kept in order of a float value, the least first — the min-list the time manager keeps at
/// <c>+0x10</c>, <c>FUN_1800aaed0</c> and <c>FUN_1800ab1b0</c> (B369).
/// </summary>
/// <typeparam name="T">What is queued.</typeparam>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The event queue, and where the far branch hands a pair off*). An add
/// at or under the minimum becomes the head; any other goes in before the first entry its value is not over, so a new
/// event precedes every queued event of equal time. The engine also threads a long list through the entries to shorten
/// the walk, rebalanced as walks lengthen; it cannot change where an entry lands, so it is not carried. **The constructor,
/// `FUN_1800aae10`, chains every entry free in index order**, so slots are numbered as a list that grows from empty numbers
/// them — last-freed-first, else the next index — whatever capacity it is built with.
/// </remarks>
public sealed class IvpMinList<T>
    where T : class
{
    /// <summary><c>0x501502f9</c>: the minimum of an empty queue.</summary>
    public const float EmptyMinimum = 1e10f;

    private const int None = -1;

    private readonly List<Entry> _entries = [];

    private int _head = None;
    private int _free = None;

    /// <summary>The head's value — <c>+0x10</c> — or <see cref="EmptyMinimum"/> when nothing is queued.</summary>
    public float Minimum { get; private set; } = EmptyMinimum;

    /// <summary>How many elements are queued — <c>+0x1c</c>.</summary>
    public int Count { get; private set; }

    /// <summary>Queues an element — <c>FUN_1800aaed0</c>.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">Its value; for the time manager, its time less the manager's base, narrowed.</param>
    /// <returns>The slot the element occupies until it is removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The queue is empty and the value is over <see cref="EmptyMinimum"/>, where the engine walks from index
    /// <c>0xffff</c>, past its entries.
    /// </exception>
    /// <remarks>
    /// **`COMISS` then `JA`**: the value becomes the head unless it is over the minimum, so a tie and a NaN do too. The walk
    /// goes on while the value is over each entry's (`JBE` stops it), so it stops before a NaN entry.
    /// </remarks>
    public int Add(T element, float value)
    {
        ArgumentNullException.ThrowIfNull(element);

        bool overMinimum = value > Minimum;

        if (overMinimum && _head == None)
        {
            throw new InvalidOperationException(
                "A value over an empty queue's minimum walks from index 0xffff, past the engine's entries.");
        }

        int previous = None;
        int next = _head;

        if (overMinimum)
        {
            while (next != None && value > _entries[next].Value)
            {
                previous = next;
                next = _entries[next].Next;
            }
        }

        int slot = Take();
        _entries[slot] = new Entry(element, value, next, previous);

        if (previous == None)
        {
            _head = slot;
            Minimum = value;
        }
        else
        {
            _entries[previous] = _entries[previous] with { Next = slot };
        }

        if (next != None)
        {
            _entries[next] = _entries[next] with { Previous = slot };
        }

        Count++;
        return slot;
    }

    /// <summary>Unlinks a queued element — <c>FUN_1800ab1b0</c>.</summary>
    /// <param name="slot">The slot <see cref="Add"/> returned.</param>
    /// <exception cref="ArgumentOutOfRangeException">The slot holds nothing.</exception>
    /// <remarks>
    /// **Removing the head makes the next entry's value the minimum**, or <see cref="EmptyMinimum"/> when none is left. The
    /// slot goes to the head of the free list.
    /// </remarks>
    public void Remove(int slot)
    {
        if (slot < 0 || slot >= _entries.Count || _entries[slot].Element is null)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "The slot holds no queued element.");
        }

        Entry entry = _entries[slot];

        if (entry.Previous != None)
        {
            _entries[entry.Previous] = _entries[entry.Previous] with { Next = entry.Next };

            if (entry.Next != None)
            {
                _entries[entry.Next] = _entries[entry.Next] with { Previous = entry.Previous };
            }
        }
        else
        {
            _head = entry.Next;

            if (entry.Next != None)
            {
                _entries[entry.Next] = _entries[entry.Next] with { Previous = None };
                Minimum = _entries[entry.Next].Value;
            }
            else
            {
                Minimum = EmptyMinimum;
            }
        }

        _entries[slot] = new Entry(null, 0f, _free, None);
        _free = slot;
        Count--;
    }

    /// <summary>Adds a shift to every queued value and to the minimum — the hull manager's rebase, <c>FUN_180094490</c>.</summary>
    /// <param name="shift">What is added, in float.</param>
    /// <param name="visit">Handed each element, head first, once its value has moved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visit"/> is null.</exception>
    /// <remarks>
    /// Each entry's next is read before its element is visited, as the engine reads it. **The minimum moves even when
    /// nothing is queued**, so an empty list's `1e10f` drifts with the shift until something is added or removed.
    /// </remarks>
    public void Offset(float shift, Action<T> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        int index = _head;

        while (index != None)
        {
            Entry entry = _entries[index];
            _entries[index] = entry with { Value = shift + entry.Value };

            if (entry.Element is T element)
            {
                visit(element);
            }

            index = entry.Next;
        }

        Minimum = shift + Minimum;
    }

    /// <summary>Subtracts a shift from every queued value and from the minimum — the time manager's rebase, <c>FUN_18008a020</c>.</summary>
    /// <param name="shift">What is subtracted, in float.</param>
    /// <remarks>
    /// `*(float *)(entry + 8) -= shift` walking from the head, then the same for the list's <c>+0x10</c> — so, as with
    /// <see cref="Offset"/>, **the minimum moves even when nothing is queued**. Order is not re-established; one float subtracted
    /// from every entry keeps it.
    /// </remarks>
    public void Shift(float shift)
    {
        for (int index = _head; index != None; index = _entries[index].Next)
        {
            _entries[index] = _entries[index] with { Value = _entries[index].Value - shift };
        }

        Minimum -= shift;
    }

    /// <summary>The head of the queue — what the event loop fires next.</summary>
    /// <param name="element">The head's element, when there is one.</param>
    /// <param name="slot">The head's slot, or <c>-1</c> when the queue is empty.</param>
    /// <returns>Whether anything is queued.</returns>
    public bool TryFirst([MaybeNullWhen(false)] out T element, out int slot)
    {
        slot = _head;

        if (_head != None && _entries[_head].Element is T first)
        {
            element = first;
            return true;
        }

        element = null;
        return false;
    }

    /// <summary>The value an element was queued with — the float at <c>+0x8</c> of its entry.</summary>
    /// <param name="slot">The slot <see cref="Add"/> returned.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The slot holds nothing.</exception>
    public float ValueOf(int slot) =>
        slot >= 0 && slot < _entries.Count && _entries[slot].Element is not null
            ? _entries[slot].Value
            : throw new ArgumentOutOfRangeException(nameof(slot), slot, "The slot holds no queued element.");

    private int Take()
    {
        if (_free != None)
        {
            int reused = _free;
            _free = _entries[reused].Next;
            return reused;
        }

        _entries.Add(new Entry(null, 0f, None, None));
        return _entries.Count - 1;
    }

    /// <summary>One entry: its element, null while free, its value, and its neighbours — the free list's next when free.</summary>
    private readonly record struct Entry(T? Element, float Value, int Next, int Previous);
}
