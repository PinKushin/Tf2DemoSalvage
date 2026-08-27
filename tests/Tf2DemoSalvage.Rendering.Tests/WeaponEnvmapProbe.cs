using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What a first-person WEAPON material declares about reflection, against what the ARMS declare.
/// </summary>
/// <remarks>
/// **The differential B170 turned on, read from the game's own shipped files.** The owner, having
/// been given `mat_phong` to test a different theory with: *"toggling phong does nothing, but
/// toggling reflections actually makes the weapon look right too"*, and then *"it is only the
/// weapons too, not the arms or hands"*.
///
/// Arms and weapon are drawn by the same pass, at the same position, with the same ambient cube and
/// the same sun — established by reading, and recorded under B170. So whatever separates them is in
/// their MATERIALS, and this reads both rather than reasoning about them.
///
/// **Shipped data is a source** (`docs/memory/shipped-data-is-a-source.md`). These VMTs are not
/// code, which is why nobody looks, and they answer the question outright.
/// </remarks>
public sealed class WeaponEnvmapProbe
{
    /// <summary>Weapon models the f12 parity demo actually loads, and the arms beside them.</summary>
    /// <remarks>
    /// **Taken from a real run's log rather than invented**, which matters: a probe pointed at a
    /// weapon the demo never draws would report on a material nobody has seen.
    /// </remarks>
    private static readonly string[] Weapons =
    [
        "c_scattergun",
        "c_shotgun",
        "c_pickaxe",
    ];

    private static readonly string[] Arms =
    [
        "c_scout_arms",
        "c_soldier_arms",
    ];

    [Test]
    [Explicit("A probe: reports what weapon and arms materials declare about reflection.")]
    public void WeaponMaterials_AgainstTheArmsBesideThem_AreReported()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);

        foreach (string name in Weapons.Concat(Arms))
        {
            if (Find(archives, name) is not { } found)
            {
                TestContext.Out.WriteLine($"{name}: no .vmt resolved");
                continue;
            }

            TestContext.Out.WriteLine($"=== {found.Path}");

            foreach (string line in Interesting(found.Text))
            {
                TestContext.Out.WriteLine($"    {line}");
            }
        }
    }

    /// <summary>The first path that resolves for a model name, with its text.</summary>
    /// <remarks>
    /// **Several folders, because TF2 does not put them all in one.** A weapon lives under
    /// `c_items` or under its own folder, and guessing a single path would report "no material"
    /// for a file that is present — an absence claim that is really a fact about the guess
    /// (`docs/memory/an-empty-search-needs-a-control.md`).
    /// </remarks>
    private static (string Path, string Text)? Find(GameArchives archives, string name)
    {
        string[] candidates =
        [
            $"materials/models/weapons/c_items/{name}.vmt",
            $"materials/models/weapons/v_models/{name}.vmt",
            $"materials/models/weapons/c_models/{name}/{name}.vmt",
            $"materials/models/weapons/c_models/{name}.vmt",
        ];

        foreach (string path in candidates)
        {
            if (archives.Read(path) is { } bytes)
            {
                return (path, Encoding.UTF8.GetString(bytes));
            }
        }

        return null;
    }

    /// <summary>The lines that decide how much reflection a material adds.</summary>
    private static IEnumerable<string> Interesting(string vmt)
    {
        string[] wanted =
        [
            "$envmap", "$envmapmask", "$envmaptint", "$envmapcontrast", "$envmapsaturation",
            "$basealphaenvmapmask", "$normalmapalphaenvmapmask", "$basemapalphaenvmapmask",
            "$phong", "$phongboost", "$bumpmap", "shader",
        ];

        foreach (string raw in vmt.Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // **Matched by CONTAINS, not by prefix, because a VMT line begins with a QUOTE.** The
            // real form is `"$envmap" "env_cubemap"`, so a prefix test on `$envmap` matches nothing
            // — which this probe did on its first run and reported as "the weapons declare no
            // reflection parameters", the exact opposite of the truth.
            if (line.Contains("Generic", StringComparison.OrdinalIgnoreCase) ||
                wanted.Any(key => line.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                yield return line;
            }
        }
    }
}
