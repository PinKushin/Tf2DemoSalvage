using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A particle definition's or operator's attributes, as sorted arrays read by binary search (finding 80).</summary>
/// <remarks>
/// **The same read contract as the dictionary it replaces**, ordinal like the DMX reader's: a name is found or it is
/// absent, and absence still means the caller's default (<see cref="ParticleFunction.Number"/>). One `Dictionary` per
/// operator was most of 239 MB per map on f12 (`Entry[]` 184 MB); two arrays hold the same pairs in a fraction of it.
/// Immutable, so a definition shared between maps cannot be written by one.
/// </remarks>
public sealed class ParticleParameterDictionary : IReadOnlyDictionary<string, DmxValue>
{
    private readonly string[] _names;
    private readonly DmxValue[] _values;

    private ParticleParameterDictionary(string[] names, DmxValue[] values)
    {
        _names = names;
        _values = values;
    }

    /// <summary>The compact form of a dictionary's pairs.</summary>
    /// <param name="attributes">The pairs.</param>
    /// <returns>The same pairs, sorted ordinally.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attributes"/> is null.</exception>
    public static ParticleParameterDictionary Of(IReadOnlyDictionary<string, DmxValue> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes is ParticleParameterDictionary already)
        {
            return already;
        }

        string[] names = new string[attributes.Count];
        DmxValue[] values = new DmxValue[attributes.Count];
        int at = 0;

        foreach ((string name, DmxValue value) in attributes)
        {
            names[at] = name;
            values[at] = value;
            at++;
        }

        Array.Sort(names, values, StringComparer.Ordinal);

        return new ParticleParameterDictionary(names, values);
    }

    /// <inheritdoc/>
    public int Count => _names.Length;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _names;

    /// <inheritdoc/>
    public IEnumerable<DmxValue> Values => _values;

    /// <inheritdoc/>
    public DmxValue this[string key] => TryGetValue(key, out DmxValue value) ? value : throw new KeyNotFoundException(key);

    /// <inheritdoc/>
    public bool ContainsKey(string key) => Find(key) >= 0;

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out DmxValue value)
    {
        int at = Find(key);
        value = at >= 0 ? _values[at] : default;
        return at >= 0;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, DmxValue>> GetEnumerator()
    {
        for (int at = 0; at < _names.Length; at++)
        {
            yield return new KeyValuePair<string, DmxValue>(_names[at], _values[at]);
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Array.BinarySearch(_names, key, StringComparer.Ordinal);
    }
}
