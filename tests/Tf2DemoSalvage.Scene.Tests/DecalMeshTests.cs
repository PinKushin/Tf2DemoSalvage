using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><see cref="DecalMesh.Build"/>: placed decals to fans, one run per drawn material (B415).</summary>
public sealed class DecalMeshTests
{
    private static readonly DecalMaterial Hole = new("decals/concrete/shot1_subrect", 64, 64, 0.16f, Draws: "decals/decals_mod2x");
    private static readonly DecalMaterial Scorch = new("decals/scorch1", 128, 128, 1f, Draws: "decals/scorch1");

    private static readonly AtlasRect[] Lightmaps = [default, new AtlasRect(0.5f, 0.25f, 0.1f, 0.2f)];

    [Test]
    public void Build_TwoDecalsOfOneMaterial_AreOneRunOfFans()
    {
        (List<WorldVertex> vertices, List<WorldBatch> batches) = Build([Quad(0, Hole), Quad(1, Hole)]);

        // A quad fans into two triangles, six corners each.
        batches.ShouldBe([new WorldBatch(7, 0, 12, Category: SurfaceCategory.Overlay)]);
        vertices.Count.ShouldBe(12);
    }

    [Test]
    public void Build_ALitDecal_TakesItsFacesAtlasRectangle()
    {
        (List<WorldVertex> vertices, _) = Build([Quad(0, Scorch, face: 1)]);

        // The quad's third corner is at lightmap (1, 1): the rectangle's far corner.
        vertices[2].LightU.ShouldBe(0.6f, 1e-6f);
        vertices[2].LightV.ShouldBe(0.45f, 1e-6f);
    }

    [Test]
    public void Build_AnUnlitDecal_TakesTheWhiteTexelAndItsVertexLight()
    {
        (List<WorldVertex> vertices, _) = Build([Quad(0, Hole, face: 1), Quad(1, Scorch, face: 1)]);

        vertices[2].LightU.ShouldBe(0f);
        vertices[2].LightV.ShouldBe(0f);
        (vertices[2].Red, vertices[2].Green, vertices[2].Blue).ShouldBe((1.25f, 1.25f, 1.25f));

        // The lit one keeps the vertex light a brush face carries: one.
        vertices[8].Red.ShouldBe(1f);
    }

    [Test]
    public void Build_AnUnloadedMaterial_DrawsNothing()
    {
        (List<WorldVertex> vertices, List<WorldBatch> batches) =
            Build([Quad(0, Hole with { Draws = "missing" })]);

        vertices.ShouldBeEmpty();
        batches.ShouldBeEmpty();
    }

    private static (List<WorldVertex> Vertices, List<WorldBatch> Batches) Build(IReadOnlyList<PlacedDecal> decals)
    {
        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        DecalMesh.Build(
            decals,
            material => material.Draws switch
            {
                "decals/decals_mod2x" => 7,
                "decals/scorch1" => 9,
                _ => -1,
            },
            index => index == 7 ? 1.25f : null,
            Lightmaps,
            vertices,
            batches);

        return (vertices, batches);
    }

    private static PlacedDecal Quad(int slot, DecalMaterial material, int face = 0) =>
        new(
            slot,
            face,
            material,
            [
                new DecalVertex(new Vector3(0f, 0f, 0f), 0f, 0f, 0f, 0f),
                new DecalVertex(new Vector3(1f, 0f, 0f), 1f, 0f, 1f, 0f),
                new DecalVertex(new Vector3(1f, 1f, 0f), 1f, 1f, 1f, 1f),
                new DecalVertex(new Vector3(0f, 1f, 0f), 0f, 1f, 0f, 1f),
            ]);
}
