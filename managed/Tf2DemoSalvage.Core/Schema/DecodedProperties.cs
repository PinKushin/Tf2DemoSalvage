using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>A decoded property list nothing writes once it is made — what the decoder reads and merges into.</summary>
/// <remarks>
/// **Its own type so a checkpoint can share it** (D217 step 4): the decoder's class baselines, its per-entity baseline
/// slots and every entity it hands out hold these lists, and a resume copied each one element by element — 161 MB of a
/// rgl-pug resume was the decoder. The list it wraps is built by its maker and handed over here, so no one else holds
/// a reference that could write it; <c>StateCopy.Immutable</c> names this type.
/// </remarks>
internal sealed class DecodedProperties(List<DecodedProperty> items) : IReadOnlyList<DecodedProperty>
{
    private readonly List<DecodedProperty> _items = items;

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public DecodedProperty this[int index] => _items[index];

    /// <inheritdoc/>
    public IEnumerator<DecodedProperty> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
