using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <see cref="IvpWorldLedge.Support"/> — the GJK support function a convex-convex narrow phase is
/// built on (B58, D149).
/// </summary>
/// <remarks>
/// **The primitive this project never had, read out of `vphysics.dll` and recorded in
/// `docs/findings/51`.** IVP's own mindist geometry (`FUN_180096680`) is a warm-started GJK/EPA
/// solver: a simplex built entirely out of calls to each shape's own extreme-point-along-a-direction
/// function. A ledge here could answer "which face is shallowest" but never "which vertex is
/// furthest this way", because only its planes were kept — a plane set bounds a hull's interior but
/// cannot reproduce its vertices. This is the first thing that can ask the second question.
///
/// **A real box, not an invented shape (D38).** The eight-corner slab built here is the SAME
/// construction `IvpWorldContactConformanceTests.Floor` uses for the rest of this suite — a convex
/// piece this project's own collision builder already produces from real triangle data, not a
/// fixture shaped to make the assertion easy.
/// </remarks>
public sealed class IvpWorldLedgeSupportTests
{
    [Test]
    public void Support_AlongAUniqueDiagonal_ReturnsTheExtremeCorner()
    {
        IvpWorldLedge ledge = Box().Ledges[0];

        Vector3 corner = ledge.Support(new Vector3(1f, 1f, 1f));

        // x+y+z is maximised at exactly one of the box's eight corners: the top, +X, +Y one.
        Assert.That(corner.X, Is.EqualTo(1000f).Within(0.01f));
        Assert.That(corner.Y, Is.EqualTo(1000f).Within(0.01f));
        Assert.That(corner.Z, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Support_AlongTheOppositeUniqueDiagonal_ReturnsTheOppositeCorner()
    {
        IvpWorldLedge ledge = Box().Ledges[0];

        // Maximises x − y − z, which is unique at the bottom, +X, −Y corner.
        Vector3 corner = ledge.Support(new Vector3(1f, -1f, -1f));

        Assert.That(corner.X, Is.EqualTo(1000f).Within(0.01f));
        Assert.That(corner.Y, Is.EqualTo(-1000f).Within(0.01f));
        Assert.That(corner.Z, Is.EqualTo(-100f).Within(0.01f));
    }

    [Test]
    public void Support_WithNoVertices_ReturnsTheCentre()
    {
        IvpWorldLedge empty = new(Vector3.One, 1f, [], IvpWorldCollision.ContentsSolid, []);

        Vector3 result = empty.Support(Vector3.UnitX);

        Assert.That(result, Is.EqualTo(Vector3.One));
    }

    /// <summary>The same box `IvpWorldContactConformanceTests.Floor` builds — a real convex ledge.</summary>
    private static IvpWorldCollision Box()
    {
        const float Wide = 1000f;
        const float Depth = 100f;

        List<Vector3> points =
        [
            Ivp(-Wide, -Wide, -Depth), Ivp(Wide, -Wide, -Depth),
            Ivp(Wide, Wide, -Depth), Ivp(-Wide, Wide, -Depth),
            Ivp(-Wide, -Wide, 0f), Ivp(Wide, -Wide, 0f),
            Ivp(Wide, Wide, 0f), Ivp(-Wide, Wide, 0f),
        ];

        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7),       // top,    +Z
            (0, 2, 1), (0, 3, 2),       // bottom, -Z
            (0, 1, 5), (0, 5, 4),       // -Y
            (2, 3, 7), (2, 7, 6),       // +Y
            (1, 2, 6), (1, 6, 5),       // +X
            (3, 0, 4), (3, 4, 7),       // -X
        ];

        IvpWorldCollision world = new();

        world.Add(
            points,
            triangles,
            Ivp(0f, 0f, -Depth / 2f),
            Wide * 2f * Metre,
            Vector3.Zero,
            IvpWorldCollision.ContentsSolid);

        return world;
    }

    private static Vector3 Ivp(float x, float y, float z) =>
        new(x * Metre, z * Metre, -y * Metre);

    private const float Metre = 0.0254f;
}
