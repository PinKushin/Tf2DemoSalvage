using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>An axis-aligned box as the compact ledge the real engine's <c>BBoxToCollide</c> builds, word for word.</summary>
/// <remarks>
/// **Read out of the live engine** (`vphysics-virtual-terrain-drop` with `TF2VPHYSICS_PROBE_TRACE_IMPACTS=1`, which dumps the body's
/// ledge at its first attach): the point order, each triangle's three edge words and each header's pierce field, for a box of half
/// 4. A hand-wound cube stood here before, and it started every minimize from a different vertex than the engine's box does: on
/// the virtual-terrain drop that walked one corner onto the diagonal of a terrain triangle, where the engine's own box never goes,
/// and the pair froze behind the triangle's back face (B369).
/// </remarks>
internal static class IvpTestCube
{
    /// <summary>Each point's signs, in the engine's order.</summary>
    private static readonly (int X, int Y, int Z)[] Signs =
    [
        (1, -1, 1), (-1, 1, 1), (-1, -1, 1), (1, 1, 1),
        (1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1),
    ];

    /// <summary>Each triangle's edge words' low sixteen bits: the points its three edges start at.</summary>
    private static readonly List<(int A, int B, int C)> Triangles =
    [
        (0, 1, 2), (0, 3, 1), (2, 4, 0), (2, 5, 4),
        (2, 1, 5), (5, 1, 6), (0, 7, 3), (0, 4, 7),
        (1, 3, 6), (6, 3, 7), (7, 4, 6), (6, 4, 5),
    ];

    /// <summary>Each edge word's bits 16–30, signed: the words to its twin.</summary>
    private static readonly (int, int, int)[] Offsets =
    [
        (6, 15, 8), (22, 27, -6), (6, 19, -8), (6, 32, -6),
        (-15, 3, -6), (-3, 13, 24), (6, 12, -22), (-19, 11, -6),
        (-27, 3, -13), (-3, -12, 4), (-11, 3, -4), (-3, -32, -24),
    ];

    /// <summary>Each header's bits 12–23: triangle <c>t</c> names <c>11 − t</c>.</summary>
    private static readonly int[] Pierces = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

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
        List<Vector3> points = [];

        foreach ((int sx, int sy, int sz) in Signs)
        {
            points.Add(new Vector3(sx * x, sy * y, sz * z));
        }

        return [new PhysicsLedge(points, Triangles, Offsets, Pierces, new int[Triangles.Count], Vector3.Zero, MathF.Sqrt((x * x) + (y * y) + (z * z)))];
    }
}
