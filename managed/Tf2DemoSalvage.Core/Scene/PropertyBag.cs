using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// One entity's properties by key, each with the sequence number of its last write — <see cref="EntityState"/>'s storage.
/// </summary>
/// <remarks>
/// **Shaped for checkpoints** (D217 step 3). It replaced two dictionaries, values and write sequences, which a checkpoint
/// copied whole whenever any one property changed: measured on f12, 15.7 M dictionary entries re-created over 291
/// checkpoints, the largest holder left. Here the key → slot map is unchanged once an entity has been sent in full, and
/// the values sit in 32-slot chunks, so a checkpoint shares the map and every chunk no write touched.
///
/// **Enumeration order is the old dictionary's — insertion order, restarted by <see cref="Clear"/>** — because the slot
/// map IS a dictionary keyed the same way and filled in the same order, and several readers take the first key that
/// matches a pattern.
/// </remarks>
internal sealed class PropertyBag : IReadOnlyDictionary<string, PropertyValue>
{
    private const int ChunkBits = 5;

    private const int ChunkSize = 1 << ChunkBits;

    /// <summary>
    /// A bag's contents made immutable at a checkpoint (D217 step 4), which <see cref="StateCopy"/> shares rather than
    /// copies: nothing writes its map or its chunks again, because the next write to the bag thaws first.
    /// </summary>
    internal sealed class Frozen(Dictionary<string, int> slots, (PropertyValue Value, long Sequence)[][] chunks)
    {
        public Dictionary<string, int> Slots { get; } = slots;

        public (PropertyValue Value, long Sequence)[][] Chunks { get; } = chunks;
    }

    // Exactly one of the two forms is live: _frozen, or the three below it (null while frozen, so a copy of a frozen
    // bag follows nothing but the shared Frozen).
    private Frozen? _frozen;

    private Dictionary<string, int>? _slots = new(StringComparer.Ordinal);

    /// <summary>Whether <see cref="_slots"/> is a frozen form's map, to be copied before a key is added.</summary>
    private bool _slotsShared;

    private List<(PropertyValue Value, long Sequence)[]>? _chunks = [];

    /// <summary>Per chunk, whether this bag owns it or a frozen form does (copy on first write).</summary>
    private List<bool>? _owned = [];

    private Dictionary<string, int> Slots => _frozen?.Slots ?? _slots!;

    /// <inheritdoc/>
    public int Count => Slots.Count;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => Slots.Keys;

    /// <inheritdoc/>
    public IEnumerable<PropertyValue> Values => Slots.Values.Select(slot => Read(slot).Value);

    /// <summary>Makes the contents immutable and shareable; a no-op when nothing was written since the last freeze.</summary>
    /// <remarks>
    /// **Only a checkpoint freezes** (D217 step 4): a straight build never calls this, so it never pays a thaw. A resume
    /// copy then shares every bag, and pays a thaw only for the entities its own window writes.
    /// </remarks>
    public void Freeze()
    {
        if (_frozen is not null)
        {
            return;
        }

        _frozen = new Frozen(_slots!, [.. _chunks!]);
        _slots = null;
        _chunks = null;
        _owned = null;
        _slotsShared = false;
    }

    private void Thaw()
    {
        if (_frozen is null)
        {
            return;
        }

        _slots = _frozen.Slots;
        _slotsShared = true;
        _chunks = [.. _frozen.Chunks];
        _owned = [.. Enumerable.Repeat(false, _chunks.Count)];
        _frozen = null;
    }

    /// <inheritdoc/>
    public PropertyValue this[string key] =>
        TryGetValue(key, out PropertyValue value) ? value : throw new KeyNotFoundException(key);

    /// <summary>Writes a value and the sequence number it was written at.</summary>
    /// <param name="key">The property's key.</param>
    /// <param name="value">The value.</param>
    /// <param name="sequence">The entity's write counter at this write.</param>
    public void Set(string key, PropertyValue value, long sequence)
    {
        Thaw();

        if (!_slots!.TryGetValue(key, out int slot))
        {
            if (_slotsShared)
            {
                // Copied in insertion order: nothing is ever removed from a slot map, only cleared.
                _slots = new Dictionary<string, int>(_slots, StringComparer.Ordinal);
                _slotsShared = false;
            }

            slot = _slots.Count;
            _slots.Add(key, slot);

            if ((slot >> ChunkBits) == _chunks!.Count)
            {
                _chunks.Add(new (PropertyValue, long)[ChunkSize]);
                _owned!.Add(true);
            }
        }

        int chunk = slot >> ChunkBits;

        if (!_owned![chunk])
        {
            _chunks![chunk] = ((PropertyValue Value, long Sequence)[])_chunks[chunk].Clone();
            _owned[chunk] = true;
        }

        _chunks![chunk][slot & (ChunkSize - 1)] = (value, sequence);
    }

    /// <summary>When a key was last written, or −1 when it never was.</summary>
    /// <param name="key">The property's key.</param>
    /// <returns>The sequence number.</returns>
    public long Sequence(string key) => Slots.TryGetValue(key, out int slot) ? Read(slot).Sequence : -1;

    /// <summary>Forgets every property; enumeration restarts from the next write, as the dictionary's did.</summary>
    public void Clear()
    {
        _frozen = null;
        _slots = new(StringComparer.Ordinal);
        _slotsShared = false;
        _chunks = [];
        _owned = [];
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => Slots.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out PropertyValue value)
    {
        if (Slots.TryGetValue(key, out int slot))
        {
            value = Read(slot).Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        foreach ((string key, int slot) in Slots)
        {
            yield return new KeyValuePair<string, PropertyValue>(key, Read(slot).Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private (PropertyValue Value, long Sequence) Read(int slot) =>
        (_frozen is { } frozen ? frozen.Chunks[slot >> ChunkBits] : _chunks![slot >> ChunkBits])[slot & (ChunkSize - 1)];
}
