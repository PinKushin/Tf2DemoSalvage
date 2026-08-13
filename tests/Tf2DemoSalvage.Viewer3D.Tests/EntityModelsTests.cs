using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Posing a demo's entity models into world space.
/// </summary>
/// <remarks>
/// **Tested with a fake loader**, because reading a model needs the map's pakfile or the game's
/// archives and the part worth checking is the posing. One triangle at a known offset says more
/// about a transform than a rock with two thousand.
/// </remarks>
public sealed class EntityModelsTests
{
    [Test]
    public void AModelIsPlacedWhereTheEntityStands()
    {
        // The corner sits one unit along X in the model's own coordinates, and the entity stands
        // at (100, 200, 30) unrotated - so the corner lands at (101, 200, 30). Anything else means
        // origin and rotation have been applied in the wrong order or the wrong space.
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build(
            [Prop("models/props/crate.mdl", x: 100f, y: 200f, z: 30f)],
            OneTriangle,
            vertices,
            batches);

        vertices.Count.ShouldBe(3);
        vertices[0].X.ShouldBe(101f, 1e-4f);
        vertices[0].Y.ShouldBe(200f, 1e-4f);
        vertices[0].Depth.ShouldBe(30f, 1e-4f, "the vertex carries world height, not a depth");
    }

    [Test]
    public void AYawedModel_TurnsAboutTheVertical()
    {
        // Ninety degrees of yaw sends the model's +X to the world's +Y, which is Valve's own
        // convention and the reason this shares PropTransform rather than reimplementing it.
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build(
            [Prop("models/props/crate.mdl", yaw: 90f)],
            OneTriangle,
            vertices,
            batches);

        vertices[0].X.ShouldBe(0f, 1e-4f);
        vertices[0].Y.ShouldBe(1f, 1e-4f);
    }

    [Test]
    public void BrushModelsAndSprites_AreNotHandedToTheStudioLoader()
    {
        // A "*3" is an inline BSP submodel and a ".vmt" is a camera-facing sprite. Neither is a
        // .mdl, and giving either to a studio loader draws nothing while reporting nothing - so
        // they are skipped here and handled by their own path when it exists.
        List<string> asked = [];
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build(
            [
                Prop("*3"),
                Prop("sprites/glow06.spr"),
                Prop("models/props/crate.mdl"),
            ],
            path =>
            {
                asked.Add(path);
                return OneTriangle(path);
            },
            vertices,
            batches);

        asked.ShouldBe(["models/props/crate.mdl"]);
    }

    [Test]
    public void ModelsSharingAMaterial_AreDrawnInOneBatch()
    {
        // A match carries many copies of one rocket, and a bind per copy is the cost this avoids.
        // Two instances of the same model must produce one batch, not two.
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build(
            [Prop("models/props/crate.mdl", x: 10f), Prop("models/props/crate.mdl", x: 20f)],
            OneTriangle,
            vertices,
            batches);

        batches.Count.ShouldBe(1);
        batches[0].VertexCount.ShouldBe(6);
        vertices.Count.ShouldBe(6);
    }

    [Test]
    public void EveryBatchCoversTheVerticesItClaims()
    {
        // The batches index into one shared list, so a wrong offset draws another material's
        // triangles with this material's texture - which looks like a texture assignment bug and
        // is an arithmetic one.
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build(
            [Prop("models/props/crate.mdl"), Prop("models/props/barrel.mdl")],
            path => path.Contains("barrel", StringComparison.Ordinal)
                ? [new(0f, 0f, 0f, 0f, 0f, MaterialIndex: 7)]
                : OneTriangle(path),
            vertices,
            batches);

        batches.Sum(batch => batch.VertexCount).ShouldBe(vertices.Count);

        foreach (WorldBatch batch in batches)
        {
            (batch.FirstVertex + batch.VertexCount).ShouldBeLessThanOrEqualTo(vertices.Count);
        }
    }

    [Test]
    public void AModelThatWillNotLoad_IsSkippedRatherThanDrawnEmpty()
    {
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        EntityModels.Build([Prop("models/props/missing.mdl")], _ => null, vertices, batches);

        vertices.ShouldBeEmpty();
        batches.ShouldBeEmpty();
    }

    /// <summary>A triangle whose first corner sits one unit along the model's own X.</summary>
    private static IReadOnlyList<PropVertex>? OneTriangle(string path) =>
    [
        new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
        new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
        new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
    ];

    private static SceneProp Prop(
        string model, float x = 0f, float y = 0f, float z = 0f, float yaw = 0f) =>
        new(
            EntityIndex: 1,
            model,
            ScenePropTrack.Classify(model),
            new ScenePose { X = x, Y = y, Z = z, Yaw = yaw });
}
