using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The weapon rules the ammo HUD reads: `Precache`, `GetMaxClip1`, `GetMaxAmmo`, `UsesPrimaryAmmo`.</summary>
/// <remarks>
/// Scout's `AmmoMax` is 32 primary, 36 secondary; the scattergun's script has `clip_size 6` (bare, as shipped scripts
/// write it) and primary ammo. Item 1 multiplies `mult_clipsize` by 1.5, item 2 turns on `mod_use_metal_ammo_type`, item 3
/// halves `mult_maxammo_primary`, item 4 adds two `mult_clipsize_upgrade_atomic` projectiles.
/// </remarks>
public sealed class TfAmmoConformanceTests
{
    private const string ItemsGame = """
        "items_game"
        {
            "attributes"
            {
                "1" { "name" "clip" "attribute_class" "mult_clipsize" "description_format" "value_is_percentage" }
                "2" { "name" "metal" "attribute_class" "mod_use_metal_ammo_type" "description_format" "value_is_additive" }
                "3" { "name" "maxammo" "attribute_class" "mult_maxammo_primary" "description_format" "value_is_percentage" }
                "4" { "name" "atomic" "attribute_class" "mult_clipsize_upgrade_atomic" "description_format" "value_is_additive" }
            }
            "items"
            {
                "1" { "static_attrs" { "clip" "1.5" } }
                "2" { "static_attrs" { "metal" "1" } }
                "3" { "static_attrs" { "maxammo" "0.5" } }
                "4" { "static_attrs" { "atomic" "2" } }
            }
        }
        """;

    private static readonly Dictionary<string, string> Files = new()
    {
        ["scripts/tf_weapon_scattergun.txt"] = "WeaponData { \"primary_ammo\" \"TF_AMMO_PRIMARY\" clip_size 6 }",
        ["scripts/tf_weapon_rocketlauncher.txt"] = "WeaponData { \"primary_ammo\" \"TF_AMMO_PRIMARY\" clip_size 4 }",
        ["scripts/tf_weapon_medigun.txt"] = "WeaponData { \"primary_ammo\" \"None\" }",
        ["scripts/tf_weapon_raygun.txt"] = "WeaponData { \"primary_ammo\" \"TF_AMMO_PRIMARY\" clip_size 4 }",
        ["scripts/playerclasses/scout.txt"] = "PlayerClass { AmmoMax { \"TF_AMMO_PRIMARY\" 32 \"TF_AMMO_SECONDARY\" 36 \"TF_AMMO_METAL\" 100 } }",
    };

    private static readonly AttributeHooks Hooks = new(ItemSchema.Read(Encoding.UTF8.GetBytes(ItemsGame)));

    [Test]
    public void For_AStockScattergun_IsItsClipAndReserveAgainstTheScriptAndClass()
    {
        TfAmmoState ammo = TfAmmo.For(Scout("CTFScatterGun", null), Scripts(), Hooks);

        ammo.ShouldBe(new TfAmmoState(true, true, true, true, 4, 20, 32, 6));
    }

    [Test]
    public void For_ClipSizeBonus_MultipliesTheScriptClip() =>
        TfAmmo.For(Scout("CTFScatterGun", 1), Scripts(), Hooks).MaxClip1.ShouldBe(9);

    [Test]
    public void For_ABlastWeapon_AddsAtomicProjectilesAfterTheMultiplier() =>
        TfAmmo.For(Scout("CTFRocketLauncher", 4), Scripts(), Hooks).MaxClip1.ShouldBe(6);

    [Test]
    public void For_MaxAmmoAttribute_HooksOnThePlayer() =>
        TfAmmo.For(Scout("CTFScatterGun", 3), Scripts(), Hooks).MaxAmmo.ShouldBe(16);

    [Test]
    public void For_TheMetalOverride_HidesTheCount()
    {
        TfAmmoState ammo = TfAmmo.For(Scout("CTFScatterGun", 2), Scripts(), Hooks);

        (ammo.Shown, ammo.Reserve).ShouldBe((false, 100), "metal is TF_AMMO_METAL's count");
    }

    [Test]
    public void For_TheMedigun_IsNotShown() => TfAmmo.For(Scout("CWeaponMedigun", null), Scripts(), Hooks).Shown.ShouldBeFalse();

    [Test]
    public void For_AnEnergyWeapon_UsesNoPrimaryAmmo() =>
        TfAmmo.For(Scout("CTFRaygun", null), Scripts(), Hooks).UsesPrimaryAmmo.ShouldBeFalse();

    [Test]
    public void For_NoActiveWeapon_IsNothing() =>
        TfAmmo.For(Scout("CTFScatterGun", null) with { ActiveWeapon = 99 }, Scripts(), Hooks).ShouldBe(default);

    private static TfWeaponData Scripts() => new(path => Files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null);

    private static ScenePlayer Scout(string weapon, int? definition) =>
        new(1, 0f, 0f, 0f, 2, 125, 1, ActiveWeapon: 5)
        {
            WeaponClip1 = 4,
            Ammo = [0, 20, 30, 100, 0, 0, 0],
            Items = [new SceneItem(5, weapon, definition, new EconAttributeWire([], [], false), IsWeapon: true)],
        };
}
