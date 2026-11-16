using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>One `econ_item_description_line_t` (econ_item_description.h:57): its text, `attrib_colors_t` and meta flags.</summary>
/// <param name="Text">`sText`.</param>
/// <param name="Color">`eColor`.</param>
/// <param name="MetaType">`unMetaType`, the `kDescLineFlag_*` bits.</param>
public readonly record struct TfDescriptionLine(string Text, int Color, int MetaType);

/// <summary>What `CEconItemDescription` reads beyond the item: the schema, the localizer, persona names and the clock.</summary>
public sealed class TfItemDescriptionContext
{
    /// <summary>The item schema.</summary>
    public required ItemSchema Schema { get; init; }

    /// <summary>`CLocalizationProvider::Find`, handed a token without its `#`.</summary>
    public required Func<string, string?> Find { get; init; }

    /// <summary>
    /// `InventoryManager()->PersonaName_Get( account )` (econ_item_description.cpp:166): Steam's name for an account, or
    /// null when Steam does not know it — which the engine prints as "[unknown]".
    /// </summary>
    public Func<uint, string?> PersonaName { get; init; } = _ => null;

    /// <summary>`CRTime::RTime32TimeCur()`: the watcher's clock, which the holiday and trade-date lines read.</summary>
    public DateTime Now { get; init; } = TfHolidays.WatcherClock;

    /// <summary>`TFGameRules()->IsMannVsMachineMode()`, which keeps an MvM upgrade's line in its own color.</summary>
    public bool IsMannVsMachine { get; init; }
}

/// <summary>
/// `CEconItemDescription::GenerateDescriptionLines` (econ_item_description.cpp:347) and `GenerateLocalizedFullItemName`
/// (:610), for an item as a demo has it: no backpack (`GetSOCData` is null, econ_item_view.cpp:839), so `GetQuality()` is
/// `AE_NORMAL` and the origin, flags, custom name and description are absent (B751).
/// </summary>
/// <remarks>
/// <para>
/// **What a demo item can never produce, and why it is not built**: a bundle's contents, dueling medals, map contributions,
/// map stamps, the friendly hat, MvM challenges, squad surplus, dynamic recipes, Xifier and Unusualifier targets, loot
/// lists and case bonus content are each an item class or tool the engine checks for first, or GC data (`CTFDuelSummary`,
/// `CTFMapContribution`) a demo watcher never has; the drop period reads a string attribute, which the networked list does
/// not carry; the market line is deprecated (:3136); the DirectX 8 warning needs `mat_dxlevel` below 90.
/// </para>
/// <para>
/// **Not built (B754):** anything that needs a paint kit's own definition or an item collection — a war paint's name and
/// wear, its rarity through its War Paint, and the collection lines. The kit's index is read (its presence changes the
/// name's quality), its definition is not.
/// </para>
/// </remarks>
public static class TfItemDescription
{
    /// <summary>`kDescLineFlag_Name`.</summary>
    public const int FlagName = 0x001;

    /// <summary>`kDescLineFlag_Type`.</summary>
    public const int FlagType = 0x002;

    /// <summary>`kDescLineFlag_Desc`.</summary>
    public const int FlagDesc = 0x004;

    /// <summary>`kDescLineFlag_Attribute`.</summary>
    public const int FlagAttribute = 0x008;

    /// <summary>`kDescLineFlag_Misc`.</summary>
    public const int FlagMisc = 0x010;

    /// <summary>`kDescLineFlag_Empty`.</summary>
    public const int FlagEmpty = 0x020;

    /// <summary>`kDescLineFlag_Set`.</summary>
    public const int FlagSet = 0x040;

    /// <summary>`kDescLineFlag_SetName`.</summary>
    public const int FlagSetName = 0x100;

    /// <summary>`kDescLineFlag_Collection`.</summary>
    public const int FlagCollection = 0x200;

    /// <summary>`kDescLineFlag_CollectionCurrentItem`.</summary>
    public const int FlagCollectionCurrentItem = 0x400;

    /// <summary>`kDescLineFlag_CollectionName`.</summary>
    public const int FlagCollectionName = 0x800;

    /// <summary>`kDescLineFlag_CaseBonusContent`.</summary>
    public const int FlagCaseBonusContent = 0x1000;

    /// <summary>`kDescLineFlag_MouseOverPanel`.</summary>
    public const int FlagMouseOverPanel = 0x2000;

    /// <summary>`ATTRIB_COL_LEVEL` (econ_item_constants.h:551).</summary>
    public const int ColorLevel = 0;

    /// <summary>`ATTRIB_COL_NEUTRAL`.</summary>
    public const int ColorNeutral = 1;

    /// <summary>`ATTRIB_COL_POSITIVE`.</summary>
    public const int ColorPositive = 2;

    /// <summary>`ATTRIB_COL_NEGATIVE`.</summary>
    public const int ColorNegative = 3;

    /// <summary>`ATTRIB_COL_ITEMSET_NAME`.</summary>
    public const int ColorItemSetName = 4;

    /// <summary>`ATTRIB_COL_ITEMSET_EQUIPPED`.</summary>
    public const int ColorItemSetEquipped = 5;

    /// <summary>`ATTRIB_COL_ITEMSET_MISSING`.</summary>
    public const int ColorItemSetMissing = 6;

    /// <summary>`ATTRIB_COL_LIMITED_QUANTITY`.</summary>
    public const int ColorLimitedQuantity = 10;

    /// <summary>`ATTRIB_COL_STRANGE`.</summary>
    public const int ColorStrange = 20;

    /// <summary>`ATTRIB_COL_UNUSUAL`.</summary>
    public const int ColorUnusual = 21;

    /// <summary>`NUM_ATTRIB_COLORS`: "do not override".</summary>
    private const int ColorNone = 22;

    /// <summary>`AE_NORMAL` (econ_item_constants.h).</summary>
    public const int QualityNormal = 0;

    /// <summary>`AE_UNUSUAL`.</summary>
    public const int QualityUnusual = 5;

    /// <summary>`AE_UNIQUE`.</summary>
    public const int QualityUnique = 6;

    /// <summary>`AE_COMMUNITY`.</summary>
    public const int QualityCommunity = 7;

    /// <summary>`AE_SELFMADE`.</summary>
    public const int QualitySelfMade = 9;

    /// <summary>`AE_STRANGE`.</summary>
    public const int QualityStrange = 11;

    /// <summary>`AE_PAINTKITWEAPON`.</summary>
    public const int QualityPaintKitWeapon = 15;

    /// <summary>`MAX_ITEM_NAME_LENGTH`.</summary>
    private const int NameChars = 128;

    /// <summary>`CConstructLocalizedString`'s buffer: 512 characters (ilocalize.h:488).</summary>
    private const int ConstructChars = 512;

    /// <summary>`g_szQualityLocalizationStrings` (econ_item_constants.cpp:120), `#` stripped.</summary>
    private static readonly string[] QualityTokens =
    [
        "Normal", "rarity1", "rarity2", "vintage", "rarity3", "rarity4", "unique", "community", "developer", "selfmade",
        "customized", "strange", "completed", "haunted", "collectors", "paintkitWeapon",
        "Rarity_Default", "Rarity_Common", "Rarity_Uncommon", "Rarity_Rare", "Rarity_Mythical", "Rarity_Legendary", "Rarity_Ancient",
    ];

    /// <summary>`g_szQualityColorStrings` (econ_item_constants.cpp:79): `EconQuality_GetColorString`.</summary>
    private static readonly string[] QualityColors =
    [
        "QualityColorNormal", "QualityColorrarity1", "QualityColorrarity2", "QualityColorVintage", "QualityColorrarity3",
        "QualityColorrarity4", "QualityColorUnique", "QualityColorCommunity", "QualityColorDeveloper", "QualityColorSelfMade",
        "QualityColorSelfMadeCustomized", "QualityColorStrange", "QualityColorCompleted", "QualityColorHaunted",
        "QualityColorCollectors", "QualityColorPaintkitWeapon", "ItemRarityDefault", "ItemRarityCommon", "ItemRarityUncommon",
        "ItemRarityRare", "ItemRarityMythical", "ItemRarityLegendary", "ItemRarityAncient",
    ];

    /// <summary>`g_KillEaterAttr` (econ_item_constants.cpp:488-495): score, score type, restriction type, restriction value.</summary>
    private static readonly (string Score, string Type, string Restriction, string RestrictionValue)[] KillEaterAttributes =
    [
        ("kill eater", "kill eater score type", "strange restriction type 1", "strange restriction value 1"),
        ("kill eater 2", "kill eater score type 2", "strange restriction type 2", "strange restriction value 2"),
        ("kill eater 3", "kill eater score type 3", "strange restriction type 3", "strange restriction value 3"),
        ("kill eater user 1", "kill eater user score type 1", "strange restriction user type 1", "strange restriction user value 1"),
        ("kill eater user 2", "kill eater user score type 2", "strange restriction user type 2", "strange restriction user value 2"),
        ("kill eater user 3", "kill eater user score type 3", "strange restriction user type 3", "strange restriction user value 3"),
    ];

    /// <summary>`FindAttribute( GetKillEaterAttr_Score( i ) )` for any `i &lt; GetKillEaterAttrCount()`: whether it counts anything.</summary>
    /// <param name="schema">The schema naming the attributes.</param>
    /// <param name="item">The item.</param>
    /// <returns>Whether any kill-eater score is present.</returns>
    internal static bool HasKillEaterScore(ItemSchema schema, TfItemView item) =>
        Array.Exists(KillEaterAttributes, set => item.Attribute(schema, set.Score) is not null);

    /// <summary>`kStrangeEventRestriction_VictimSteamAccount` (econ_item_constants.h:739).</summary>
    private const uint RestrictionVictimAccount = 1;

    /// <summary>`kStrangeEventRestriction_Map`.</summary>
    private const uint RestrictionMap = 2;

    /// <summary>`kStrangeEventRestriction_Competitive`.</summary>
    private const uint RestrictionCompetitive = 3;

    /// <summary>`EconQuality_GetColorString( quality )` (econ_item_constants.cpp:109), or null outside the table.</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>The scheme color name, or null.</returns>
    public static string? QualityColor(int quality) => quality >= 0 && quality < QualityColors.Length ? QualityColors[quality] : null;

    /// <summary>`GetQuality()` for an item with no backpack (econ_item_view.cpp:446): `AE_NORMAL`, whatever was networked.</summary>
    /// <param name="item">The item.</param>
    /// <returns>`AE_NORMAL`.</returns>
    /// <remarks>
    /// The store flag is the store's; the origin override is set only for a store or preview item (or `kEconItemOrigin_Invalid`,
    /// as the freeze panel's powerup item sets it, :438) — so in a demo `GetOrigin() != kEconItemOrigin_Invalid` is false.
    /// </remarks>
    public static int Quality(TfItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return QualityNormal;
    }

    /// <summary>`GetDescription()`'s lines: `GenerateDescriptionLines` (econ_item_description.cpp:347), in its order.</summary>
    /// <param name="item">The item.</param>
    /// <param name="context">The schema, localizer, persona names and clock.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<TfDescriptionLine> Generate(TfItemView item, TfItemDescriptionContext context) =>
        Generate(item, context, out _);

    /// <summary>As <see cref="Generate(TfItemView, TfItemDescriptionContext)"/>, with `HasUnknownPlayer()`.</summary>
    /// <param name="item">The item.</param>
    /// <param name="context">The schema, localizer and clock.</param>
    /// <param name="hasUnknownPlayer">Whether a name the lines needed was "[unknown]" (econ_item_description.h:199).</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<TfDescriptionLine> Generate(TfItemView item, TfItemDescriptionContext context, out bool hasUnknownPlayer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        Builder builder = new(item, context);
        builder.Run();
        hasUnknownPlayer = builder.HasUnknownPlayer;
        return builder.Lines;
    }

    /// <summary>`GenerateLocalizedFullItemName( …, k_EGenerateLocalizedFullItemName_Default, bHashContextOff )` (:610).</summary>
    /// <param name="item">The item.</param>
    /// <param name="context">The schema and localizer.</param>
    /// <param name="useProperName">`bHashContextOff`: whether a unique item takes its article; true on the client (:1067).</param>
    /// <returns>The name.</returns>
    public static string FullItemName(TfItemView item, TfItemDescriptionContext context, bool useProperName = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        return new Builder(item, context).FullName(Quality(item), useProperName);
    }

    /// <summary>The walk, holding the item and the lines so far, as `CEconItemDescription` holds `m_vecDescLines`.</summary>
    private sealed class Builder(TfItemView item, TfItemDescriptionContext context)
    {
        public List<TfDescriptionLine> Lines { get; } = [];

        private ItemSchema Schema => context.Schema;

        public void Run()
        {
            ItemName();
            ItemRarityDesc();
            ItemLevelDesc();
            CraftTag();
            StyleDesc();
            Painted();
            HolidayRestriction();
            SaxxyAwardDesc();
            VisibleAttributes();
            QualityDesc();
            ItemDesc();
            GiftedBy();
            EventDetail();
            ItemSetDesc();
            ExpirationDesc();
            FlagsAttributes();
        }

        /// <summary>`pLocalizationProvider->Find`: the `#` stripped, null for null.</summary>
        private string? Find(string? token)
        {
            if (token is null)
            {
                return null;
            }

            return context.Find(token.StartsWith('#') ? token[1..] : token);
        }

        /// <summary>`FindSafe`: "" where `Find` has nothing.</summary>
        private string FindSafe(string token) => Find(token) ?? string.Empty;

        /// <summary>`CConstructLocalizedString( format, … )`: "" for a null format.</summary>
        private static string Construct(string? format, params string?[] arguments) =>
            VguiLocalize.ConstructString(format, ConstructChars, arguments);

        private void AddDescLine(string text, int color, int metaType) => Lines.Add(new TfDescriptionLine(text, color, metaType));

        /// <summary>`AddEmptyDescLine` (:3734): a single space, neutral, `kDescLineFlag_Empty`.</summary>
        private void AddEmptyDescLine() => AddDescLine(" ", ColorNeutral, FlagEmpty);

        /// <summary>`LocalizedAddDescLine` (:3742): the token's string, or the token itself unless it starts with `#`.</summary>
        private void LocalizedAddDescLine(string token, int color, int metaType)
        {
            if (Find(token) is { } text)
            {
                AddDescLine(text, color, metaType);
            }
            else if (!token.StartsWith('#'))
            {
                AddDescLine(token, color, metaType);
            }
        }

        /// <summary>`FindAttribute( def, &amp;value )`: the attribute's 32 bits by schema name, or null.</summary>
        private EconAttributeValue? Attribute(string name) => item.Attribute(Schema, name);

        private bool Has(string name) => Attribute(name) is not null;

        /// <summary>`GetPaintKitDefIndex( item )` (econ_item_interface.h:502).</summary>
        private bool IsPaintKitItem => Has("paintkit_proto_def_index");

        /// <summary>A definition string attribute, present only when the walk reads the definition (<see cref="TfItemView.IteratesDefinition"/>).</summary>
        private string? StringAttribute(string name) =>
            item.IteratesDefinition ? Schema.DefinitionStringAttribute(item.DefinitionIndex, name) : null;

        /// <summary>`GetMarketQuality()` (econ_item_interface.cpp:402).</summary>
        private int MarketQuality()
        {
            int quality = Quality(item);

            if (!IsPaintKitItem)
            {
                return quality;
            }

            if (quality == QualitySelfMade)
            {
                return QualitySelfMade;
            }

            if (Has("attach particle effect") || Has("hat only unusual effect") || Has("taunt only unusual effect") || Has("taunt attach particle index"))
            {
                return QualityUnusual;
            }

            return Array.Exists(KillEaterAttributes, set => Has(set.Score)) ? QualityStrange : QualityPaintKitWeapon;
        }

        /// <summary>`Generate_ItemName` (:1037): no custom name without a backpack.</summary>
        private void ItemName() => AddDescLine(FullName(MarketQuality(), useProperName: true), ColorLevel, FlagName);

        /// <summary>`GetLocalizedBaseItemName` (:587): the token's string, or the token raw; null with no `item_name`.</summary>
        private string? BaseName(int definition)
        {
            if (Schema.ItemBaseName(definition) is not { } token)
            {
                return null;
            }

            return Find(token) is { Length: > 0 } localized ? localized : token;
        }

        /// <summary>`GenerateLocalizedFullItemName` (:610), its quality already `GetMarketQuality()`.</summary>
        public string FullName(int quality, bool useProperName)
        {
            // "no definition, no name" (:628).
            if (!Schema.HasDefinition(item.DefinitionIndex))
            {
                return string.Empty;
            }

            bool ignoreQuality = IsPaintKitItem && quality != QualityStrange && quality != QualitySelfMade;

            // "item name text override", a string attribute (:659): its string if it has one; if not, the name is left
            // unset, which here reads as the base name — the engine's buffer is uninitialised there.
            string name = StringAttribute("item name text override") is { } overrideToken && Find(overrideToken) is { } overridden
                ? overridden
                : BaseName(item.DefinitionIndex) ?? "Unknown Item";

            string killStreak = string.Empty;

            if (Attribute("killstreak tier") is { } tier && !ignoreQuality)
            {
                uint value = (uint)tier.Value;

                if (Has("killstreak effect"))
                {
                    value = 3;
                }
                else if (Has("killstreak idleeffect"))
                {
                    value = 2;
                }

                string? streak = Find(value switch
                {
                    1 => "ItemNameKillStreakv0",
                    2 => "ItemNameKillStreakv1",
                    _ => "ItemNameKillStreakv2",
                });

                if (streak is not null)
                {
                    killStreak = streak;
                    useProperName = false;
                }
            }

            bool festivized = Has("is_festivized");

            if (festivized)
            {
                useProperName = false;
            }

            string? qualityOverride = StringAttribute("quality text override");
            string qualityText = string.Empty;

            if (quality == QualitySelfMade || !ignoreQuality)
            {
                string? qualityToken = qualityOverride ?? (quality >= 0 && quality < QualityTokens.Length ? QualityTokens[quality] : null);

                if (quality > 0 && qualityToken is not null && quality != QualityPaintKitWeapon)
                {
                    if (quality == QualityUnique && qualityOverride is null)
                    {
                        qualityText = useProperName && Schema.HasProperName(item.DefinitionIndex) ? Find("TF_Unique_Prepend_Proper") ?? string.Empty : string.Empty;
                    }
                    else if (Find(qualityToken) is { } localizedQuality)
                    {
                        qualityText = localizedQuality + FindSafe("#Rarity_Spacer");
                    }
                }

                if (!Has("hide_strange_prefix") && StrangeRank() is { } rank)
                {
                    qualityText = quality == QualityStrange
                        ? Construct("%s1%s2%s3", rank.Rank, rank.Secondary ?? string.Empty, FindSafe("#Strange_Spacer"))
                        : Construct("%s1%s2 %s3", rank.Rank, rank.Secondary ?? string.Empty, qualityText);
                }
            }

            string australium = Has("is australium item") ? Find("ItemNameAustralium") ?? string.Empty : string.Empty;
            string festive = festivized ? Find("ItemNameFestive") ?? string.Empty : string.Empty;

            string qualityFormat = qualityOverride is null && (quality is QualityNormal or QualityUnique or QualityPaintKitWeapon || ignoreQuality) && quality != QualitySelfMade
                ? "ItemNameNormalOrUniqueQualityFormat"
                : "ItemNameQualityFormat";

            string duckBadge = string.Empty;

            if (Attribute("duck rating") is { } duck && unchecked((uint)duck.RawBits) is var duckLevel && duckLevel != 0
                && Schema.ItemLevelRankName("Journal_DuckBadge", duckLevel) is { } badge)
            {
                duckBadge = (Find(badge) ?? string.Empty) + " ";
            }

            qualityText = Construct(Find(qualityFormat), qualityText, festive, killStreak, australium, duckBadge);

            string crateSeries = CrateSeries();
            string craftIndex = string.Empty;

            if (Find("ItemNameCraftNumberFormat") is { } craftFormat && Attribute("unique craft index") is { } craft
                && unchecked((uint)craft.RawBits) is var craftNumber && craftNumber is > 0 and <= 100)
            {
                craftIndex = VguiLocalize.ConstructString(craftFormat, 128, craftNumber.ToString(CultureInfo.InvariantCulture));
            }

            string toolTarget = ToolTargetName();

            // "PaintKit and Wear" (:992-1011): the kit's definition is not read (B754), so `pPaintKitStr` is null and the
            // name is left as it is — which is what `Econ_ConcatPaintKitName` does with a null kit string.
            return Find("ItemNameFormat") is { } nameFormat
                ? VguiLocalize.ConstructString(nameFormat, NameChars, qualityText, name, craftIndex, crateSeries, toolTarget, string.Empty)
                : "Unknown Item";
        }

        /// <summary>The crate and release series (:872-906).</summary>
        private string CrateSeries()
        {
            string series = string.Empty;
            bool hideSeries = Attribute("hide crate series number") is { } hide && hide.Value != 0f;

            if (!hideSeries && string.Equals(Schema.ItemClass(item.DefinitionIndex), "supply_crate", StringComparison.OrdinalIgnoreCase)
                && Schema.CollectionReference(item.DefinitionIndex) is null
                && Attribute("set supply crate series") is { } crate && crate.Value != 0f)
            {
                series = Construct(Find("ItemNameCraftSeries"), ((uint)crate.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (Attribute("series number") is { } release && release.Value != 0f)
            {
                series = Construct(Find("ItemNameCraftSeries"), ((uint)release.Value).ToString(CultureInfo.InvariantCulture));
            }

            return series;
        }

        /// <summary>A tool's target (:938-988): "tool target item" on an item of class `tool`.</summary>
        /// <remarks>*Not built:* the dynamic recipe's single output (:963), whose component attributes are strings.</remarks>
        private string ToolTargetName()
        {
            if (!string.Equals(Schema.ItemClass(item.DefinitionIndex), "tool", StringComparison.OrdinalIgnoreCase)
                || Attribute("tool target item") is not { } target)
            {
                return string.Empty;
            }

            string targetName = BaseName((int)target.Value) ?? "Unknown Item";
            return Construct(Find("ItemNameToolTargetNameFormat"), targetName);
        }

        /// <summary>`CStrangeRankLocalizationGenerator` (:470): the rank and its secondary, or null when not valid.</summary>
        private (string? Rank, string? Secondary, uint Type, uint Score, int Slot)? StrangeRank()
        {
            int slot = 0;

            if (Attribute("strange score selector") is { } selector)
            {
                slot = (int)Math.Min(unchecked((uint)selector.RawBits), (uint)KillEaterAttributes.Length);
            }

            // `GetKillEaterAttr_Score( count )` is out of the table: a NULL definition, which nothing has.
            if (slot >= KillEaterAttributes.Length || Attribute(KillEaterAttributes[slot].Score) is not { } score)
            {
                return null;
            }

            uint scoreValue = unchecked((uint)score.RawBits);
            uint type = ScoreType(slot);
            string block = Schema.KillEaterLevelingDataName(type) ?? "KillEaterRank";

            if (Schema.ItemLevelRankName(block, scoreValue) is not { } rankToken)
            {
                return null;
            }

            string? secondary = null;

            if (Attribute(KillEaterAttributes[slot].Restriction) is { } restriction
                && Attribute(KillEaterAttributes[slot].RestrictionValue) is { } restrictionValue)
            {
                uint restrictionType = unchecked((uint)restriction.RawBits);

                if (restrictionType == RestrictionMap)
                {
                    if (Schema.MasterMap(restrictionValue.RawBits) is { StrangePrefix: { } prefix })
                    {
                        secondary = Find(prefix);
                    }
                }
                else if (restrictionType == RestrictionCompetitive)
                {
                    secondary = Find("TF_StrangeFilter_Prefix_Competitive");
                }
            }

            return (Find(rankToken), secondary, type, scoreValue, slot);
        }

        /// <summary>`GetScoreTypeForKillEaterAttr` (:416): the type attribute's bits read as a float and truncated, else 0.</summary>
        private uint ScoreType(int slot) => Attribute(KillEaterAttributes[slot].Type) is { } type ? (uint)type.Value : 0u;

        /// <summary>`GetLocalizedStringForKillEaterTypeAttr` (:1076).</summary>
        private string KillEaterTypeText(uint type) => Schema.KillEaterTypeName(type) is { } token ? Find(token) ?? string.Empty : string.Empty;

        /// <summary>`GetLocalizedStringForStrangeRestrictionAttr` (:1106), wrapped by `CStrangeRestrictionAttrWrapper` (:1088).</summary>
        private string RestrictionText(int slot)
        {
            string? text = null;

            if (Attribute(KillEaterAttributes[slot].Restriction) is { } restriction
                && Attribute(KillEaterAttributes[slot].RestrictionValue) is { } value
                && unchecked((uint)restriction.RawBits) is var type && type != 0)
            {
                // The map case falls through to the competitive one when the map is unknown (:1120-1129).
                text = type switch
                {
                    RestrictionMap => Schema.MasterMap(value.RawBits) is { } map ? Find(map.Name) : Find("ItemTypeDescStrangeFilterCompetitive"),
                    RestrictionCompetitive => Find("ItemTypeDescStrangeFilterCompetitive"),
                    RestrictionVictimAccount => PersonaName(unchecked((uint)value.RawBits)),
                    _ => null,
                };
            }

            return text is null ? string.Empty : Construct(Find("ItemTypeDescStrangeFilterSubStr"), text);
        }

        /// <summary>
        /// `FindAccountPersonaName` (:186): Steam's name, "[unknown]" when Steam does not know it, "Unknown User" for account 0.
        /// The lookup of a non-zero account sets <see cref="HasUnknownPlayer"/> (:152, :169).
        /// </summary>
        private string PersonaName(uint account)
        {
            if (account == 0)
            {
                return "Unknown User";
            }

            string? name = context.PersonaName(account);
            HasUnknownPlayer = name is null;
            return name ?? "[unknown]";
        }

        /// <summary>`m_bUnknownPlayer`: whether the last account looked up was "[unknown]".</summary>
        public bool HasUnknownPlayer { get; private set; }

        /// <summary>`Generate_ItemRarityDesc` (:1521).</summary>
        /// <remarks>A war paint's rarity is its War Paint's (`GetRarity`, econ_item_interface.cpp:384), which is B754.</remarks>
        private void ItemRarityDesc()
        {
            if (Schema.Rarity(item.DefinitionIndex) is not { } rarity)
            {
                return;
            }

            string type = Find(Schema.ItemTypeName(item.DefinitionIndex)) ?? string.Empty;

            // `GetPaintKitWear` (econ_item_interface.cpp:457): either wear attribute, else false.
            EconAttributeValue? wearValue = Attribute("set_item_texture_wear") ?? Attribute("texture_wear_default");
            string wear = wearValue is { } worn ? Find(WearToken(worn.Value)) ?? string.Empty : string.Empty;
            string format = wearValue is null ? "TFUI_InvTooltip_RarityNoWear" : "TFUI_InvTooltip_Rarity";

            AddDescLine(Construct(Find(format), Find(rarity.LocKey), type, wear), rarity.AttribColor, FlagMisc);
        }

        /// <summary>`GetWearLocalizationString` (tf_item_schema.cpp): the wear's band.</summary>
        private static string WearToken(float wear) => wear switch
        {
            <= 0.2f => "#TFUI_InvTooltip_FactoryNew",
            <= 0.4f => "#TFUI_InvTooltip_MinimalWear",
            <= 0.6f => "#TFUI_InvTooltip_FieldTested",
            <= 0.8f => "#TFUI_InvTooltip_WellWorn",
            <= 1.0f => "#TFUI_InvTooltip_BattleScared",

            // "default wear" (econ_item_constants.cpp, `EconWear_ToIntCategory`), NaN included.
            _ => "#TFUI_InvTooltip_FieldTested",
        };

        /// <summary>`Generate_ItemLevelDesc` (:1378).</summary>
        private void ItemLevelDesc()
        {
            string? typeName = Find(Schema.ItemTypeName(item.DefinitionIndex));

            if (StrangeNameAndStats(typeName))
            {
                return;
            }

            if (IsPaintKitItem || Schema.CollectionReference(item.DefinitionIndex) is not null)
            {
                return;
            }

            ItemLevelDescDefault(typeName);
        }

        /// <summary>`BGenerate_ItemLevelDesc_StrangeNameAndStats` (:1139).</summary>
        private bool StrangeNameAndStats(string? typeName)
        {
            if (StrangeRank() is not { } rank)
            {
                return false;
            }

            if (IsPaintKitItem)
            {
                AddDescLine(Construct(Find("Attrib_stattrakmodule"), rank.Rank), ColorStrange, FlagMisc);

                for (int i = 0; i < KillEaterAttributes.Length; i++)
                {
                    if (Attribute(KillEaterAttributes[i].Score) is not { } alternate || (i != 0 && !Has(KillEaterAttributes[i].Type)))
                    {
                        continue;
                    }

                    AddDescLine(
                        Construct(Find("ItemTypeDescKillEaterAltv2"), Count(alternate), KillEaterTypeText(ScoreType(i)), RestrictionText(i)),
                        ColorLevel,
                        FlagMisc);
                }

                return true;
            }

            bool limited = Has("limited quantity item");

            AddDescLine(
                Construct(
                    Find("ItemTypeDescKillEater"),
                    rank.Rank,
                    typeName ?? string.Empty,
                    rank.Score.ToString(CultureInfo.InvariantCulture),
                    KillEaterTypeText(rank.Type),
                    RestrictionText(rank.Slot),
                    rank.Secondary ?? string.Empty,
                    limited ? Find("LimitedQualityDesc") : string.Empty),
                limited ? ColorLimitedQuantity : ColorLevel,
                FlagType);

            for (int i = 0; i < KillEaterAttributes.Length; i++)
            {
                if (Attribute(KillEaterAttributes[i].Score) is not { } alternate || (i != 0 && !Has(KillEaterAttributes[i].Type)))
                {
                    continue;
                }

                uint type = ScoreType(i);

                if (type == rank.Type)
                {
                    continue;
                }

                AddDescLine(Construct(Find("ItemTypeDescKillEaterAlt"), Count(alternate), KillEaterTypeText(type), RestrictionText(i)), ColorLevel, FlagMisc);
            }

            return true;
        }

        /// <summary>A score's 32 bits as `%u`.</summary>
        private static string Count(EconAttributeValue score) => unchecked((uint)score.RawBits).ToString(CultureInfo.InvariantCulture);

        /// <summary>`GetItemDescriptionDisplayLevel` (:1239): "wide item level", else `GetItemLevel()`.</summary>
        private string DisplayLevel() =>
            (Attribute("wide item level") is { } wide ? unchecked((uint)wide.RawBits) : unchecked((uint)item.Level)).ToString(CultureInfo.InvariantCulture);

        /// <summary>`Generate_ItemLevelDesc_Default` (:1252).</summary>
        private void ItemLevelDescDefault(string? typeName)
        {
            // "Level 0 Hat" is not shown for a store, preview or rarity item: the type alone.
            if (Schema.Rarity(item.DefinitionIndex) is not null)
            {
                if (!string.IsNullOrEmpty(typeName))
                {
                    AddDescLine(typeName, ColorLevel, FlagType);
                }

                return;
            }

            bool forceLevel = Attribute("force_level_display") is { } force && force.Value > 0f;

            if (string.IsNullOrEmpty(typeName) && !forceLevel)
            {
                return;
            }

            if (typeName is not null)
            {
                string? format = null;

                if (Attribute("override item level desc string") is { } custom && unchecked((uint)custom.RawBits) == 1)
                {
                    format = Find("ItemTypeDescCustomLevelString_MvMTour");
                }

                if (format is null)
                {
                    if (Has("limited quantity item"))
                    {
                        AddDescLine(Construct(Find("ItemTypeDescLimited"), DisplayLevel(), typeName, Find("LimitedQualityDesc")), ColorLimitedQuantity, FlagType);
                        return;
                    }

                    format = Find("ItemTypeDesc");
                }

                AddDescLine(Construct(format, DisplayLevel(), typeName), ColorLevel, FlagType);
            }
            else
            {
                AddDescLine(Construct(Find("ItemTypeDescNoLevel"), DisplayLevel()), ColorLevel, FlagType);
            }
        }

        /// <summary>`Generate_CraftTag` (:1407): "makers mark id".</summary>
        private void CraftTag()
        {
            if (Attribute("makers mark id") is { } mark && Schema.AttributeDefinitionIndex("makers mark id") is { } index)
            {
                AddAttributeDescription(index, mark.RawBits, ColorNone, 0);
            }
        }

        /// <summary>`Generate_StyleDesc` (:1424).</summary>
        private void StyleDesc()
        {
            if (item.Style(Schema) is not { } style || Schema.StyleName(item.DefinitionIndex, style) is not { } styleToken
                || Find(styleToken) is not { } styleName)
            {
                return;
            }

            AddDescLine(Construct(Find("#Econ_Style_Desc"), styleName), ColorLevel, FlagMisc);
        }

        /// <summary>`Generate_Painted` (:2402): the paint effect lines, then the paint can's name.</summary>
        private void Painted()
        {
            string? token = Attribute("Paint Effect")?.Value switch
            {
                1f => "Econ_Paint_Effect_Oscillating",
                2f => "Econ_Paint_Effect_Position",
                3f => "Econ_Paint_Effect_LowHealthWarning",
                _ => null,
            };

            if (token is not null)
            {
                LocalizedAddDescLine(token, ColorNeutral, FlagMisc);
            }

            // "Find the name of the paint we have applied in the least efficient way imaginable!" (:2426-2444): not on a
            // tool unless it says to show it.
            if ((Schema.IsTool(item.DefinitionIndex) && !Has("show paint description"))
                || Attribute("set item tint RGB") is not { } tint
                || Schema.PaintCanFor(tint.RawBits) is not { } paintCan
                || Schema.ItemBaseName(paintCan) is not { } paintToken
                || Find(paintToken) is not { } paintName)
            {
                return;
            }

            AddDescLine(Construct(Find("Econ_Paint_Name"), paintName), ColorLevel, FlagMisc);
        }

        /// <summary>`Generate_HolidayRestriction` (:1447): level-colored while the calendar has it on, negative otherwise.</summary>
        private void HolidayRestriction()
        {
            if (Schema.HolidayRestriction(item.DefinitionIndex) is not { } holiday)
            {
                return;
            }

            bool active = TfHolidays.EconCalendarActive(TfHolidays.HolidayForString(holiday), context.Now);
            LocalizedAddDescLine("Econ_holiday_restriction_" + holiday, active ? ColorLevel : ColorNegative, FlagMisc);
        }

        /// <summary>`Generate_SaxxyAwardDesc` (:2005): the Saxxy and the Memory Maker only.</summary>
        private void SaxxyAwardDesc()
        {
            string? name = Schema.ItemDefinitionName(item.DefinitionIndex);
            bool saxxy = string.Equals(name, "Saxxy", StringComparison.Ordinal);
            bool memoryMaker = string.Equals(name, "Memory Maker", StringComparison.Ordinal);

            if ((!saxxy && !memoryMaker) || Attribute("saxxy award category") is not { } category || Attribute("event date") is not { } date)
            {
                return;
            }

            int year = DateTimeOffset.FromUnixTimeSeconds(unchecked((uint)date.RawBits)).ToLocalTime().Year;

            AddDescLine(
                Construct(
                    Find(memoryMaker ? "#Attrib_MemoryMakerAward" : "#Attrib_SaxxyAward"),
                    Find("Replay_Contest_Category" + unchecked((uint)category.RawBits).ToString(CultureInfo.InvariantCulture)),
                    year.ToString(CultureInfo.InvariantCulture)),
                ColorPositive,
                FlagMisc);
        }

        /// <summary>`Generate_VisibleAttributes` (:3415): the shown ones, sorted by effect type then index.</summary>
        private void VisibleAttributes()
        {
            List<(ItemAttributeDefinition Definition, int Bits)> visible = [];

            foreach (EconAttributeValue attribute in item.Iterated)
            {
                if (Schema.AttributeDefinition(attribute.DefinitionIndex) is { Hidden: false } definition)
                {
                    visible.Add((definition, attribute.RawBits));
                }
            }

            // `CUtlVector::Sort` is `qsort`, not stable: equal keys are the same definition, so their order does not show.
            visible.Sort((a, b) => a.Definition.EffectType != b.Definition.EffectType
                ? a.Definition.EffectType - b.Definition.EffectType
                : a.Definition.Index - b.Definition.Index);

            bool hideSeries = Has("hide crate series number");

            foreach ((ItemAttributeDefinition definition, int bits) in visible)
            {
                if (hideSeries && string.Equals(definition.Name, "set supply crate series", StringComparison.Ordinal))
                {
                    continue;
                }

                AddAttributeDescription(definition.Index, bits, ColorNone, 0);
            }
        }

        /// <summary>`Generate_QualityDesc` (:1471): self-made and community only, which `GetQuality()` never is in a demo.</summary>
        private void QualityDesc()
        {
            int quality = Quality(item);

            if (quality != QualitySelfMade && quality != QualityCommunity)
            {
                return;
            }

            LocalizedAddDescLine(quality == QualitySelfMade ? "Attrib_Selfmade_Description" : "Attrib_Community_Description", ColorNeutral, FlagMisc);
        }

        /// <summary>`Generate_ItemDesc` (:1575): no custom description without a backpack.</summary>
        private void ItemDesc()
        {
            if (Schema.ItemDescription(item.DefinitionIndex) is { } description)
            {
                LocalizedAddDescLine(description, ColorNeutral, FlagDesc);
            }
        }

        /// <summary>`Generate_GiftedBy` (:1747).</summary>
        private void GiftedBy()
        {
            if (Attribute("gifter account id") is not { } gifter || Schema.AttributeDefinitionIndex("gifter account id") is not { } index)
            {
                return;
            }

            AddAttributeDescription(index, gifter.RawBits, ColorNone, 0);

            if (Attribute("event date") is { } date && Schema.AttributeDefinitionIndex("event date") is { } dateIndex)
            {
                AddAttributeDescription(dateIndex, date.RawBits, ColorNone, 0);
            }
        }

        /// <summary>`Generate_EventDetail` (:2640).</summary>
        private void EventDetail()
        {
            if (Schema.EventDescriptionFooter(item.DefinitionIndex) is { } footer)
            {
                AddEmptyDescLine();
                LocalizedAddDescLine(footer, ColorPositive, FlagMisc);
            }
        }

        /// <summary>
        /// `Generate_ItemSetDesc` (:2696). `IsItemEquipped` reads the owner's inventory (:2667), which a demo watcher has
        /// not got: every item of the set is missing, so a hidden set shows nothing and the bonus is grayed.
        /// </summary>
        private void ItemSetDesc()
        {
            if (Schema.ItemSet(item.DefinitionIndex) is not { } set)
            {
                return;
            }

            // "only displayed when the full set is equipped", which it never is here (:2763).
            if (set.IsHidden)
            {
                return;
            }

            AddEmptyDescLine();

            if (set.LocalizedName is { } setName)
            {
                LocalizedAddDescLine(setName, ColorItemSetName, FlagSet | FlagSetName);
            }

            foreach (int member in set.Items)
            {
                // "if ( !pItemSetEntryDef ) continue" (:2731) never fires: `ItemSet` lists only names the items define.
                // `CEconItemLocalizedFullNameGenerator( provider, def )`: `AE_UNIQUE`, the definition's attributes.
                string memberName = new Builder(DefinitionView(member), context).FullName(QualityUnique, useProperName: true);
                AddDescLine(memberName, ColorItemSetMissing, FlagSet);
            }

            bool visibleBonus = false;

            foreach ((int attribute, float _) in set.Attributes)
            {
                if (Schema.AttributeDefinition(attribute) is { Hidden: false })
                {
                    visibleBonus = true;
                    break;
                }
            }

            if (!visibleBonus)
            {
                return;
            }

            AddEmptyDescLine();
            LocalizedAddDescLine("#Econ_Set_Bonus", ColorItemSetName, FlagSet);

            foreach ((int attribute, float value) in set.Attributes)
            {
                AddAttributeDescription(attribute, BitConverter.SingleToInt32Bits(value), ColorItemSetMissing, 0);
            }
        }

        /// <summary>An item made of a definition alone, as `CGameItemDefinition_EconItemInterfaceWrapper` (:3771) is.</summary>
        private TfItemView DefinitionView(int definition)
        {
            IReadOnlyList<EconAttributeValue> attributes = Schema.DefinitionAttributesFor(definition);
            Dictionary<int, EconAttributeValue> byIndex = [];

            foreach (EconAttributeValue attribute in attributes)
            {
                byIndex.TryAdd(attribute.DefinitionIndex, attribute);
            }

            return new TfItemView(definition, QualityUnique, byIndex) { Iterated = attributes, IteratesDefinition = true };
        }

        /// <summary>`Generate_ExpirationDesc` (:3051): the later of the schema's date and the attribute's; nothing without either.</summary>
        /// <remarks>*Interpolated:* the date is printed as .NET's long date where the engine calls `BGetLocalFormattedDate`.</remarks>
        private void ExpirationDesc()
        {
            uint attributeExpiration = Attribute("expiration date") is { } attribute ? unchecked((uint)attribute.RawBits) : 0u;
            uint expiration = Math.Max(SchemaExpiration(), attributeExpiration);

            if (expiration == 0)
            {
                return;
            }

            AddEmptyDescLine();

            if (Has("quest loaner id low") || Has("quest loaner id hi"))
            {
                AddDescLine(Find("#Attrib_LoanerItemExpirationDate") ?? string.Empty, ColorNegative, FlagMisc);
                return;
            }

            AddDescLine(Construct(Find("#Attrib_ExpirationDate"), Date(expiration)), ColorNegative, FlagMisc);
        }

        /// <summary>
        /// `GetExpirationDate()` (econ_item_schema.cpp:3102-3118): "YYYY-MM-DD hh:mm:ss" in local time, as `mktime` reads
        /// it (rtime.cpp:605), or a custom date after a "!": "end_of_halloween" is `g_Holiday_Halloween`'s cached end
        /// (tf_item_schema.cpp:2531, econ_holidays.cpp:418) — *interpolated* as the watcher's year's 11-08, the year the
        /// calendar check caches — and anything else `k_RTime32Nil`.
        /// </summary>
        private uint SchemaExpiration()
        {
            if (Schema.ExpirationDate(item.DefinitionIndex) is not { Length: > 0 } text)
            {
                return 0u;
            }

            if (text[0] == '!')
            {
                return text[1..].Equals("end_of_halloween", StringComparison.OrdinalIgnoreCase)
                    ? RTime(new DateTime(context.Now.Year, 11, 8, 0, 0, 0, DateTimeKind.Local))
                    : 0u;
            }

            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime at)
                ? RTime(at)
                : 0u;
        }

        private static uint RTime(DateTime at) => unchecked((uint)new DateTimeOffset(at).ToUnixTimeSeconds());

        /// <summary>
        /// `Generate_FlagsAttributes` (:3159) for an item with no backpack: no flags, origin `kEconItemOrigin_Invalid`, quality
        /// normal — so the only lines are a trade timer's, and "cannot craft" where the timer applies.
        /// </summary>
        private void FlagsAttributes()
        {
            EconAttributeValue? tradable = Attribute("tradable after date");
            EconAttributeValue? escrow = Attribute("tool escrow until date");
            long now = new DateTimeOffset(context.Now).ToUnixTimeSeconds();
            List<string> notes = [];

            if (tradable is not null || escrow is not null)
            {
                if (tradable is { } trade && unchecked((uint)trade.RawBits) > now && Schema.AttributeDefinitionIndex("tradable after date") is { } tradeIndex)
                {
                    AddAttributeDescription(tradeIndex, trade.RawBits, ColorNone, 0);
                }

                if (escrow is { } tool && unchecked((uint)tool.RawBits) > now && Schema.AttributeDefinitionIndex("tool escrow until date") is { } escrowIndex)
                {
                    AddAttributeDescription(escrowIndex, tool.RawBits, ColorNone, 0);
                }

                // `IsUsableInCrafting()`: "always tradable" says yes; else the origin, `kEconItemOrigin_Invalid`, says no.
                if (!Has("always tradable"))
                {
                    notes.Add("#Attrib_CannotCraft");
                }
            }

            // Every other branch ends at `kEconItemOrigin_Invalid`'s "do nothing" (:3296), or is the always-tradable one.
            if (notes.Count == 0 || Find("#AttribFormat_AdditionalNote") is not { } format)
            {
                return;
            }

            AddEmptyDescLine();

            foreach (string note in notes)
            {
                AddDescLine(Construct(format, Find(note)), ColorNeutral, FlagMisc);
            }
        }

        /// <summary>
        /// `CLocalizedStringArg&lt;CLocalizedRTime32&gt;` (:112): `Econ_DateFormat` with the day, hour, minute and second, or
        /// `Econ_DateFormat_GMT` with them in GMT when forced.
        /// </summary>
        /// <remarks>*Interpolated:* `BGetLocalFormattedDate` is the OS's long date; .NET's is used.</remarks>
        private string Date(uint time, bool gmt = false)
        {
            DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(time);
            DateTime local = gmt ? at.UtcDateTime : at.ToLocalTime().DateTime;
            Dictionary<string, string> tokens = new(StringComparer.OrdinalIgnoreCase)
            {
                ["day"] = local.ToString("D", CultureInfo.CurrentCulture),
                ["hour"] = local.Hour.ToString(CultureInfo.InvariantCulture),
                ["min"] = local.Minute.ToString("00", CultureInfo.InvariantCulture),
                ["sec"] = local.Second.ToString("00", CultureInfo.InvariantCulture),
            };

            string token = gmt ? "Econ_DateFormat_GMT" : "Econ_DateFormat";

            return Find(token) is { } format ? VguiLocalize.ConstructString(format, tokens) : string.Empty;
        }

        /// <summary>`AddAttributeDescription` (:3665): `CEconAttributeDescription`, then the line in its color unless overridden.</summary>
        private void AddAttributeDescription(int attribute, int bits, int overrideColor, int additionalMeta)
        {
            if (Schema.AttributeDefinition(attribute) is not { } definition || Describe(definition, bits) is not { Length: > 0 } text)
            {
                return;
            }

            int color = DefaultColor(definition);

            // `kUserGeneratedAttributeType_MVMEngineering` outside MvM, or a Halloween spell out of season, is grayed (:3694-3704).
            bool spellOutOfSeason = definition.UserGenerationType == 2
                && !(TfHolidays.EconCalendarActive(TfHolidays.Halloween, context.Now) || TfHolidays.EconCalendarActive(TfHolidays.FullMoon, context.Now));

            if ((definition.UserGenerationType == 1 && !context.IsMannVsMachine) || spellOutOfSeason)
            {
                color = ColorItemSetMissing;
            }

            AddDescLine(text, overrideColor != ColorNone ? overrideColor : color, FlagAttribute | additionalMeta);
        }

        /// <summary>`GetAttributeDefaultColor` (:3459).</summary>
        private static int DefaultColor(ItemAttributeDefinition definition) => definition.EffectType switch
        {
            ItemAttributeDefinition.EffectPositive => ColorPositive,
            ItemAttributeDefinition.EffectNegative => ColorNegative,
            ItemAttributeDefinition.EffectStrange => ColorStrange,
            ItemAttributeDefinition.EffectUnusual => ColorUnusual,
            _ => ColorNeutral,
        };

        /// <summary>`CEconAttributeDescription::InternalConstruct` (:3478): the line, or null where it makes none.</summary>
        private string? Describe(ItemAttributeDefinition definition, int bits)
        {
            if (Find(definition.DescriptionString) is not { } format)
            {
                return null;
            }

            float asFloat = BitConverter.Int32BitsToSingle(bits);
            uint asUInt = unchecked((uint)bits);
            string value = string.Empty;

            switch (definition.DescriptionFormat)
            {
                case ItemAttributeDefinition.FormatAdditivePercentage:
                    value = FloatArgument(asFloat * 100f);
                    break;

                case ItemAttributeDefinition.FormatAccountId:
                    value = PersonaName(asUInt);
                    break;

                case ItemAttributeDefinition.FormatAdditive:
                    value = definition.StoredAsInteger ? asUInt.ToString(CultureInfo.InvariantCulture) : FloatArgument(asFloat);
                    break;

                case ItemAttributeDefinition.FormatInvertedPercentage when asFloat < 1.0:
                    // `( 1.0 - value_as_float ) * 100.0f`: a double, narrowed to the argument's float.
                    value = FloatArgument((float)((1.0 - asFloat) * 100.0));
                    break;

                case ItemAttributeDefinition.FormatInvertedPercentage:
                case ItemAttributeDefinition.FormatPercentage:
                    value = FloatArgument((asFloat * 100f) - 100f);
                    break;

                case ItemAttributeDefinition.FormatDate:
                    // GMT for the employee badge alone (:3555-3562).
                    value = Date(asUInt, gmt: string.Equals(definition.Name, "custom employee number", StringComparison.OrdinalIgnoreCase));
                    break;

                case ItemAttributeDefinition.FormatParticleIndex:
                    value = Find("#Attrib_Particle" + (definition.StoredAsInteger ? asUInt.ToString(CultureInfo.InvariantCulture) : ((int)asFloat).ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
                    break;

                case ItemAttributeDefinition.FormatKillStreakEffectIndex:
                    value = Find("#Attrib_KillStreakEffect" + ((int)asFloat).ToString(CultureInfo.InvariantCulture)) ?? string.Empty;
                    break;

                case ItemAttributeDefinition.FormatKillStreakIdleEffectIndex:
                    value = Find("#Attrib_KillStreakIdleEffect" + ((int)asFloat).ToString(CultureInfo.InvariantCulture)) ?? string.Empty;
                    break;

                case ItemAttributeDefinition.FormatOr:
                    break;

                case ItemAttributeDefinition.FormatFromLookupTable:
                    if (Schema.StringTableEntry(definition.Name, (int)asFloat) is not { } entryToken || Find(entryToken) is not { } entry)
                    {
                        return null;
                    }

                    value = entry;
                    break;

                default:
                    // `ATTDESCFORM_VALUE_IS_ITEM_DEF`, and any format the client does not know, writes nothing (:3606-3616).
                    return null;
            }

            string line = Construct(format, value);

            if (definition.UserGenerationType != 0
                && Find("#Econ_Attrib_UserGeneratedWrapper_" + definition.UserGenerationType.ToString(CultureInfo.InvariantCulture)) is { } wrapper)
            {
                line = Construct(wrapper, line);
            }

            return line;
        }

        /// <summary>
        /// `CLocalizedStringArg&lt;float&gt;` (ilocalize.h:306): `%.0f`, or `%.1f` for a magnitude under one and above
        /// `FLT_EPSILON`. A tie rounds to even, as the Universal CRT's `printf` does.
        /// </summary>
        private static string FloatArgument(float value)
        {
            const float FltEpsilon = 1.1920929E-07f;
            float magnitude = Math.Abs(value);
            int decimals = magnitude <= FltEpsilon || magnitude >= 1.0f ? 0 : 1;

            return Math.Round((double)value, decimals, MidpointRounding.ToEven).ToString(decimals == 0 ? "F0" : "F1", CultureInfo.InvariantCulture);
        }
    }
}
