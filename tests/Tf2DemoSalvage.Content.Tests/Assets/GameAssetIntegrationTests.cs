using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The asset chain against a real TF2 install: VPK to VMT to VTF.
/// </summary>
/// <remarks>
/// **Skipped when the game is not installed**, so this runs on a developer's machine and stays out
/// of CI's way. The unit tests cover the formats with built fixtures; what only a real install can
/// show is whether this project's reading of them agrees with what Valve actually shipped —
/// including the LZMA-compressed pakfile entries that .NET's own zip reader refuses outright.
///
/// **The gamma check is the one worth understanding.** A map's `texdata` carries a `reflectivity`
/// float3 that the compiler computed by averaging the texture, in LINEAR space, while the texture
/// itself is sRGB. So averaging this project's decoded pixels and applying the gamma curve should
/// land on a number written years ago by a different program from the same source image. That is a
/// value recorded twice by unrelated routes, which tests the DXT decoder against Valve rather than
/// against this project's own reading of the format.
/// </remarks>
public sealed class GameAssetIntegrationTests
{
    /// <summary>Where the game is, when it is installed on this machine.</summary>
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

    private string _tf = string.Empty;

    [SetUp]
    public void RequireTheGame()
    {
        if (GameFolder is not { } folder)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _tf = folder;
    }

    [Test]
    public void GameAssets_TheTexturesArchive_HoldsAStockMaterial()
    {
        VpkArchive archive = VpkArchive.Open(Path.Combine(_tf, "tf2_textures_dir.vpk"));

        archive.Version.ShouldBe(2);
        archive.Count.ShouldBeGreaterThan(1000);
    }

    [Test]
    public void GameAssets_AStockMaterial_ResolvesToADecodableTexture()
    {
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));
        VpkArchive textures = VpkArchive.Open(Path.Combine(_tf, "tf2_textures_dir.vpk"));

        byte[]? vmt = misc.ReadFile("materials/concrete/concretefloor007b.vmt")
            ?? textures.ReadFile("materials/concrete/concretefloor007b.vmt");

        vmt.ShouldNotBeNull("the stock concrete floor material is missing from the game's archives");

        VmtMaterial material = VmtMaterial.Parse(vmt);
        material.BaseTexture.ShouldNotBeNull();

        byte[]? vtf = textures.ReadFile("materials/" + material.BaseTexture + ".vtf")
            ?? misc.ReadFile("materials/" + material.BaseTexture + ".vtf");

        vtf.ShouldNotBeNull();

        VtfTexture image = VtfTexture.Decode(vtf);

        image.Width.ShouldBeGreaterThan(1, "a full-size decode must not return the smallest mip");
        image.MipCount.ShouldBeGreaterThan(1);
        image.Pixels.Length.ShouldBe(image.Width * image.Height * 4);
    }

    [Test]
    public void GameAssets_DecodedPixels_AgreeWithTheMapsReflectivity()
    {
        // The cross-check described on this type. Averaging the decoded sRGB pixels and applying
        // the gamma curve must land near a number the map compiler wrote from the same texture.
        string map = Path.Combine(_tf, "maps", "cp_process_final.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_final is not installed.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(map);
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));
        VpkArchive textures = VpkArchive.Open(Path.Combine(_tf, "tf2_textures_dir.vpk"));

        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(bytes);
        BspMaterial concrete = default;

        foreach (BspMaterial candidate in materials)
        {
            if (candidate.Name.Contains("CONCRETEFLOOR007B", StringComparison.OrdinalIgnoreCase))
            {
                concrete = candidate;
                break;
            }
        }

        concrete.Name.ShouldNotBeNullOrEmpty("the map does not use the expected material");

        byte[]? vtf = textures.ReadFile("materials/concrete/concretefloor007b.vtf")
            ?? misc.ReadFile("materials/concrete/concretefloor007b.vtf");
        vtf.ShouldNotBeNull();

        VtfTexture image = VtfTexture.Decode(vtf);
        double sum = 0;

        for (int index = 0; index < image.Width * image.Height; index++)
        {
            sum += image.Pixels[index * 4] / 255.0;
        }

        double average = sum / (image.Width * image.Height);
        double linear = Math.Pow(average, 2.2);

        // Valve's own number for the same texture, from a different program, years earlier.
        linear.ShouldBe(concrete.Reflectivity.Red, tolerance: 0.05);
    }

    [TestCase("cp_process_final")]
    [TestCase("cp_badlands")]
    [TestCase("koth_viaduct")]
    public void GameAssets_EveryBrushworkCorner_LandsInsideItsLightmap(string map)
    {
        // **The check that says the lightmap coordinates are RIGHT rather than merely present.**
        // Lightmap vectors are shared by every face using a texinfo, and produce coordinates in a
        // grid covering all of them; the face's own LightmapTextureMinsInLuxels is what places
        // this face inside its own lightmap. Forget it and every face still samples somewhere -
        // the map looks lit, with the wrong patch of light on every surface.
        //
        // So the measurement is a range: with the mins subtracted, a corner must land in 0..1.
        //
        // **Displacements are excluded, and that is not a fudge.** A displacement's entry in FACES
        // is the base quad the terrain was built on, while its lightmap covers the subdivided
        // surface - so its corners fall outside by construction. Measured: 25% to 40% of
        // displacements do, and ZERO brushwork faces do, across four maps.
        string path = Path.Combine(_tf, "maps", map + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{map} is not installed.");
            return;
        }

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(File.ReadAllBytes(path));

        int checkedCorners = 0;
        int outside = 0;

        foreach (BspSurface surface in surfaces)
        {
            if (surface.Lightmap.IsEmpty || surface.IsDisplacement)
            {
                continue;
            }

            foreach (SurfaceVertex vertex in surface.Vertices)
            {
                checkedCorners++;

                if (vertex.LightU is < -0.01f or > 1.01f || vertex.LightV is < -0.01f or > 1.01f)
                {
                    outside++;
                }
            }
        }

        // The guard: a map that contributed no corners would pass the assertion below vacuously.
        checkedCorners.ShouldBeGreaterThan(10_000, "no brushwork corners were checked");
        outside.ShouldBe(0, $"{outside} of {checkedCorners} brushwork corners miss their lightmap");
    }

    [Test]
    public void GameAssets_Displacements_AreIdentifiedNotSilentlyWrong()
    {
        // The control for the exclusion above. If nothing were identified as a displacement, the
        // previous test would be excluding nothing and passing for the wrong reason.
        string path = Path.Combine(_tf, "maps", "cp_badlands.bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore("cp_badlands is not installed.");
            return;
        }

        int displacements = 0;

        foreach (BspSurface surface in BspSurfaces.Read(File.ReadAllBytes(path)))
        {
            displacements += surface.IsDisplacement ? 1 : 0;
        }

        displacements.ShouldBeGreaterThan(100, "an outdoor map has displacement terrain");
    }

    [Test]
    public void GameAssets_ACommunityMapsPakfile_ReadsItsLzmaEntries()
    {
        // .NET refuses zip method 14 outright, which is why this project reads the zip itself.
        string map = Path.Combine(_tf, "maps", "cp_process_final.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_final is not installed.");
            return;
        }

        PakFile pak = PakFile.ReadFrom(File.ReadAllBytes(map));

        pak.Count.ShouldBeGreaterThan(100, "a community map ships its own content");

        int read = 0;

        foreach (string path in pak.Paths)
        {
            if (!path.EndsWith(".VMT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pak.ReadFile(path).ShouldNotBeNull();
            read++;

            if (read == 5)
            {
                break;
            }
        }

        read.ShouldBeGreaterThan(0, "the map's pakfile holds no materials");
    }
}
