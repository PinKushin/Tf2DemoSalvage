using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// <c>BspCubemaps</c> against a real map, which is the only thing that can falsify the stride.
/// </summary>
/// <remarks>
/// **The synthetic tests were written by the same person who wrote the reader**, so they prove the
/// two agree rather than that either matches a map vbsp compiled. A 13-byte record read at 16 gives
/// plausible coordinates on a fixture and on a real map alike — what it cannot do is put every
/// cubemap inside the map's own bounding box.
///
/// That is the measurement here: not a count, but whether the positions are somewhere a map could
/// be. A stride error walks the reader forward through the lump and composes coordinates from the
/// tail of one record and the head of the next, which puts them tens of thousands of units out —
/// well beyond Source's own ±16384 world limit.
/// </remarks>
public sealed class CubemapPlacementTests
{
    private const string MapName = "cp_process_final";

    /// <summary>Source's world half-extent, <c>MAX_COORD_INTEGER</c> in <c>bspfile.h</c>.</summary>
    private const int WorldLimit = 16384;

    [Test]
    public void ARealMapBakesCubemapsAndTheyAllStandInsideTheWorld()
    {
        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(LoadTheMap());

        TestContext.Out.WriteLine($"{cubemaps.Count} cubemaps on {MapName}");

        cubemaps.Count.ShouldBeGreaterThan(
            0, $"{MapName} is a modern TF2 map and every one of them bakes cubemaps");

        foreach (BspCubemap cubemap in cubemaps)
        {
            // **The falsifier.** A wrong stride composes an origin from two adjacent records and
            // lands far outside the world; a correct one cannot, because vbsp took these positions
            // from entities the map compiler had already bounds-checked.
            Math.Abs(cubemap.X).ShouldBeLessThan(WorldLimit, $"cubemap at {Describe(cubemap)}");
            Math.Abs(cubemap.Y).ShouldBeLessThan(WorldLimit, $"cubemap at {Describe(cubemap)}");
            Math.Abs(cubemap.Z).ShouldBeLessThan(WorldLimit, $"cubemap at {Describe(cubemap)}");
        }
    }

    [Test]
    public void EveryBakedSizeIsAPowerOfTwoWithinWhatATextureCanBe()
    {
        // The size is stored as a shift count, so anything that comes back must be a power of two —
        // and must be a plausible texture edge. This is what catches the escape value being fed
        // through the shift: `1 << (0 - 1)` is `1 << 31`, which is a power of two and is nowhere
        // near a texture size, so both halves of the assertion are doing work.
        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(LoadTheMap());

        foreach (int size in cubemaps.Select(cubemap => cubemap.Size).Distinct())
        {
            TestContext.Out.WriteLine($"size {size}");

            size.ShouldBeGreaterThan(0);
            size.ShouldBeLessThanOrEqualTo(1024);
            (size & (size - 1)).ShouldBe(0, $"{size} is not a power of two");
        }
    }

    [Test]
    public void EveryCubemapsDerivedTextureNameIsActuallyInTheMapsArchives()
    {
        // **The one that can fail when the naming is wrong.** The synthetic test asserts the format
        // string; this asserts that the string names a file vbsp really wrote.
        //
        // A separator, a case, or a sign in the wrong place produces a name that is well-formed and
        // resolves to nothing — which downstream is indistinguishable from a map with no
        // reflections, and would surface much later as "cubemaps do not work".
        //
        // **Searched in the MAP's pakfile, not the game's archives**, which is where vbsp puts them:
        // the reflections are baked from this map's geometry and exist nowhere else. Looking in the
        // game's VPKs finds zero of them and says nothing about the naming — a fact about the
        // instrument, which is how the first version of this test read.
        byte[] map = LoadTheMap();

        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(map);
        PakFile pak = PakFile.ReadFrom(map);

        List<string> missing = [];

        foreach (BspCubemap cubemap in cubemaps)
        {
            string name = BspCubemaps.TextureName(MapName, cubemap);

            if (!pak.Contains($"materials/{name}.vtf") && !pak.Contains($"materials/{name}.hdr.vtf"))
            {
                missing.Add(name);
            }
        }

        TestContext.Out.WriteLine(
            $"{cubemaps.Count - missing.Count} of {cubemaps.Count} cubemap textures found " +
            $"in a pakfile of {pak.Count} entries" +
            (missing.Count > 0 ? $"; first missing: {string.Join(", ", missing.Take(4))}" : string.Empty));

        // **The positive control comes first**, because an empty pakfile would make the loop below
        // vacuous and the whole test would pass having compared nothing.
        pak.Count.ShouldBeGreaterThan(0, "the map's pakfile is where a baked cubemap lives");

        missing.ShouldBeEmpty(
            "every cubemap the lump places was baked into the map's own pakfile by vbsp");
    }

    private static string Describe(BspCubemap cubemap) =>
        $"({cubemap.X}, {cubemap.Y}, {cubemap.Z}) size {cubemap.Size}";

    private static string MapPath(string game) =>
        Path.Combine(game, "maps", MapName + ".bsp");

    /// <summary>The reference map's bytes, or skips.</summary>
    private static byte[] LoadTheMap()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");

            throw new InvalidOperationException("unreachable; Assert.Ignore throws");
        }

        string map = MapPath(game);

        if (!File.Exists(map))
        {
            Assert.Ignore($"{MapName} is not installed.");
        }

        return File.ReadAllBytes(map);
    }
}
