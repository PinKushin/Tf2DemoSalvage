using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An item's muzzle flash override and its weapon class — `CEconItemDefinition::GetMuzzleFlash( team )`
/// (`econ_item_schema.h:1982`) and `item_class` (B415).
/// </summary>
/// <remarks>
/// `CTFWeaponBase::GetMuzzleFlashParticleEffect` takes the weapon script's `MuzzleFlashParticleEffect` unless the item
/// names a `muzzle_flash` (`econ_item_schema.cpp:2623`), chosen by `GetBestVisualTeamData` as a sound is.
/// </remarks>
public sealed class ItemMuzzleFlashConformanceTests
{
    private const int Red = 2;
    private const int Blu = 3;

    [Test]
    public void MuzzleFlash_AnItemsOwn_IsTheOverride()
    {
        Read().MuzzleFlash(800, Red).ShouldBe("muzzle_base");
    }

    [Test]
    public void MuzzleFlash_ATeamWithItsOwnBlock_UsesThatBlockAlone()
    {
        ItemSchema schema = Read();

        schema.MuzzleFlash(801, Red).ShouldBe("muzzle_red");
        schema.MuzzleFlash(801, Blu).ShouldBe("muzzle_base");
        schema.MuzzleFlash(802, Red).ShouldBeNull("the red block exists and names none");
    }

    [Test]
    public void MuzzleFlash_ANoneOrAnUnknownItem_IsNothing()
    {
        ItemSchema schema = Read();

        schema.MuzzleFlash(803, Red).ShouldBeNull();
        schema.MuzzleFlash(65535, Red).ShouldBeNull();
    }

    [Test]
    public void ItemClass_IsInheritedFromAPrefab()
    {
        Read().ItemClass(803).ShouldBe("tf_weapon_scattergun");
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "a_scattergun"
                {
                    "item_class" "tf_weapon_scattergun"
                }
            }
            "items"
            {
                "800"
                {
                    "visuals"
                    {
                        "muzzle_flash" "muzzle_base"
                    }
                }
                "801"
                {
                    "visuals"
                    {
                        "muzzle_flash" "muzzle_base"
                    }
                    "visuals_red"
                    {
                        "muzzle_flash" "muzzle_red"
                    }
                }
                "802"
                {
                    "visuals"
                    {
                        "muzzle_flash" "muzzle_base"
                    }
                    "visuals_red"
                    {
                        "sound_single_shot" "red.single"
                    }
                }
                "803"
                {
                    "prefab" "a_scattergun"
                }
            }
        }
        """;
}
