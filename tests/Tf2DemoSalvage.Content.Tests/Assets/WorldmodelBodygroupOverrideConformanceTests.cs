using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item that sets a body part by NUMBER — <c>wm_bodygroup_override</c> (B353).
/// </summary>
/// <remarks>
/// **The last arm of `CEconEntity::UpdateBodygroups`, and the only one that does not use a name**
/// (<c>econ_entity.cpp:2083</c>):
///
/// <code>
///   int iBodyOverride = pItemDef->GetWorldmodelBodygroupOverride( pOwner->GetTeamNumber() );
///   int iBodyStateOverride = pItemDef->GetWorldmodelBodygroupStateOverride( pOwner->GetTeamNumber() );
///   if ( iBodyOverride > -1 &amp;&amp; iBodyStateOverride > -1 )
///       pOwner->SetBodygroup( iBodyOverride, iBodyStateOverride );
/// </code>
///
/// **Exactly two shipped items declare it** — 524, The Purity Fist (`wm_bodygroup_override 1`,
/// state 2) and 528, The Short Circuit (`wm_bodygroup_override 2`, state 2). Both replace a hand
/// with a robot arm, so the part being switched is the wearer's arm rather than a cosmetic slot.
///
/// **BOTH keys are required, and the default is -1** (<c>econ_item_schema.h:1065</c>). That matters
/// more than it looks: a reader defaulting them to 0 satisfies `> -1` for every item in the schema
/// and sets part 0 to 0 on every player — and part 0 is `hat` on several class models, so the
/// mistake would silently put every hidden hair back.
/// </remarks>
public sealed class WorldmodelBodygroupOverrideConformanceTests
{
    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringBoth_IsThePartAndTheState()
    {
        Read().WorldmodelBodygroupOverrideFor(524).ShouldBe((1, 2));
    }

    /// <remarks>
    /// The control, and the one that catches a zero default. An item with no override must not set
    /// part 0 to 0 — which is a real change on a model whose part 0 is `hat`.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringNeither_IsMinusOneForBoth()
    {
        Read().WorldmodelBodygroupOverrideFor(999).ShouldBe((-1, -1));
    }

    /// <remarks>
    /// **Half a declaration is not a declaration**: the engine's guard is `iBodyOverride > -1 &amp;&amp;
    /// iBodyStateOverride > -1`, so an item naming the part without a state does nothing. Reading
    /// the missing half as 0 would set that part to its first alternative instead.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringOnlyThePart_LeavesTheStateAtMinusOne()
    {
        Read().WorldmodelBodygroupOverrideFor(600).ShouldBe((3, -1));
    }

    /// <remarks>
    /// **The viewmodel pair is deliberately NOT what this reads.** `vm_bodygroup_override` sets a
    /// part on the player's own view model (<c>econ_entity.cpp:2091</c>), which a demo viewer
    /// drawing another player never has — and the Purity Fist declares both, with the same numbers,
    /// so a reader keyed to the wrong prefix passes every shipped case by accident.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringOnlyTheViewmodelPair_IsMinusOneForBoth()
    {
        Read().WorldmodelBodygroupOverrideFor(601).ShouldBe((-1, -1));
    }

    /// <remarks>
    /// Inherited like every other visual, because hundreds of items carry no `visuals` block of
    /// their own.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_IsInheritedFromAPrefab()
    {
        Read().WorldmodelBodygroupOverrideFor(602).ShouldBe((5, 1));
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "robot_arm"
                {
                    "visuals"
                    {
                        "wm_bodygroup_override" "5"
                        "wm_bodygroup_state_override" "1"
                    }
                }
            }
            "items"
            {
                "524"
                {
                    "name" "The Purity Fist"
                    "visuals"
                    {
                        "wm_bodygroup_override" "1"
                        "wm_bodygroup_state_override" "2"
                        "vm_bodygroup_override" "1"
                        "vm_bodygroup_state_override" "2"
                    }
                }
                "600"
                {
                    "name" "half a declaration"
                    "visuals"
                    {
                        "wm_bodygroup_override" "3"
                    }
                }
                "601"
                {
                    "name" "the viewmodel only"
                    "visuals"
                    {
                        "vm_bodygroup_override" "4"
                        "vm_bodygroup_state_override" "1"
                    }
                }
                "602"
                {
                    "name" "inherits the arm"
                    "prefab" "robot_arm"
                }
                "999"
                {
                    "name" "declares nothing"
                }
            }
        }
        """;
}
