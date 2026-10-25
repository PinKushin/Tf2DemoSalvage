using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFWeaponBase::CreateMuzzleFlashEffects` and what it reads (`tf_weaponbase.cpp:3113`) (B415).</summary>
public sealed class WeaponMuzzleFlashesConformanceTests
{
    private const int Red = 2;

    [Test]
    public void For_AScriptedWeapon_IsItsScriptsParticleAndModel()
    {
        WeaponMuzzleFlash flash = Resolver().For(200, Red);

        flash.Particle.ShouldBe("muzzle_scattergun");
        flash.Model.ShouldBe("models/effects/flash.mdl");
        flash.ModelLifetime.ShouldBe(0.1f);
        flash.Backblast.ShouldBeFalse();
    }

    [Test]
    public void For_AnItemsMuzzleFlash_ReplacesTheScripts()
    {
        // `GetMuzzleFlashParticleEffect`: the script's, unless the item names one.
        Resolver().For(201, Red).Particle.ShouldBe("muzzle_item");
    }

    [Test]
    public void For_ARocketLauncher_AlsoBlastsBack()
    {
        WeaponMuzzleFlash flash = Resolver().For(513, Red);

        flash.Particle.ShouldBeNull("its script names none");
        flash.Backblast.ShouldBeTrue();
        flash.ModelLifetime.ShouldBe(WeaponMuzzleFlashes.DefaultModelLifetime);
    }

    [Test]
    public void For_AnUnknownItem_IsNothing()
    {
        WeaponMuzzleFlash flash = Resolver().For(9999, Red);

        flash.Particle.ShouldBeNull();
        flash.Model.ShouldBeNull();
        flash.Backblast.ShouldBeFalse();
    }

    /// <remarks>
    /// `C_World::Precache` → `CPrecacheRegister::Precache` reads every registered weapon's script before play (B595 part 6).
    /// The manifest is TF2's shipped one, which names only the steadycam, so the schema's item classes must carry it.
    /// </remarks>
    [Test]
    public void Precache_ThenFor_ReadsNothingInPlayAndAnswersAsTheLazyRead()
    {
        List<string> reads = [];
        WeaponMuzzleFlashes precached = Resolver(reads);
        WeaponMuzzleFlash lazy = Resolver().For(200, Red);

        precached.Precache();
        reads.ShouldContain("scripts/tf_weapon_scattergun.txt", "the control: the precache read the script");
        reads.Clear();

        precached.For(200, Red).ShouldBe(lazy);
        precached.For(513, Red);

        reads.ShouldBeEmpty();
    }

    private static WeaponMuzzleFlashes Resolver(List<string>? reads = null)
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/weapon_manifest.txt"] = Encoding.UTF8.GetBytes(
                """
                weapon_manifest
                {
                    "file" "scripts/weapon_ifm_steadycam.txt"
                }
                """),
            ["scripts/tf_weapon_scattergun.txt"] = Encoding.UTF8.GetBytes(
                """
                WeaponData
                {
                    "MuzzleFlashParticleEffect" "muzzle_scattergun"
                    "MuzzleFlashModel" "models/effects/flash.mdl"
                    "MuzzleFlashModelDuration" "0.1"
                }
                """),
            ["scripts/tf_weapon_rocketlauncher.txt"] = Encoding.UTF8.GetBytes("WeaponData\n{\n}\n"),
        };

        ItemSchema items = ItemSchema.Read(Encoding.UTF8.GetBytes(
            """
            "items_game"
            {
                "items"
                {
                    "200" { "item_class" "tf_weapon_scattergun" }
                    "201"
                    {
                        "item_class" "tf_weapon_scattergun"
                        "visuals" { "muzzle_flash" "muzzle_item" }
                    }
                    "513" { "item_class" "tf_weapon_rocketlauncher" }
                }
            }
            """));

        return new WeaponMuzzleFlashes(path =>
        {
            reads?.Add(path);
            return files.GetValueOrDefault(path);
        }, items);
    }
}
