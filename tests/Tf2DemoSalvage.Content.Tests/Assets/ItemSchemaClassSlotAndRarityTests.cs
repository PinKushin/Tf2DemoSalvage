using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CTFItemDefinition::GetLoadoutSlot( iClass )` (tf_item_schema.cpp:1271) and `CEconItemSchema::GetRarityColor`
/// (econ_item_schema.cpp:6633) — what `CTFHudPlayerClass::OnThink` (tf_hud_playerstatus.cpp:222, :327) asks of an item.
/// </summary>
public sealed class ItemSchemaClassSlotAndRarityTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "shotgun"
                {
                    "item_slot" "secondary"
                    "item_rarity" "uncommon"
                    "used_by_classes" { "soldier" "1" "heavy" "1" }
                }
            }
            "items"
            {
                "10"
                {
                    "prefab" "shotgun"
                    "used_by_classes" { "Engineer" "primary" }
                }
                "11"
                {
                    "item_slot" "melee"
                    "item_rarity" "any"
                }
                "12"
                {
                    "item_slot" "melee"
                    "item_rarity" "odd"
                }
            }
            "rarities"
            {
                "uncommon" { "value" "2" "loc_key" "Rarity_Uncommon" "color" "desc_uncommon" }
                "odd" { "value" "9" "loc_key" "Rarity_Odd" "color" "not_a_color" }
            }
            "colors"
            {
                "desc_uncommon" { "color_name" "ItemRarityUncommon" }
                "desc_level" { "color_name" "ItemAttribLevel" }
            }
        }
        """;

    [Test]
    public void LoadoutSlot_UsedByClasses_IsTheDefaultOrTheNamedSlot()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

        // "1" takes the default slot (:972); a name takes its own (:974), merged with the prefab's block.
        schema.LoadoutSlot(10, 3).ShouldBe(ItemSchema.LoadoutSlotSecondary, "soldier, from the prefab");
        schema.LoadoutSlot(10, 6).ShouldBe(ItemSchema.LoadoutSlotSecondary, "heavy, from the prefab");
        schema.LoadoutSlot(10, 9).ShouldBe(ItemSchema.LoadoutSlotPrimary, "engineer, named, matched without case");
        schema.LoadoutSlot(10, 1).ShouldBe(ItemSchema.LoadoutSlotInvalid, "a scout never uses it");
        schema.LoadoutSlot(10, 0).ShouldBe(ItemSchema.LoadoutSlotSecondary, "class 0 is the default slot (:1278)");
    }

    [Test]
    public void RarityColor_ByRarityValue_IsTheColorDefinitionsName()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

        schema.RarityColor(10).ShouldBe("ItemRarityUncommon", "inherited item_rarity, its color's color_name");
        schema.RarityColor(11).ShouldBeNull("\"any\" is k_unItemRarity_Any, which has no definition");

        // An unknown color name is index 0 (econ_item_constants.cpp:301), ATTRIB_COL_LEVEL — desc_level.
        schema.RarityColor(12).ShouldBe("ItemAttribLevel");
        schema.RarityColor(999).ShouldBeNull();
    }
}
