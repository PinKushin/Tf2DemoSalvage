using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Packing entity models once and posing them with a matrix.
/// </summary>
/// <remarks>
/// **The arrangement is the engine's**: model-space vertices in a buffer that never changes, and a
/// matrix per instance handed to the shader. The first version of this transformed every vertex on
/// the processor each frame, which is exactly the work <c>LoadBoneMatrix</c> exists to avoid.
///
/// Tested with a fake loader, because reading a model needs the map's pakfile and the game's
/// archives while the parts worth checking are the packing and the matrix.
/// </remarks>
public sealed class EntityModelsTests
{
    [Test]
    public void AModelIsPackedInItsOwnCoordinates()
    {
        // **Not moved to where the entity stands**, which is the whole difference from the version
        // this replaced. The vertex keeps the model's own coordinates and the matrix carries the
        // placement, so the buffer can be uploaded once and never touched again.
        EntityModelSet models = new();

        models.Add([Prop("models/props/crate.mdl", x: 100f, y: 200f, z: 30f)], OneTriangle);

        models.Vertices.Count.ShouldBe(3);
        models.Vertices[0].X.ShouldBe(1f, 1e-4f);
        models.Vertices[0].Y.ShouldBe(0f, 1e-4f);
        models.Vertices[0].Depth.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void AModelIsPackedOnce_HoweverManyEntitiesWearIt()
    {
        // A match carries many copies of one rocket. Packing per instance would multiply the
        // buffer by the number of entities and defeat the arrangement entirely.
        EntityModelSet models = new();

        models.Add(
            [
                Prop("models/props/crate.mdl", x: 10f),
                Prop("models/props/crate.mdl", x: 20f),
                Prop("models/props/crate.mdl", x: 30f),
            ],
            OneTriangle);

        models.Count.ShouldBe(1);
        models.Vertices.Count.ShouldBe(3);
    }

    [Test]
    public void EachInstanceCarriesItsOwnPlacement()
    {
        // Three entities, one model, three matrices. The translation lives in the last row.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/props/crate.mdl", x: 10f),
            Prop("models/props/crate.mdl", x: 20f),
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[0].Matrix[12].ShouldBe(10f, 1e-4f);
        instances[1].Matrix[12].ShouldBe(20f, 1e-4f);
    }

    [Test]
    public void AnInstanceWhoseModelDidNotLoad_IsNotDrawn()
    {
        // Otherwise the renderer sets a matrix and draws nothing, once per frame per missing
        // model - invisible in the picture and pure cost.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop("models/props/missing.mdl")];

        models.Add(props, _ => null);
        models.Instances(props, instances);

        instances.ShouldBeEmpty();
    }

    [Test]
    public void AFailedModelIsNotRetriedEveryFrame()
    {
        // Asking again sixty times a second buries the log in one repeated line, which is how a
        // real missing asset stops being noticeable.
        EntityModelSet models = new();
        int attempts = 0;

        SceneProp[] props = [Prop("models/props/missing.mdl")];

        for (int frame = 0; frame < 5; frame++)
        {
            models.Add(
                props,
                _ =>
                {
                    attempts++;
                    return null;
                });
        }

        attempts.ShouldBe(1);
    }

    [Test]
    public void BrushModelsAndSprites_AreNotHandedToTheStudioLoader()
    {
        // A "*3" is an inline BSP submodel and a ".spr" is a camera-facing sprite. Neither is a
        // .mdl, and giving either to a studio loader draws nothing while reporting nothing.
        EntityModelSet models = new();
        List<string> asked = [];

        models.Add(
            [Prop("*3"), Prop("sprites/glow06.spr"), Prop("models/props/crate.mdl")],
            path =>
            {
                asked.Add(path);
                return OneTriangle(path);
            });

        asked.ShouldBe(["models/props/crate.mdl"]);
    }

    [Test]
    public void EveryBatchCoversTheVerticesItClaims()
    {
        // Batches index into one shared buffer, so a wrong offset draws another model's triangles
        // with this model's texture - which looks like a texture bug and is an arithmetic one.
        EntityModelSet models = new();

        SceneProp[] props = [Prop("models/props/crate.mdl"), Prop("models/props/barrel.mdl")];

        models.Add(
            props,
            path => path.Contains("barrel", StringComparison.Ordinal)
                ? [new(0f, 0f, 0f, 0f, 0f, MaterialIndex: 7)]
                : OneTriangle(path));

        List<WorldBatch> all =
            [.. props.SelectMany(prop => models.Batches(prop.ModelPath))];

        all.Sum(batch => batch.VertexCount).ShouldBe(models.Vertices.Count);

        foreach (WorldBatch batch in all)
        {
            (batch.FirstVertex + batch.VertexCount)
                .ShouldBeLessThanOrEqualTo(models.Vertices.Count);
        }
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
