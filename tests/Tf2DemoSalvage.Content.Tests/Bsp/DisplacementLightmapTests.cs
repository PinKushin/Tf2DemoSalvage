using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A displacement's lightmap coordinates, which are assigned rather than projected.
/// </summary>
/// <remarks>
/// **The mechanism is different from every other face and it is not guessable.** An ordinary face's
/// lightmap coordinates come from projecting its vertices through <c>lightmapVecs</c>. A
/// displacement's do not: the compiler writes them straight from the corner ordering, spanning
/// texel centres over the face's own lightmap, in <c>CCoreDispSurface::CalcLuxelCoords</c>.
///
/// Projecting the base quad and interpolating across the grid looks obviously right and produces
/// plausible numbers, which is why it survived: it put 219 of cp_process_final's 578 displacements
/// outside their own lightmap, worst case by 389x. Those were clamped, so each drew in one flat
/// shade from an edge texel — diffuse dark patches over the terrain, scattered across the map.
/// </remarks>
public sealed class DisplacementLightmapTests
{
    private static string? MapFile => GameInstall.Find("maps/cp_process_final.bsp");

    [Test]
    public void DisplacementLightmaps_EveryTerrainVertex_LandsInsideItsOwnLightmap()
    {
        // **The measurement that found the defect, stated as a rule.** A coordinate outside [0,1]
        // is clamped downstream, so the whole face samples one edge texel and draws flat. Nothing
        // throws and nothing looks obviously broken; it just goes dark.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        BspTerrain terrain = BspTerrain.Create(map);

        int checkedFaces = 0;
        List<string> outside = [];

        foreach (BspSurface surface in BspSurfaces.Read(map).Where(surface => surface.IsDisplacement))
        {
            IReadOnlyList<SurfaceVertex> triangles = terrain.ReadTriangles(surface);

            if (triangles.Count == 0)
            {
                continue;
            }

            checkedFaces++;

            foreach (SurfaceVertex vertex in triangles)
            {
                if (vertex.LightU is < 0f or > 1f || vertex.LightV is < 0f or > 1f)
                {
                    outside.Add($"face {surface.FaceIndex}: ({vertex.LightU:F3}, {vertex.LightV:F3})");
                    break;
                }
            }
        }

        checkedFaces.ShouldBeGreaterThan(100, "the map should have plenty of terrain");

        outside.ShouldBeEmpty(
            $"{outside.Count} of {checkedFaces} displacements land outside their lightmap: " +
            string.Join("; ", outside.Take(3)));
    }

    [Test]
    public void DisplacementLightmaps_TerrainCoordinates_SpanTexelCentres()
    {
        // Valve's own assignment runs 0.5 to size + 0.5 luxels, so a corner sits at a texel CENTRE
        // and never at the image edge. Running corner to corner instead samples half a texel of the
        // neighbouring face along every border, which is a bright seam around each patch.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        BspTerrain terrain = BspTerrain.Create(map);

        BspSurface displacement = BspSurfaces.Read(map)
            .First(surface => surface.IsDisplacement && surface.LuxelWidth > 2);

        IReadOnlyList<SurfaceVertex> triangles = terrain.ReadTriangles(displacement);

        triangles.Min(vertex => vertex.LightU).ShouldBeGreaterThan(0f);
        triangles.Max(vertex => vertex.LightU).ShouldBeLessThan(1f);
    }
}
