using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item definition's own attached particle systems, per team, and a style's smoke switch (B69 a, d).
/// </summary>
/// <remarks>
/// <c>CEconItemDefinition::BInitVisualBlockFromKV</c> (<c>econ_item_schema.cpp:2463-2534</c>): per visuals block,
/// <c>use_visualsblock_as_base</c> starts the block as a copy of the named one; each child of <c>attached_particlesystems</c> adds
/// <c>{ system, attachment → pszControlPoints[0], attach_to_rootbone, iCustomType 0 }</c>; <c>custom_particlesystem</c> and
/// <c>custom_particlesystem2</c> add one with <c>iCustomType</c> 1 and 2. <c>GetNumAttachedParticles( iTeam )</c> reads the
/// block <c>GetBestVisualTeamData</c> picks (<c>econ_item_schema.h:1931-1960</c>). A style's
/// <c>use_smoke_particle_effect</c> is <c>GetBool( …, true )</c> (<c>:2867</c>).
/// </remarks>
public sealed class AttachedParticleConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "notes_prefab"
                {
                    "visuals"
                    {
                        "attached_particlesystems"
                        {
                            "0" { "system" "inherited_zero" "attachment" "ear_R" }
                            "1" { "system" "inherited_one" "attachment" "ear_L" }
                        }
                    }
                }
            }
            "items"
            {
                "100"
                {
                    "name" "Headphones"
                    "visuals"
                    {
                        "attached_particlesystems"
                        {
                            "0" { "system" "headphone_notes" "attachment" "ear_R" }
                            "1" { "system" "headphone_notes" "attachment" "ear_L" "attach_to_rootbone" "1" }
                        }
                        "custom_particlesystem" { "system" "medicgun_beam" }
                    }
                }
                "101"
                {
                    "name" "Inherits"
                    "prefab" "notes_prefab"
                    "visuals"
                    {
                        "attached_particlesystems"
                        {
                            "1" { "system" "own_one" "attachment" "head" }
                        }
                    }
                }
                "102"
                {
                    "name" "Team copy"
                    "visuals"
                    {
                        "attached_particlesystems"
                        {
                            "0" { "system" "base_glow" "attachment" "head" }
                        }
                    }
                    "visuals_red"
                    {
                        "use_visualsblock_as_base" "visuals"
                        "attached_particlesystems"
                        {
                            "0" { "system" "red_glow" "attachment" "head" }
                        }
                    }
                    "visuals_blu"
                    {
                        "skin" "1"
                    }
                }
                "103"
                {
                    "name" "Pipe"
                    "visuals"
                    {
                        "styles"
                        {
                            "0" { "name" "smoking" }
                            "1" { "name" "quiet" "use_smoke_particle_effect" "0" }
                        }
                    }
                }
            }
        }
        """;

    [Test]
    public void AttachedParticlesFor_AnItemDeclaringTwo_ReadsEachInOrderWithItsCustomType()
    {
        AttachedParticle[] particles = [.. Read().AttachedParticlesFor(100, 2)];

        particles.Select(particle => (particle.System, particle.Attachment, particle.FollowRootBone, particle.CustomType))
            .ShouldBe(
            [
                ("headphone_notes", "ear_R", false, 0),
                ("headphone_notes", "ear_L", true, 0),
                ("medicgun_beam", null, false, 1),
            ]);
    }

    /// <summary>The prefab's children merge with the item's by name, as <c>RecursiveInheritKeyValues</c> merges (<c>:2897</c>).</summary>
    [Test]
    public void AttachedParticlesFor_AnItemOverAPrefab_ReplacesTheSameNumberedChild()
    {
        Read().AttachedParticlesFor(101, null).Select(particle => particle.System).ShouldBe(["inherited_zero", "own_one"]);
    }

    /// <summary>
    /// <c>visuals_red</c> starts as a copy of <c>visuals</c> and appends its own; <c>visuals_blu</c> declares no particles,
    /// and it is the block BLU reads — <c>GetBestVisualTeamData</c> picks a declared team block whole.
    /// </summary>
    [Test]
    public void AttachedParticlesFor_ATeamBlockBuiltOnTheBase_HasTheBasesThenItsOwn()
    {
        ItemSchema schema = Read();

        schema.AttachedParticlesFor(102, 2).Select(particle => particle.System).ShouldBe(["base_glow", "red_glow"]);
        schema.AttachedParticlesFor(102, 3).ShouldBeEmpty();
        schema.AttachedParticlesFor(102, 0).Select(particle => particle.System).ShouldBe(["base_glow"]);
    }

    [TestCase(0, true, TestName = "StyleUsesSmokeParticleEffect_AStyleSayingNothing_IsTrue")]
    [TestCase(1, false, TestName = "StyleUsesSmokeParticleEffect_AStyleSayingZero_IsFalse")]
    public void StyleUsesSmokeParticleEffect_AStyle_IsItsFlagDefaultingTrue(int style, bool smokes)
    {
        Read().StyleUsesSmokeParticleEffect(103, style).ShouldBe(smokes);
    }

    /// <summary>No style info (<c>GetStyleInfo</c> null) is no answer: the engine's rule does not run.</summary>
    [Test]
    public void StyleUsesSmokeParticleEffect_NoStyle_IsNull()
    {
        ItemSchema schema = Read();

        schema.StyleUsesSmokeParticleEffect(103, null).ShouldBeNull();
        schema.StyleUsesSmokeParticleEffect(103, 5).ShouldBeNull();
        schema.StyleUsesSmokeParticleEffect(100, 0).ShouldBeNull();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
