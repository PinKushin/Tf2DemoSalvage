using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The key an entity's property is stored under — <c>Table.Prop</c>, or the element path. The one rule for it.</summary>
/// <remarks>
/// **Built once per class, not once per write** (D217 step 4, part 3): every entity kept its own copy of every key it held,
/// so strings were 254 MB of what rgl-pug's checkpoints kept of the entity table. The decoder builds each class's keys
/// with this and keeps them (<c>EntityDecoder.KeysFor</c>), scoped to itself, so one demo's keys go with its decoder.
/// </remarks>
internal static class PropertyKeys
{
    /// <summary>The key a decoded property is stored under.</summary>
    /// <param name="definition">The property's flattened definition.</param>
    /// <returns>The element path for an element-scoped property; otherwise <c>Table.Prop</c>.</returns>
    public static string Of(in FlatProperty definition) =>
        definition.ElementScoped && definition.Path.Length > 0
            ? definition.Path
            : $"{definition.OwnerTable}.{definition.Property.Name}";
}
