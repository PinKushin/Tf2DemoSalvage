using System;
using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>A decoded property list nothing can write once it is made — what the decoder reads and merges into.</summary>
/// <remarks>
/// **Its own type so a checkpoint can share it** (D217 step 4): the decoder's class baselines, its per-entity baseline
/// slots and every entity it hands out hold these lists, and a resume copied each one element by element — 161 MB of a
/// rgl-pug resume was the decoder. <c>StateCopy.Immutable</c> names this type, so immutability is enforced here rather
/// than promised by callers: every constructor COPIES what it is given into an array no one else can reach.
///
/// **Stored without the definitions when it can be** (D217 step 4, part 3): every property a class decodes carries
/// <c>flat[Index]</c> as its definition, by value — some 130 of a property's 210 bytes — and the per-entity baseline
/// slots alone kept 812 MB of rgl-pug's checkpoints. Given the class's flattened list, a property whose definition IS
/// <c>flat[Index]</c> is kept as the rest, and its definition is read back from the list. One whose definition is
/// anything else keeps the list whole, so what is read back is always exactly what was put in.
///
/// **Read without building a property** where the reader allows: <see cref="IndexAt"/>, <see cref="ValueAt"/> and
/// <see cref="KeyAt"/> skip the 130-byte definition copy the indexer makes, which the entity table and the merge do per
/// property per update.
/// </remarks>
internal sealed class DecodedProperties : IReadOnlyList<DecodedProperty>
{
    /// <remarks>The value packed (24 bytes, not 40), and read back field for field: <see cref="PropertyValue.Pack"/>.</remarks>
    private readonly record struct Compact(
        int Index, PropertyValue.Packed Value, int IndexPayloadBits, int CoordShape, IReadOnlyList<int>? ElementShapes);

    private readonly IReadOnlyList<FlatProperty>? _flat;

    /// <summary>The class's storage keys, parallel to <see cref="_flat"/>, when the decoder gave them.</summary>
    private readonly string[]? _keys;

    private readonly Compact[]? _compact;

    private readonly DecodedProperty[]? _full;

    private DecodedProperties(IReadOnlyList<FlatProperty>? flat, string[]? keys, Compact[]? compact, DecodedProperty[]? full)
    {
        _flat = flat;
        _keys = keys;
        _compact = compact;
        _full = full;
    }

    /// <summary>A list holding a private copy of <paramref name="items"/>, definitions included.</summary>
    /// <param name="items">The properties; later writes to the caller's storage do not reach this list.</param>
    public DecodedProperties(ReadOnlySpan<DecodedProperty> items)
        : this(null, null, null, items.ToArray())
    {
    }

    /// <summary>A list of one class's properties, stored without their definitions when each is the class's own.</summary>
    /// <param name="flat">The class's flattened properties, which the schema owns and nothing writes.</param>
    /// <param name="items">The properties; copied, as by the other constructor.</param>
    public DecodedProperties(IReadOnlyList<FlatProperty> flat, ReadOnlySpan<DecodedProperty> items)
        : this(flat, null, Compacted(flat, items, verify: true), null)
    {
        _full = _compact is null ? items.ToArray() : null;
        _flat = _compact is null ? null : flat;
    }

    /// <summary>A list the decoder read for a class, whose every definition is <c>flat[Index]</c> by construction.</summary>
    /// <param name="flat">The class's flattened properties.</param>
    /// <param name="keys">The class's storage keys, parallel to <paramref name="flat"/>.</param>
    /// <param name="items">The properties, each made with <c>flat[Index]</c>; copied.</param>
    /// <returns>The list, compared against nothing: the caller is the one place that knows the definitions are the list's.</returns>
    internal static DecodedProperties OfClass(IReadOnlyList<FlatProperty> flat, string[] keys, ReadOnlySpan<DecodedProperty> items) =>
        new(flat, keys, Compacted(flat, items, verify: false), null);

    private static Compact[]? Compacted(IReadOnlyList<FlatProperty> flat, ReadOnlySpan<DecodedProperty> items, bool verify)
    {
        ArgumentNullException.ThrowIfNull(flat);

        Compact[] compact = new Compact[items.Length];

        for (int index = 0; index < items.Length; index++)
        {
            ref readonly DecodedProperty item = ref items[index];

            if (item.Index < 0 || item.Index >= flat.Count || (verify && !item.Definition.Equals(flat[item.Index])))
            {
                // Not the class's own definition: the caller keeps everything, so a read gives back exactly this.
                return null;
            }

            compact[index] = new Compact(item.Index, item.Value.Pack(), item.IndexPayloadBits, item.CoordShape, item.ElementShapes);
        }

        return compact;
    }

    /// <summary>The class's flattened list this is stored against, or null when it keeps its definitions.</summary>
    public IReadOnlyList<FlatProperty>? Flat => _flat;

    /// <inheritdoc/>
    public int Count => _compact?.Length ?? _full!.Length;

    /// <summary>One property's flattened index, without building the property.</summary>
    /// <param name="index">Which property.</param>
    /// <returns>Its index.</returns>
    public int IndexAt(int index) => _compact is { } compact ? compact[index].Index : _full![index].Index;

    /// <summary>One property's value, without building the property.</summary>
    /// <param name="index">Which property.</param>
    /// <returns>Its value.</returns>
    public PropertyValue ValueAt(int index) =>
        _compact is { } compact ? PropertyValue.Unpack(compact[index].Value) : _full![index].Value;

    /// <summary>The key one property is stored under in an entity, from the class's keys when the decoder gave them.</summary>
    /// <param name="index">Which property.</param>
    /// <returns>The key, the class's own instance when there is one.</returns>
    public string KeyAt(int index) =>
        _keys is { } keys && _compact is { } compact ? keys[compact[index].Index] : Scene.PropertyKeys.Of(this[index].Definition);

    /// <inheritdoc/>
    public DecodedProperty this[int index]
    {
        get
        {
            if (_full is not null)
            {
                return _full[index];
            }

            Compact item = _compact![index];
            return new DecodedProperty(
                item.Index, _flat![item.Index], PropertyValue.Unpack(item.Value), item.IndexPayloadBits, item.CoordShape, item.ElementShapes);
        }
    }

    /// <summary>The baseline with the update laid over it, by index, when both are one class's compact lists in index order.</summary>
    /// <param name="baseline">What the entity was.</param>
    /// <param name="update">What the snapshot said, which wins wherever it spoke.</param>
    /// <returns>The merged list, or null when either is not such a list and the general merge must run.</returns>
    /// <remarks>
    /// **A linear merge of the compact records**, which is what <c>BaselineMerge.Overlay</c>'s sorted dictionary computes
    /// for this case: no property is built and no definition compared, because both sides' definitions are the same
    /// list's. Indices must rise strictly on both sides — the wire's own order — or this declines.
    /// </remarks>
    internal static DecodedProperties? Merge(DecodedProperties baseline, DecodedProperties update)
    {
        if (baseline._compact is not { } from || update._compact is not { } over
            || !ReferenceEquals(baseline._flat, update._flat) || !Rising(from) || !Rising(over))
        {
            return null;
        }

        List<Compact> merged = new(from.Length + over.Length);
        int left = 0;
        int right = 0;

        while (left < from.Length || right < over.Length)
        {
            if (right == over.Length || (left < from.Length && from[left].Index < over[right].Index))
            {
                merged.Add(from[left++]);
            }
            else
            {
                if (left < from.Length && from[left].Index == over[right].Index)
                {
                    left++;
                }

                merged.Add(over[right++]);
            }
        }

        return new DecodedProperties(update._flat, update._keys ?? baseline._keys, [.. merged], null);
    }

    private static bool Rising(Compact[] items)
    {
        for (int index = 1; index < items.Length; index++)
        {
            if (items[index].Index <= items[index - 1].Index)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A struct enumerator, for a reader that holds this type rather than the interface.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<DecodedProperty> IEnumerable<DecodedProperty>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the list without allocating when held by its own type.</summary>
    internal struct Enumerator(DecodedProperties list) : IEnumerator<DecodedProperty>
    {
        private int _index = -1;

        /// <inheritdoc/>
        public readonly DecodedProperty Current => list[_index];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext() => ++_index < list.Count;

        /// <inheritdoc/>
        public void Reset() => _index = -1;

        /// <inheritdoc/>
        public readonly void Dispose()
        {
            // Nothing is held.
        }
    }
}
