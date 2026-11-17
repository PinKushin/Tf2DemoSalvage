using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One `KeyValues` node of a paint kit recipe, as `CreatePaintKitDefinitionKV` and `OperationToKV` build it
/// (econ_paintkit.cpp:151-409) for `NewTextureCompositor`: a name, a string value or children, in insertion order.
/// </summary>
/// <param name="name">The key, as `KeyValues( name )` takes it.</param>
public sealed class PaintKitNode(string name)
{
    /// <summary>The key.</summary>
    public string Name { get; } = name;

    /// <summary>`SetStringValue`'s value, or null for a node with children.</summary>
    public string? Value { get; set; }

    private readonly List<PaintKitNode> _children = [];

    /// <summary>The subkeys in the order `AddSubKey` appended them; a name may repeat.</summary>
    public IReadOnlyList<PaintKitNode> Children => _children;

    /// <summary>`AddSubKey( child )`: appended, whatever its name.</summary>
    /// <param name="child">The subkey.</param>
    public void Add(PaintKitNode child) => _children.Add(child);

    /// <summary>`FindKey( name )`: the first subkey of that name, case-insensitively, or null.</summary>
    /// <param name="key">The name.</param>
    /// <returns>The subkey, or null.</returns>
    public PaintKitNode? Find(string key) => _children.Find(child => string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>`SetString( name, value )`: the first subkey of that name takes the value, or a new one is appended.</summary>
    /// <param name="key">The name.</param>
    /// <param name="value">The value.</param>
    public void SetString(string key, string value)
    {
        if (Find(key) is { } existing)
        {
            existing.Value = value;
            return;
        }

        _children.Add(new PaintKitNode(key) { Value = value });
    }
}
