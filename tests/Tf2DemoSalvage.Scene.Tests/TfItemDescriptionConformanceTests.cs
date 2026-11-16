using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CEconItemDescription::GenerateDescriptionLines` (econ_item_description.cpp:347) for an item with no backpack, as every
/// item in a demo is (B751). Each expected line is the engine's formula worked by hand on the synthetic schema below.
/// </summary>
public sealed class TfItemDescriptionConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "items"
            {
                "18"
                {
                    "name" "The Launcher"
                    "item_name" "#Launcher"
                    "item_type_name" "#Type_Launcher"
                    "item_description" "#Launcher_Desc"
                    "propername" "1"
                }
                "19"
                {
                    "name" "The Partner"
                    "item_name" "#Partner"
                    "holiday_restriction" "halloween"
                }
                "20"
                {
                    "name" "Hidden Partner"
                    "item_name" "#HiddenPartner"
                }
                "21"
                {
                    "name" "Decal Tool"
                    "item_name" "#DecalTool"
                    "item_class" "tool"
                    "tool" { "type" "decal" }
                    "attributes" { "set item tint RGB" { "value" "12073019" } }
                }
                "22"
                {
                    "name" "Paint Can Red"
                    "item_name" "#PaintRed"
                    "item_class" "tool"
                    "tool" { "type" "paint_can" }
                    "attributes" { "set item tint RGB" { "value" "12073019" } }
                }
                "23"
                {
                    "name" "Seasonal"
                    "item_name" "#Seasonal"
                    "expiration_date" "2030-01-02 03:04:05"
                }
                "24"
                {
                    "name" "Festive Launcher"
                    "item_name" "#Launcher"
                    "set_item_remap" "The Launcher"
                }
                "26"
                {
                    "name" "Missing"
                    "item_name" "#TF_Missing"
                }
                "25"
                {
                    "name" "Graded Launcher"
                    "item_name" "#Launcher"
                    "item_type_name" "#Type_Launcher"
                    "item_rarity" "uncommon"
                }
            }
            "rarities"
            {
                "uncommon" { "value" "2" "loc_key" "Rarity_Uncommon" "color" "desc_uncommon" }
            }
            "attributes"
            {
                "2" { "name" "damage bonus" "description_string" "#Attrib_Damage" "description_format" "value_is_percentage" "effect_type" "positive" }
                "3" { "name" "clip size penalty" "description_string" "#Attrib_Clip" "description_format" "value_is_percentage" "effect_type" "negative" }
                "4" { "name" "inverted" "description_string" "#Attrib_Inverted" "description_format" "value_is_inverted_percentage" "effect_type" "neutral" }
                "5" { "name" "hidden one" "description_string" "#Attrib_Hidden" "description_format" "value_is_additive" "hidden" "1" }
                "6" { "name" "additive small" "description_string" "#Attrib_Additive" "description_format" "value_is_additive" "effect_type" "neutral" }
                "134" { "name" "attach particle effect" "description_string" "#Attrib_Particle" "description_format" "value_is_particle_index" "effect_type" "unusual" }
                "214" { "name" "kill eater" "description_format" "value_is_additive" "hidden" "1" "stored_as_integer" "1" }
                "211" { "name" "tradable after date" "description_string" "#Attrib_TradableAfter" "description_format" "value_is_date" "hidden" "1" "stored_as_integer" "1" }
                "142" { "name" "set item tint RGB" "description_format" "value_is_additive" "hidden" "1" }
                "143" { "name" "expiration date" "description_format" "value_is_date" "hidden" "1" "stored_as_integer" "1" }
                "228" { "name" "makers mark id" "description_format" "value_is_account_id" "hidden" "1" "stored_as_integer" "1" }
                "834" { "name" "paintkit_proto_def_index" "hidden" "1" "stored_as_integer" "1" }
            }
            "item_levels"
            {
                "KillEaterRank" { "0" { "score" "10" "rank_name" "Rank0" } "1" { "score" "25" "rank_name" "Rank1" } }
            }
            "kill_eater_score_types"
            {
                "0" { "type_name" "#KillsType" }
            }
            "item_sets"
            {
                "pair" { "name" "#Set_Pair" "items" { "The Launcher" "1" "The Partner" "1" } "attributes" { "damage bonus" { "value" "1.5" } } }
                "secret" { "name" "#Set_Secret" "items" { "Hidden Partner" "1" } "is_hidden_set" "1" }
            }
        }
        """;

    private static readonly Dictionary<string, string> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ItemNameFormat"] = "%s1%s2%s3%s4%s5%s6",
        ["ItemNameNormalOrUniqueQualityFormat"] = "%s1%s2%s3%s4%s5",
        ["ItemNameQualityFormat"] = "%s1 %s2%s3%s4%s5",
        ["TF_Unique_Prepend_Proper"] = "The ",
        ["strange"] = "Strange",
        ["Rarity_Spacer"] = " ",
        ["Strange_Spacer"] = " ",
        ["Launcher"] = "Launcher",
        ["Partner"] = "Partner",
        ["HiddenPartner"] = "Hidden Partner",
        ["Type_Launcher"] = "Rocket Launcher",
        ["Launcher_Desc"] = "It launches.",
        ["ItemTypeDesc"] = "Level %s1 %s2",
        ["ItemTypeDescKillEater"] = "%s1 %s2 - %s4: %s3%s5%s6%s7",
        ["KillsType"] = "Kills",
        ["Rank0"] = "Strange",
        ["Rank1"] = "Unremarkable",
        ["Attrib_Damage"] = "+%s1% damage bonus",
        ["Attrib_Clip"] = "%s1% clip size",
        ["Attrib_Inverted"] = "%s1% inverted",
        ["Attrib_Hidden"] = "hidden %s1",
        ["Attrib_Additive"] = "+%s1 additive",
        ["Attrib_Particle"] = "Effect: %s1",
        ["Attrib_Particle13"] = "Burning Flames",
        ["Attrib_TradableAfter"] = "Tradable after %s1",
        ["Econ_holiday_restriction_halloween"] = "Halloween only",
        ["Set_Pair"] = "The Pair",
        ["Econ_Set_Bonus"] = "Set bonus:",
        ["AttribFormat_AdditionalNote"] = "( %s1 )",
        ["Attrib_CannotCraft"] = "Not Usable in Crafting",
        ["Econ_DateFormat"] = "%day%",
        ["PaintRed"] = "Red Paint",
        ["DecalTool"] = "Decal Tool",
        ["Econ_Paint_Name"] = "Paint Color: %s1",
        ["Attrib_ExpirationDate"] = "Expires %s1",
        ["Rarity_Uncommon"] = "Mercenary",
        ["TFUI_InvTooltip_Rarity"] = "%s1 %s2 (%s3)",
        ["TFUI_InvTooltip_RarityNoWear"] = "%s1 %s2",
        ["TFUI_InvTooltip_FactoryNew"] = "Factory New",
    };

    [Test]
    public void Generate_APaintKitItemWithNoWearAttribute_IsFactoryNew()
    {
        // `GetPaintKitWear` answers true for a paint-kit item with no wear attribute, leaving the caller's `flWear = 0`
        // (econ_item_interface.cpp:475-480, econ_item_description.cpp:1536-1540): Factory New.
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 25, quality: 6, level: 1, (834, 350));

        TfItemDescription.Generate(view, Context(schema)).ShouldContain(line => line.Text == "Mercenary Rocket Launcher (Factory New)");
    }

    [Test]
    public void Generate_AnItemWithNeitherWearNorPaintKit_TakesTheNoWearForm()
    {
        // The control: no paint kit, so false, "TFUI_InvTooltip_RarityNoWear".
        ItemSchema schema = Read();

        TfItemDescription.Generate(Item(schema, 25, quality: 6, level: 1), Context(schema))
            .ShouldContain(line => line.Text == "Mercenary Rocket Launcher");
    }

    [Test]
    public void FullItemName_AScoredPaintKitItem_KeepsItsRank()
    {
        // `GetMarketQuality()` (econ_item_description.cpp:625) is strange for a scored paint-kit item, so the rank stays;
        // at `GetQuality()`, normal, a paint kit ignores the quality and drops it.
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 6, level: 1, (834, 350), (214, 12));

        TfItemDescription.FullItemName(view, Context(schema)).ShouldStartWith("Unremarkable");
    }

    [Test]
    public void Generate_AHiddenAccountAttributeNobodyKnows_HasAnUnknownPlayer()
    {
        // The persona pre-pass looks up every `value_is_account_id` attribute, shown or not (:272-281).
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 6, level: 1, (228, 77));

        TfItemDescription.Generate(view, Context(schema), out bool unknown);
        TfItemDescriptionContext named = new()
        {
            Schema = schema,
            Find = token => Strings.GetValueOrDefault(token),
            PersonaName = _ => "Bob",
        };

        TfItemDescription.Generate(view, named, out bool known);

        (unknown, known).ShouldBe((true, false));
    }

    [Test]
    public void Generate_APaintedItem_NamesThePaintCanOfItsTint()
    {
        // `GetPaintItemDefinitionForPaintedItem` (econ_item_description.cpp:2333): the first tool whose tool type is
        // "paint_can" and whose own tint has the same bits. The decal tool before it has the tint but not the type.
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 6, level: 1, (142, Float(12073019f)));

        TfItemDescription.Generate(view, Context(schema))
            .ShouldContain(new TfDescriptionLine("Paint Color: Red Paint", TfItemDescription.ColorLevel, TfItemDescription.FlagMisc));
    }

    [Test]
    public void Generate_ATintNoPaintCanHas_NamesNoPaint()
    {
        // The control: a tint no can carries finds nothing, so no line (:2432).
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 6, level: 1, (142, Float(1f)));

        TfItemDescription.Generate(view, Context(schema)).ShouldNotContain(line => line.Text.StartsWith("Paint Color", StringComparison.Ordinal));
    }

    [Test]
    public void Generate_ASchemaExpirationLaterThanItsAttribute_WritesTheSchemasDate()
    {
        // `MAX( timeSchemaExpiration, timeAttrExpiration )` (:3062-3068); the schema's string is local time (rtime.cpp:605).
        ItemSchema schema = Read();
        DateTime expected = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Local);
        TfItemView view = Item(schema, 23, quality: 6, level: 1, (143, 86400));

        TfItemDescription.Generate(view, Context(schema))
            .ShouldContain(new TfDescriptionLine(
                "Expires " + expected.ToString("D", System.Globalization.CultureInfo.CurrentCulture),
                TfItemDescription.ColorNegative,
                TfItemDescription.FlagMisc));
    }

    [Test]
    public void Generate_ASetItemRemap_ListsTheSetOfTheItemItNames()
    {
        // `set_item_remap` (econ_item_schema.cpp:3248, :5482-5497): the festive launcher takes the launcher's set, so its
        // description lists "The Pair" though the set names only the launcher and the partner.
        ItemSchema schema = Read();

        TfItemDescription.Generate(Item(schema, 24, quality: 6, level: 1), Context(schema))
            .ShouldContain(line => line.Text == "The Pair" && (line.MetaType & TfItemDescription.FlagSetName) != 0);
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private static TfItemDescriptionContext Context(ItemSchema schema, DateTime? now = null) => new()
    {
        Schema = schema,
        Find = token => Strings.GetValueOrDefault(token),
        Now = now ?? new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local),
    };

    /// <summary>An item as a demo carries it: a quality and level networked, attributes from the demo list.</summary>
    private static TfItemView Item(ItemSchema schema, int definition, int quality, int level, params (int Index, int Bits)[] attributes)
    {
        List<EconAttributeValue> networked = [];

        foreach ((int index, int bits) in attributes)
        {
            networked.Add(new EconAttributeValue(index, bits));
        }

        SceneItem item = new(30, "CTFRocketLauncher", definition, new EconAttributeWire([], networked, HasValidItemId: true), IsWeapon: true)
        {
            Quality = quality,
            Level = level,
        };

        return TfItemView.From(item, schema).ShouldNotBeNull();
    }

    private static int Float(float value) => BitConverter.SingleToInt32Bits(value);

    [Test]
    public void Generate_ANetworkedStrangeQualityWithoutAScore_NamesTheItemAsNormal()
    {
        // `GetMarketQuality()` is `GetQuality()` (econ_item_interface.cpp:409), which without a backpack is `AE_NORMAL`
        // (econ_item_view.cpp:446-456) whatever `m_iEntityQuality` says — so no "Strange" and no "The".
        ItemSchema schema = Read();

        TfDescriptionLine name = TfItemDescription.Generate(Item(schema, 18, quality: 11, level: 1), Context(schema))[0];

        name.ShouldBe(new TfDescriptionLine("Launcher", TfItemDescription.ColorLevel, TfItemDescription.FlagName));
    }

    /// <remarks>
    /// `CEconItemLocalizedFullNameGenerator( provider, def, bUseProperName )` (econ_item_description.cpp:3814-3820): the
    /// definition alone (`CGameItemDefinition_EconItemInterfaceWrapper`, :3771-3809) at `AE_UNIQUE`, so a proper name takes
    /// "The" only when asked to (:762-777).
    /// </remarks>
    [TestCase(true, "The Launcher")]
    [TestCase(false, "Launcher")]
    public void DefinitionFullName_AProperNamedDefinition_TakesTheArticleOnlyWhenAsked(bool useProperName, string expected)
    {
        ItemSchema schema = Read();

        TfItemDescription.DefinitionFullName(18, Context(schema), useProperName).ShouldBe(expected);
    }

    [Test]
    public void DefinitionFullName_ATokenWithNoString_UsesTheTokenRaw()
    {
        // `GetLocalizedBaseItemName` (econ_item_description.cpp:587): the token itself when it localizes to nothing.
        TfItemDescription.DefinitionFullName(26, Context(Read()), useProperName: true).ShouldBe("#TF_Missing");
    }

    [Test]
    public void DefinitionFullName_NoSuchDefinition_IsEmpty()
    {
        // "no definition, no name" (:628-632).
        TfItemDescription.DefinitionFullName(9999, Context(Read()), useProperName: true).ShouldBe(string.Empty);
    }

    [Test]
    public void FullItemName_ANetworkedUniqueWithAProperName_TakesNoArticle()
    {
        // The article is `AE_UNIQUE`'s (econ_item_description.cpp:762-777), and the item's own `GetQuality()` is normal; a
        // set lists its members as unique (econ_item_description.h:354), which the set test's "The Launcher" holds.
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 6, level: 1);

        TfItemDescription.FullItemName(view, Context(schema)).ShouldBe("Launcher");
    }

    [Test]
    public void Generate_AScoredItem_NamesItsRankAndWritesTheKillEaterLine()
    {
        // Score 12 is past 10 and under 25: "Rank1" (econ_item_schema.cpp:6325). A normal-quality rank takes "%s1%s2 %s3"
        // (econ_item_description.cpp:812) — "Unremarkable" + "" + " " + "" — then the normal format.
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 18, quality: 11, level: 1, (214, 12));

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(view, Context(schema));

        lines[0].Text.ShouldBe("Unremarkable Launcher");
        lines[1].ShouldBe(new TfDescriptionLine("Unremarkable Rocket Launcher - Kills: 12", TfItemDescription.ColorLevel, TfItemDescription.FlagType));
    }

    [Test]
    public void Generate_AnUnscoredItem_WritesItsLevelAndType()
    {
        ItemSchema schema = Read();

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(Item(schema, 18, quality: 6, level: 37), Context(schema));

        lines[1].ShouldBe(new TfDescriptionLine("Level 37 Rocket Launcher", TfItemDescription.ColorLevel, TfItemDescription.FlagType));
    }

    [Test]
    public void Generate_VisibleAttributes_AreSortedByEffectTypeThenIndexWithEachFormatApplied()
    {
        // Unusual (0) first, then neutral (2) by index, positive (3), negative (4); the hidden one is left out.
        // Percentage: 1.25 * 100 - 100 = 25; 0.8 -> -20. Inverted below one: (1 - 0.75) * 100 = 25. Additive 0.5 keeps a
        // decimal, being under one (ilocalize.h:308).
        ItemSchema schema = Read();
        TfItemView view = Item(
            schema, 18, quality: 6, level: 1,
            (3, Float(0.8f)), (2, Float(1.25f)), (5, Float(3f)), (6, Float(0.5f)), (4, Float(0.75f)), (134, Float(13f)));

        List<string> attributes = [];

        foreach (TfDescriptionLine line in TfItemDescription.Generate(view, Context(schema)))
        {
            // The launcher is in a set too, whose grayed bonus is an attribute line of its own (:2801).
            if ((line.MetaType & TfItemDescription.FlagAttribute) != 0 && line.Color != TfItemDescription.ColorItemSetMissing)
            {
                attributes.Add(line.Text + "|" + line.Color);
            }
        }

        attributes.ShouldBe(
        [
            "Effect: Burning Flames|21",
            "25% inverted|1",
            "+0.5 additive|1",
            "+25% damage bonus|2",
            "-20% clip size|3",
        ]);
    }

    [Test]
    public void Generate_AnItemOfASet_ListsEveryMemberMissingAndGraysTheBonus()
    {
        // `IsItemEquipped` finds no inventory in a demo (econ_item_description.cpp:2667): every member is
        // `ATTRIB_COL_ITEMSET_MISSING`, and the bonus too (:2804).
        ItemSchema schema = Read();

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(Item(schema, 19, quality: 6, level: 1), Context(schema));
        List<TfDescriptionLine> set = [];

        foreach (TfDescriptionLine line in lines)
        {
            // The bonus line is `kDescLineFlag_Attribute` alone (:2801), so it is picked out by its color.
            if ((line.MetaType & TfItemDescription.FlagSet) != 0 || line.Text == " " || line.Color == TfItemDescription.ColorItemSetMissing)
            {
                set.Add(line);
            }
        }

        set.ShouldBe(
        [
            new(" ", TfItemDescription.ColorNeutral, TfItemDescription.FlagEmpty),
            new("The Pair", TfItemDescription.ColorItemSetName, TfItemDescription.FlagSet | TfItemDescription.FlagSetName),
            new("The Launcher", TfItemDescription.ColorItemSetMissing, TfItemDescription.FlagSet),
            new("Partner", TfItemDescription.ColorItemSetMissing, TfItemDescription.FlagSet),
            new(" ", TfItemDescription.ColorNeutral, TfItemDescription.FlagEmpty),
            new("Set bonus:", TfItemDescription.ColorItemSetName, TfItemDescription.FlagSet),
            new("+50% damage bonus", TfItemDescription.ColorItemSetMissing, TfItemDescription.FlagAttribute),
        ]);
    }

    [Test]
    public void Generate_AnItemOfAHiddenSet_ListsNoSet()
    {
        ItemSchema schema = Read();

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(Item(schema, 20, quality: 6, level: 1), Context(schema));

        lines.ShouldAllBe(line => (line.MetaType & TfItemDescription.FlagSet) == 0);
    }

    [Test]
    public void Generate_AHolidayRestriction_IsLevelColoredInSeasonAndNegativeOut()
    {
        ItemSchema schema = Read();
        TfItemView view = Item(schema, 19, quality: 6, level: 1);

        TfDescriptionLine inSeason = Array.Find(
            [.. TfItemDescription.Generate(view, Context(schema, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Local)))],
            line => line.Text == "Halloween only");
        TfDescriptionLine outOfSeason = Array.Find(
            [.. TfItemDescription.Generate(view, Context(schema, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Local)))],
            line => line.Text == "Halloween only");

        // The control: a missing line would read as color 0, which is `ATTRIB_COL_LEVEL`.
        inSeason.Text.ShouldBe("Halloween only");
        (inSeason.Color, inSeason.MetaType).ShouldBe((TfItemDescription.ColorLevel, TfItemDescription.FlagMisc));
        outOfSeason.Color.ShouldBe(TfItemDescription.ColorNegative);
    }

    [Test]
    public void Generate_ADefinitionDescription_IsANeutralDescLine()
    {
        ItemSchema schema = Read();

        TfItemDescription.Generate(Item(schema, 18, quality: 6, level: 1), Context(schema))
            .ShouldContain(new TfDescriptionLine("It launches.", TfItemDescription.ColorNeutral, TfItemDescription.FlagDesc));
    }

    [Test]
    public void Generate_ATradeTimerInTheFuture_WritesItsDateAndCannotCraft()
    {
        // `bHasExpiringTimer` (econ_item_description.cpp:3247): the date line when still ahead, then `IsUsableInCrafting()`,
        // false for `kEconItemOrigin_Invalid` (econ_item_interface.cpp:235), as an additional note.
        ItemSchema schema = Read();
        DateTime now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        int later = (int)new DateTimeOffset(now.AddDays(3)).ToUnixTimeSeconds();
        TfItemView view = Item(schema, 18, quality: 6, level: 1, (211, later));

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(view, Context(schema, now));

        lines[^2].ShouldBe(new TfDescriptionLine(" ", TfItemDescription.ColorNeutral, TfItemDescription.FlagEmpty));
        lines[^1].ShouldBe(new TfDescriptionLine("( Not Usable in Crafting )", TfItemDescription.ColorNeutral, TfItemDescription.FlagMisc));
        lines.ShouldContain(line => line.Text.StartsWith("Tradable after ", StringComparison.Ordinal));
    }

    [Test]
    public void Generate_ATradeTimerPassed_WritesOnlyCannotCraft()
    {
        ItemSchema schema = Read();
        DateTime now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        int earlier = (int)new DateTimeOffset(now.AddDays(-3)).ToUnixTimeSeconds();

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(Item(schema, 18, quality: 6, level: 1, (211, earlier)), Context(schema, now));

        lines.ShouldNotContain(line => line.Text.StartsWith("Tradable after ", StringComparison.Ordinal));
        lines[^1].Text.ShouldBe("( Not Usable in Crafting )");
    }
}
