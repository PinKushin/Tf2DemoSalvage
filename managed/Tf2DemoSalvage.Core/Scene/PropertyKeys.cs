using System.Collections.Concurrent;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The key an entity's property is stored under — <c>Table.Prop</c>, or the element path — one string per key.</summary>
/// <remarks>
/// **One instance per distinct key** (D217 step 4, part 3): the key was built per write, and every entity kept its own
/// copy of every key it held, so strings were 254 MB of what rgl-pug's checkpoints kept of the entity table. Static, so
/// no copy of the state copies it; bounded by the schemas' distinct table and property names.
/// </remarks>
internal static class PropertyKeys
{
    // ponytail: process-wide and never emptied; bounded by distinct schema names, so it is a few thousand strings.
    private static readonly ConcurrentDictionary<(string Table, string Name), string> Flat = new();

    /// <summary>The key a decoded property is stored under.</summary>
    /// <param name="definition">The property's flattened definition.</param>
    /// <returns>The element path for an element-scoped property; otherwise <c>Table.Prop</c>, the same instance every time.</returns>
    public static string Of(in FlatProperty definition) =>
        definition.ElementScoped && definition.Path.Length > 0
            ? definition.Path
            : Flat.GetOrAdd((definition.OwnerTable, definition.Property.Name), static key => $"{key.Table}.{key.Name}");
}
