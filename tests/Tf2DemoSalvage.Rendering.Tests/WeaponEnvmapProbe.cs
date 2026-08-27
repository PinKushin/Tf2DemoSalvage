using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

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
        if (GameInstall.Root is not { } game)
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

        // **Every candidate that resolves, not the first.** Taking the first reported
        // `c_items/c_scattergun.vmt` — which exists and is NOT the material the renderer loads —
        // and so reported the scattergun as declaring no `$envmap` at all, the exact opposite of
        // the truth. A probe that stops at the first hit is asserting that the first hit is the
        // right one, which is a guess wearing a measurement's clothes.
        (string Path, string Text)? found = null;

        foreach (string path in candidates)
        {
            if (archives.Read(path) is not { } bytes)
            {
                continue;
            }

            string text = Encoding.UTF8.GetString(bytes);

            TestContext.Out.WriteLine($"--- resolved {path}");

            foreach (string line in Interesting(text))
            {
                TestContext.Out.WriteLine($"      {line}");
            }

            found ??= (path, text);
        }

        return found;
    }

    /// <summary>
    /// What the RENDERER resolved for each weapon and arms material, not what a guessed path says.
    /// </summary>
    /// <remarks>
    /// **The probe above guesses `.vmt` paths and that is a weakness, not a detail.** It resolved
    /// nothing at all for `c_scout_arms` and `c_soldier_arms`, which is proof its candidate list is
    /// incomplete — and a list incomplete for the arms may equally have read the WRONG file for a
    /// weapon. An absence it reports is a fact about the guess
    /// (`docs/memory/an-empty-search-needs-a-control.md`).
    ///
    /// This asks `MapAssets` instead, which is the same load the viewer performs, so a material
    /// named here is a material the renderer actually uses and `LocalReflections[i]` is exactly the
    /// value the shader is handed. That closes the contradiction the VMT probe left open: two of
    /// the three weapons appeared to declare no `$envmap` at all, while the owner reports that
    /// toggling reflections fixes the weapon.
    /// </remarks>
    [Test]
    [Explicit("A probe: reports the reflection each weapon material resolved to.")]
    public void WeaponMaterials_AsTheRendererResolvesThem_AreReported()
    {
        if (GameInstall.Root is not { } game || game.Length == 0)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        MapAssets assets = MapCache.Load(entityModels:
        [
            "models/weapons/c_models/c_scattergun.mdl",
            "models/weapons/c_models/c_shotgun/c_shotgun.mdl",
            "models/weapons/c_models/c_scout_arms.mdl",
            "models/weapons/c_models/c_soldier_arms.mdl",
        ]);

        for (int index = 0; index < assets.Materials.Count; index++)
        {
            string name = assets.Materials[index].Name;

            if (!name.Contains("weapon", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("arms", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MapEnvmapShading? local =
                index < assets.LocalReflections.Count ? assets.LocalReflections[index] : null;

            MapCubemap? baked = index < assets.Cubemaps.Count ? assets.Cubemaps[index] : null;
            MapPhong? phong = index < assets.Phong.Count ? assets.Phong[index] : null;

            TestContext.Out.WriteLine(
                $"{name}: local={(local is { } l ? $"tint {l.Tint}, baseMask {l.MaskedByBaseAlpha}, " +
                    $"normalMask {l.MaskedByNormalMapAlpha}, contrast {l.Contrast}" : "none")}" +
                $" | baked={(baked is null ? "none" : "yes")}" +
                $" | phong={(phong is { } p ? $"boost {p.Boost}" : "none")}");
        }

        // **What the tint is multiplying, which is the other half and the half nobody measured.** A
        // tint of 0.085 is only small against a sample near one. Source's HDR cubemaps are stored in
        // formats that carry values far above white, so the FORMAT decides whether these weapons
        // reflect at eight percent of white or at eight percent of something much larger.
        foreach (MapPlacedCubemap placed in assets.PlacedCubemaps.Take(3))
        {
            string formats = string.Join(
                ", ", placed.Faces.Select(face => face.Image.Format.ToString()).Distinct());

            TestContext.Out.WriteLine(
                $"PLACED CUBEMAP at ({placed.Placement.X}, {placed.Placement.Y}, {placed.Placement.Z}): " +
                $"{placed.Faces.Count} faces, formats {formats}");
        }

        TestContext.Out.WriteLine($"PLACED CUBEMAPS: {assets.PlacedCubemaps.Count} in total");
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
