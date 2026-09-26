using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary><see cref="WeaponScript.Value"/>: a key of the script's top block, read as KeyValues reads it.</summary>
/// <remarks>
/// `tf_weapon_rocketlauncher` writes `clip_size 4` bare, as KeyValues allows; a key that only a nested block declares is
/// not the top block's.
/// </remarks>
public sealed class WeaponScriptValueTests
{
    private const string Script = """
        WeaponData
        {
            "primary_ammo"  "TF_AMMO_PRIMARY"
            clip_size       4
            SoundData
            {
                "single_shot"   "Weapon_RPG.Single"
            }
        }
        """;

    [TestCase("clip_size", "4")]
    [TestCase("CLIP_SIZE", "4")]
    [TestCase("primary_ammo", "TF_AMMO_PRIMARY")]
    [TestCase("single_shot", null)]
    [TestCase("SoundData", null)]
    public void Value_TheTopBlocksKey_EvenWrittenBare(string key, string? expected)
    {
        WeaponScript script = WeaponScript.Read(path => path == "scripts/test.txt" ? Encoding.UTF8.GetBytes(Script) : null, "test")!;

        script.Value(key).ShouldBe(expected);
    }
}
