using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item hides parts of the player it is worn on — <c>player_bodygroups</c> (B352).
/// </summary>
/// <remarks>
/// **This is how a hat removes the head it sits on.** `CEconEntity::UpdateBodygroups`
/// (<c>econ_entity.cpp:2024</c>) walks the item definition's modified bodygroups, resolves each by
/// NAME on the wearer, and sets it:
///
/// <code>
///   int iNumBodyGroups = pItemDef->GetNumModifiedBodyGroups( 0 );
///   for ( int i=0; i&lt;iNumBodyGroups; ++i )
///   {
///       int iBody = 0;
///       const char *pszBodyGroup = pItemDef->GetModifiedBodyGroup( 0, i, iBody );
///       if ( iBody != iState ) continue;
///       int iBodyGroup = pOwner->FindBodygroupByName( pszBodyGroup );
///       if ( iBodyGroup == -1 ) continue;
///       pOwner->SetBodygroup( iBodyGroup, iState );
///   }
/// </code>
///
/// **747 shipped items declare one**, and the common names are exactly the parts a cosmetic
/// replaces: `hat` on 457 of them, `headphones` on 306, then `grenades`, `head`, `dogtags`,
/// `shoes_socks`, `backpack`. On `scout.mdl` those are real body parts whose alternative 1 carries
/// NO MESH — measured — so setting one removes the default part rather than swapping it.
///
/// **The value is matched against the state, not stored as one.** `if ( iBody != iState ) continue;`
/// means a `"hat" "1"` entry applies only during the pass for state 1; an entry with value 0 exists
/// to force a part back ON. Reading the pair as "set this group to this number" is the plausible
/// misreading and gives the same answer for every shipped item, which is why it is asserted here
/// rather than left to the implementation.
/// </remarks>
public sealed class PlayerBodygroupConformanceTests
{
    [Test]
    public void PlayerBodygroups_ForAHat_NameTheGroupsItHides()
    {
        Read().PlayerBodygroupsFor(100).ShouldBe(
            new Dictionary<string, int> { ["hat"] = 1 });
    }

    /// <remarks>
    /// **Two groups on one item is the ordinary case, not an edge.** 306 shipped items set
    /// `headphones` and nearly all of those set `hat` as well — a Scout hat that leaves the headset
    /// showing is the defect this reproduces.
    /// </remarks>
    [Test]
    public void PlayerBodygroups_ForAnItemHidingTwoParts_NameBoth()
    {
        Read().PlayerBodygroupsFor(101).ShouldBe(
            new Dictionary<string, int> { ["hat"] = 1, ["headphones"] = 1 });
    }

    /// <remarks>
    /// **Inherited from a prefab, like every other visual.** Hundreds of cosmetics carry no visuals
    /// block of their own and take the whole thing from `hat` or a class prefab, so a reader that
    /// looked only at the item would find nothing for most of the 747.
    /// </remarks>
    [Test]
    public void PlayerBodygroups_AreInheritedFromAPrefab()
    {
        Read().PlayerBodygroupsFor(102).ShouldBe(
            new Dictionary<string, int> { ["hat"] = 1 });
    }

    /// <remarks>
    /// **The item's OWN entry wins over the prefab's for the same name**, which is `model_player`'s
    /// rule rather than `attached_models`': a bodygroup is a single state per name, so there is
    /// nothing to accumulate. An item that says `"hat" "0"` while its prefab says `"hat" "1"` is
    /// putting the part back.
    /// </remarks>
    [Test]
    public void PlayerBodygroups_WhenTheItemAndItsPrefabDisagree_TakeTheItems()
    {
        Read().PlayerBodygroupsFor(103).ShouldBe(
            new Dictionary<string, int> { ["hat"] = 0 });
    }

    /// <remarks>
    /// The control for the file: an item with no visuals hides nothing, so a reader leaking another
    /// item's groups would be caught here rather than by a headless scout.
    /// </remarks>
    [Test]
    public void PlayerBodygroups_ForAnItemThatDeclaresNone_AreEmpty()
    {
        Read().PlayerBodygroupsFor(999).ShouldBeEmpty();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "hat"
                {
                    "item_slot" "head"
                    "visuals"
                    {
                        "player_bodygroups"
                        {
                            "hat" "1"
                        }
                    }
                }
            }
            "items"
            {
                "100"
                {
                    "name" "a plain hat"
                    "visuals"
                    {
                        "player_bodygroups"
                        {
                            "hat" "1"
                        }
                    }
                }
                "101"
                {
                    "name" "a hat with headphones"
                    "visuals"
                    {
                        "player_bodygroups"
                        {
                            "hat" "1"
                            "headphones" "1"
                        }
                    }
                }
                "102"
                {
                    "name" "inherits everything"
                    "prefab" "hat"
                }
                "103"
                {
                    "name" "puts the hat back"
                    "prefab" "hat"
                    "visuals"
                    {
                        "player_bodygroups"
                        {
                            "hat" "0"
                        }
                    }
                }
                "999"
                {
                    "name" "hides nothing"
                }
            }
        }
        """;
}
