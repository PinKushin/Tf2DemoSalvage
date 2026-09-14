using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Sides for IVP's time-of-impact searches: a tetrahedron's topology, and a body placed where a test wants it (B369).</summary>
internal static class IvpSearchFixtures
{
    /// <summary>The interval every search test runs over: one step of <c>0.015</c> seconds.</summary>
    public const double End = 0.015d;

    /// <summary>The identity, unmoved.</summary>
    public static readonly IvpMatrix Identity = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

    /// <summary>A quarter turn about Z: <c>+X</c> to <c>+Y</c>, <c>+Y</c> to <c>−X</c>.</summary>
    public static readonly IvpMatrix QuarterTurnAboutZ =
        IvpMatrix.FromRotation((0f, 0f, 0.70710677f, 0.70710677f), (0d, 0d, 0d));

    /// <summary>The tetrahedron's topology: every edge word hops to its twin, as in <c>IvpLedgeTopologyConformanceTests</c>.</summary>
    /// <returns>Triangles <c>(0, 1, 2)</c>, <c>(0, 3, 1)</c>, <c>(0, 2, 3)</c>, <c>(1, 3, 2)</c>.</returns>
    /// <remarks>
    /// Edge <c>(0, 0)</c> runs from point 0 to point 1; its twin is triangle 1's third edge, from point 1 to point 0,
    /// whose triangle's third point is point 3. The ring from edge <c>(0, 0)</c> visits <c>0 → 2</c>, <c>0 → 3</c>,
    /// <c>0 → 1</c>.
    /// </remarks>
    public static IvpLedgeTopology TetrahedronTopology() =>
        new(
            [(0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2)],
            [(6, 13, 6), (6, 7, -6), (-6, 4, -6), (-7, -4, -13)],
            [3, 3, 1, 0],
            [0, 0, 0, 0]);

    /// <summary>A tetrahedron side on a body at a position, falling along −Z, resting when it does not fall.</summary>
    /// <param name="points">The tetrahedron's four points.</param>
    /// <param name="position">Where the body is.</param>
    /// <param name="fallingAt">How fast it falls, in inches a second; negative rises.</param>
    /// <param name="core">The core's bounds.</param>
    /// <returns>The side.</returns>
    public static IvpSearchSide Side(
        IReadOnlyList<(float X, float Y, float Z)> points,
        (double X, double Y, double Z) position,
        float fallingAt,
        IvpCoreBounds core) =>
        Side(points, TetrahedronTopology(), position, fallingAt, resting: fallingAt == 0f, core);

    /// <summary>A side on a body at a position, falling along −Z.</summary>
    /// <param name="points">The ledge's points.</param>
    /// <param name="topology">The ledge's topology.</param>
    /// <param name="position">Where the body is.</param>
    /// <param name="fallingAt">How fast it falls, in inches a second; negative rises.</param>
    /// <param name="resting">Whether the motion cache treats the body as not moving.</param>
    /// <param name="core">The core's bounds.</param>
    /// <returns>The side.</returns>
    public static IvpSearchSide Side(
        IReadOnlyList<(float X, float Y, float Z)> points,
        IvpLedgeTopology topology,
        (double X, double Y, double Z) position,
        float fallingAt,
        bool resting,
        IvpCoreBounds core)
    {
        IvpRigidBody body = new()
        {
            Position = position,
            PreviousVelocity = (0f, 0f, -fallingAt),
            Orientation = (0f, 0f, 0f, 1f),
            WorkingOrientation = (0f, 0f, 0f, 1f),
            LastStepped = 0d,
            InverseStep = 66f,
        };

        IvpMatrix current = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), position);

        return new IvpSearchSide(points, topology, new IvpMotionCache(body, current, resting), core);
    }
}
