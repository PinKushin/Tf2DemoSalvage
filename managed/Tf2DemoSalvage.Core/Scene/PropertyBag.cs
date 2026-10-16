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

    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    private readonly List<(PropertyValue Value, long Sequence)[]> _chunks = [];

    /// <inheritdoc/>
    public int Count => _slots.Count;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _slots.Keys;

    /// <inheritdoc/>
    public IEnumerable<PropertyValue> Values => _slots.Values.Select(slot => At(slot).Value);

    /// <inheritdoc/>
    public PropertyValue this[string key] =>
        TryGetValue(key, out PropertyValue value) ? value : throw new KeyNotFoundException(key);

    /// <summary>Writes a value and the sequence number it was written at.</summary>
    /// <param name="key">The property's key.</param>
    /// <param name="value">The value.</param>
    /// <param name="sequence">The entity's write counter at this write.</param>
    public void Set(string key, PropertyValue value, long sequence)
    {
        if (!_slots.TryGetValue(key, out int slot))
        {
            slot = _slots.Count;
            _slots.Add(key, slot);

            if ((slot >> ChunkBits) == _chunks.Count)
            {
                _chunks.Add(new (PropertyValue, long)[ChunkSize]);
            }
        }

        At(slot) = (value, sequence);
    }

    /// <summary>When a key was last written, or −1 when it never was.</summary>
    /// <param name="key">The property's key.</param>
    /// <returns>The sequence number.</returns>
    public long Sequence(string key) => _slots.TryGetValue(key, out int slot) ? At(slot).Sequence : -1;

    /// <summary>Forgets every property; enumeration restarts from the next write, as the dictionary's did.</summary>
    public void Clear()
    {
        _slots.Clear();
        _chunks.Clear();
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _slots.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out PropertyValue value)
    {
        if (_slots.TryGetValue(key, out int slot))
        {
            value = At(slot).Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, PropertyValue>> GetEnumerator()
    {
        foreach ((string key, int slot) in _slots)
        {
            yield return new KeyValuePair<string, PropertyValue>(key, At(slot).Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ref (PropertyValue Value, long Sequence) At(int slot) => ref _chunks[slot >> ChunkBits][slot & (ChunkSize - 1)];
}
