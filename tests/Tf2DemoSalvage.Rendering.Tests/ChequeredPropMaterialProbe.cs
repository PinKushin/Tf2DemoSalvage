using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Where the materials of `cp_fulgur`'s chequered props actually live — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **B229's second half.** <see cref="PropMaterialResolutionTests"/> names 117 model materials that
/// resolve to no texture; this asks the next question, which no assertion can: for each of them, is
/// the VMT in the map's own pakfile, in the game's archives, or nowhere at all — and is it the VMT
/// or the VTF underneath it that is missing?
///
/// Those are three different defects with three different fixes, and the failure message cannot
/// tell them apart because `MapAssets` resolves prop materials with <c>report: false</c>.
///
/// Explicit, and it asserts nothing about the map: what a community map packs is a fact about the
/// map (D38).
/// </remarks>
[Explicit("Diagnostic: reports where a chequered prop's material lives, if anywhere.")]
public sealed class ChequeredPropMaterialProbe
{
    /// <summary>The map the owner saw the chequer on.</summary>
    private const string Fulgur = "cp_fulgur";

    /// <summary>One material name per failing model, spanning every folder that failed.</summary>
    private static readonly string[] Wanted =
    [
        "models/props_aquatic/pipes01",
        "models/props_industrial/pipe256d",
        "models/props_antiquity/skycards_jungle01",
        "Models/props_spytech/computer_screen_01",
        "models/props_spytech/computer_wall_1401b",
        "models/props_frontline/bunkerladder_2",
        "models/props_enclosure/waterfall001_alphatest",
    ];

    [Test]
    public void Resolve_TheChequeredPropMaterials_ReportsWhereEachOneLives()
    {
        PakFile pak = PakFile.ReadFrom(MapCache.Bytes(Fulgur));
        GameArchives game = GameArchives.Open(GameInstall.Root);

        TestContext.Out.WriteLine($"pakfile holds {pak.Count} files");

        foreach (string wanted in Wanted)
        {
            string vmt = "materials/" + wanted + ".vmt";

            bool inPak = pak.Contains(vmt);
            bool inGame = game.Read(vmt) is not null;

            TestContext.Out.WriteLine(
                $"{vmt}: pak={inPak}, game={inGame}");
        }

        // **Every pakfile entry whose name mentions one of the failing folders**, because a
        // near-miss is the interesting case: a VMT packed under a slightly different path is a
        // lookup defect, and no VMT at all is a different one entirely.
        string[] folders =
        [
            "props_aquatic", "props_industrial", "props_antiquity",
            "props_spytech", "props_frontline", "props_enclosure",
        ];

        foreach (string folder in folders)
        {
            List<string> packed =
            [
                .. pak.Paths
                    .Where(path => path.Contains(folder, StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Take(12),
            ];

            TestContext.Out.WriteLine(
                $"pak mentions {folder}: {packed.Count} shown of "
                + $"{pak.Paths.Count(path => path.Contains(folder, StringComparison.OrdinalIgnoreCase))}");

            foreach (string path in packed)
            {
                TestContext.Out.WriteLine($"    {path}");
            }
        }

        // **A precondition on the HARNESS, not a claim about the map.** A community map packs its
        // custom content, so an empty pakfile would mean this probe read the wrong lump and every
        // "not found" above would be a fact about the reader.
        pak.Count.ShouldBeGreaterThan(0, "the map's pakfile lump read as empty");
    }

    /// <summary>Models whose materials are only partly packed, and the packing is the clue.</summary>
    private static readonly string[] Suspects =
    [
        "models/props_antiquity/skycards_jungle256bump.mdl",
        "models/props_frontline/bunkerladder_medium.mdl",
        "models/props_industrial/pipe_large_256.mdl",
        "models/props_aquatic/pipe_256.mdl",
    ];

    [Test]
    public void Read_AChequeredModel_ReportsItsSkinTableAndTheSkinsItsPlacementsAsk()
    {
        // **The question a swap-from-family-zero design cannot ask itself.** The pakfile holds
        // `SKYCARDS_JUNGLE05.VMT` and `06` and none of `01`–`04` or `07`–`12`, which is what a map
        // author packing only the skins actually PLACED looks like. If that is right, family zero's
        // material is legitimately absent and the mesh must take the placement's own family — and
        // this project resolves family zero first, then expresses every other family as a swap
        // FROM it, so a −1 at family zero cannot be swapped out of.
        PakFile pak = PakFile.ReadFrom(MapCache.Bytes(Fulgur));
        GameArchives game = GameArchives.Open(GameInstall.Root);

        IReadOnlyList<BspStaticProp> placements = BspStaticProps.Read(MapCache.Bytes(Fulgur));

        int described = 0;

        foreach (string path in Suspects)
        {
            byte[]? file = pak.ReadFile(path) ?? game.Read(path);

            if (file is null)
            {
                TestContext.Out.WriteLine($"{path}: not found");
                continue;
            }

            described++;

            StudioModelInfo model = StudioModel.Read(file);
            short[] table = StudioSkins.Read(file);
            int families = StudioSkins.Families(file);
            int references = StudioSkins.References(file);

            TestContext.Out.WriteLine(
                $"{path}: {families} families x {references} references, "
                + $"{model.Materials.Count} textures, {model.Meshes.Count} meshes");

            TestContext.Out.WriteLine(
                "    folders: " + string.Join(", ", model.MaterialFolders.Select(f => $"'{f}'")));

            for (int index = 0; index < model.Materials.Count; index++)
            {
                bool packed = model.MaterialPaths(index)
                    .Any(candidate => pak.Contains("materials/" + candidate + ".vmt")
                        || game.Read("materials/" + candidate + ".vmt") is not null);

                TestContext.Out.WriteLine(
                    $"    texture[{index}] '{model.Materials[index]}' {(packed ? "SHIPPED" : "absent")}");
            }

            for (int family = 0; family < families; family++)
            {
                TestContext.Out.WriteLine(
                    $"    skin[{family}] -> "
                    + string.Join(
                        ", ",
                        Enumerable.Range(0, references)
                            .Select(reference => table[(family * references) + reference])));
            }

            TestContext.Out.WriteLine(
                "    meshes reference: "
                + string.Join(", ", model.Meshes.Select(mesh => mesh.MaterialIndex)));

            List<int> skins =
            [
                .. placements
                    .Where(placement => string.Equals(
                        placement.Model.Replace('\\', '/'),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(placement => placement.Skin)
                    .Distinct()
                    .Order(),
            ];

            TestContext.Out.WriteLine(
                $"    placements ask for skins: {string.Join(", ", skins)}");
        }

        described.ShouldBeGreaterThan(0, "none of the suspect models could be read at all");
    }
}
