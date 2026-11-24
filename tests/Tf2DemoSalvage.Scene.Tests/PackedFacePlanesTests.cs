using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A brush entity's stored face planes, in the order its packed vertices hold its faces (B803 review 6).</summary>
public sealed class PackedFacePlanesTests
{
    private const string Path = "*1";

    /// <remarks>
    /// **The packing groups by material, and each face keeps its own stored plane through it** (`engine.dll`
    /// `FUN_1800df1a0` keeps each surface record's plane, `+4`). A two-triangle face of material 5, then one-triangle faces
    /// of materials 7 and 5, planes p0, p1 and p2 in corner order: packed, the material-5 faces come first, so the planes
    /// read p0, p2, p1. The two-triangle face is the case a face index of corner ÷ 3 gets wrong (review 7); its second
    /// triangle opens on the fan's diagonal, so its first corner's edge is not the polygon's. The control: the packing did
    /// reorder the corners.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfFacesThePackingReorders_FollowTheirFaces()
    {
        StoredPlane p0 = new(1f, 0f, 0f, 1f, false);
        StoredPlane p1 = new(0f, 1f, 0f, 2f, true);
        StoredPlane p2 = new(0f, 0f, 1f, 3f, false);

        PropVertex Corner(float x, int material, bool faceEdge = true) => new(x, 0f, 0f, 0f, 0f, material, FaceEdge: faceEdge);

        PropModels.ModelFrames Brush(string _) =>
            new(
                [
                    new PropVertex[]
                    {
                        Corner(0f, 5), Corner(1f, 5), Corner(2f, 5, false),
                        Corner(0f, 5, false), Corner(2f, 5), Corner(3f, 5),
                        Corner(10f, 7), Corner(11f, 7), Corner(12f, 7),
                        Corner(20f, 5), Corner(21f, 5), Corner(22f, 5),
                    },
                ],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [p0, p1, p2]);

        EntityModelSet models = new() { Geometry = Brush };

        models.Precache([Path]);

        models.Vertices.Select(vertex => vertex.X).ShouldBe(
            [0f, 1f, 2f, 0f, 2f, 3f, 20f, 21f, 22f, 10f, 11f, 12f], "the control: the packing reordered the faces");
        models.PackedFacePlanes(Path).ShouldBe([p0, p2, p1]);
    }

    /// <remarks>
    /// **A model whose planes do not fit its faces has none, never a misfit list** (review 7): two planes for three faces.
    /// The upload then reports it and outlines the model by its triangles' planes.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_FewerThanTheFaces_AreNone()
    {
        PropVertex Corner(float x) => new(x, 0f, 0f, 0f, 0f, 5);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [new PropVertex[] { Corner(0f), Corner(1f), Corner(2f), Corner(3f), Corner(4f), Corner(5f), Corner(6f), Corner(7f), Corner(8f) }],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [new StoredPlane(1f, 0f, 0f, 0f, false), new StoredPlane(0f, 1f, 0f, 0f, false)]),
        };

        models.Precache([Path]);

        models.PackedFacePlanes(Path).ShouldBeNull();
    }

    /// <remarks>
    /// **A triangle the packing splits has no planes** (review 8, Fable): corners of one triangle in materials 5, 7, 5,
    /// then one all 5. The material groups pack the first triangle's corners apart, so a packed triangle holds corners of
    /// two, and a face's plane could pair with another's; none instead. A brush face's corners share one material, so a
    /// real brush entity never does this.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfATriangleThePackingSplits_AreNone()
    {
        PropVertex Corner(float x, int material) => new(x, 0f, 0f, 0f, 0f, material);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [new PropVertex[] { Corner(0f, 5), Corner(1f, 7), Corner(2f, 5), Corner(3f, 5), Corner(4f, 5), Corner(5f, 5) }],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [new StoredPlane(1f, 0f, 0f, 0f, false), new StoredPlane(0f, 1f, 0f, 0f, false)]),
        };

        models.Precache([Path]);

        models.PackedFacePlanes(Path).ShouldBeNull();
    }

    /// <remarks>
    /// **A triangle the packing rotates has no planes** (review 9, Fable): materials A A A | B B A | B B B, the last two
    /// triangles one quad, pack as [0, 1, 2, 5, 3, 4, 6, 7, 8]. Every packed triple is one source triangle's corners, but
    /// the second opens on corner 5, whose face edge is the quad's diagonal, so the quad goes uncounted while the packed
    /// faces and the planes still agree on one. Each packed triangle must be its source triangle in order; none instead.
    /// The control: the packing did put corner 5 fourth.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfATriangleThePackingRotates_AreNone()
    {
        PropVertex Corner(float x, int material, bool faceEdge = true) => new(x, 0f, 0f, 0f, 0f, material, FaceEdge: faceEdge);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [
                    new PropVertex[]
                    {
                        Corner(0f, 5), Corner(1f, 5), Corner(2f, 5),
                        Corner(3f, 7), Corner(4f, 7), Corner(5f, 5, false),
                        Corner(3f, 7, false), Corner(5f, 7), Corner(6f, 7),
                    },
                ],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [new StoredPlane(1f, 0f, 0f, 0f, false), new StoredPlane(0f, 1f, 0f, 0f, false)]),
        };

        models.Precache([Path]);

        models.Vertices.Select(vertex => vertex.X).ShouldBe([0f, 1f, 2f, 5f, 3f, 4f, 3f, 5f, 6f], "the control: corner 5 packed fourth");
        models.PackedFacePlanes(Path).ShouldBeNull();
    }

    /// <remarks>
    /// **Corners that are not whole triangles have no planes** (review 9, Sonnet, Grok): seven corners, the seventh left
    /// out of the face count and so out of the planes' check. None instead.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfCornersThatAreNotWholeTriangles_AreNone()
    {
        PropVertex Corner(float x) => new(x, 0f, 0f, 0f, 0f, 5);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [new PropVertex[] { Corner(0f), Corner(1f), Corner(2f), Corner(3f), Corner(4f), Corner(5f), Corner(6f) }],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [new StoredPlane(1f, 0f, 0f, 0f, false), new StoredPlane(0f, 1f, 0f, 0f, false)]),
        };

        models.Precache([Path]);

        models.Vertices.Count.ShouldBe(7, "the control: all seven corners were packed, so the refusal is PackedFacePlanes'");
        models.PackedFacePlanes(Path).ShouldBeNull();
    }

    /// <remarks>
    /// **A triangle the packing moves away from its source predecessor has no planes** (review 10, Fable): T0 (material
    /// 5, edge), T1 (5, no edge) | T2 (7, edge), T3 (5, no edge), two faces. It packs T0 T1 T3 | T2, every triple whole
    /// and in order, but T3, which does not start a face, now follows T1 instead of T2, so it would join face 0 and be
    /// culled on its plane while both counts agree at two. None instead. A split that keeps each triangle after its
    /// predecessor is accepted (the next test). The control: the packing did put T3 third.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfATriangleThePackingMovesFromItsPredecessor_AreNone()
    {
        PropVertex Corner(float x, int material, bool faceEdge = true) => new(x, 0f, 0f, 0f, 0f, material, FaceEdge: faceEdge);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [
                    new PropVertex[]
                    {
                        Corner(0f, 5), Corner(1f, 5), Corner(2f, 5, false),
                        Corner(0f, 5, false), Corner(2f, 5), Corner(3f, 5),
                        Corner(10f, 7), Corner(11f, 7), Corner(12f, 7, false),
                        Corner(10f, 5, false), Corner(12f, 5), Corner(13f, 5),
                    },
                ],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [new StoredPlane(1f, 0f, 0f, 0f, false), new StoredPlane(0f, 1f, 0f, 0f, false)]),
        };

        models.Precache([Path]);

        models.Vertices.Select(vertex => vertex.X).ShouldBe(
            [0f, 1f, 2f, 0f, 2f, 3f, 10f, 12f, 13f, 10f, 11f, 12f], "the control: T3 packed third");
        models.PackedFacePlanes(Path).ShouldBeNull();
    }

    /// <remarks>
    /// **A face split across material groups keeps its plane when each triangle still follows its predecessor** (review
    /// 11, Fable): T0 (material 7, edge) and T1 (5, no edge), one face. The groups pack in first-appearance order, so it
    /// packs T0 then T1, the buffer counts one face, and the one stored plane fits it. The control: the packing kept the
    /// order.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfAFaceSplitAcrossGroupsInOrder_KeepItsPlane()
    {
        StoredPlane plane = new(1f, 0f, 0f, 1f, false);

        PropVertex Corner(float x, int material, bool faceEdge = true) => new(x, 0f, 0f, 0f, 0f, material, FaceEdge: faceEdge);

        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [
                    new PropVertex[]
                    {
                        Corner(0f, 7), Corner(1f, 7), Corner(2f, 7, false),
                        Corner(0f, 5, false), Corner(2f, 5), Corner(3f, 5),
                    },
                ],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                FacePlanes: [plane]),
        };

        models.Precache([Path]);

        models.Vertices.Select(vertex => vertex.X).ShouldBe([0f, 1f, 2f, 0f, 2f, 3f], "the control: T0 then T1");
        models.PackedFacePlanes(Path).ShouldBe([plane]);
    }

    /// <remarks>A studio model has no faces' planes: none, and the outline derives nothing for it, since it draws none.</remarks>
    [Test]
    public void PackedFacePlanes_OfAModelWithoutThem_AreNone()
    {
        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [new PropVertex[] { new(0f, 0f, 0f, 0f, 0f, 0), new(1f, 0f, 0f, 0f, 0f, 0), new(0f, 1f, 0f, 0f, 0f, 0) }],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true]),
        };

        models.Precache(["models/props/still.mdl"]);

        models.PackedFacePlanes("models/props/still.mdl").ShouldBeNull();
    }
}
