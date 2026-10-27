using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

using Swept = (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance);

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A terrain sweep bounded by the brush trace's fraction gives the unbounded sweep's answer wherever that answer is
/// shorter, and the bound otherwise — so `MapLevel.TraceBrushOnly`'s choice between terrain and brushes is unchanged.
/// </summary>
/// <remarks>
/// **Why the bound exists** (2026-10-26, measurements/2026-10-26-threaded-particles-and-sun.md): the light cache's sky
/// trace is 57,016 units long, so its travel box overlapped most of a map's displacements and every triangle of each was
/// swept, while the brush trace had already stopped at the skybox. The engine's world trace tests a displacement only in
/// the leaves the ray reaches before it stops (`CM_TraceToLeaf`), so the bound is nearer the engine, not a shortcut.
/// **The control is the unbounded sweep itself**: over a seeded set of rays and bounds, the bounded answer is the
/// unbounded one, field for field, whenever the unbounded fraction is below the bound.
/// </remarks>
public sealed class DisplacementSweepLimitTests
{
    [Test]
    public void SweepSurface_BoundedByAShorterFraction_IsTheUnboundedAnswerWhereItIsShorterAndTheBoundElsewhere()
    {
        DisplacementCollision terrain = Hills();
        Random random = new(514);
        int shorter = 0;
        int longer = 0;

        for (int ray = 0; ray < 4000; ray++)
        {
            (float X, float Y, float Z) from = (Next(random, -300f, 1900f), Next(random, -300f, 1900f), Next(random, -40f, 220f));
            (float X, float Y, float Z) to = (Next(random, -3000f, 4600f), Next(random, -3000f, 4600f), Next(random, -500f, 900f));
            (float X, float Y, float Z) extents = ray % 3 == 0 ? (0f, 0f, 0f) : (8f, 8f, 16f);
            float limit = ray % 5 == 0 ? 1f : Next(random, 0f, 1f);

            Swept whole = terrain.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, BspLeafTree.MaskSolid);
            Swept bounded = terrain.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, BspLeafTree.MaskSolid, limit);

            if (whole.Fraction < limit)
            {
                bounded.ShouldBe(whole, $"ray {ray}");
                shorter++;
            }
            else
            {
                bounded.ShouldBe((limit, -1, false, (0f, 0f, 0f), 0f), $"ray {ray}");
                longer++;
            }
        }

        // The control: both halves of the claim were exercised many times.
        shorter.ShouldBeGreaterThan(500);
        longer.ShouldBeGreaterThan(500);
    }

    private static float Next(Random random, float low, float high) => low + ((float)random.NextDouble() * (high - low));

    /// <summary>A 4 × 4 field of displacements, each 8 × 8 quads of rolling hills, 2048 units square.</summary>
    private static DisplacementCollision Hills()
    {
        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> displacements = [];
        const float Cell = 64f;

        for (int across = 0; across < 4; across++)
        {
            for (int down = 0; down < 4; down++)
            {
                List<SurfaceVertex> corners = [];

                for (int x = 0; x < 8; x++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        SurfaceVertex At(int i, int j)
                        {
                            float wx = ((across * 8) + i) * Cell;
                            float wy = ((down * 8) + j) * Cell;

                            return new SurfaceVertex(wx, wy, 80f * MathF.Sin(wx / 300f) * MathF.Cos(wy / 250f), 0f, 0f, 0f, 0f);
                        }

                        corners.AddRange([At(x, y), At(x + 1, y), At(x + 1, y + 1)]);
                        corners.AddRange([At(x, y), At(x + 1, y + 1), At(x, y + 1)]);
                    }
                }

                displacements.Add(((across * 4) + down, corners));
            }
        }

        return DisplacementCollision.FromTriangles(displacements);
    }
}
