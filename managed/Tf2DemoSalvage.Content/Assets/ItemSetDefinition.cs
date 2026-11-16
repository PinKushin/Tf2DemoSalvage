using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>`CEconItemSetDefinition` (econ_item_schema.cpp:407): what an item's description lists for its set.</summary>
/// <param name="LocalizedName">`name`, a localization token, or null.</param>
/// <param name="Items">`m_iItemDefs`, in the file's order.</param>
/// <param name="Attributes">`m_iAttributes`: each bonus attribute's definition index and value.</param>
/// <param name="IsHidden">`is_hidden_set`: shown only with the whole set equipped.</param>
public sealed record ItemSetDefinition(
    string? LocalizedName,
    IReadOnlyList<int> Items,
    IReadOnlyList<(int Attribute, float Value)> Attributes,
    bool IsHidden);
