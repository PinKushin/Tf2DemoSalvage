using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A brush entity's stored face planes, in the order its packed vertices hold its faces (B803 review 6).</summary>
public sealed class PackedFacePlanesTests
{
    private const string Path = "*1";

    /// <remarks>
    /// **The packing groups by material, and each face keeps its own stored plane through it** (`engine.dll`
    /// `FUN_1800df1a0` keeps each surface record's plane, `+4`). Three one-triangle faces of materials 5, 7 and 5, planes
    /// p0, p1 and p2 in corner order: packed, the material-5 faces come first, so the planes read p0, p2, p1. The control:
    /// the packing did reorder the corners.
    /// </remarks>
    [Test]
    public void PackedFacePlanes_OfFacesThePackingReorders_FollowTheirFaces()
    {
        StoredPlane p0 = new(1f, 0f, 0f, 1f, false);
        StoredPlane p1 = new(0f, 1f, 0f, 2f, true);
        StoredPlane p2 = new(0f, 0f, 1f, 3f, false);

        PropVertex Corner(float x, int material) => new(x, 0f, 0f, 0f, 0f, material);

        PropModels.ModelFrames Brush(string _) =>
            new(
                [
                    new PropVertex[]
                    {
                        Corner(0f, 5), Corner(1f, 5), Corner(2f, 5),
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

        models.Vertices.Select(vertex => vertex.X).ShouldBe([0f, 1f, 2f, 20f, 21f, 22f, 10f, 11f, 12f], "the control: the packing reordered the faces");
        models.PackedFacePlanes(Path).ShouldBe([p0, p2, p1]);
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
