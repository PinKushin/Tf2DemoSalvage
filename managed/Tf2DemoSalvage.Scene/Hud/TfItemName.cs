using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `GenerateLocalizedFullItemName` (econ_item_description.cpp:609) — what `CEconItemView::GetItemName` shows for an item the
/// client describes itself, so with the hash context off and proper names used.
/// </summary>
/// <remarks>
/// **Only the path an item with no attributes takes.** A demo carries a weapon's definition and quality, not its attribute
/// list, so a name tag, killstreak, festive, australium, paint kit, strange rank, duck badge, craft number, crate series and
/// tool target — every one of them an attribute — are left empty, as they are for a stock item.
/// </remarks>
public static class TfItemName
{
    private const int NameChars = 128; // MAX_ITEM_NAME_LENGTH
    private const int Normal = 0;
    private const int Unique = 6;
    private const int PaintKitWeapon = 15;

    // `g_szQualityLocalizationStrings` (econ_item_constants.cpp:120), `#` already stripped as `Find` strips it.
    private static readonly string[] QualityTokens =
    [
        "Normal", "rarity1", "rarity2", "vintage", "rarity3", "rarity4", "unique", "community", "developer", "selfmade",
        "customized", "strange", "completed", "haunted", "collectors", "paintkitWeapon",
        "Rarity_Default", "Rarity_Common", "Rarity_Uncommon", "Rarity_Rare", "Rarity_Mythical", "Rarity_Legendary", "Rarity_Ancient",
    ];

    /// <summary>The item's full localised name.</summary>
    /// <param name="schema">The item schema.</param>
    /// <param name="definition">`m_iItemDefinitionIndex`.</param>
    /// <param name="quality">`m_iEntityQuality`.</param>
    /// <param name="find">`Find`, handed a token without its `#`.</param>
    public static string Generate(ItemSchema schema, int definition, int quality, Func<string, string?> find)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(find);

        if (schema.ItemBaseName(definition) is not { } baseName)
        {
            return string.Empty;
        }

        // `GetLocalizedBaseItemName`: the token's string, or the token raw when it has none.
        string name = Find(find, baseName) is { Length: > 0 } localized ? localized : baseName;
        string qualityText = string.Empty;
        string? qualityToken = quality >= 0 && quality < QualityTokens.Length ? QualityTokens[quality] : null;

        if (quality == Unique)
        {
            // Unique items use proper names; a language without the article falls back to nothing.
            qualityText = schema.HasProperName(definition) ? find("TF_Unique_Prepend_Proper") ?? string.Empty : string.Empty;
        }
        else if (quality > 0 && qualityToken is not null && quality != PaintKitWeapon && find(qualityToken) is { } localizedQuality)
        {
            qualityText = localizedQuality + (find("Rarity_Spacer") ?? string.Empty);
        }

        // `&& unQuality != AE_SELFMADE` only matters with `bIgnoreQuality`, a paint kit's.
        string qualityFormat = quality is Normal or Unique or PaintKitWeapon
            ?"ItemNameNormalOrUniqueQualityFormat"
            : "ItemNameQualityFormat";

        // "Strange Unusual Festive Killstreak Australium ducks": every one after the quality is an attribute.
        qualityText = VguiLocalize.ConstructString(find(qualityFormat), NameChars, qualityText, string.Empty, string.Empty, string.Empty, string.Empty);

        return find("ItemNameFormat") is { } format
            ? VguiLocalize.ConstructString(format, NameChars, qualityText, name, string.Empty, string.Empty, string.Empty, string.Empty)
            : "Unknown Item";
    }

    private static string? Find(Func<string, string?> find, string token)
    {
        if (token.Length == 0)
        {
            return null;
        }

        return find(token.StartsWith('#') ? token[1..] : token);
    }
}
