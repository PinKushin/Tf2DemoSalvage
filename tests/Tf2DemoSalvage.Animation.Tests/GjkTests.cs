using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <see cref="Gjk.Distance"/> — the closest-points algorithm behind IVP's own mindist (B58, D149).
/// </summary>
/// <remarks>
/// **Two real convex ledges, not invented shapes (D38).** Both boxes here are the same construction
/// `IvpWorldContactConformanceTests.Floor` and `IvpWorldLedgeSupportTests` already use — a genuine
/// piece of geometry this project's own collision builder produces, positioned twice rather than
/// hand-rolled into a bespoke fixture.
/// </remarks>
public sealed class GjkTests
{
    [Test]
    public void Distance_BetweenTwoSeparatedBoxes_IsTheExactGapBetweenTheirFaces()
    {
        IvpWorldLedge boxAtOrigin = Box(Vector3.Zero).Ledges[0];
        IvpWorldLedge boxAbove = Box(new Vector3(0f, 0f, 500f)).Ledges[0];

        // Origin box: z in [-100, 0]. Second box shifted +500: z in [400, 500]. Gap is exactly 400.
        GjkResult? result = Gjk.Distance(boxAtOrigin.Support, boxAbove.Support);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Distance, Is.EqualTo(400f).Within(0.01f));
        Assert.That(result.Value.PointOnA.Z, Is.EqualTo(0f).Within(0.01f));
        Assert.That(result.Value.PointOnB.Z, Is.EqualTo(400f).Within(0.01f));
    }

    [Test]
    public void Distance_BetweenTwoTouchingBoxes_IsZero()
    {
        IvpWorldLedge boxAtOrigin = Box(Vector3.Zero).Ledges[0];
        IvpWorldLedge boxAbove = Box(new Vector3(0f, 0f, 100f)).Ledges[0];

        // Origin box top face is z=0; the second box's bottom face is z=0 too. Exactly touching.
        GjkResult? result = Gjk.Distance(boxAtOrigin.Support, boxAbove.Support);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Distance, Is.EqualTo(0f).Within(0.5f));
    }

    [Test]
    public void Distance_OffsetSidewaysAndUp_IsTheDiagonalGap()
    {
        // Origin box spans x,y in [-1000, 1000], z in [-100, 0]. Shifted +2500 in x and +300 in z,
        // the nearest faces are the origin box's +X face (x=1000) and the shifted box's -X face
        // (x=1500), and its bottom face (z=200) against the origin box's top (z=0) — the true
        // closest points are the two boxes' nearest EDGES, at a diagonal gap this predicts exactly:
        // dx = 1500 - 1000 = 500, dz = 200 - 0 = 200, distance = sqrt(500^2 + 200^2).
        IvpWorldLedge boxAtOrigin = Box(Vector3.Zero).Ledges[0];
        IvpWorldLedge shifted = Box(new Vector3(2500f, 0f, 300f)).Ledges[0];

        GjkResult? result = Gjk.Distance(boxAtOrigin.Support, shifted.Support);

        float expected = MathF.Sqrt((500f * 500f) + (200f * 200f));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Distance, Is.EqualTo(expected).Within(0.1f));
    }

    [Test]
    public void Distance_FromAPointOverTheInteriorOfAFace_IsThePerpendicularGap()
    {
        // A single point is its own support function — the same point for every direction — and
        // this one sits well inside the box's top face footprint (x,y in [-1000,1000], off the
        // diagonal that splits the face into its two triangles), not near an edge or a corner.
        // Reducing the working simplex correctly here needs the actual FACE branch of the triangle
        // test — a box's many vertices give a segment-only reduction nothing to converge on.
        IvpWorldLedge box = Box(Vector3.Zero).Ledges[0];
        Vector3 point = new(300f, 200f, 500f);

        GjkResult? result = Gjk.Distance(_ => point, box.Support);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Distance, Is.EqualTo(500f).Within(0.01f));
        Assert.That(result.Value.PointOnB.Z, Is.EqualTo(0f).Within(0.01f));
        Assert.That(result.Value.PointOnB.X, Is.EqualTo(300f).Within(0.01f));
        Assert.That(result.Value.PointOnB.Y, Is.EqualTo(200f).Within(0.01f));
    }

    /// <summary>The same box `IvpWorldContactConformanceTests.Floor` builds, translated.</summary>
    private static IvpWorldCollision Box(Vector3 offset)
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
            (4, 5, 6), (4, 6, 7),
            (0, 2, 1), (0, 3, 2),
            (0, 1, 5), (0, 5, 4),
            (2, 3, 7), (2, 7, 6),
            (1, 2, 6), (1, 6, 5),
            (3, 0, 4), (3, 4, 7),
        ];

        IvpWorldCollision world = new();

        world.Add(
            points,
            triangles,
            Ivp(0f, 0f, -Depth / 2f),
            Wide * 2f * Metre,
            offset,
            IvpWorldCollision.ContentsSolid);

        return world;
    }

    /// <summary>A Source point in IVP's own convention — Valve's own map (B400).</summary>
    /// <remarks>
    /// Read out of `vphysics.dll`; see <c>IvpWorldContactConformanceTests.Ivp</c> for why this calls
    /// it rather than writing the three lines out again.
    /// </remarks>
    private static Vector3 Ivp(float x, float y, float z)
    {
        (float ivpX, float ivpY, float ivpZ) = IvpTransform.Position(x, y, z);

        return new Vector3(ivpX, ivpY, ivpZ);
    }

    private const float Metre = 0.0254f;
}
