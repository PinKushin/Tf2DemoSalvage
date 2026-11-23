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
}
