using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What `CEconEntity::UpdateBodygroups` (econ_entity.cpp:2024-2088) reads of an item for a world player.</summary>
public sealed class GameAppearanceBodygroupConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "items"
            {
                "604"
                {
                    "visuals" { "player_bodygroups" { "hat" "1" } "wm_bodygroup_override" "1" "wm_bodygroup_state_override" "1" }
                    "visuals_blu" { "player_bodygroups" { "headphones" "1" } "wm_bodygroup_override" "7" "wm_bodygroup_state_override" "3" }
                }
            }
        }
        """;

    [Test]
    public void BodygroupsOf_ABluWearer_ReadsTheBaseBlocksGroupsAndTheBlueOverride()
    {
        GameAppearance appearance = new(null, null, ItemSchema.Read(Encoding.UTF8.GetBytes(Schema)));

        ItemBodygroups blue = appearance.BodygroupsOf(604, 3);

        // `GetModifiedBodyGroup( 0, ... )` (:2045): the base block only, whatever the team.
        blue.Named.ShouldBe(new Dictionary<string, int> { ["hat"] = 1 }, ignoreOrder: true);
        (blue.OverrideGroup, blue.OverrideState).ShouldBe((7, 3), "GetWorldmodelBodygroupOverride( GetTeamNumber() ) (:2083)");
        (appearance.BodygroupsOf(604, 2).OverrideGroup, appearance.BodygroupsOf(604, 2).OverrideState).ShouldBe((1, 1));
    }
}
