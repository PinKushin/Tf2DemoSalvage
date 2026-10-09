using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>An item's own handedness — <c>flip_viewmodel</c> (B515).</summary>
/// <remarks>
/// `CEconItemDefinition::BInitFromKV`: `m_bFlipViewModel = m_pKVItem-&gt;GetInt( "flip_viewmodel", 0 ) != 0;`
/// (`econ_item_schema.cpp:3169`), read after `RecursiveInheritKeyValues` has folded the prefabs in, so a
/// prefab's value reaches every item using it. The shipped file sets it once, on the Huntsman's block, whose
/// `c_bow.mdl` is built left-handed.
/// </remarks>
public sealed class ItemSchemaFlipViewmodelTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "weapon_bow"
                {
                    "model_player" "models/weapons/c_models/c_bow/c_bow.mdl"
                    "flip_viewmodel" "1"
                }
            }
            "items"
            {
                "56"
                {
                    "prefab" "weapon_bow"
                }
                "13"
                {
                    "model_player" "models/weapons/c_models/c_scattergun.mdl"
                }
                "14"
                {
                    "prefab" "weapon_bow"
                    "flip_viewmodel" "0"
                }
            }
        }
        """;

    [Test]
    public void FlipsViewmodel_AnItemWhosePrefabSetsIt_IsTrue()
    {
        Read().FlipsViewmodel(56).ShouldBeTrue();
    }

    [Test]
    public void FlipsViewmodel_AnItemThatNeverSaysSo_IsFalse()
    {
        Read().FlipsViewmodel(13).ShouldBeFalse();
    }

    [Test]
    public void FlipsViewmodel_AnItemOverridingItsPrefabWithZero_IsFalse()
    {
        // The item's own key wins over its prefab's — `RecursiveInheritKeyValues` sets each of an item's
        // own keys over the prefab's — and `"0"` is an answer, not a gap.
        Read().FlipsViewmodel(14).ShouldBeFalse();
    }

    [Test]
    public void FlipsViewmodel_AnUnknownItem_IsFalse()
    {
        Read().FlipsViewmodel(99999).ShouldBeFalse();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
