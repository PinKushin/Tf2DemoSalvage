using System;
using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>A decoded property list nothing can write once it is made — what the decoder reads and merges into.</summary>
/// <remarks>
/// **Its own type so a checkpoint can share it** (D217 step 4): the decoder's class baselines, its per-entity baseline
/// slots and every entity it hands out hold these lists, and a resume copied each one element by element — 161 MB of a
/// rgl-pug resume was the decoder. <c>StateCopy.Immutable</c> names this type, so immutability is enforced here rather
/// than promised by callers: the constructor COPIES what it is given into an array no one else can reach.
///
/// **Stored without the definitions when it can be** (D217 step 4, part 3): every property a class decodes carries
/// <c>flat[Index]</c> as its definition, by value — some 130 of a property's 210 bytes — and the per-entity baseline
/// slots alone kept 812 MB of rgl-pug's checkpoints. Given the class's flattened list, a property whose definition IS
/// <c>flat[Index]</c> is kept as the rest, and its definition is read back from the list. One whose definition is
/// anything else keeps the list whole, so what is read back is always exactly what was put in.
/// </remarks>
internal sealed class DecodedProperties : IReadOnlyList<DecodedProperty>
{
    /// <summary>A property without its definition, which is <c>_flat[Index]</c>.</summary>
    private readonly record struct Compact(
        int Index, PropertyValue Value, int IndexPayloadBits, int CoordShape, IReadOnlyList<int>? ElementShapes);

    private readonly IReadOnlyList<FlatProperty>? _flat;

    private readonly Compact[]? _compact;

    private readonly DecodedProperty[]? _full;

    /// <summary>A list holding a private copy of <paramref name="items"/>, definitions included.</summary>
    /// <param name="items">The properties; later writes to the caller's storage do not reach this list.</param>
    public DecodedProperties(ReadOnlySpan<DecodedProperty> items) => _full = items.ToArray();

    /// <summary>A list of one class's properties, stored without their definitions when each is the class's own.</summary>
    /// <param name="flat">The class's flattened properties, which the schema owns and nothing writes.</param>
    /// <param name="items">The properties; copied, as by the other constructor.</param>
    public DecodedProperties(IReadOnlyList<FlatProperty> flat, ReadOnlySpan<DecodedProperty> items)
    {
        ArgumentNullException.ThrowIfNull(flat);

        Compact[] compact = new Compact[items.Length];

        for (int index = 0; index < items.Length; index++)
        {
            DecodedProperty item = items[index];

            if (item.Index < 0 || item.Index >= flat.Count || !item.Definition.Equals(flat[item.Index]))
            {
                // Not the class's own definition: keep everything, so a read gives back exactly this.
                _full = items.ToArray();
                return;
            }

            compact[index] = new Compact(item.Index, item.Value, item.IndexPayloadBits, item.CoordShape, item.ElementShapes);
        }

        _flat = flat;
        _compact = compact;
    }

    /// <summary>The class's flattened list this is stored against, or null when it keeps its definitions.</summary>
    public IReadOnlyList<FlatProperty>? Flat => _flat;

    /// <inheritdoc/>
    public int Count => _compact?.Length ?? _full!.Length;

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
            return new DecodedProperty(item.Index, _flat![item.Index], item.Value, item.IndexPayloadBits, item.CoordShape, item.ElementShapes);
        }
    }

    /// <inheritdoc/>
    public IEnumerator<DecodedProperty> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
