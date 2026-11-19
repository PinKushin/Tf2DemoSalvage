using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconEntity::GetEconParticleSystems</c> (<c>econ_entity.cpp:1419-1505</c>): an item's particle systems, before any is
/// drawn (B69 b, c).
/// </summary>
/// <remarks>
/// In order: the definition's own for the entity's team (<c>GetNumAttachedParticles( GetTeamNumber() )</c>), the quality
/// sparkle, <c>set_attached_particle_static</c>, then <c>set_attached_particle</c> unless <c>throwable_particle_trail_only</c>,
/// its <c>_teamcolor_red</c>/<c>_teamcolor_blue</c> swapped to the entity's team and looked up again by name; finally any
/// entry with no system name is dropped. **The quality sparkle is never there in a demo**: <c>GetQualityParticleType</c> asks
/// <c>GetSOCData()</c> (<c>econ_item_view.cpp:1076-1088</c>), which a networked view answers from
/// <c>m_pNonSOEconItem</c> — never set on one — or the inventory of the item's account (<c>:839-857</c>), which a demo viewer
/// has for nobody it is watching; <c>m_iEntityQuality</c>, which the demo does carry, is not read.
/// </remarks>
public sealed class EconParticleSystemsConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "items"
            {
                "100"
                {
                    "name" "Headphones"
                    "visuals"
                    {
                        "attached_particlesystems" { "0" { "system" "headphone_notes" "attachment" "ear_R" } "1" { "attachment" "nothing" } }
                        "custom_particlesystem" { "system" "medicgun_beam" }
                    }
                }
            }
            "attribute_controlled_attached_particles"
            {
                "cosmetic_unusual_effects"
                {
                    "13" { "system" "superrare_burning1" "attachment" "bip_head" "control_point_2" "eyes" }
                    "30" { "system" "unusual_glow_teamcolor_red" "draw_in_viewmodel" "1" }
                    "31" { "system" "unusual_glow_teamcolor_blue" }
                    "40" { "system" "unusual_orphan_teamcolor_red" }
                    "50" { "system" "pipe_static" "use_suffix_name" "1" }
                }
            }
        }
        """;

    private const int Red = 2;
    private const int Blue = 3;

    [Test]
    public void Of_AnItemWithItsOwnSystemsAndBothAttributes_IsDefinitionThenStaticThenDynamic()
    {
        EconParticle[] systems = [.. EconParticleSystems.Of(Items(), 100, Red, staticEffect: 50, dynamicEffect: 13, throwableTrailOnly: false)];

        systems.Select(system => (system.System, system.CustomType, system.ControlPoints[0]))
            .ShouldBe(
            [
                ("headphone_notes", 0, "ear_R"),
                ("medicgun_beam", 1, (string?)null),
                ("pipe_static", 0, null),
                ("superrare_burning1", 0, "bip_head"),
            ],
            "the nameless definition entry is dropped (:1496-1504)");

        systems[2].UseSuffixName.ShouldBeTrue();
        systems[3].ControlPoints[2].ShouldBe("eyes");
    }

    [Test]
    public void Of_ADynamicEffectOnAThrowableTrail_IsLeftOut()
    {
        EconParticleSystems.Of(Items(), null, Red, staticEffect: 0, dynamicEffect: 13, throwableTrailOnly: true).ShouldBeEmpty();
    }

    [TestCase(Blue, 30, "unusual_glow_teamcolor_blue", false, TestName = "Of_ARedTeamColourOnBlu_IsSwappedToTheBlueSystem")]
    [TestCase(Red, 31, "unusual_glow_teamcolor_red", true, TestName = "Of_ABlueTeamColourOnRed_IsSwappedToTheRedSystem")]
    [TestCase(Red, 30, "unusual_glow_teamcolor_red", true, TestName = "Of_ARedTeamColourOnRed_IsKept")]
    public void Of_ATeamColouredEffect_IsLookedUpAgainForTheTeam(int team, int effect, string system, bool drawsInViewModel)
    {
        EconParticle swapped = EconParticleSystems.Of(Items(), null, team, staticEffect: 0, dynamicEffect: effect, throwableTrailOnly: false)
            .ShouldHaveSingleItem();

        (swapped.System, swapped.DrawInViewModel).ShouldBe((system, drawsInViewModel));
    }

    /// <summary>A swap to a name the schema does not declare finds nothing, and nothing is added (<c>:1487-1490</c>).</summary>
    [Test]
    public void Of_ATeamColourSwapToAMissingSystem_AddsNothing()
    {
        EconParticleSystems.Of(Items(), null, Blue, staticEffect: 0, dynamicEffect: 40, throwableTrailOnly: false).ShouldBeEmpty();
    }

    private static ItemSchema Items() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
