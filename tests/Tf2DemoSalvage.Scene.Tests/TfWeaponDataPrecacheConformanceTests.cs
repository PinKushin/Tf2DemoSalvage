using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// One weapon script is read once for every question asked of it, as `m_WeaponInfoDatabase` holds one parse per name
/// (`ReadWeaponDataFromFileForSlot` returns early on `bParsedScript`, weapon_parse.cpp:282).
/// </summary>
public sealed class TfWeaponDataPrecacheConformanceTests
{
    [Test]
    public void WeaponHud_AfterWeapon_ReadsTheScriptOnce()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/tf_weapon_rocketlauncher.txt"] = Encoding.UTF8.GetBytes("""
                WeaponData
                {
                    "clip_size" "4"
                    "primary_ammo" "TF_AMMO_PRIMARY"
                    "DrawCrosshair" "0"
                }
                """),
        };
        List<string> reads = [];
        TfWeaponData data = new(path =>
        {
            reads.Add(path);
            return files.GetValueOrDefault(path);
        });

        data.Weapon("CTFRocketLauncher", 3).ShouldBe((4, 1, true));
        data.WeaponHud("CTFRocketLauncher", 3).DrawCrosshair.ShouldBeFalse();

        reads.ShouldBe(["scripts/tf_weapon_rocketlauncher.txt"]);
    }
}
