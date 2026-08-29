using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The world sweep the chase camera is handed — brushes AND terrain.
/// </summary>
/// <remarks>
/// **This is the only level that can catch the bug B227 was.** `DisplacementSweepConformanceTests`
/// pins the primitive against hand-computed fractions; `DisplacementCollisionTests` checks the
/// whole-map set against a brute force. Neither notices that the camera is handed a sweep which
/// asks the BSP tree and nothing else — and that is exactly what shipped, so the chase camera
/// passed through every hillside in TF2 while both of those would have been green.
///
/// **`MainForm` is one line past this** (`_spectator.World = ... map.Level.Sweep`), which is as close
/// as a test without a window gets.
/// </remarks>
public sealed class MapLevelSweepTests
{
    /// <summary>A map built on terrain, which is what makes it the subject.</summary>
    private const string Terrain = "cp_badlands";

    [Test]
    public void Sweep_DroppedOntoTerrain_IsStoppedWhereTheBrushTraceAloneIsNot()
    {
        MapLevel level = MapLevel.Read(MapCache.Bytes(Terrain), NullLogger.Instance);

        level.Terrain.ShouldNotBeNull();
        level.Displacements.Count.ShouldBeGreaterThan(0, "cp_badlands is built on displacements");

        // **The condition is SEARCHED for rather than assumed, and the first two attempts show why.**
        // Dropping onto a displacement vertex from 40 units up gave `brushes 0` — startsolid — at
        // (1024, −384, 288): the space just above a displacement vertex is often inside the brush
        // the terrain was carved out of, so the brush trace reports the box as already embedded.
        // That is the brush trace behaving correctly and the test picking a useless spot.
        //
        // What proves the terrain term is reaching the camera is a sweep where BRUSHES FIND
        // NOTHING and terrain stops the box. Those exist in quantity on this map; finding one is a
        // search for a valid experimental condition, and the assertion on it is still exact.
        const float HalfExtent = 6f;

        (float X, float Y, float Z) found = default;
        float terrainOnly = 1f;
        float both = 1f;
        int examined = 0;

        foreach (BspSurface surface in level.Surfaces.Where(each => each.IsDisplacement).Take(400))
        {
            System.Collections.Generic.IReadOnlyList<SurfaceVertex> corners =
                level.Terrain.ReadTriangles(surface);

            if (corners.Count == 0)
            {
                continue;
            }

            SurfaceVertex target = corners[0];

            (float X, float Y, float Z) from = (target.X, target.Y, target.Z + 64f);
            (float X, float Y, float Z) to = (target.X, target.Y, target.Z - 16f);

            examined++;

            float brushes = level.Leaves is { } tree
                ? tree.Sweep(from.X, from.Y, from.Z, to.X, to.Y, to.Z, HalfExtent)
                : 1f;

            float terrain = level.Displacements.Sweep(
                from.X, from.Y, from.Z, to.X, to.Y, to.Z, HalfExtent);

            if (brushes < 1f || terrain >= 1f)
            {
                continue;
            }

            found = from;
            terrainOnly = terrain;
            both = level.Sweep(from, to, HalfExtent);
            break;
        }

        TestContext.Out.WriteLine(
            $"examined {examined} columns; terrain-only stop at {found} is {terrainOnly}, "
            + $"combined {both}");

        // **The claim.** There is a place on this map where the BSP tree finds nothing and the
        // terrain stops the box — which is the whole of B227, since the camera was handed only the
        // first of those two. A sweep that kept only the brush term reports 1 here and looks
        // perfectly healthy in every other suite in the repository.
        terrainOnly.ShouldBeLessThan(
            1f,
            $"no column among {examined} had brushes clear and terrain solid, so this test cannot "
            + "distinguish a sweep that consults terrain from one that does not");

        both.ShouldBe(terrainOnly, 0.000001, "the combined sweep must take the shorter of the two");
    }

    [Test]
    public void Sweep_IntoABrushWall_IsStillStoppedByTheBrush()
    {
        // **The control for the test above, and it guards the other direction.** Adding a terrain
        // term must not weaken the brush term: a `Sweep` that returned only the displacement answer
        // would satisfy every assertion above and silently stop clipping against walls, which is the
        // behaviour that already worked.
        MapLevel level = MapLevel.Read(MapCache.Bytes(Terrain), NullLogger.Instance);

        level.Leaves.ShouldNotBeNull();

        // Straight down from high above the map: the first thing under the sky on a closed map is
        // world geometry of one kind or another, and it must not be missed.
        BspSurface surface = level.Surfaces.First(each => each.IsDisplacement);
        SurfaceVertex target = level.Terrain.ShouldNotBeNull().ReadTriangles(surface)[0];

        (float X, float Y, float Z) from = (target.X, target.Y, target.Z + 4096f);
        (float X, float Y, float Z) to = (target.X, target.Y, target.Z - 64f);

        level.Sweep(from, to, 6f).ShouldBeLessThan(
            1f, "a sweep from the sky to below the ground has to be stopped by something");
    }
}
