using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What `CEconItemDescription` (econ_item_description.cpp) and `CItemModelPanel` (item_model_panel.cpp) ask of the schema,
/// off a synthetic items_game (B751).
/// </summary>
public sealed class ItemSchemaDescriptionTests
{
    private const string Schema = """
        "items_game"
        {
            "rarities"
            {
                "common" { "value" "1" "loc_key" "Rarity_Common" "color" "desc_common" }
                "rare" { "value" "3" "loc_key" "Rarity_Rare" "color" "no_such_color" }
            }
            "colors"
            {
                "desc_level" { "color_name" "ItemAttribLevel" }
                "desc_common" { "color_name" "ItemRarityCommon" }
                "desc_attrib_positive" { "color_name" "ItemAttribPositive" }
            }
            "prefabs"
            {
                "weapon_base"
                {
                    "item_type_name" "#TF_Weapon_RocketLauncher"
                    "image_inventory" "backpack/weapons/w_models/w_rocketlauncher"
                    "image_inventory_size_w" "128"
                    "image_inventory_size_h" "82"
                }
            }
            "items"
            {
                "18"
                {
                    "prefab" "weapon_base"
                    "item_description" "#TF_Weapon_Desc"
                    "image_inventory_pos_x" "-4"
                    "image_inventory_pos_y" "2"
                    "image_inventory_overlay" "backpack/overlay_1"
                    "image_inventory_overlay2" "backpack/overlay_2"
                    "holiday_restriction" "halloween_or_fullmoon"
                }
                "19"
                {
                    "item_rarity" "common"
                    "visuals"
                    {
                        "styles"
                        {
                            "0" { "name" "#Style_A" "image_inventory" "backpack/style_a" }
                            "1" { "name" "#Style_B" }
                        }
                    }
                }
                "20"
                {
                    "item_rarity" "rare"
                }
                "21"
                {
                    "name" "The Degreaser"
                    "collection_reference" "some_collection"
                    "event_desc_footer" "#Footer"
                }
                "22"
                {
                    "name" "The Powerjack"
                }
            }
            "item_sets"
            {
                "polycount_pyro"
                {
                    "name" "#TF_Set_Polycount_Pyro"
                    "items"
                    {
                        "The Degreaser" "1"
                        "The Powerjack" "1"
                        "Not An Item" "1"
                    }
                    "attributes"
                    {
                        "damage bonus" { "attribute_class" "mult_dmg" "value" "1.25" }
                    }
                    "is_hidden_set" "1"
                }
            }
            "master_maps_list"
            {
                "1" { "name" "cp_gorge" "localizedname" "#TF_Map_cp_gorge" }
                "7" { "name" "koth_x" "localizedname" "#TF_Map_x" "strangeprefixtoken" "#Prefix_x" }
            }
            "attributes"
            {
                "2"
                {
                    "name" "damage bonus"
                    "description_string" "#Attrib_DamageDone_Positive"
                    "description_format" "value_is_percentage"
                    "hidden" "0"
                    "effect_type" "positive"
                    "stored_as_integer" "0"
                }
                "134"
                {
                    "name" "attach particle effect"
                    "description_string" "#Attrib_Particle"
                    "description_format" "VALUE_IS_PARTICLE_INDEX"
                    "effect_type" "unusual"
                    "hidden" "1"
                    "is_user_generated" "2"
                    "stored_as_integer" "1"
                }
                "796"
                {
                    "name" "no format"
                    "effect_type" "sideways"
                }
            }
            "item_levels"
            {
                "KillEaterRank"
                {
                    "0" { "score" "10" "rank_name" "KillEaterRank0" }
                    "1" { "score" "25" }
                }
            }
            "kill_eater_score_types"
            {
                "0" { "type_name" "Kills" }
                "7" { "type_name" "Ubers" }
            }
            "string_lookups"
            {
                "SPELL: set item tint RGB"
                {
                    "2" "#TF_HalloweenSpell_Paint_1_Attr"
                    "0" "#TF_HalloweenSpell_Paint_2_Attr"
                }
            }
        }
        """;

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    [Test]
    public void AttributeDefinition_ByIndex_CarriesItsDisplayFields()
    {
        ItemSchema schema = Read();

        ItemAttributeDefinition damage = schema.AttributeDefinition(2).ShouldNotBeNull();
        damage.Name.ShouldBe("damage bonus");
        damage.DescriptionString.ShouldBe("#Attrib_DamageDone_Positive");
        damage.DescriptionFormat.ShouldBe(ItemAttributeDefinition.FormatPercentage);
        damage.EffectType.ShouldBe(ItemAttributeDefinition.EffectPositive);
        damage.Hidden.ShouldBeFalse();
        damage.StoredAsInteger.ShouldBeFalse();
        damage.UserGenerationType.ShouldBe(0);

        ItemAttributeDefinition particle = schema.AttributeDefinition(134).ShouldNotBeNull();
        particle.DescriptionFormat.ShouldBe(ItemAttributeDefinition.FormatParticleIndex, "StringFieldToInt compares with Q_stricmp (econ_item.cpp:38)");
        particle.EffectType.ShouldBe(ItemAttributeDefinition.EffectUnusual, "\"unusual\" is index 0 of g_EffectTypes (econ_item_schema.cpp:127)");
        particle.Hidden.ShouldBeTrue();
        particle.StoredAsInteger.ShouldBeTrue();
        particle.UserGenerationType.ShouldBe(2, "is_user_generated (:2187)");
    }

    [Test]
    public void AttributeDefinition_WithAnUnknownOrMissingField_IsMinusOne()
    {
        ItemSchema schema = Read();

        ItemAttributeDefinition plain = schema.AttributeDefinition(796).ShouldNotBeNull();
        plain.DescriptionString.ShouldBeNull("GetString( \"description_string\", NULL ) (:2190)");
        plain.DescriptionFormat.ShouldBe(-1, "a missing string is -1 (econ_item.cpp:34)");
        plain.EffectType.ShouldBe(-1, "a string not in the table is -1 (econ_item.cpp:46)");
        schema.AttributeDefinition(9999).ShouldBeNull();
    }

    [Test]
    public void ItemDisplayKeys_ThroughThePrefabChain_AnswerAsTheDefinitionHasThem()
    {
        ItemSchema schema = Read();

        schema.ItemTypeName(18).ShouldBe("#TF_Weapon_RocketLauncher", "from the prefab");
        schema.ItemTypeName(21).ShouldBe(string.Empty, "GetString( \"item_type_name\", \"\" ) (econ_item_schema.cpp:3133)");
        schema.ItemDescription(18).ShouldBe("#TF_Weapon_Desc");
        schema.ItemDescription(21).ShouldBeNull("GetString( \"item_description\", NULL ) (:3134)");
        schema.InventoryImage(18).ShouldBe("backpack/weapons/w_models/w_rocketlauncher");
        schema.InventoryImage(21).ShouldBeNull();
        schema.HolidayRestriction(18).ShouldBe("halloween_or_fullmoon");
        schema.HolidayRestriction(21).ShouldBeNull();
    }

    [Test]
    public void InventoryImageData_UnstatedSizes_AreTheEngineDefaults()
    {
        ItemSchema schema = Read();

        schema.InventoryImageData(18).ShouldBe((-4, 2, 128, 82));
        schema.InventoryImageData(21).ShouldBe((0, 0, 128, 82), "atoi of \"0\", \"0\", \"128\", \"82\" (:3150-3153)");
    }

    [Test]
    public void InventoryOverlayImages_BothOverlays_AreInOrder()
    {
        ItemSchema schema = Read();

        schema.InventoryOverlayImages(18).ShouldBe(["backpack/overlay_1", "backpack/overlay_2"]);
        schema.InventoryOverlayImages(21).ShouldBeEmpty();
    }

    [Test]
    public void Style_ByIndex_HasItsNameAndInventoryImage()
    {
        ItemSchema schema = Read();

        schema.StyleName(19, 0).ShouldBe("#Style_A");
        schema.StyleName(19, 1).ShouldBe("#Style_B");
        schema.StyleName(19, 2).ShouldBeNull();
        schema.StyleName(18, 0).ShouldBeNull("no styles");
        schema.StyleInventoryImage(19, 0).ShouldBe("backpack/style_a");
        schema.StyleInventoryImage(19, 1).ShouldBeNull();
    }

    [Test]
    public void Rarity_ByTheItemsRarityName_IsItsLocKeyAndAttribColor()
    {
        ItemSchema schema = Read();

        schema.Rarity(19).ShouldBe(("Rarity_Common", ItemSchema.AttribColorRarityCommon));
        schema.Rarity(20).ShouldBe(("Rarity_Rare", 0), "an unknown color name is index 0 (econ_item_constants.cpp:301)");
        schema.Rarity(21).ShouldBeNull("no item_rarity: k_unItemRarity_Any has no definition");
    }

    [Test]
    public void AttribColorName_ByIndex_IsTheColorsSectionsNameOrNeutral()
    {
        ItemSchema schema = Read();

        schema.AttribColorName(0).ShouldBe("ItemAttribLevel");
        schema.AttribColorName(2).ShouldBe("ItemAttribPositive");
        schema.AttribColorName(3).ShouldBe("ItemAttribNeutral", "a color the schema lacks (econ_item_constants.cpp:311)");
    }

    [Test]
    public void ItemLevelRankName_ByScore_IsTheRankNameOrTheBlockAndLevel()
    {
        ItemSchema schema = Read();

        schema.ItemLevelRankName("KillEaterRank", 5).ShouldBe("KillEaterRank0");
        schema.ItemLevelRankName("KillEaterRank", 10).ShouldBe("KillEaterRank1", "GetString( \"rank_name\", \"%s%i\" ) (:7100)");
        schema.ItemLevelRankName("Missing", 5).ShouldBeNull();
    }

    [Test]
    public void KillEaterTypeName_ByScoreType_IsItsTypeName()
    {
        ItemSchema schema = Read();

        schema.KillEaterTypeName(7).ShouldBe("Ubers");
        schema.KillEaterTypeName(3).ShouldBeNull();
    }

    [Test]
    public void ItemSet_AnItemTheSetNames_IsTheSetWithItsItemsAndBonus()
    {
        ItemSchema schema = Read();

        ItemSetDefinition set = schema.ItemSet(22).ShouldNotBeNull();
        set.LocalizedName.ShouldBe("#TF_Set_Polycount_Pyro");
        set.Items.ShouldBe([21, 22], "by item `name`, an unknown name dropped (econ_item_schema.cpp:436)");
        set.Attributes.ShouldBe([(2, 1.25f)]);
        set.IsHidden.ShouldBeTrue();
        schema.ItemSet(18).ShouldBeNull();
    }

    [Test]
    public void MasterMap_ByDefIndex_IsItsTokens()
    {
        ItemSchema schema = Read();

        schema.MasterMap(1).ShouldBe(("#TF_Map_cp_gorge", null));
        schema.MasterMap(7).ShouldBe(("#TF_Map_x", "#Prefix_x"));
        schema.MasterMap(2).ShouldBeNull();
    }

    [Test]
    public void CollectionReferenceAndFooter_AsTheItemStatesThem_AreReturned()
    {
        ItemSchema schema = Read();

        schema.CollectionReference(21).ShouldBe("some_collection");
        schema.EventDescriptionFooter(21).ShouldBe("#Footer");
        schema.CollectionReference(22).ShouldBeNull();
    }

    [Test]
    public void StringTableEntry_ByTableAndIndex_IsTheToken()
    {
        ItemSchema schema = Read();

        schema.StringTableEntry("SPELL: set item tint RGB", 2).ShouldBe("#TF_HalloweenSpell_Paint_1_Attr");
        schema.StringTableEntry("SPELL: set item tint RGB", 7).ShouldBeNull();
        schema.StringTableEntry("missing", 0).ShouldBeNull();
    }
}
