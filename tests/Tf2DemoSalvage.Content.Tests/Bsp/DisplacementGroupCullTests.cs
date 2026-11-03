using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

using Swept = (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance);

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The group cull inside a displacement (B667) never changes a sweep's answer, on hand-built terrain whose answer the
/// brute force (<see cref="DisplacementBruteForce"/>) gives — and it does cull, or the agreement would be about nothing.
/// </summary>
/// <remarks>
/// Each boundary the cull's safety argument leans on has its own case, built so that a cull one step too eager changes
/// the answer: a surface `DISPCOLL_DIST_EPSILON` above an earlier one (the box's pad), a long ray past a triangle's corner
/// inside `SweepAABBTriIntersect`'s 0.001 window (the window tolerance), and a group whose box the ray enters exactly at the
/// best hit so far (the `>=`, which can only show in the count, since sweeping one more group never changes an answer).
/// </remarks>
public sealed class DisplacementGroupCullTests
{
    private const float Cell = 64f;

    /// <summary>Seven by seven quads: 98 triangles, which is not a multiple of the group size, so the last group is short.</summary>
    private const int Quads = 7;

    [Test]
    public void SweepSurface_LongDiagonalRaysOverSyntheticHills_EqualsTheBruteForceExactlyAndCullsMostGroups()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> hills = Hills();
        DisplacementCollision collision = DisplacementCollision.FromTriangles(hills);
        Random random = new(667);
        SweepCounts counts = default;
        int stopped = 0;
        int second = 0;
        const int Rays = 3000;

        for (int ray = 0; ray < Rays; ray++)
        {
            // From above the hills to far off and below them, so a ray crosses the terrain's height inside the field often.
            (float X, float Y, float Z) from = (Next(random, -200f, 1500f), Next(random, -200f, 600f), Next(random, 80f, 200f));
            (float X, float Y, float Z) to = (Next(random, -2500f, 4000f), Next(random, -2000f, 2500f), Next(random, -1500f, -100f));
            (float X, float Y, float Z) extents = ray % 3 == 0 ? (0f, 0f, 0f) : (6f, 6f, 16f);
            float limit = ray % 4 == 0 ? Next(random, 0.2f, 1f) : 1f;

            Swept culled = collision.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, BspLeafTree.MaskSolid, limit, ref counts);
            Swept brute = DisplacementBruteForce.Sweep(hills, from, to, extents, limit);

            DisplacementBruteForce.Bits(culled).ShouldBe(DisplacementBruteForce.Bits(brute), $"ray {ray} from {from} to {to}, extents {extents}, limit {limit}");
            stopped += brute.Texdata >= 0 ? 1 : 0;
            second += brute.SurfaceProp2 ? 1 : 0;
        }

        // The controls: terrain stopped many rays, both surfaceprops were struck, and the cull skipped most groups it met.
        stopped.ShouldBeGreaterThan(Rays / 3, $"culled {counts.GroupsCulled}, swept {counts.GroupsSwept}, second {second}");
        second.ShouldBeInRange(1, stopped - 1);
        counts.GroupsCulled.ShouldBeGreaterThan(counts.GroupsSwept, $"culled {counts.GroupsCulled}, swept {counts.GroupsSwept}");
    }

    [Test]
    public void SweepSurface_AxisParallelZeroLengthAndInsideStartRays_EqualsTheBruteForceExactly()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> hills = Hills();
        DisplacementCollision collision = DisplacementCollision.FromTriangles(hills);
        List<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> rays = [];

        for (float along = 3f; along < Quads * Cell * 3; along += 37f)
        {
            for (float height = -60f; height <= 60f; height += 15f)
            {
                rays.Add(((-100f, along, height), (1500f, along, height)));      // along X
                rays.Add(((along, -100f, height), (along, 600f, height)));       // along Y
                rays.Add(((along, along * 0.1f, 200f), (along, along * 0.1f, -200f))); // down Z
                rays.Add(((along, along * 0.1f, height), (along, along * 0.1f, height))); // zero length
                rays.Add(((along, 200f, height), (along + 900f, 100f, height - 300f))); // starting inside a group's box
            }
        }

        int stopped = 0;

        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
        {
            foreach ((float X, float Y, float Z) extents in (ReadOnlySpan<(float X, float Y, float Z)>)[(0f, 0f, 0f), (8f, 8f, 8f)])
            {
                Swept culled = collision.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, BspLeafTree.MaskSolid);
                Swept brute = DisplacementBruteForce.Sweep(hills, from, to, extents, 1f);

                DisplacementBruteForce.Bits(culled).ShouldBe(DisplacementBruteForce.Bits(brute), $"from {from} to {to}, extents {extents}");
                stopped += brute.Texdata >= 0 ? 1 : 0;
            }
        }

        stopped.ShouldBeGreaterThan(rays.Count / 4, "the control: most of these rays meet terrain");
    }

    /// <remarks>
    /// Two flat surfaces, the later 1/64 above the earlier, struck from above. The earlier's hit sets the best at
    /// `(128 − 1/32) / 256`; the later's own box top is entered at `(128 − 1/64) / 256`, after that, yet its triangle
    /// answers `(128 − 1/64 − 1/32) / 256`, before it — because `Against` backs every entry off by `DISPCOLL_DIST_EPSILON`.
    /// A box grown by less than that epsilon culls the hit.
    /// </remarks>
    [Test]
    public void SweepSurface_ALaterSurfaceWithinDistEpsilonAbove_IsTheOneStruck()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> stacked = [(1, Flat(0f)), (2, Flat(1f / 64f))];
        DisplacementCollision collision = DisplacementCollision.FromTriangles(stacked);

        Swept culled = collision.SweepSurface(-5f, -5f, 128f, -5f, -5f, -128f, (0f, 0f, 0f), BspLeafTree.MaskSolid);

        DisplacementBruteForce.Bits(culled).ShouldBe(DisplacementBruteForce.Bits(DisplacementBruteForce.Sweep(stacked, (-5f, -5f, 128f), (-5f, -5f, -128f), (0f, 0f, 0f), 1f)));
        culled.Texdata.ShouldBe(2, "the surface above, listed second, is struck first");
    }

    /// <remarks>
    /// A ray 100,000 units long passes 10 units outside the corner of a flat right triangle while crossing its plane, so
    /// its X and Y windows miss each other by 1e-4 of its length: inside the 0.001 `SweepAABBTriIntersect` tolerates, so
    /// the triangle stops it. Grown by one unit the box's windows still miss by 8e-5, so only the same tolerance keeps the
    /// box from culling the hit.
    /// </remarks>
    [Test]
    public void SweepSurface_ALongRayPastACornerWithinTheWindowTolerance_StillStopsOnIt()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> corner =
            [(3, [Vertex(0f, 0f, 0f), Vertex(100f, 0f, 0f), Vertex(0f, 100f, 0f)])];
        DisplacementCollision collision = DisplacementCollision.FromTriangles(corner);
        const float Half = 50000f;
        const float Miss = 10f;
        (float X, float Y, float Z) from = ((-Miss / 2f) - Half, (-Miss / 2f) + Half, 50f);
        (float X, float Y, float Z) to = ((-Miss / 2f) + Half, (-Miss / 2f) - Half, -50f);

        Swept brute = DisplacementBruteForce.Sweep(corner, from, to, (0f, 0f, 0f), 1f);
        Swept culled = collision.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), BspLeafTree.MaskSolid);

        brute.Texdata.ShouldBe(3, "the control: the triangle itself stops this ray, inside the tolerance");
        DisplacementBruteForce.Bits(culled).ShouldBe(DisplacementBruteForce.Bits(brute));
    }

    /// <remarks>
    /// Every number is exact in binary: a drop from 128 to −128 strikes a flat triangle at 0 at `(128 − 1/32) / 256`, and a
    /// second one at −31/32 has a box whose top, grown by one unit, is entered at `(128 − 1/32) / 256` exactly. A group
    /// entered at the best hit can hold no shorter one, so it is culled — the count is the only place that shows.
    /// </remarks>
    [Test]
    public void SweepSurface_AGroupEnteredExactlyAtTheBestHit_IsCulled()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> pair = [(1, Flat(0f)), (2, Flat(-31f / 32f))];
        DisplacementCollision collision = DisplacementCollision.FromTriangles(pair);
        SweepCounts counts = default;

        Swept culled = collision.SweepSurface(-5f, -5f, 128f, -5f, -5f, -128f, (0f, 0f, 0f), BspLeafTree.MaskSolid, 1f, ref counts);

        culled.Fraction.ShouldBe(4095f / 8192f);
        culled.Texdata.ShouldBe(1);
        counts.ShouldBe(new SweepCounts(GroupsCulled: 1, GroupsSwept: 1, TrianglesSwept: 1));
    }

    private static float Next(Random random, float low, float high) => low + ((float)random.NextDouble() * (high - low));

    private static SurfaceVertex Vertex(float x, float y, float z, float alpha = 0f) => new(x, y, z, 0f, 0f, 0f, 0f, alpha);

    /// <summary>One upward-facing triangle at a height, round (−5, −5).</summary>
    private static IReadOnlyList<SurfaceVertex> Flat(float z) => [Vertex(-10f, -10f, z), Vertex(10f, -10f, z), Vertex(-10f, 10f, z)];

    /// <summary>Three displacements of rolling hills in a row, each <see cref="Quads"/> square, with blend alpha that crosses the second surfaceprop's threshold.</summary>
    private static List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> Hills()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> displacements = [];

        for (int across = 0; across < 3; across++)
        {
            List<SurfaceVertex> corners = [];

            SurfaceVertex At(int i, int j)
            {
                float x = ((across * Quads) + i) * Cell;
                float y = j * Cell;

                return Vertex(x, y, 70f * MathF.Sin(x / 210f) * MathF.Cos(y / 170f), 0.5f + (0.5f * MathF.Sin((x + y) / 300f)));
            }

            for (int i = 0; i < Quads; i++)
            {
                for (int j = 0; j < Quads; j++)
                {
                    corners.AddRange([At(i, j), At(i + 1, j), At(i + 1, j + 1)]);
                    corners.AddRange([At(i, j), At(i + 1, j + 1), At(i, j + 1)]);
                }
            }

            displacements.Add((10 + across, corners));
        }

        return displacements;
    }
}
