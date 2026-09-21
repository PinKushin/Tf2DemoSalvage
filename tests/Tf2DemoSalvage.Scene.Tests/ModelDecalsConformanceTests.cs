using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CStudioRender::AddDecal`'s limits, read out of `studiorender.dll`, with `r_maxmodeldecal` 50 (B415).</summary>
public sealed class ModelDecalsConformanceTests
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    // One triangle facing +X at the origin, which a shot along −X decals.
    private static readonly WorldVertex[] Face =
    [
        new(0f, 2f, 0f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
        new(0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
        new(0f, -2f, -2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
    ];

    [Test]
    public void Add_TheFiftyFirstOnOneModel_RetiresItsOldest()
    {
        // `maxDecalsPerModel ≤ this model's count` retires this model's oldest first.
        ModelDecals decals = new();

        for (int shot = 0; shot <= ModelDecals.MaximumPerModel; shot++)
        {
            Shoot(decals, entity: 1, material: shot).ShouldBeTrue();
        }

        decals.Count.ShouldBe(ModelDecals.MaximumPerModel);
        decals.For(1)!.Value.Batches[0].MaterialIndex.ShouldBe(1, "material 0 was the oldest");
    }

    [Test]
    public void Add_PastOneAndAHalfTimesTheLimitAcrossModels_RetiresTheOldestAnywhere()
    {
        // `r_maxmodeldecal · 1.5 ≤ total` retires the oldest decal of any model: 75 held, the 76th retires entity 1's first.
        ModelDecals decals = new();

        for (int shot = 0; shot < 75; shot++)
        {
            Shoot(decals, entity: 1 + (shot / 25), material: shot);
        }

        Shoot(decals, entity: 9, material: 99);

        decals.Count.ShouldBe(75);
        decals.For(1)!.Value.Batches[0].MaterialIndex.ShouldBe(1);
    }

    [Test]
    public void For_ADecal_IsTheModelsOwnCornersWithTheDecalsUvAndMaterial()
    {
        ModelDecals decals = new() { UnlitLight = 2f };

        Shoot(decals, entity: 1, material: 7);

        (System.Collections.Generic.IReadOnlyList<WorldVertex> vertices, System.Collections.Generic.IReadOnlyList<WorldBatch> batches) =
            decals.For(1)!.Value;

        batches.ShouldHaveSingleItem().ShouldBe(new WorldBatch(7, 0, 3, Category: SurfaceCategory.Overlay));
        vertices[0].X.ShouldBe(0f);
        vertices[0].Y.ShouldBe(2f);
        vertices[0].U.ShouldBe(0.75f);
        vertices[0].Red.ShouldBe(2f);
    }

    [Test]
    public void Clear_OneEntity_LeavesTheOthers()
    {
        ModelDecals decals = new();

        Shoot(decals, entity: 1, material: 0);
        Shoot(decals, entity: 2, material: 0);

        decals.Clear(1);

        decals.For(1).ShouldBeNull();
        decals.For(2).ShouldNotBeNull();
        decals.Count.ShouldBe(1);
    }

    private static bool Shoot(ModelDecals decals, int entity, int material) =>
        decals.Add(entity, Face, [Identity], new Vector3(100f, 0f, 0f), new Vector3(-110f, 0f, 0f), 4f, material);
}
