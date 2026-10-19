using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

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
    private static string? GameFolder => GameInstall.Root;

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

    /// <remarks>
    /// **Pixels are let go once the device has them, and nothing else is** (B407). The map's texture mips were 1.3 GB of the
    /// viewer's heap after an f12 load, held for a re-upload that only a failed upload ever asks for. Every other field is still
    /// read afterwards — the world build asks whether a material is translucent — so only the images may go. Loaded fresh, not
    /// from `MapCache`, because this empties what it loads.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReleaseUploaded_OnARealMap_EmptiesImagesAndKeepsEverythingElse()
    {
        if (GameFolder is not { } game || Path.Combine(game, "maps", "cp_process_final.bsp") is not { } map || !File.Exists(map))
        {
            Assert.Ignore("Team Fortress 2 with cp_process_final is not installed.");
            return;
        }

        MapAssets assets = MapAssets.Load(File.ReadAllBytes(map), GameArchives.Open(game), maximumTextureSize: 64);
        int count = assets.Textures.Count;
        int translucent = assets.Textures.Count(texture => texture is { IsTranslucent: true });

        // The control: pixels are there before, or emptying them would prove nothing.
        assets.Textures.Count(texture => texture is { Image.IsEmpty: false }).ShouldBeGreaterThan(100);
        assets.Bumps.Count(bump => bump is { Texture.Image.IsEmpty: false }).ShouldBeGreaterThan(0);
        assets.Released.ShouldBeFalse();

        assets.ReleaseUploaded();

        assets.Released.ShouldBeTrue();
        assets.Textures.Count.ShouldBe(count);
        assets.Textures.Count(texture => texture is { IsTranslucent: true }).ShouldBe(translucent);
        assets.Textures.ShouldAllBe(texture => texture == null || texture.Value.Image.IsEmpty);
        assets.BlendTextures.ShouldAllBe(texture => texture == null || texture.Value.Image.IsEmpty);
        assets.Bumps.ShouldAllBe(bump => bump == null || bump.Value.Texture.Image.IsEmpty);
        assets.Details.ShouldAllBe(detail => detail == null || detail.Value.Texture.Image.IsEmpty);
    }

    /// <remarks>
    /// **Both go up in the same pass as the base textures** (`UploadWorldTextures`), so B407's release covers them too:
    /// a CPU copy of a texture the device holds is one Valve does not keep (`CTexture::ReconstructTexture` frees its
    /// scratch VTF, finding 80). cp_process_f12 because it animates detail textures and bakes cubemaps; its own load,
    /// since releasing a shared one would empty it for every other test.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReleaseUploaded_AnimatedDetailsAndCubemaps_EmptiesEveryFrameAndFace()
    {
        if (GameFolder is not { } game || Path.Combine(game, "maps", "cp_process_f12.bsp") is not { } map || !File.Exists(map))
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        // With the models every viewer load carries: their materials are what animate a detail texture on this map.
        MapAssets assets = MapAssets.Load(
            File.ReadAllBytes(map), SharedGame.Archives, maximumTextureSize: 64, entityModels: DemoModels.Needed(null, SharedGame.Content));
        int animated = assets.AnimatedDetails.Count;
        int cubemaps = assets.Cubemaps.Count;

        // The control: pixels are there before, or emptying them would prove nothing.
        assets.AnimatedDetails.Values.SelectMany(frames => frames).Count(frame => !frame.Image.IsEmpty).ShouldBeGreaterThan(0);
        assets.Cubemaps.Count(cubemap => cubemap is { } present && present.Faces.Any(face => !face.Image.IsEmpty)).ShouldBeGreaterThan(0);

        assets.ReleaseUploaded();

        assets.AnimatedDetails.Count.ShouldBe(animated);
        assets.Cubemaps.Count.ShouldBe(cubemaps);
        assets.AnimatedDetails.Values.SelectMany(frames => frames).ShouldAllBe(frame => frame.Image.IsEmpty);
        assets.Cubemaps.ShouldAllBe(cubemap => cubemap == null || cubemap.Value.Faces.All(face => face.Image.IsEmpty));
    }

    /// <remarks>
    /// **A texture file is read once per load, however many materials name it** (finding 80): `IMaterialSystem::FindTexture`
    /// takes a name, and `IsTextureLoaded( char const* pTextureName )` asks by name alone (`imaterialsystem.h:948-951`). On
    /// f12 the water normal map alone was read 58 times, 855 MB. The tally counts inside the resolve's own read.
    /// </remarks>
    [Test]
    [NonParallelizable]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_ARealMap_ReadsEachTextureFileOnce()
    {
        if (F12() is not { } map)
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        TextureReadTally tally = new();
        MapAssets.TextureReads = tally;

        try
        {
            _ = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 64, entityModels: DemoModels.Needed(null, SharedGame.Content));
        }
        finally
        {
            MapAssets.TextureReads = null;
        }

        tally.ByPath.Count.ShouldBeGreaterThan(300, "the control: a real map reads hundreds of textures");
        tally.ByPath.Where(pair => pair.Value.Reads > 1).Select(pair => pair.Key).ShouldBeEmpty();
    }

    /// <remarks>
    /// **Sharing changes no pixel** (finding 80). The decode depends on the file, the load's size cap and the frame, and on
    /// nothing a material decides, so a shared decode must be byte for byte the one each material made for itself.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_SharedTextures_AreByteIdenticalToEachMaterialsOwn()
    {
        if (F12() is not { } map)
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        string[] models = [.. DemoModels.Needed(null, SharedGame.Content)];
        MapAssets own = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 64, entityModels: models, shareTextures: false);
        MapAssets shared = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 64, entityModels: models);

        static byte[] Pixels(MapTexture? texture) => texture is { } present ? present.Image.ToRgba(present.Width, present.Height).ToArray() : [];

        shared.Textures.Count.ShouldBe(own.Textures.Count);
        shared.Textures.Count(texture => texture is not null).ShouldBeGreaterThan(300, "the control: textures resolved");

        for (int slot = 0; slot < own.Textures.Count; slot++)
        {
            Pixels(shared.Textures[slot]).ShouldBe(Pixels(own.Textures[slot]), $"texture slot {slot}");
            Pixels(shared.Bumps[slot]?.Texture).ShouldBe(Pixels(own.Bumps[slot]?.Texture), $"bump slot {slot}");
            Pixels(shared.Details[slot]?.Texture).ShouldBe(Pixels(own.Details[slot]?.Texture), $"detail slot {slot}");
        }

        foreach ((string name, System.Collections.Generic.IReadOnlyList<MapTexture> frames) in own.AnimatedDetails)
        {
            shared.AnimatedDetails[name].Select(frame => Pixels(frame)).ShouldBe(frames.Select(frame => Pixels(frame)), name);
        }
    }

    /// <remarks>
    /// **One copy of the particle definitions per install, not per map** (finding 80: 239 MB per held map on f12). They are
    /// read from the install alone, so two loads on one install must hand out the same instance.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_TwoMapsOnOneInstall_ShareOneSetOfParticleDefinitions()
    {
        if (F12() is not { } map)
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        MapAssets first = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 16);
        MapAssets second = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 16);

        first.ParticleSystemsByName.Count.ShouldBeGreaterThan(5000, "the control: the definitions were read");
        second.ParticleSystemsByName.ShouldBeSameAs(first.ParticleSystemsByName);
    }

    /// <remarks>
    /// **An animated model is posed by its bones, from one bind pose** (finding 80, item 3): the engine poses every studio
    /// model through `SetupBones`, and a baked frame per animation step was most of f12's 329 MB of model vertices. The
    /// sweep found the bake wrong where they differ most (a non-looping door blended back toward shut). The control: the
    /// door and the locker are among the models, and so is a model with one pose, which stays one frame either way.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Load_AnimatedModels_AreSkinnedFromOneBindPose()
    {
        const string Door = "models/props_gameplay/door_slide_large_dynamic.mdl";
        const string Locker = "models/props_gameplay/resupply_locker.mdl";

        if (F12() is not { } map)
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        MapAssets assets = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 16, entityModels: [Door, Locker, "models/items/medkit_small.mdl"]);

        foreach (string model in new[] { Door, Locker, "models/items/medkit_small.mdl" })
        {
            PropModels.ModelFrames frames = assets.Geometry(model).ShouldNotBeNull(model);

            frames.IsSkinned.ShouldBeTrue(model);
            frames.Geometry.Count.ShouldBe(1, model);
        }

        assets.EntityModels.Values.Where(frames => !frames.IsSkinned).ShouldAllBe(frames => frames.Geometry.Count == 1);
    }

    private static byte[]? F12() =>
        GameFolder is { } game && Path.Combine(game, "maps", "cp_process_f12.bsp") is { } map && File.Exists(map)
            ? File.ReadAllBytes(map)
            : null;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
