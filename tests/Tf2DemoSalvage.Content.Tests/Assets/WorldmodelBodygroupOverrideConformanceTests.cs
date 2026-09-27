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
/// **BOTH keys are required, and a visuals block defaults each to -1** (`perteamvisuals_t()`,
/// <c>econ_item_schema.h:1067-1068</c>). **An item with no visuals block at all reads 0 for both**
/// (<c>econ_item_schema.h:2166-2167, 2181-2182</c>: `!GetPerTeamVisual(iTeam)` returns 0), which passes
/// the guard and sets part 0 to 0. That reverses this file's earlier -1 for such an item, which read
/// the block's default as the item's.
/// </remarks>
public sealed class WorldmodelBodygroupOverrideConformanceTests
{
    private const int Red = 2;
    private const int Blu = 3;

    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringBoth_IsThePartAndTheState()
    {
        Read().WorldmodelBodygroupOverrideFor(524, Red).ShouldBe((1, 2));
    }

    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemWithNoVisualsBlock_IsZeroForBoth()
    {
        Read().WorldmodelBodygroupOverrideFor(999, Red).ShouldBe((0, 0));
        Read().WorldmodelBodygroupOverrideFor(12345, Red).ShouldBe((0, 0), "an unknown index is the default item, which has no visuals");
    }

    [Test]
    public void WorldmodelBodygroupOverride_ForAVisualsBlockDeclaringNeither_IsMinusOneForBoth()
    {
        Read().WorldmodelBodygroupOverrideFor(603, Red).ShouldBe((-1, -1));
    }

    /// <remarks>`GetBestVisualTeamData( iTeam )` (econ_item_schema.h:2240): a team's own block, alone, else the base.</remarks>
    [Test]
    public void WorldmodelBodygroupOverride_ATeamBlock_AnswersForThatTeamAlone()
    {
        Read().WorldmodelBodygroupOverrideFor(604, Blu).ShouldBe((7, 3));
        Read().WorldmodelBodygroupOverrideFor(604, Red).ShouldBe((1, 1));
    }

    /// <remarks>
    /// **Half a declaration is not a declaration**: the engine's guard is `iBodyOverride > -1 &amp;&amp;
    /// iBodyStateOverride > -1`, so an item naming the part without a state does nothing. Reading
    /// the missing half as 0 would set that part to its first alternative instead.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_ForAnItemDeclaringOnlyThePart_LeavesTheStateAtMinusOne()
    {
        Read().WorldmodelBodygroupOverrideFor(600, Red).ShouldBe((3, -1));
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
        Read().WorldmodelBodygroupOverrideFor(601, Red).ShouldBe((-1, -1));
    }

    /// <remarks>
    /// Inherited like every other visual, because hundreds of items carry no `visuals` block of
    /// their own.
    /// </remarks>
    [Test]
    public void WorldmodelBodygroupOverride_IsInheritedFromAPrefab()
    {
        Read().WorldmodelBodygroupOverrideFor(602, Red).ShouldBe((5, 1));
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
                "603"
                {
                    "name" "a block without the keys"
                    "visuals" { "skin" "1" }
                }
                "604"
                {
                    "name" "per team"
                    "visuals" { "wm_bodygroup_override" "1" "wm_bodygroup_state_override" "1" }
                    "visuals_blu" { "wm_bodygroup_override" "7" "wm_bodygroup_state_override" "3" }
                }
                "999"
                {
                    "name" "declares nothing"
                }
            }
        }
        """;
}
