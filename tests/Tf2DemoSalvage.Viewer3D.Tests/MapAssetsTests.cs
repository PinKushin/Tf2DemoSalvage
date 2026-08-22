using System;
using System.Diagnostics;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Loading everything one map needs to be drawn.
/// </summary>
/// <remarks>
/// Most of these skip without a TF2 install, because what they check is whether this project's
/// reading of the game's content agrees with what Valve shipped — which no fixture can answer.
/// The search-order tests do not need the game and build their own folders.
/// </remarks>
public sealed class MapAssetsTests
{
    private static string? GameFolder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            foreach (string root in new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (File.Exists(Path.Combine(root, "tf2_textures_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Disposable temp folder; a lock must not fail an otherwise passing test.
        }
    }

    [Test]
    public void Open_NoGameFolder_IsEmptyRatherThanThrowing()
    {
        // Reviewing demos on a machine without TF2 has to work: the map's own content still
        // resolves, and stock surfaces fall back to the material's recorded colour.
        GameArchives.Open(null).IsEmpty.ShouldBeTrue();
        GameArchives.Open(Path.Combine(_folder, "absent")).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Read_PrefersACustomFolderOverTheGamesOwnLooseFile()
    {
        // **The engine's own order, and the reason it matters.** A file under tf/custom REPLACES
        // the game's copy, so a viewer that searched the other way round would show the stock
        // texture where the game shows the user's replacement.
        //
        // **The fixture needs a gameinfo.txt, because that is what declares custom at all.** This
        // test used to pass against a folder with none, back when the custom convention was
        // hardcoded here; the search path now comes from the file, and a folder without one falls
        // back to loose files only - which is the behaviour that predates VPKs and custom.
        //
        // Written with |gameinfo_path| rather than TF2's literal "tf/custom/*", because a relative
        // entry resolves against the INSTALL ROOT and so assumes the mod folder is named tf. That
        // is true of the real game and not of a temporary fixture, and getting it wrong here
        // resolves to a folder that does not exist - which is silent.
        File.WriteAllText(Path.Combine(_folder, "gameinfo.txt"), """
            "GameInfo"
            {
                FileSystem
                {
                    SearchPaths
                    {
                        game+mod+custom_mod  |gameinfo_path|custom/*
                        mod+mod_write        |gameinfo_path|.
                    }
                }
            }
            """);

        Directory.CreateDirectory(Path.Combine(_folder, "materials", "concrete"));
        File.WriteAllText(
            Path.Combine(_folder, "materials", "concrete", "a.vmt"), "loose");

        string custom = Path.Combine(_folder, "custom", "mine", "materials", "concrete");
        Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(custom, "a.vmt"), "custom");

        byte[]? found = GameArchives.Open(_folder).Read("materials/concrete/a.vmt");

        found.ShouldNotBeNull();
        System.Text.Encoding.UTF8.GetString(found).ShouldBe("custom");
    }

    [Test]
    public void Read_FindsALooseFileWithNoArchivesAtAll()
    {
        // A pre-VPK install's content, extracted, or simply a folder of custom materials. Source
        // read loose files long before VPK existed and still does.
        Directory.CreateDirectory(Path.Combine(_folder, "materials"));
        File.WriteAllText(Path.Combine(_folder, "materials", "b.vmt"), "loose");

        GameArchives archives = GameArchives.Open(_folder);

        archives.IsEmpty.ShouldBeFalse();
        archives.Read("materials/b.vmt").ShouldNotBeNull();
    }

    [Test]
    public void Read_APathEscapingTheFolder_IsRefused()
    {
        // The material name comes from a map written by a stranger. Without the containment check
        // a name of "../../../windows/win.ini" reads whatever it likes (D32).
        File.WriteAllText(Path.Combine(_folder, "secret.txt"), "should not be readable");

        string content = Path.Combine(_folder, "content");
        Directory.CreateDirectory(content);

        GameArchives.Open(content).Read("../secret.txt").ShouldBeNull();
    }

    [Test]
    public void MapAssets_ARealMap_ResolvesAlmostEveryMaterial()
    {
        if (GameFolder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        string map = Path.Combine(game, "maps", "cp_process_final.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_final is not installed.");
            return;
        }

        // **Deliberately NOT the shared cache, and this is the one place that is true.** The clock
        // below is the point: this reports what a cold, full-size load costs, and taking a cached
        // one would report the cache hit and quietly retire the only timing the suite has.
        //
        // Everything else that wanted a map at some hand-picked "small enough" size now shares one
        // load through MapCache. This pays for a real one on purpose.
        Stopwatch clock = Stopwatch.StartNew();
        MapAssets assets = MapAssets.Load(
            File.ReadAllBytes(map), GameArchives.Open(game), maximumTextureSize: 512);
        clock.Stop();

        TestContext.Out.WriteLine(
            $"{assets.Resolved} resolved, {assets.Missing} missing, " +
            $"lightmap atlas {assets.Lightmaps.Width}x{assets.Lightmaps.Height}, " +
            $"{clock.Elapsed.TotalSeconds:F1}s");

        // Measured at 208 of 211 with the game installed. The threshold is below that rather than
        // equal to it, so a Valve update that renames one material does not fail the suite - but
        // it is high enough that losing the pakfile or an archive would.
        assets.Resolved.ShouldBeGreaterThan(200);
        assets.Materials.Count.ShouldBe(assets.Textures.Count);

        // The lighting has to be there too, and a real map's atlas is not a scrap.
        assets.Lightmaps.Width.ShouldBeGreaterThan(256);
        assets.Lightmaps.Height.ShouldBeGreaterThan(256);
    }

    [Test]
    public void MapAssets_AMapsOwnContent_IsMostlyPatchesOverStockMaterials()
    {
        // **A map's pakfile is not a self-contained copy of what it needs**, which is worth
        // knowing before promising anything about a machine without TF2.
        //
        // Measured on cp_process_final: the pakfile holds 3,413 entries including 77 VMTs, and 51
        // of the map's materials resolve their VMT from it — but ZERO of those resolve a VTF from
        // it. The names say why:
        //
        //   materials/maps/cp_process_final/icarus/glasschrome001_544_1952_929.vmt
        //   materials/maps/cp_process_final/nature/blendgroundtograss007_wvt_patch.vmt
        //
        // Those are cubemap patches — the numbers are the cubemap's position — and blend-material
        // patches. Each is a Patch stub that includes a STOCK material and overrides its envmap.
        // The pixels live in the game's archives, not in the map.
        //
        // So without the game there is nothing to draw those surfaces with, and the fallback to
        // the material's recorded reflectivity is the whole answer rather than a corner case.
        if (GameFolder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed.");
            return;
        }

        string map = Path.Combine(game, "maps", "cp_process_final.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_final is not installed.");
            return;
        }

        MapAssets withoutGame = MapAssets.Load(
            File.ReadAllBytes(map), GameArchives.Open(null), maximumTextureSize: 256);

        withoutGame.Resolved.ShouldBe(
            0, "process's pakfile carries patch materials whose textures are stock");

        // The control, and the point of the comparison: the same map WITH the game resolves almost
        // everything. Without it, the difference is not a small shortfall - it is all of them.
        MapAssets withGame = MapAssets.Load(
            File.ReadAllBytes(map), GameArchives.Open(game), maximumTextureSize: 256);

        withGame.Resolved.ShouldBeGreaterThan(200);
    }
}
