using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Tf2DemoSalvage.Scene;

/// <summary>ConVar values by name, compared by their entries so settings holding the same ones are equal.</summary>
public sealed class ConVarValues : ReadOnlyDictionary<string, string>, IEquatable<ConVarValues>
{
    /// <summary>No values set.</summary>
    public static ConVarValues None { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Wraps a dictionary the caller no longer changes.</summary>
    /// <param name="values">The values, keyed case-insensitively.</param>
    public ConVarValues(IDictionary<string, string> values)
        : base(values)
    {
    }

    /// <inheritdoc/>
    public bool Equals(ConVarValues? other)
    {
        if (other is null || other.Count != Count)
        {
            return false;
        }

        foreach ((string name, string value) in this)
        {
            if (!other.TryGetValue(name, out string? theirs) || !string.Equals(value, theirs, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ConVarValues);

    /// <inheritdoc/>
    public override int GetHashCode() => Count;
}
