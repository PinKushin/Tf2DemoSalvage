using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A displacement's collision tree as the engine builds it — <c>CCoreDispInfo::GenerateDispSurf</c>,
/// <c>GenerateCollisionSurface</c> and <c>CDispCollTree</c>'s AABB tree and sphere walk (B369).
/// </summary>
/// <remarks>
/// Read from published source (`public/builddisp.cpp:896-966`, `:1918-1994`; `public/dispcoll_common.cpp:406-465`, `:718-780`;
/// `public/dispcoll_common.h:338-424`). A flat power-2 displacement over a 16-unit square, so every vertex, triangle and leaf is
/// predicted by hand. Synthetic conformance (D38).
/// </remarks>
public sealed class DisplacementCollisionTreeConformanceTests
{
    /// <remarks>
    /// **Row i runs along p0→p1, column j along the row's two edge points** — `p0 + edgeInt·i`, then `+ segInt·j`, then the field
    /// vector times its distance.
    /// </remarks>
    [Test]
    public void Build_AFlatSquareWithOneRaisedVertex_PlacesEveryVertexOnTheGrid()
    {
        DisplacementCollisionTree tree = Square(raised: (7, 3f));

        tree.Vertices.Count.ShouldBe(25);
        tree.Vertices[7].ShouldBe(new Vector3(8f, 4f, 3f), "row 1 is 4 along p0→p1 (y), column 2 is 8 along the row (x)");
        tree.Vertices[24].ShouldBe(new Vector3(16f, 16f, 0f));
    }

    /// <remarks>**An even grid index is split bottom-left to top-right, an odd one top-left to bottom-right** (`builddisp.cpp:896-966`).</remarks>
    [Test]
    public void Build_TheFirstTwoCells_AlternateTheirDiagonal()
    {
        DisplacementCollisionTree tree = Square();

        tree.Triangles.Count.ShouldBe(32);
        tree.Triangles[0].ShouldBe((0, 5, 6));
        tree.Triangles[1].ShouldBe((0, 6, 1));
        tree.Triangles[2].ShouldBe((1, 6, 2));
        tree.Triangles[3].ShouldBe((2, 6, 7));
    }

    /// <remarks>
    /// **A small sphere at the centre vertex touches the four middle cells**, each in its own quadrant, and the walk is breadth-first:
    /// root, then the four quadrant nodes in child order, then their leaves, each leaf giving both its triangles.
    /// </remarks>
    [Test]
    public void TrianglesInSphere_AtTheCentreVertex_AnswersTheFourMiddleCellsQuadrantByQuadrant()
    {
        DisplacementCollisionTree tree = Square();

        tree.TrianglesInSphere(new Vector3(8f, 8f, 0f), 0.1f, 0xc00).ShouldBe([10, 11, 12, 13, 18, 19, 20, 21]);
    }

    /// <remarks>**A leaf is emitted only while both its triangles fit under the cap**, so a cap of three answers one leaf.</remarks>
    [Test]
    public void TrianglesInSphere_UnderACap_StopsAtTheLastWholeLeaf()
    {
        DisplacementCollisionTree tree = Square();

        tree.TrianglesInSphere(new Vector3(8f, 8f, 0f), 0.1f, 3).ShouldBe([10, 11]);
    }

    /// <remarks>**A sphere clear of the surface's boxes answers nothing.**</remarks>
    [Test]
    public void TrianglesInSphere_FarAboveTheSurface_AnswersNothing()
    {
        Square().TrianglesInSphere(new Vector3(8f, 8f, 50f), 1f, 0xc00).ShouldBeEmpty();
    }

    private static DisplacementCollisionTree Square((int Index, float Distance)? raised = null)
    {
        (Vector3 Direction, float Distance)[] field = new (Vector3, float)[25];

        if (raised is (int index, float distance))
        {
            field[index] = (Vector3.UnitZ, distance);
        }

        return DisplacementCollisionTree.Build(
            [Vector3.Zero, new Vector3(0f, 16f, 0f), new Vector3(16f, 16f, 0f), new Vector3(16f, 0f, 0f)], 2, field);
    }
}
