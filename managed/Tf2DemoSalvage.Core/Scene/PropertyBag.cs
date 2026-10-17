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
///
/// **A clear keeps the map and hides its keys** (D217 step 4, part 3): only the first <see cref="_count"/> slots are the
/// bag's, so an entity that re-enters and is sent its keys in the same order as before re-uses its map rather than
/// building another — slot maps were 210 MB of what rgl-pug's checkpoints kept of the entity table. A key that arrives
/// out of that order makes the bag a map of its own, holding exactly its visible keys in their order.
/// </remarks>
internal sealed class PropertyBag : IReadOnlyDictionary<string, PropertyValue>
{
    private const int ChunkBits = 5;

    private const int ChunkSize = 1 << ChunkBits;

    /// <summary>
    /// A bag's contents made immutable at a checkpoint (D217 step 4), which <see cref="StateCopy"/> shares rather than
    /// copies: nothing writes its map or its chunks again, because the next write to the bag thaws first.
    /// </summary>
    /// <remarks>
    /// **Packed** (D217 step 4, part 3): a frozen slot is a <see cref="PropertyValue.Packed"/> and its sequence, 32 bytes
    /// against 48, because the entity table was the largest thing rgl-pug's checkpoints kept (789 MB). A read unpacks.
    /// </remarks>
    internal sealed class Frozen(Dictionary<string, int> slots, int count, (PropertyValue.Packed Value, long Sequence)[][] chunks)
    {
        public Dictionary<string, int> Slots { get; } = slots;

        /// <summary>How many of the map's slots, in order, are the bag's.</summary>
        public int Count { get; } = count;

        public (PropertyValue.Packed Value, long Sequence)[][] Chunks { get; } = chunks;
    }

    // Exactly one of the two forms is live: _frozen, or the three below it (null while frozen, so a copy of a frozen
    // bag follows nothing but the shared Frozen).
    private Frozen? _frozen;

    /// <summary>
    /// While thawed, the frozen form it thawed from: a chunk not yet written since is read from there, still packed, and
    /// is handed to the next freeze as it is.
    /// </summary>
    private Frozen? _base;

    private Dictionary<string, int>? _slots = new(StringComparer.Ordinal);

    /// <summary>While thawed, how many of <see cref="_slots"/>'s slots, in order, are the bag's.</summary>
    private int _count;

    /// <summary>The writable chunks; null for one still read from <see cref="_base"/>.</summary>
    private List<(PropertyValue Value, long Sequence)[]?>? _chunks = [];

    // OWNERSHIP IS NOT COPIED, so false and null mean "owns nothing", and that is what a copy starts with: a copy of a
    // thawed bag may hold a chunk or a map that a prior copy reuses, and a copied "owned" would write straight into it.
    // A copy instead copies before its first write, as a thawed bag does. Set true only where this instance makes the map.

    /// <summary>Whether this instance made <see cref="_slots"/>, and may add a key to it in place.</summary>
    [StateCopy.NotCopied]
    private bool _ownsSlots = true;

    /// <summary>Per chunk, whether this instance made it and may write it in place; missing or null is not owned.</summary>
    [StateCopy.NotCopied]
    private List<bool>? _owned = [];

    /// <summary>
    /// Packed chunks unpacked for reading, by chunk: a read of a frozen chunk unpacks it once, not once per value. Never
    /// copied, so it is this instance's alone; it describes <see cref="_frozen"/>, or <see cref="_base"/> while thawed.
    /// </summary>
    [StateCopy.NotCopied]
    private (PropertyValue Value, long Sequence)[]?[]? _unpacked;

    private Dictionary<string, int> Slots => _frozen?.Slots ?? _slots!;

    /// <summary>The key → slot map in use, by identity: what a re-entered entity re-uses (D217 step 4, part 3).</summary>
    internal object SlotMap => Slots;

    /// <summary>Whether the bag is in its frozen, shared form.</summary>
    internal bool IsFrozen => _frozen is not null;

    /// <inheritdoc/>
    public int Count => _frozen?.Count ?? _count;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => Slots.Keys.Take(Count);

    /// <inheritdoc/>
    public IEnumerable<PropertyValue> Values => Slots.Values.Take(Count).Select(slot => Read(slot).Value);

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

        (PropertyValue.Packed, long)[][] packed = new (PropertyValue.Packed, long)[_chunks!.Count][];

        for (int chunk = 0; chunk < packed.Length; chunk++)
        {
            packed[chunk] = _chunks[chunk] is { } written ? Pack(written) : _base!.Chunks[chunk];
        }

        _frozen = new Frozen(_slots!, _count, packed);
        _base = null;
        _unpacked = null;
        _slots = null;
        _chunks = null;
        // Nothing to reset beyond this: ownership is read only while thawed, and Thaw sets it before any read.
        _owned = null;
    }

    /// <summary>Back to the writable form over the frozen one's map and chunks, owning none of them.</summary>
    /// <remarks>The one place a thawed bag learns it shares: every write below copies before it writes.</remarks>
    private void Thaw()
    {
        if (_frozen is null)
        {
            return;
        }

        _slots = _frozen.Slots;
        _count = _frozen.Count;
        _ownsSlots = false;
        _chunks = [.. new (PropertyValue, long)[]?[_frozen.Chunks.Length]];
        _owned = null;
        _base = _frozen;
        _frozen = null;
    }

    private static (PropertyValue.Packed, long)[] Pack((PropertyValue Value, long Sequence)[] chunk)
    {
        (PropertyValue.Packed, long)[] packed = new (PropertyValue.Packed, long)[chunk.Length];

        for (int index = 0; index < chunk.Length; index++)
        {
            packed[index] = (chunk[index].Value.Pack(), chunk[index].Sequence);
        }

        return packed;
    }

    private static (PropertyValue Value, long Sequence)[] Unpack((PropertyValue.Packed Value, long Sequence)[] chunk)
    {
        (PropertyValue, long)[] values = new (PropertyValue, long)[chunk.Length];

        for (int index = 0; index < chunk.Length; index++)
        {
            values[index] = (PropertyValue.Unpack(chunk[index].Value), chunk[index].Sequence);
        }

        return values;
    }

    private bool Owns(int chunk) => _owned is { } owned && chunk < owned.Count && owned[chunk];

    private void Own(int chunk)
    {
        _owned ??= [];

        while (_owned.Count <= chunk)
        {
            _owned.Add(false);
        }

        _owned[chunk] = true;
    }

    /// <summary>The slot a key holds in the bag, or −1: a key the map holds past the visible count is not the bag's.</summary>
    private int SlotOf(string key) => Slots.TryGetValue(key, out int slot) && slot < Count ? slot : -1;

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

        int slot = SlotOf(key);

        if (slot < 0)
        {
            slot = Append(key);

            if ((slot >> ChunkBits) == _chunks!.Count)
            {
                _chunks.Add(new (PropertyValue, long)[ChunkSize]);
                Own(slot >> ChunkBits);
            }
        }

        int chunk = slot >> ChunkBits;

        if (!Owns(chunk))
        {
            _chunks![chunk] = ((PropertyValue, long)[])(_chunks[chunk] ?? Unpacked(_base!, chunk)).Clone();
            Own(chunk);
        }

        _chunks![chunk]![slot & (ChunkSize - 1)] = (value, sequence);
    }

    /// <summary>Makes a new key the next visible slot: the map's own next key when it is this one, else a map of its own.</summary>
    private int Append(string key)
    {
        if (_slots!.TryGetValue(key, out int held) && held == _count)
        {
            // The same key in the same place as before the clear: the map already says so.
            return _count++;
        }

        if (!_ownsSlots || _slots.Count != _count)
        {
            // Copied in insertion order, visible keys only: nothing is ever removed from a slot map, only hidden. An
            // enumeration of Keys in progress goes on over the old map rather than throwing, which no caller relies on.
            Dictionary<string, int> own = new(_count + 1, StringComparer.Ordinal);

            foreach ((string visible, int at) in _slots.Take(_count))
            {
                own.Add(visible, at);
            }

            _slots = own;
            _ownsSlots = true;
        }

        _slots.Add(key, _count);
        return _count++;
    }

    /// <summary>When a key was last written, or −1 when it never was.</summary>
    /// <param name="key">The property's key.</param>
    /// <returns>The sequence number.</returns>
    public long Sequence(string key) => SlotOf(key) is >= 0 and int slot ? Read(slot).Sequence : -1;

    /// <summary>Forgets every property; enumeration restarts from the next write, as the dictionary's did.</summary>
    /// <remarks>The map is kept, its keys hidden, for an entity sent the same keys in the same order again.</remarks>
    public void Clear()
    {
        if (_frozen is not null)
        {
            _slots = _frozen.Slots;
            _ownsSlots = false;
        }

        _frozen = null;
        _base = null;
        _unpacked = null;
        _count = 0;
        _chunks = [];
        _owned = [];
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => SlotOf(key) >= 0;

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out PropertyValue value)
    {
        int slot = SlotOf(key);

        if (slot >= 0)
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
        foreach ((string key, int slot) in Slots.Take(Count))
        {
            yield return new KeyValuePair<string, PropertyValue>(key, Read(slot).Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private (PropertyValue Value, long Sequence) Read(int slot)
    {
        int chunk = slot >> ChunkBits;
        int at = slot & (ChunkSize - 1);

        if ((_frozen ?? (_chunks![chunk] is null ? _base : null)) is { } packed)
        {
            return Unpacked(packed, chunk)[at];
        }

        return _chunks![chunk]![at];
    }

    /// <summary>A packed chunk of the frozen form this bag reads, unpacked once and kept for later reads.</summary>
    private (PropertyValue Value, long Sequence)[] Unpacked(Frozen packed, int chunk)
    {
        _unpacked ??= new (PropertyValue, long)[]?[packed.Chunks.Length];
        return _unpacked[chunk] ??= Unpack(packed.Chunks[chunk]);
    }
}
