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
/// </remarks>
internal sealed class DecodedProperties : IReadOnlyList<DecodedProperty>
{
    private readonly DecodedProperty[] _items;

    /// <summary>A list holding a private copy of <paramref name="items"/>.</summary>
    /// <param name="items">The properties; later writes to the caller's storage do not reach this list.</param>
    public DecodedProperties(ReadOnlySpan<DecodedProperty> items) => _items = items.ToArray();

    /// <inheritdoc/>
    public int Count => _items.Length;

    /// <inheritdoc/>
    public DecodedProperty this[int index] => _items[index];

    /// <inheritdoc/>
    public IEnumerator<DecodedProperty> GetEnumerator() => ((IEnumerable<DecodedProperty>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
