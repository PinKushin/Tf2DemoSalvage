using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item's `animation_replacement` — `CEconItemDefinition::GetActivityOverride( iTeam, baseAct )`
/// (`econ_item_schema.cpp:3567-3595`), which `TranslateActivity` asks after the weapon's table (B437).
/// </summary>
/// <remarks>
/// **The block is the best one for the team** — `GetNumAnimations` and `GetAnimationData` both go through
/// `GetBestVisualTeamData` (`econ_item_schema.h:1831-1854`, `:2240-2253`): a team's own `visuals_red`/`visuals_blu`
/// alone when declared, the base `visuals` otherwise. A prefab's block is inherited as every other visual is.
/// </remarks>
public sealed class ItemActivityReplacementTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "reloads_differently"
                {
                    "visuals" { "animation_replacement" { "ACT_MP_RELOAD_STAND_PRIMARY" "ACT_MP_RELOAD_STAND_PRIMARY3" } }
                }
            }
            "items"
            {
                "1"
                {
                    "visuals"
                    {
                        "animation_replacement"
                        {
                            "ACT_MP_ATTACK_STAND_PRIMARY" "ACT_MP_ATTACK_STAND_PRIMARY_ALT"
                        }
                        "animation_sequence" { "ACT_MP_RUN_PRIMARY" "not_a_replacement" }
                    }
                    "visuals_red"
                    {
                        "animation_replacement" { "ACT_MP_RELOAD_STAND_PRIMARY" "ACT_MP_RELOAD_STAND_PRIMARY_2" }
                    }
                }
                "2" { "prefab" "reloads_differently" }
                "3" { "item_slot" "primary" }
            }
        }
        """;

    [Test]
    public void ActivityOverride_TheBestBlockForTheTeam_ReplacesOnlyItsOwnActivities()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

        // Blue has no block of its own, so the base one answers; red has one, and it alone answers.
        schema.ActivityOverride(1, 3, "ACT_MP_ATTACK_STAND_PRIMARY").ShouldBe("ACT_MP_ATTACK_STAND_PRIMARY_ALT");
        schema.ActivityOverride(1, 2, "ACT_MP_ATTACK_STAND_PRIMARY").ShouldBe("ACT_MP_ATTACK_STAND_PRIMARY", "red's block hides the base");
        schema.ActivityOverride(1, 2, "ACT_MP_RELOAD_STAND_PRIMARY").ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY_2");
        schema.ActivityOverride(1, 3, "ACT_MP_RUN_PRIMARY").ShouldBe("ACT_MP_RUN_PRIMARY", "a sequence is not a replacement");

        schema.ActivityOverride(2, 2, "ACT_MP_RELOAD_STAND_PRIMARY").ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY3", "from the prefab");
        schema.ActivityOverride(3, 2, "ACT_MP_RELOAD_STAND_PRIMARY").ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY", "the control: none");
        schema.ActivityOverride(99, 2, "ACT_MP_RELOAD_STAND_PRIMARY").ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY", "unknown item");
    }
}
