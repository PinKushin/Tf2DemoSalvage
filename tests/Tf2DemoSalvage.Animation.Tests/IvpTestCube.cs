using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>An axis-aligned cube as one compact ledge, with its edge words linked to their twins.</summary>
/// <remarks>
/// **The edge offsets are what the minimize walks.** A cube whose offsets were all zero hopped every edge to itself, so a pair
/// could never move off the vertices it started on and measured the centers' distance — nine, where the faces were one apart —
/// and two bodies drove through each other with the pair still filed far.
/// </remarks>
internal static class IvpTestCube
{
    private static readonly List<(int A, int B, int C)> Triangles =
    [
        (4, 5, 6), (4, 6, 7), (0, 2, 1), (0, 3, 2),
        (0, 1, 5), (0, 5, 4), (2, 3, 7), (2, 7, 6),
        (1, 2, 6), (1, 6, 5), (3, 0, 4), (3, 4, 7),
    ];

    /// <summary>The triangle across the cube from each: the other face's triangle of the same winding.</summary>
    private static readonly int[] Across = [2, 3, 0, 1, 6, 7, 4, 5, 10, 11, 8, 9];

    /// <summary>The cube as a ledge list, a body's <see cref="Animating.IvpRigidBody.Ledges"/>.</summary>
    /// <param name="half">The half extent.</param>
    /// <returns>One ledge.</returns>
    public static List<PhysicsLedge> Ledges(float half) => Box(half, half, half);

    /// <summary>An axis-aligned box as a ledge list.</summary>
    /// <param name="x">The half extent along x.</param>
    /// <param name="y">The half extent along y.</param>
    /// <param name="z">The half extent along z.</param>
    /// <returns>One ledge.</returns>
    public static List<PhysicsLedge> Box(float x, float y, float z)
    {
        List<Vector3> points =
        [
            new(-x, -y, -z), new(x, -y, -z),
            new(x, y, -z), new(-x, y, -z),
            new(-x, -y, z), new(x, -y, z),
            new(x, y, z), new(-x, y, z),
        ];

        (int, int, int)[] offsets = new (int, int, int)[Triangles.Count];

        for (int triangle = 0; triangle < Triangles.Count; triangle++)
        {
            offsets[triangle] = (Twin(triangle, 0), Twin(triangle, 1), Twin(triangle, 2));
        }

        return [new PhysicsLedge(points, Triangles, offsets, Across, new int[Triangles.Count], Vector3.Zero, MathF.Sqrt((x * x) + (y * y) + (z * z)))];
    }

    /// <summary>The words from an edge to the edge running the other way — <c>16·t + 4 + 4·s</c> addresses, over four.</summary>
    private static int Twin(int triangle, int slot)
    {
        (int from, int to) = Edge(triangle, slot);

        for (int other = 0; other < Triangles.Count; other++)
        {
            for (int otherSlot = 0; otherSlot < 3; otherSlot++)
            {
                if (Edge(other, otherSlot) == (to, from))
                {
                    return ((16 * other) + (4 * otherSlot) - (16 * triangle) - (4 * slot)) / 4;
                }
            }
        }

        throw new InvalidOperationException("A cube edge has no twin, so its triangles are not consistently wound.");
    }

    private static (int From, int To) Edge(int triangle, int slot)
    {
        (int a, int b, int c) = Triangles[triangle];
        int[] corners = [a, b, c];
        return (corners[slot], corners[(slot + 1) % 3]);
    }
}
