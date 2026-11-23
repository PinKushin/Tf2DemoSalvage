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
    /// **With no stored plane, a face with no area is drawn whole** (B803 review 6): three corners on one line in z = 3
    /// have no plane to give, and the zero plane faces every eye, below as above. Review 2 took the corner's normal here,
    /// a heuristic the stored plane replaced; a real brush face reaches the outline with its stored plane (below).
    /// </remarks>
    [Test]
    public void Faces_OfAFaceWithNoAreaAndNoStoredPlane_AreSeenFromEverywhere()
    {
        WorldVertex Corner(float x) => new(x, 0f, 3f, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0f, NormalZ: 2f);

        FacePlane face = FaceFan.Faces([Corner(0f), Corner(1f), Corner(2f)]).ShouldHaveSingleItem();

        (face, face.FacesEye(1f, 0f, 0f), face.FacesEye(1f, 0f, 6f)).ShouldBe((new FacePlane(0, 3, 0f, 0f, 0f, 0f), true, true));
    }

    /// <remarks>
    /// **A stored plane decides, whatever the fan's triangles say** (B803 review 6, Fable): the engine culls a brush
    /// surface by the plane its record stores (`FUN_1800df1a0`, `+4`). Two faces at 4096:
    /// - a T-junction fan whose first corner vbsp left 0.1 off the edge it splits, 64 along a 512 edge (`OFF_EPSILON` 0.25,
    ///   `utils/vbsp/faces.cpp:31`; kept on a brush entity, `:652`), whose first triangle's sine is 1.6e-3, a plane at an
    ///   arbitrary angle about that edge;
    /// - a real thin face, a sine of 1e-3.
    /// Each takes its stored plane exactly, plane-back flag included, and neither takes a triangle's.
    /// </remarks>
    [Test]
    public void Faces_WithStoredPlanes_TakeThemAndNotTheTriangles()
    {
        WorldVertex Corner(float x, float y, float z, bool faceEdge) =>
            new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: 0f, NormalY: 0f, NormalZ: 1f, FaceEdge: faceEdge);

        const float B = 4096f;

        // The T-junction: base (B, B, 0), the edge to (B + 512 · 0.6, B + 512 · 0.8, 0), its split corner 64 along it and
        // 0.1 off it, out of the face's plane z = 0; then the far corner and one across.
        WorldVertex a = Corner(B, B, 0f, true);
        WorldVertex t = Corner(B + (64f * 0.6f), B + (64f * 0.8f), 0.1f, true);
        WorldVertex e = Corner(B + (512f * 0.6f), B + (512f * 0.8f), 0f, false);
        WorldVertex across = Corner(B - 400f, B + 300f, 0f, true);

        System.Numerics.Vector3 u = new(t.X - a.X, t.Y - a.Y, t.Depth - a.Depth);
        System.Numerics.Vector3 v = new(e.X - a.X, e.Y - a.Y, e.Depth - a.Depth);
        float sine = System.Numerics.Vector3.Cross(u, v).Length() / (u.Length() * v.Length());

        sine.ShouldBeInRange(1.4e-3f, 1.8e-3f, "the premise: the T-junction triangle's sine is about 1.6e-3");

        StoredPlane tJunction = new(0f, 0f, 1f, 0f, PlaneBack: false);
        StoredPlane thin = new(0f, 0f, -1f, -7f, PlaneBack: true);

        FacePlane[] faces = FaceFan.Faces(
            [
                a, t, e, a with { FaceEdge = false }, e with { FaceEdge = true }, across,
                Corner(0f, 0f, 7f, true), Corner(1000f, 0f, 7f, true), Corner(2000f, 2f, 7f, true),
            ],
            [tJunction, thin]);

        faces.ShouldBe([new FacePlane(0, 6, 0f, 0f, 1f, 0f), new FacePlane(6, 3, 0f, 0f, -1f, -7f, PlaneBack: true)]);
    }

    /// <remarks>A stored plane list that is not one per face is refused, not matched by position.</remarks>
    [Test]
    public void Faces_WithAStoredPlaneCountThatIsNotTheFaces_Throws() =>
        Should.Throw<ArgumentException>(() => FaceFan.Faces(
            [new WorldVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new WorldVertex(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new WorldVertex(0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f)],
            [new StoredPlane(0f, 0f, 1f, 0f, false), new StoredPlane(0f, 0f, 1f, 0f, false)]));

    /// <remarks>
    /// **The translucent rule measures against the node's plane, which is a plane-back face's own flipped** (`FUN_1800dbdb0`:
    /// kept when the plane-back flag equals `node dist &lt; 0`; vbsp writes the face's own plane, `writebsp.cpp:465-467`). A
    /// face on z = 0 facing +z: an eye above sees it and one below does not, plane-back or not, translucent or opaque. The
    /// flag shows only for an eye exactly on the plane: a translucent plane-back face is not kept there (its node distance
    /// is −0, not below 0), a translucent plain one is, and the opaque test (−0.01) keeps both.
    /// </remarks>
    [TestCase(1f, true, true, true)]
    [TestCase(-1f, true, true, false)]
    [TestCase(0f, true, true, false)]
    [TestCase(0f, true, false, true)]
    [TestCase(0f, false, true, true)]
    [TestCase(-1f, false, true, false)]
    public void OutlinedFrom_APlaneBackFace_IsKeptByTheNodesSide(float z, bool translucent, bool planeBack, bool expected) =>
        new FacePlane(0, 3, 0f, 0f, 1f, 0f, PlaneBack: planeBack).OutlinedFrom(0f, 0f, z, translucent, noCull: false).ShouldBe(expected);

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
    /// **With no stored plane, the face's largest triangle decides** (B803 review 6, the fallback for a model the upload
    /// gives no planes, which no brush entity is): corners (0, 0, 0), (1000, 0, 0) and (2000, 0.0005, 0.0007) are a
    /// sliver whose cross product (0, −0.7, 0.5) points 54° from the face; the second triangle, to (0, 1000, 0), is the
    /// face's largest and spans it in z = 0: (0, 0, 1) to a thousandth. The corners' normal is tilted, (0, 0.6, 0.8), so
    /// only the second triangle's plane reads so. Reviews 4 and 5's sine and ulp floors are gone with the stored plane.
    /// </remarks>
    [Test]
    public void Faces_WithoutStoredPlanes_TakeTheLargestTrianglesPlane()
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
