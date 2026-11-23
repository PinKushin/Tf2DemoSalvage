using System;
using System.Collections.Generic;
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
    /// so it points along −x (corners (5, 0, 0), (5, 0, 4), (5, 4, 0)), each with its vertex normal the other way: the plane
    /// takes the vertex normal's side, unit length, and its distance from the origin along it. Review 2 corrected the
    /// triangle, which had been wound along +x and so covered no flip.
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
            Corner(5f, 0f, 0f, true, east), Corner(5f, 0f, 4f, true, east), Corner(5f, 4f, 0f, true, east),
        ]);

        faces.ShouldBe([new FacePlane(0, 6, 0f, 0f, 1f, 2f), new FacePlane(6, 3, 1f, 0f, 0f, 5f)]);
    }

    /// <remarks>
    /// **A face whose first triangle has no area takes its plane from the next that has** (B803 review 2, Grok): the
    /// engine tests the plane each surface stores, whatever the fan. The first triangle's corners lie on one line in
    /// z = 3; the second spans the face. The corners' normal is smoothed, (0, 0.6, 0.8), so a plane left on it reads
    /// apart from the triangle's (0, 0, 1).
    /// </remarks>
    [Test]
    public void Faces_WhoseFirstTriangleHasNoArea_TakeThePlaneOfTheNextThatHas()
    {
        WorldVertex Corner(float x, float y, bool faceEdge) =>
            new(x, y, 3f, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0.6f, NormalZ: 0.8f, FaceEdge: faceEdge);

        FacePlane[] faces = FaceFan.Faces(
        [
            Corner(0f, 0f, true), Corner(1f, 0f, true), Corner(2f, 0f, false),
            Corner(0f, 0f, false), Corner(2f, 0f, true), Corner(2f, 2f, true),
        ]);

        faces.ShouldBe([new FacePlane(0, 6, 0f, 0f, 1f, 3f)]);
    }

    /// <remarks>
    /// **A face with no area at all keeps a plane, its corner's normal through that corner** (B803 review 2), so an eye
    /// behind it does not see it: three corners on one line in z = 3, normal +z, an eye at z = 0 below.
    /// </remarks>
    [Test]
    public void Faces_OfAFaceWithNoArea_AreItsCornersNormalPlaneAndCullFromBehind()
    {
        WorldVertex Corner(float x) => new(x, 0f, 3f, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0f, NormalZ: 2f);

        FacePlane face = FaceFan.Faces([Corner(0f), Corner(1f), Corner(2f)]).ShouldHaveSingleItem();

        (face, face.FacesEye(1f, 0f, 0f), face.FacesEye(1f, 0f, 6f)).ShouldBe((new FacePlane(0, 3, 0f, 0f, 1f, 3f), false, true));
    }

    /// <remarks>
    /// **A face with area but no side to choose is drawn whole** (B803 review 3, Fable, Grok): a triangle in z = 0 whose
    /// corner normal lies along it, (1, 0, 0), has no front. It gets the zero plane, which every eye sees, above or below;
    /// review 2's corner-normal plane stood across it and hid it from every eye at x &lt; 0.
    /// </remarks>
    [Test]
    public void Faces_WithAreaButANormalAlongTheFace_AreSeenFromEverywhere()
    {
        WorldVertex Corner(float x, float y) => new(x, y, 0f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalY: 0f, NormalZ: 0f);

        FacePlane face = FaceFan.Faces([Corner(0f, 0f), Corner(0f, 4f), Corner(4f, 0f)]).ShouldHaveSingleItem();

        (face.FacesEye(-5f, 1f, 3f), face.FacesEye(-5f, 1f, -3f), face.FacesEye(5f, 1f, 3f)).ShouldBe((true, true, true));
    }

    /// <remarks>
    /// **A non-finite corner gives a face drawn whole, not one never drawn** (B803 review 3, Sonnet): a NaN made every term
    /// of the plane NaN, and every comparison with NaN false.
    /// </remarks>
    [Test]
    public void Faces_WithANaNCorner_AreSeenFromEverywhere()
    {
        WorldVertex Corner(float x, float y) => new(x, y, 0f, 0f, 0f, 0f, 0f, 0f, NormalZ: 1f);

        FacePlane face = FaceFan.Faces([Corner(0f, 0f), Corner(float.NaN, 4f), Corner(4f, 0f)]).ShouldHaveSingleItem();

        (face.FacesEye(1f, 1f, 3f), face.FacesEye(1f, 1f, -3f)).ShouldBe((true, true));
    }

    /// <remarks>
    /// **Each pass culls by its own rule** (B803 review 3, `engine.dll`): an opaque brush face by `FUN_1800dcb20`'s
    /// `≥ −0.01`, no flag read; a translucent one by `FUN_1800dbdb0`'s side of its node's plane, no tolerance, NOCULL
    /// exempt. The plane z = 0 facing up, eyes above, just below (−0.005) and well below.
    /// </remarks>
    [TestCase(1f, false, false, true)]
    [TestCase(-0.005f, false, false, true)]
    [TestCase(-10f, false, true, false)]
    [TestCase(1f, true, false, true)]
    [TestCase(-0.005f, true, false, false)]
    [TestCase(-10f, true, true, true)]
    public void OutlinedFrom_AnOpaqueOrTranslucentFace_TakesItsPassesRule(float z, bool translucent, bool noCull, bool expected) =>
        new FacePlane(0, 3, 0f, 0f, 1f, 0f).OutlinedFrom(0f, 0f, z, translucent, noCull).ShouldBe(expected);

    /// <remarks>
    /// **A run's outline ranges stay inside it** (B803 review 3, Grok, Sonnet). Faces of six vertices at 0, 6, 12 and 18;
    /// all lie in z = −1, below the eye at the origin, and the second faces down, away from it:
    /// - a whole run of three faces, front, back, front: (0, 6) and (12, 18);
    /// - a later run from 18: (18, 24), nothing of the faces before it;
    /// - a run cut mid-face, from 15 (as a cap on the run before can leave it): (15, 24), the face at 12 only from 15;
    /// - a run capped mid-face, to 9: (0, 6) only, the back face's part dropped; to 3, (0, 3), a partial outline of the
    ///   first face, which is what was drawn of it (the cap is a development control and draws what it keeps);
    /// - a run cut mid-face in the culled face, 9 to 18: (12, 18), none of the culled face's part (review 4).
    /// </remarks>
    [TestCase(0, 18, new[] { 0, 6, 12, 18 })]
    [TestCase(18, 24, new[] { 18, 24 })]
    [TestCase(15, 24, new[] { 15, 24 })]
    [TestCase(0, 9, new[] { 0, 6 })]
    [TestCase(0, 3, new[] { 0, 3 })]
    [TestCase(9, 18, new[] { 12, 18 })]
    public void Runs_OfARunAmongFaces_StayInsideIt(int first, int end, int[] expected)
    {
        FacePlane Up(int at) => new(at, 6, 0f, 0f, 1f, -1f);
        FacePlane Down(int at) => new(at, 6, 0f, 0f, -1f, 1f);
        List<(int First, int End)> runs = [];

        FaceFan.Runs([Up(0), Down(6), Up(12), Up(18)], first, end, (0f, 0f, 0f), translucent: false, noCull: false, runs);

        runs.SelectMany(run => new[] { run.First, run.End }).ShouldBe(expected);
    }

    /// <remarks>
    /// **`Runs` hands the pass's rule through** (B803 review 4, Fable, Sonnet): one face 0.005 ahead of the eye's back (its
    /// plane z = 0.005 facing up, the eye at the origin). Opaque, the −0.01 tolerance keeps it; translucent, the exact test
    /// culls it; translucent and `$nocull`, it is kept. Swapping the two flags anywhere on the way reads otherwise.
    /// </remarks>
    [TestCase(false, false, new[] { 0, 6 })]
    [TestCase(true, false, new int[0])]
    [TestCase(true, true, new[] { 0, 6 })]
    public void Runs_OfAFaceJustBehindTheEye_TakeTheRunsPassRule(bool translucent, bool noCull, int[] expected)
    {
        List<(int First, int End)> runs = [];

        FaceFan.Runs([new FacePlane(0, 6, 0f, 0f, 1f, 0.005f)], 0, 6, (0f, 0f, 0f), translucent, noCull, runs);

        runs.SelectMany(run => new[] { run.First, run.End }).ShouldBe(expected);
    }

    /// <remarks>
    /// **A first triangle that is a sliver does not set the plane** (B803 review 4, Fable): corners (0, 0, 0), (1000, 0, 0)
    /// and (2000, 0.0005, 0.0007) are a T-junction's line, their cross product (0, −0.7, 0.5) rounding-sized against edges
    /// of 1000 and 2000 (a sine of 4e-7), yet a direction 54° from the face. The second triangle, to (0, 1000, 0), spans
    /// the face in z = 0, and decides: (0, 0, 1) to a thousandth. The corners' normal is tilted, (0, 0.6, 0.8), so the
    /// corner-normal plane, the no-area fallback, reads apart from the next triangle's (review 5).
    /// </remarks>
    [Test]
    public void Faces_WhoseFirstTriangleIsASliver_TakeThePlaneOfTheNext()
    {
        WorldVertex Corner(float x, float y, float z, bool faceEdge) =>
            new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0.6f, NormalZ: 0.8f, FaceEdge: faceEdge);

        FacePlane face = FaceFan.Faces(
        [
            Corner(0f, 0f, 0f, true), Corner(1000f, 0f, 0f, true), Corner(2000f, 0.0005f, 0.0007f, false),
            Corner(0f, 0f, 0f, false), Corner(2000f, 0.0005f, 0.0007f, true), Corner(0f, 1000f, 0f, true),
        ]).ShouldHaveSingleItem();

        (face.First, face.Count).ShouldBe((0, 6));
        face.NormalZ.ShouldBeGreaterThan(0.999f, $"was ({face.NormalX}, {face.NormalY}, {face.NormalZ})");
    }

    /// <remarks>
    /// **Far from the origin a sliver's noise is the coordinates' rounding, not its edges'** (B803 review 5, Fable): at
    /// (4096, 4096, 0) an ulp is 4.9e-4. A T-junction two units along the oblique edge (1, 2, 2)/3 from the fan's base,
    /// and the edge's far end 1000 along it, are each rounded to that, and their triangle's sine clears the 1e-4 floor
    /// while its cross product is rounding (printed). The ulp floor takes it as no area, and the second triangle, up z
    /// by 1000, decides: the plane of (1, 2, 2) and z, normal (2, −1, 0)/√5, to a thousandth. The corners' normal is
    /// tilted off it, so the fallback reads apart.
    /// </remarks>
    [Test]
    public void Faces_WhoseFirstTriangleIsASliverFarFromTheOrigin_TakeThePlaneOfTheNext()
    {
        WorldVertex Corner(float x, float y, float z, bool faceEdge) =>
            new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: 0.8f, NormalY: -0.2f, NormalZ: 0.565f, FaceEdge: faceEdge);

        const float Base = 4096f;
        float Along(float distance, float share) => Base + (distance * share);

        WorldVertex a = Corner(Base, Base, 0f, true);
        WorldVertex b = Corner(Along(2f, 1f / 3f), Along(2f, 2f / 3f), 2f * 2f / 3f, true);
        WorldVertex c = Corner(Along(1000f, 1f / 3f), Along(1000f, 2f / 3f), 1000f * 2f / 3f, false);
        WorldVertex up = Corner(Base, Base, 1000f, true);

        System.Numerics.Vector3 u = new(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        System.Numerics.Vector3 v = new(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        TestContext.Out.WriteLine($"first triangle's sine {System.Numerics.Vector3.Cross(u, v).Length() / (u.Length() * v.Length()):E2}");

        FacePlane face = FaceFan.Faces([a, b, c, a with { FaceEdge = false }, c with { FaceEdge = true }, up]).ShouldHaveSingleItem();

        (face.First, face.Count).ShouldBe((0, 6));
        System.Numerics.Vector3 normal = new(face.NormalX, face.NormalY, face.NormalZ);
        (normal - new System.Numerics.Vector3(2f, -1f, 0f) / MathF.Sqrt(5f)).Length().ShouldBeLessThan(1e-3f, $"was {normal}");
    }

    /// <remarks>
    /// **A real thin triangle keeps its own plane** (B803 review 5, Grok): (0, 0, 0), (1000, 0, 0), (2000, 2, 0), a sine
    /// of 1e-3, lies in z = 0 and is no sliver; the second triangle is tilted 10° off it, to (0, 1000, 176), and the
    /// corners' normal is tilted otherwise, so only keeping the first triangle's plane reads (0, 0, 1). A floor as loose
    /// as 1e-2 takes the second's.
    /// </remarks>
    [Test]
    public void Faces_WhoseFirstTriangleIsThinButReal_KeepItsPlane()
    {
        WorldVertex Corner(float x, float y, float z, bool faceEdge) =>
            new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0.6f, NormalZ: 0.8f, FaceEdge: faceEdge);

        FacePlane face = FaceFan.Faces(
        [
            Corner(0f, 0f, 0f, true), Corner(1000f, 0f, 0f, true), Corner(2000f, 2f, 0f, false),
            Corner(0f, 0f, 0f, false), Corner(2000f, 2f, 0f, true), Corner(0f, 1000f, 176f, true),
        ]).ShouldHaveSingleItem();

        face.NormalZ.ShouldBeGreaterThan(0.99999f, $"was ({face.NormalX}, {face.NormalY}, {face.NormalZ})");
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
