using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>What TF2's shipped soundscripts actually contain, before a reader is written for them.</summary>
[Explicit("Scans the game's script files; run deliberately.")]
public sealed class SoundScriptProbe
{
    [Test]
    public void SoundScripts_TheirKeysAndShape_AreCounted()
    {
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        List<string> scripts = [.. archive.Paths
            .Where(p => p.Contains("GAME_SOUNDS", StringComparison.OrdinalIgnoreCase)
                     || p.Contains("game_sounds", StringComparison.OrdinalIgnoreCase))];

        TestContext.Out.WriteLine($"soundscript files: {scripts.Count}");

        foreach (string path in scripts.Take(12))
        {
            TestContext.Out.WriteLine($"  {path}");
        }

        // The manifest names which of them the game loads.
        string? manifest = scripts.FirstOrDefault(
            p => p.Contains("MANIFEST", StringComparison.OrdinalIgnoreCase));

        if (manifest is not null && archive.ReadFile(manifest) is { } bytes)
        {
            TestContext.Out.WriteLine($"--- {manifest} ---");
            TestContext.Out.WriteLine(
                System.Text.Encoding.UTF8.GetString(bytes)[..Math.Min(3000, bytes.Length)]);
        }

        // And one real script, so the syntax is read rather than assumed.
        string? weapons = scripts.FirstOrDefault(
            p => p.Contains("WEAPON", StringComparison.OrdinalIgnoreCase));

        if (weapons is not null && archive.ReadFile(weapons) is { } weaponBytes)
        {
            TestContext.Out.WriteLine($"--- {weapons}, past the comment header ---");

            string[] lines = System.Text.Encoding.UTF8.GetString(weaponBytes)
                .Split('\n', StringSplitOptions.None);

            foreach (string line in lines.Skip(55).Take(46))
            {
                TestContext.Out.WriteLine(line.TrimEnd());
            }
        }

        scripts.Count.ShouldBeGreaterThan(0, "no soundscript was found, so nothing was measured");
    }
}
