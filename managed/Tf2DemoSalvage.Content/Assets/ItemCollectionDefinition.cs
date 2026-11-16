using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>`CEconItemCollectionDefinition` (econ_item_schema.h:321-344): an `item_collections` entry.</summary>
/// <param name="Name">`m_strName`, the entry's key.</param>
/// <param name="LocalizedName">`m_pszLocalizedName`, its `name` token.</param>
/// <param name="LocalizedDescription">`m_pszLocalizedDesc`, its `description` token.</param>
/// <param name="Items">`m_iItemDefs`, highest rarity first.</param>
public sealed record ItemCollectionDefinition(string Name, string? LocalizedName, string? LocalizedDescription, IReadOnlyList<int> Items);
