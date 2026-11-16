namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// `CEconItemAttributeDefinition`'s display fields (econ_item_schema.cpp:2175): what `CEconAttributeDescription` reads to
/// write an attribute's line on an item's description (B751).
/// </summary>
/// <param name="Index">`m_nDefIndex`.</param>
/// <param name="Name">`name`.</param>
public sealed record ItemAttributeDefinition(int Index, string Name)
{
    /// <summary>`ATTDESCFORM_VALUE_IS_PERCENTAGE`, index 0 of `g_AttributeDescriptionFormats` (econ_item_schema.cpp:111).</summary>
    public const int FormatPercentage = 0;

    /// <summary>`ATTDESCFORM_VALUE_IS_INVERTED_PERCENTAGE`.</summary>
    public const int FormatInvertedPercentage = 1;

    /// <summary>`ATTDESCFORM_VALUE_IS_ADDITIVE`.</summary>
    public const int FormatAdditive = 2;

    /// <summary>`ATTDESCFORM_VALUE_IS_ADDITIVE_PERCENTAGE`.</summary>
    public const int FormatAdditivePercentage = 3;

    /// <summary>`ATTDESCFORM_VALUE_IS_OR`.</summary>
    public const int FormatOr = 4;

    /// <summary>`ATTDESCFORM_VALUE_IS_DATE`.</summary>
    public const int FormatDate = 5;

    /// <summary>`ATTDESCFORM_VALUE_IS_ACCOUNT_ID`.</summary>
    public const int FormatAccountId = 6;

    /// <summary>`ATTDESCFORM_VALUE_IS_PARTICLE_INDEX`.</summary>
    public const int FormatParticleIndex = 7;

    /// <summary>`ATTDESCFORM_VALUE_IS_KILLSTREAKEFFECT_INDEX`.</summary>
    public const int FormatKillStreakEffectIndex = 8;

    /// <summary>`ATTDESCFORM_VALUE_IS_KILLSTREAK_IDLEEFFECT_INDEX`.</summary>
    public const int FormatKillStreakIdleEffectIndex = 9;

    /// <summary>`ATTDESCFORM_VALUE_IS_ITEM_DEF`.</summary>
    public const int FormatItemDef = 10;

    /// <summary>`ATTDESCFORM_VALUE_IS_FROM_LOOKUP_TABLE`.</summary>
    public const int FormatFromLookupTable = 11;

    /// <summary>`ATTRIB_EFFECT_UNUSUAL` (econ_item_schema.h:696), index 0 of `g_EffectTypes`.</summary>
    public const int EffectUnusual = 0;

    /// <summary>`ATTRIB_EFFECT_STRANGE`.</summary>
    public const int EffectStrange = 1;

    /// <summary>`ATTRIB_EFFECT_NEUTRAL`.</summary>
    public const int EffectNeutral = 2;

    /// <summary>`ATTRIB_EFFECT_POSITIVE`.</summary>
    public const int EffectPositive = 3;

    /// <summary>`ATTRIB_EFFECT_NEGATIVE`.</summary>
    public const int EffectNegative = 4;

    /// <summary>`description_string`, or null.</summary>
    public string? DescriptionString { get; init; }

    /// <summary>`description_format` by `StringFieldToInt`: -1 when missing or not in the table.</summary>
    public int DescriptionFormat { get; init; } = -1;

    /// <summary>`effect_type` by `StringFieldToInt`: -1 when missing or not in the table.</summary>
    public int EffectType { get; init; } = -1;

    /// <summary>`hidden`, read with `GetInt`.</summary>
    public bool Hidden { get; init; }

    /// <summary>`stored_as_integer`, read with `GetInt`.</summary>
    public bool StoredAsInteger { get; init; }

    /// <summary>`is_user_generated`, `m_iUserGenerationType`.</summary>
    public int UserGenerationType { get; init; }
}
