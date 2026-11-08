using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A model's shadow LOD (`DrawModelShadowSetup`, `engine.dll` `FUN_1800eba80`; finding 107) packed beside it, under
/// <see cref="PropModels.ShadowLodSuffix"/>, for the shadow pass to draw instead of the model's own corners.
/// </summary>
public sealed class ShadowLodPackingTests
{
    private const string Path = "models/a.mdl";

    /// <summary>The fixture's triangle and a second one, as a shadow LOD of six corners.</summary>
    private static PropModels.ModelFrames WithShadowLod(string path) =>
        ModelFramesFixture.OneTriangle(path) with
        {
            ShadowGeometry =
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                    new(2f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 2f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 2f, 0f, 1f, MaterialIndex: 3),
                },
            ],
        };

    [Test]
    public void Precache_AModelWithAShadowLod_PacksItBesideTheModelUnderItsOwnPath()
    {
        EntityModelSet models = new() { Geometry = WithShadowLod };

        models.Precache([Path]);

        models.Paths.ShouldContain(Path + PropModels.ShadowLodSuffix);
        models.Count.ShouldBe(1, "a shadow LOD is not another model");
        Corners(models, Path).ShouldBe(3);
        Corners(models, Path + PropModels.ShadowLodSuffix).ShouldBe(6);
    }

    /// <remarks>
    /// Frame for frame: a model of two baked frames packs a shadow LOD of two, each frame's runs over that frame's own
    /// corners — the one the shadow pass draws at the instance's frame.
    /// </remarks>
    [Test]
    public void Precache_ABakedModelOfTwoFrames_PacksAShadowLodOfTwo()
    {
        PropVertex[] near = [new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3), new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3), new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3)];
        PropVertex[] far = [.. near.Select(static corner => corner with { X = corner.X + 10f })];

        EntityModelSet models = new()
        {
            Geometry = path => new PropModels.ModelFrames(
                [near, far],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 2, 1f) },
                [0],
                [true])
            {
                ShadowGeometry = [far, near],
            },
        };

        models.Precache([Path]);

        IReadOnlyList<IReadOnlyList<WorldBatch>> frames = models.AllFrames(Path + PropModels.ShadowLodSuffix);

        frames.Count.ShouldBe(2);
        frames.ShouldAllBe(frame => frame.Sum(batch => batch.VertexCount) == 3);
        frames[0].Single().FirstVertex.ShouldNotBe(frames[1].Single().FirstVertex);
    }

    /// <remarks>The control: a model whose shadow LOD is its level zero packs nothing beside it.</remarks>
    [Test]
    public void Precache_AModelWithoutAShadowLod_PacksOnlyItself()
    {
        EntityModelSet models = new() { Geometry = ModelFramesFixture.OneTriangle };

        models.Precache([Path]);

        models.Paths.ShouldBe([Path]);
    }

    private static int Corners(EntityModelSet models, string path) =>
        models.AllFrames(path).Single().Sum(batch => batch.VertexCount);
}
