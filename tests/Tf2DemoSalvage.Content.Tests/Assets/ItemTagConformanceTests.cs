using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>An item's econ tags — what `CEconItemDefinition::HasEconTag` asks, such as `ghost_wearable` (B767).</summary>
/// <remarks>
/// A `tags` block of names on the item or a prefab it inherits from, as every key is inherited
/// (`RecursiveInheritKeyValues`, econ_item_schema.cpp:2909). `CTFWearable::ShouldDraw` asks for `ghost_wearable` on a
/// ghost's cosmetics (`tf_item_wearable.cpp:330`).
/// </remarks>
public sealed class ItemTagConformanceTests
{
    [TestCase(100, "ghost_wearable", true, TestName = "HasTag_AnItemDeclaringIt_IsTrue")]
    [TestCase(200, "ghost_wearable", true, TestName = "HasTag_AnItemInheritingItFromAPrefab_IsTrue")]
    [TestCase(300, "ghost_wearable", false, TestName = "HasTag_AnItemDeclaringAnotherTag_IsFalse")]
    [TestCase(100, "GHOST_WEARABLE", true, TestName = "HasTag_AnyCase_IsTheSameTag")]
    [TestCase(999, "ghost_wearable", false, TestName = "HasTag_AnItemTheSchemaLacks_IsFalse")]
    public void HasTag_AnItem_IsWhatItOrItsPrefabsDeclare(int item, string tag, bool expected)
    {
        Read().HasTag(item, tag).ShouldBe(expected);
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(
        """
        "items_game"
        {
            "prefabs"
            {
                "ghostly" { "tags" { "ghost_wearable" "1" } }
            }
            "items"
            {
                "100" { "tags" { "ghost_wearable" "1" } }
                "200" { "prefab" "ghostly" }
                "300" { "tags" { "can_deal_damage" "1" } }
            }
        }
        """));
}
