using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item's replacement for one of its weapon's sounds — <c>CEconItemDefinition::GetWeaponReplacementSound</c> (B415).
/// </summary>
/// <remarks>
/// **Where an explosion's sound comes from when an item changes it.** `TFExplosionCallback` asks
/// <c>pItemDef->GetWeaponReplacementSound( local team, m_nSound )</c> (`tf_fx_explosions.cpp:141`), and the answer is
/// a `sound_&lt;category&gt;` key in the item's visuals (`econ_item_schema.cpp:2648`), the category being one of
/// `pWeaponSoundCategories` (`weapon_parse.cpp:20`) — `special1` is index 11. The team rule is
/// `GetBestVisualTeamData` (`econ_item_schema.h:2240`): a team with its own block uses that block ALONE, and every
/// other team — spectator included, whose section is null in `g_TeamVisualSections` — uses the base `visuals`.
/// </remarks>
public sealed class WeaponSoundReplacementConformanceTests
{
    private const int Red = 2;
    private const int Blu = 3;
    private const int Spectator = 1;
    private const int Single = 1;
    private const int Special1 = 11;

    /// <remarks>The Black Box's own line, which is what makes its rockets sound different when they land.</remarks>
    [Test]
    public void WeaponSoundReplacement_AnItemsSpecial1_IsTheReplacement()
    {
        Read().WeaponSoundReplacement(228, Red, Special1).ShouldBe("Weapon_RPG_BlackBox.Explode");
    }

    /// <remarks>
    /// One category replaced says nothing about another: an item that changes its firing sound keeps the weapon's
    /// explosion unless it names that too.
    /// </remarks>
    [Test]
    public void WeaponSoundReplacement_ACategoryTheItemDoesNotReplace_IsNothing()
    {
        ItemSchema schema = Read();

        schema.WeaponSoundReplacement(600, Red, Special1).ShouldBeNull();
        schema.WeaponSoundReplacement(600, Red, Single).ShouldBe("firing.only");
    }

    [Test]
    public void WeaponSoundReplacement_ATeamWithItsOwnBlock_UsesThatBlock()
    {
        Read().WeaponSoundReplacement(700, Red, Special1).ShouldBe("red.sound");
    }

    [Test]
    public void WeaponSoundReplacement_ATeamWithoutItsOwnBlock_UsesTheBase()
    {
        Read().WeaponSoundReplacement(700, Blu, Special1).ShouldBe("base.sound");
    }

    /// <remarks>
    /// **The team block is used ALONE**: `GetBestVisualTeamData` picks the red block because it exists, and the red
    /// block has no `sound_special1`, so the answer is null — the base block's sound is not a fallback.
    /// </remarks>
    [Test]
    public void WeaponSoundReplacement_ATeamBlockWithoutTheSound_DoesNotFallBackToTheBase()
    {
        Read().WeaponSoundReplacement(701, Red, Special1).ShouldBeNull();
    }

    /// <remarks>What a SourceTV recording's local player is, and why an STV demo hears the base block.</remarks>
    [Test]
    public void WeaponSoundReplacement_ASpectator_UsesTheBase()
    {
        Read().WeaponSoundReplacement(700, Spectator, Special1).ShouldBe("base.sound");
    }

    [Test]
    public void WeaponSoundReplacement_IsInheritedFromAPrefab()
    {
        Read().WeaponSoundReplacement(702, Red, Special1).ShouldBe("prefab.sound");
    }

    [Test]
    public void WeaponSoundReplacement_AnUnknownItemOrASoundOutOfRange_IsNothing()
    {
        ItemSchema schema = Read();

        schema.WeaponSoundReplacement(65535, Red, Special1).ShouldBeNull();
        schema.WeaponSoundReplacement(228, Red, 16).ShouldBeNull();
        schema.WeaponSoundReplacement(228, Red, -1).ShouldBeNull();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "loud_rockets"
                {
                    "visuals"
                    {
                        "sound_special1" "prefab.sound"
                    }
                }
            }
            "items"
            {
                "228"
                {
                    "name" "The Black Box"
                    "visuals"
                    {
                        "sound_single_shot" "Weapon_RPG_BlackBox.Single"
                        "sound_special1" "Weapon_RPG_BlackBox.Explode"
                    }
                }
                "600"
                {
                    "name" "replaces its firing sound only"
                    "visuals"
                    {
                        "sound_single_shot" "firing.only"
                    }
                }
                "700"
                {
                    "name" "a red block"
                    "visuals"
                    {
                        "sound_special1" "base.sound"
                    }
                    "visuals_red"
                    {
                        "sound_special1" "red.sound"
                    }
                }
                "701"
                {
                    "name" "a red block without the sound"
                    "visuals"
                    {
                        "sound_special1" "base.sound"
                    }
                    "visuals_red"
                    {
                        "sound_single_shot" "red.single"
                    }
                }
                "702"
                {
                    "name" "inherits the sound"
                    "prefab" "loud_rockets"
                }
            }
        }
        """;
}
