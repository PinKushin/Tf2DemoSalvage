using System;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which edges of a fanned face are the polygon's own (B803).</summary>
/// <remarks>
/// A face of n corners fans into triangles (0, i, i + 1), i from 1. `mat_wireframe 3` outlines the face's vertex loop
/// (`engine.dll` `FUN_1800e6780`), so the edges a fan adds, from corner 0 to every corner but its neighbours, are not
/// face edges.
/// </remarks>
public sealed class FaceFanTests
{
    /// <remarks>A triangle is its own polygon: every edge is a face edge.</remarks>
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void IsFaceEdge_OfALoneTriangle_IsEveryEdge(int corner, bool expected) =>
        FaceFan.IsFaceEdge(0, 1, corner).ShouldBe(expected);

    /// <remarks>
    /// A hexagon's four triangles: (0, 1) is the first triangle's opening edge, (i, i + 1) every triangle's middle edge,
    /// (5, 0) the last one's closing edge. Every other is a diagonal from corner 0.
    /// </remarks>
    [TestCase(0, 0, true)]
    [TestCase(0, 1, true)]
    [TestCase(0, 2, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(2, 0, false)]
    [TestCase(2, 1, true)]
    [TestCase(2, 2, false)]
    [TestCase(3, 0, false)]
    [TestCase(3, 1, true)]
    [TestCase(3, 2, true)]
    public void IsFaceEdge_OfAHexagonsFan_IsOnlyItsLoop(int triangle, int corner, bool expected) =>
        FaceFan.IsFaceEdge(triangle, 4, corner).ShouldBe(expected);

    /// <remarks>
    /// **Two indices per vertex, so a run of vertices is the same run of lines at twice the offset.** A fanned quad's
    /// corners say T T F, F T T: its four loop edges are (0, 1), (1, 2), (4, 5) and (5, 3); each diagonal slot is a
    /// zero-length line on its own corner, which lies on the loop and so draws nothing the loop does not.
    /// </remarks>
    [Test]
    public void Outline_OfAFannedQuad_IsItsLoopWithTheDiagonalsCollapsed()
    {
        WorldVertex Corner(bool faceEdge) => new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, FaceEdge: faceEdge);

        FaceFan.Outline([Corner(true), Corner(true), Corner(false), Corner(false), Corner(true), Corner(true)])
            .ShouldBe([0u, 1u, 1u, 2u, 2u, 2u, 3u, 3u, 4u, 5u, 5u, 3u]);
    }

    /// <remarks>A run of triangles that are not a polygon's, a model's, outlines every edge, as a line fill would.</remarks>
    [Test]
    public void Outline_OfUnmarkedTriangles_IsEveryEdge() =>
        FaceFan.Outline([new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f)])
            .ShouldBe([0u, 1u, 1u, 2u, 2u, 0u]);

    /// <remarks>
    /// **A vertex count that is not whole triangles is refused** (B803 review 1, Fable): the fourth vertex's partner would be
    /// index 4, past the end of a four-vertex buffer, which the GPU reads as whatever follows.
    /// </remarks>
    [TestCase(4)]
    [TestCase(5)]
    public void Outline_OfAVertexCountThatIsNotWholeTriangles_Throws(int count) =>
        Should.Throw<ArgumentException>(() => FaceFan.Outline([.. Enumerable.Repeat(new WorldVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), count)]));

    /// <remarks>
    /// **A face starts at each triangle whose first corner's edge is the polygon's** (B803 review 1): a fan's later triangles
    /// open on a diagonal. A quad in the plane z = 2, wound so its cross product points down, and a triangle in x = 5 wound
    /// so it points along −x, each with its vertex normal the other way: the plane takes the vertex normal's side, unit
    /// length, and its distance from the origin along it.
    /// </remarks>
    [Test]
    public void Faces_OfAFannedQuadAndATriangle_AreTwoPlanesOnTheirNormalsSide()
    {
        WorldVertex Corner(float x, float y, float z, bool faceEdge, (float X, float Y, float Z) normal) =>
            new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: normal.X, NormalY: normal.Y, NormalZ: normal.Z, FaceEdge: faceEdge);

        (float, float, float) up = (0f, 0f, 1f);
        (float, float, float) east = (1f, 0f, 0f);

        FacePlane[] faces = FaceFan.Faces(
        [
            Corner(0f, 0f, 2f, true, up), Corner(0f, 4f, 2f, true, up), Corner(4f, 4f, 2f, false, up),
            Corner(0f, 0f, 2f, false, up), Corner(4f, 4f, 2f, true, up), Corner(4f, 0f, 2f, true, up),
            Corner(5f, 0f, 0f, true, east), Corner(5f, 4f, 0f, true, east), Corner(5f, 0f, 4f, true, east),
        ]);

        faces.ShouldBe([new FacePlane(0, 6, 0f, 0f, 1f, 2f), new FacePlane(6, 3, 1f, 0f, 0f, 5f)]);
    }

    /// <remarks>
    /// **The engine's brush-model cull, `dot − dist ≥ −0.01`** (`engine.dll` `FUN_1800dcb20`, the surface loop before any
    /// draw): the plane z = 0 faces an eye above it, and one 0.005 below, and not one 0.02 below or further.
    /// </remarks>
    [TestCase(10f, true)]
    [TestCase(-0.005f, true)]
    [TestCase(-0.02f, false)]
    [TestCase(-10f, false)]
    public void FacesEye_AtAHeightAboveOrBelowThePlane_IsTheEnginesBackfaceTest(float z, bool expected) =>
        new FacePlane(0, 3, 0f, 0f, 1f, 0f).FacesEye(3f, -7f, z).ShouldBe(expected);
}
