using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Terrain collision over a whole map, rather than one triangle.
/// </summary>
/// <remarks>
/// **The level `DisplacementSweepConformanceTests` cannot reach.** That suite pins the primitive
/// against hand-computed fractions; this one asks whether the primitive is ever handed a real
/// triangle. A set that read no displacements, or built them with the wrong winding, passes every
/// conformance case in the file next door and stops nothing on a map
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **The sweep target is found from the map rather than written down.** Hardcoding a coordinate on
/// `cp_badlands` would be a constant nobody could check and one map update from being wrong; taking
/// a vertex off a displacement the map actually has is a prediction that stays true.
/// </remarks>
public sealed class DisplacementCollisionTests
{
    private static string? MapFile => GameInstall.Find("maps/cp_badlands.bsp");

    /// <summary>The map's surfaces and terrain, read once for the fixture.</summary>
    private static (IReadOnlyList<BspSurface> Surfaces, BspTerrain Terrain)? Map()
    {
        if (MapFile is not { } path)
        {
            return null;
        }

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        return (BspSurfaces.Read(file), BspTerrain.Create(file));
    }

    [Test]
    public void From_AMapBuiltOnTerrain_ReadsEveryDisplacement()
    {
        if (Map() is not { } map)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        DisplacementCollision collision = DisplacementCollision.From(map.Surfaces, map.Terrain);

        // Measured 2026-08-29. `LeafDisplacementReachTests` counts the same 1191 displacement faces
        // from the face lump directly, so the two agree by construction only if every one of them
        // produced triangles — which is the claim.
        collision.Count.ShouldBe(1191, "cp_badlands' displacement faces, all of them read");

        collision.TriangleCount.ShouldBeGreaterThan(
            collision.Count * 2,
            "a displacement is a subdivided heightfield, not a quad: even power 2 is 32 triangles");
    }

    [Test]
    public void Sweep_DroppedOntoTerrain_IsStoppedByIt()
    {
        if (Map() is not { } map)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        DisplacementCollision collision = DisplacementCollision.From(map.Surfaces, map.Terrain);

        // **Checked against a brute force over every triangle, which is the honest oracle here.**
        //
        // Two earlier versions tried to predict the STOP HEIGHT from a chosen vertex, and both were
        // wrong about the map rather than about the code: dropped 512 units the box stopped at
        // z = 793 over a vertex at 288, and from 24 units up it stopped at 311. `cp_badlands` stacks
        // terrain above terrain, so the first surface down a column is very often not the one whose
        // vertex was picked. Each time the sweep was right and the prediction was a guess about
        // geometry nobody had looked at.
        //
        // So this asserts the thing the class actually ADDS. The primitive is pinned to
        // hand-computed fractions next door; `DisplacementCollision` contributes reading the
        // triangles and rejecting displacements by their bounds. Sweeping every triangle with no
        // narrowing at all is an independent answer to the same question, and the two must agree
        // exactly — a bounds test that rejects a displacement it should have kept shows up here as a
        // longer fraction, which is exactly the bug that lets a camera through a hillside.
        //
        // **MANY rays, and one is not enough — measured.** With a single drop, shrinking the bounds
        // test's Z ceiling by 64 units changed nothing: the displacement that stopped that ray sat
        // far enough below for the tightening not to exclude it, so the sabotage survived. Each ray
        // only exercises the handful of displacements it passes near, so the instrument's
        // sensitivity is in their number.
        DisplacementTriangle[] every = map.Surfaces
            .Where(each => each.IsDisplacement)
            .SelectMany(each => Triples(map.Terrain.ReadTriangles(each)))
            .ToArray();

        every.Length.ShouldBe(
            collision.TriangleCount, "the oracle must hold the same triangles, or it is not one");

        // **A SHORT sweep, the length a chase camera actually makes, and that is what gives this
        // test its teeth.** With a 640-unit drop, tightening the bounds test's Z ceiling by 64 units
        // changed no answer at all — a long ray's box is so tall that only a displacement lying
        // entirely within a thin band near its top is excluded, and none of the twelve depended on
        // one. Over 80 units the ray's box is barely larger than the box being swept, so any error
        // in the bounds test excludes a displacement the sweep needed.
        const float Above = 40f;
        const float Below = 40f;
        const float HalfExtent = 6f;

        int stopped = 0;

        foreach (SurfaceVertex target in Targets(map))
        {
            float fraction = collision.Sweep(
                target.X, target.Y, target.Z + Above,
                target.X, target.Y, target.Z - Below,
                HalfExtent);

            float brute = 1f;

            foreach (DisplacementTriangle triangle in every)
            {
                brute = DisplacementSweep.Against(
                    (target.X, target.Y, target.Z + Above),
                    (0f, 0f, -(Above + Below)),
                    (HalfExtent, HalfExtent, HalfExtent),
                    triangle,
                    brute);
            }

            fraction.ShouldBe(
                brute,
                0.000001,
                $"narrowing changed the answer at ({target.X}, {target.Y}, {target.Z})");

            if (brute < 1f)
            {
                stopped++;
            }
        }

        // **Without this the agreement is worth nothing**: two sweeps that both hit nothing agree
        // perfectly. At least most of these columns must actually contain terrain.
        stopped.ShouldBeGreaterThan(
            8, "the sample must mostly hit terrain, or 'they agree' is a statement about empty air");
    }

    /// <summary>A spread of points over terrain, one per displacement sampled across the map.</summary>
    /// <remarks>
    /// Spread through the displacement list rather than taken from the front: displacements are
    /// stored in face order, which is spatial, so the first dozen are all in one corner of the map
    /// and would exercise the same few bounding boxes.
    /// </remarks>
    private static IEnumerable<SurfaceVertex> Targets(
        (IReadOnlyList<BspSurface> Surfaces, BspTerrain Terrain) map)
    {
        List<BspSurface> displacements = [.. map.Surfaces.Where(each => each.IsDisplacement)];

        for (int index = 0; index < displacements.Count; index += Math.Max(1, displacements.Count / 12))
        {
            IReadOnlyList<SurfaceVertex> corners = map.Terrain.ReadTriangles(displacements[index]);

            if (corners.Count > 0)
            {
                yield return corners[0];
            }
        }
    }

    /// <summary>Groups a triangle list's corners into triangles.</summary>
    private static IEnumerable<DisplacementTriangle> Triples(IReadOnlyList<SurfaceVertex> corners)
    {
        for (int triangle = 0; triangle + 2 < corners.Count; triangle += 3)
        {
            yield return DisplacementTriangle.From(
                (corners[triangle].X, corners[triangle].Y, corners[triangle].Z),
                (corners[triangle + 1].X, corners[triangle + 1].Y, corners[triangle + 1].Z),
                (corners[triangle + 2].X, corners[triangle + 2].Y, corners[triangle + 2].Z));
        }
    }

    [Test]
    public void Sweep_HighAboveTheMap_IsNotStopped()
    {
        // **The control, and without it every assertion above is satisfied by a sweep that always
        // reports a hit.** Well above the map's terrain, travelling sideways, nothing may stop it.
        if (Map() is not { } map)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        DisplacementCollision collision = DisplacementCollision.From(map.Surfaces, map.Terrain);

        BspSurface surface = map.Surfaces.First(each => each.IsDisplacement);
        SurfaceVertex target = map.Terrain.ReadTriangles(surface)[0];

        float fraction = collision.Sweep(
            target.X, target.Y, target.Z + 8192f,
            target.X + 256f, target.Y, target.Z + 8192f,
            6f);

        fraction.ShouldBe(1f, "eight thousand units above the ground there is no terrain to hit");
    }

    [Test]
    public void Sweep_WithNoDisplacements_IsAlwaysClear()
    {
        // A map with no terrain must not be a special case at the call site, and `Empty` is the
        // value a failed read produces — so this is the path a broken map takes.
        DisplacementCollision.Empty.Sweep(0f, 0f, 0f, 0f, 0f, -1000f, 6f).ShouldBe(1f);
        DisplacementCollision.Empty.Count.ShouldBe(0);
    }
}
