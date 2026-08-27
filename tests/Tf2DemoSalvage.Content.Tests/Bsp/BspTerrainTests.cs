using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Reading a whole map's terrain against reading it one face at a time.
/// </summary>
/// <remarks>
/// **The reader exists for one reason: the per-face entry point re-reads the lumps every call.**
/// <see cref="BspDisplacements.ReadTriangles"/> parses the header and decompresses both displacement
/// lumps for each face it is asked about, so a map with 578 displacements decompresses them 578
/// times. On cp_process_final that is most of an 830 ms world rebuild, and the world is rebuilt
/// whenever the viewport changes size — which is what made full screen crawl.
///
/// So the measurement here is differential, not a fixture: the reader must produce **exactly** what
/// the per-face path produces, corner for corner. A fixture could only test this project's reading
/// of its own reading; comparing the two paths on a real map tests that the fast one did not quietly
/// change the answer.
/// </remarks>
public sealed class BspTerrainTests
{
    /// <summary>A shipped map with real displacements, when the game is installed.</summary>
    private static string? MapFile => GameInstall.Find("maps/cp_process_final.bsp");

    private ReadOnlyMemory<byte> _map;
    private IReadOnlyList<BspSurface> _displacements = [];

    [SetUp]
    public void RequireAMapWithTerrain()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _map = File.ReadAllBytes(path);
        _displacements =
        [
            .. BspSurfaces.Read(_map).Where(surface => surface.IsDisplacement)
        ];

        _displacements.Count.ShouldBeGreaterThan(100, "the map should have real terrain in it");
    }

    [Test]
    public void ReadTriangles_MatchesThePerFacePath_CornerForCorner()
    {
        BspTerrain terrain = BspTerrain.Create(_map);

        foreach (BspSurface surface in _displacements)
        {
            IReadOnlyList<SurfaceVertex> fast = terrain.ReadTriangles(surface);
            IReadOnlyList<SurfaceVertex> slow = BspDisplacements.ReadTriangles(_map, surface);

            fast.Count.ShouldBe(slow.Count, $"face {surface.FaceIndex}");
            fast.ShouldBe(slow, $"face {surface.FaceIndex}");
        }
    }

    [Test]
    public void ReadTriangles_ReadsAWholeMapFasterThanThePerFacePath()
    {
        // **A timing assertion, deliberately, because speed is the whole reason this type exists.**
        // The margin is enormous on purpose: the measured difference is around fiftyfold, so a
        // factor of five fails only if the lump caching has actually been lost. A tighter bound
        // would measure the machine's mood instead of the code.
        BspTerrain terrain = BspTerrain.Create(_map);

        Stopwatch clock = Stopwatch.StartNew();

        foreach (BspSurface surface in _displacements)
        {
            terrain.ReadTriangles(surface);
        }

        TimeSpan fast = clock.Elapsed;

        clock.Restart();

        foreach (BspSurface surface in _displacements)
        {
            BspDisplacements.ReadTriangles(_map, surface);
        }

        TimeSpan slow = clock.Elapsed;

        fast.ShouldBeLessThan(
            slow / 5,
            $"the cached reader took {fast.TotalMilliseconds:F0} ms against {slow.TotalMilliseconds:F0} ms");
    }

    [Test]
    public void ReadTriangles_ANonDisplacementFace_HasNoTerrain()
    {
        BspTerrain terrain = BspTerrain.Create(_map);

        BspSurface flat = BspSurfaces.Read(_map).First(surface => !surface.IsDisplacement);

        terrain.ReadTriangles(flat).ShouldBeEmpty();
    }
}
