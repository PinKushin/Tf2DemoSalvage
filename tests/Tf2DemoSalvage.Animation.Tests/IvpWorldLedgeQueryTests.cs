using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <see cref="IvpWorldCollision.LedgesInSphere"/> — the grid query <c>IvpContact.Find</c> uses in place of
/// walking every ledge in the map.
/// </summary>
/// <remarks>
/// **The property that makes the swap safe, tested directly**: every ledge whose bounding sphere meets
/// the query sphere is returned, each once, in ascending index order. The walk it replaced visited
/// ledges by index and applied the same sphere test, so a superset in that order raises the same
/// contacts in the same sequence.
/// </remarks>
public sealed class IvpWorldLedgeQueryTests
{
    /// <remarks>
    /// Four hundred cubes scattered across three cell sizes of space, some large enough to land in the
    /// coarse tier, probed from two hundred places — deterministic, so a failure names a reproducible
    /// seed rather than a flake.
    /// </remarks>
    [Test]
    public void LedgesInSphere_ScatteredLedges_ReturnsEveryOverlapOnceInAscendingOrder()
    {
        ulong draws = 20260915;
        IvpWorldCollision world = new();

        for (int index = 0; index < 400; index++)
        {
            Vector3 centre = new(Coordinate(ref draws), Coordinate(ref draws), Coordinate(ref draws));
            float half = index % 20 == 0 ? 3000f : 4f + (Unit(ref draws) * 200f);

            AddCube(world, centre, half);
        }

        List<int> found = [];

        for (int probe = 0; probe < 200; probe++)
        {
            Vector3 at = new(Coordinate(ref draws), Coordinate(ref draws), Coordinate(ref draws));
            float radius = 1f + (Unit(ref draws) * 300f);

            world.LedgesInSphere(at, radius, found);

            for (int position = 1; position < found.Count; position++)
            {
                found[position].ShouldBeGreaterThan(found[position - 1], "ascending and unique");
            }

            for (int ledge = 0; ledge < world.Ledges.Count; ledge++)
            {
                IvpWorldLedge candidate = world.Ledges[ledge];
                float apart = candidate.Radius + radius;

                if ((candidate.Center - at).LengthSquared() <= apart * apart)
                {
                    found.ShouldContain(ledge, $"probe {probe}: ledge {ledge} meets the sphere");
                }
            }
        }
    }

    [Test]
    public void LedgesInSphere_NothingNearby_ReturnsNothing()
    {
        IvpWorldCollision world = new();

        AddCube(world, Vector3.Zero, 10f);

        List<int> found = [99];

        world.LedgesInSphere(new Vector3(100_000f, 0f, 0f), 5f, found);

        found.ShouldBeEmpty();
    }

    private static float Coordinate(ref ulong state) => (Unit(ref state) * 8000f) - 4000f;

    /// <summary>SplitMix64's next draw as a float in [0, 1): a fixed stream, so every run tests the same scatter.</summary>
    private static float Unit(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong mixed = state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;

            return (mixed >> 40) / (float)(1UL << 24);
        }
    }

    /// <summary>An axis-aligned cube of the given half extent, in Source units, as an IVPS ledge.</summary>
    private static void AddCube(IvpWorldCollision world, Vector3 centre, float half)
    {
        List<Vector3> points = [];

        foreach (float z in new[] { -half, half })
        {
            points.Add(Ivp(centre + new Vector3(-half, -half, z)));
            points.Add(Ivp(centre + new Vector3(half, -half, z)));
            points.Add(Ivp(centre + new Vector3(half, half, z)));
            points.Add(Ivp(centre + new Vector3(-half, half, z)));
        }

        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7),
            (0, 2, 1), (0, 3, 2),
            (0, 1, 5), (0, 5, 4),
            (2, 3, 7), (2, 7, 6),
            (1, 2, 6), (1, 6, 5),
            (3, 0, 4), (3, 4, 7),
        ];

        world.Add(
            points,
            triangles,
            Ivp(centre),
            half * MathF.Sqrt(3f) * Metre,
            Vector3.Zero,
            IvpWorldCollision.ContentsSolid);
    }

    private static Vector3 Ivp(Vector3 source)
    {
        (float x, float y, float z) = IvpTransform.Position(source.X, source.Y, source.Z);

        return new Vector3(x, y, z);
    }

    private const float Metre = 0.0254f;
}
