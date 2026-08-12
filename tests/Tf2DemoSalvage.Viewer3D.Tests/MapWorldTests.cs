using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Turning a map's surfaces into batched triangles.
/// </summary>
/// <remarks>
/// What matters here is that each material's triangles end up contiguous and its batch points at
/// them: a batch whose range covered another material's vertices would draw a real texture on the
/// wrong surfaces, which looks like a mapping error rather than an indexing one.
/// </remarks>
public sealed class MapWorldTests
{
    /// <summary>Ten ordinary materials, none of them tools.</summary>
    private static readonly BspMaterial[] Materials =
        [.. Enumerable.Range(0, 10).Select(index =>
            new BspMaterial($"concrete/wall{index}", (0.5f, 0.5f, 0.5f), 512, 512))];

    private static readonly TopDownCamera Camera =
        TopDownCamera.Fit([(0f, 0f), (1000f, 1000f)], 800, 600);

    [Test]
    public void Build_NoSurfaces_ProducesNothing()
    {
        MapWorld world = MapWorldBuilder.Build([], Materials, LightmapAtlas.Pack([]), Camera, null);

        world.Vertices.ShouldBeEmpty();
        world.Batches.ShouldBeEmpty();
    }

    [Test]
    public void Build_AQuad_BecomesTwoTriangles()
    {
        MapWorld world = MapWorldBuilder.Build([Surface(0, material: 3, corners: 4)], Materials, LightmapAtlas.Pack([]), Camera, null);

        world.Vertices.Count.ShouldBe(6);
        world.Batches.Count.ShouldBe(1);
        world.Batches[0].MaterialIndex.ShouldBe(3);
        world.Batches[0].VertexCount.ShouldBe(6);
    }

    [Test]
    public void Build_DownwardFacingSurfaces_AreDropped()
    {
        // The same rule the outline view uses: a ceiling faces down into the room it encloses, and
        // an overhead camera should not see it. This is the engine's own backface culling.
        MapWorld world = MapWorldBuilder.Build([Surface(0, material: 0, corners: 3, normalZ: -1f)], Materials, LightmapAtlas.Pack([]),
            Camera,
            null);

        world.Vertices.ShouldBeEmpty();
    }

    [Test]
    public void Build_ToolMaterials_AreDroppedEvenWithoutAToolFlag()
    {
        // **518 of cp_process_final's 578 displacement faces are painted with
        // tools/toolsinvisibledisplacement**, which the engine never draws. Its VMT declares
        // LightmappedGeneric and its texinfo carries no nodraw flag, so nothing but the path
        // identifies it - and drawn, its black texture covers exactly the areas that should be
        // grass.
        BspMaterial[] materials =
        [
            new("tools/toolsinvisibledisplacement", (0f, 0f, 0f), 32, 32),
            new("nature/blendgroundtograss007", (0.3f, 0.4f, 0.2f), 512, 512),
        ];

        MapWorld world = MapWorldBuilder.Build(
            [Surface(0, material: 0, corners: 3), Surface(1, material: 1, corners: 3)],
            materials,
            LightmapAtlas.Pack([]),
            Camera,
            null);

        world.Batches.Count.ShouldBe(1, "only the real material should be drawn");
        world.Batches[0].MaterialIndex.ShouldBe(1);
    }

    [Test]
    public void Build_ToolSurfaces_AreDropped()
    {
        // Drawing nodraw would put solid slabs across the map.
        MapWorld world = MapWorldBuilder.Build([Surface(0, material: 0, corners: 3, flags: SurfaceProperties.NoDraw)], Materials, LightmapAtlas.Pack([]),
            Camera,
            null);

        world.Vertices.ShouldBeEmpty();
    }

    [Test]
    public void Build_EachBatchCoversOnlyItsOwnMaterialsVertices()
    {
        // **The measurement that matters.** Three surfaces over two materials, interleaved, so a
        // builder that emitted them in encounter order would produce batches whose ranges overlap
        // the wrong material - and every face would still draw, with the wrong texture.
        List<BspSurface> surfaces =
        [
            Surface(0, material: 5, corners: 3),
            Surface(1, material: 9, corners: 3),
            Surface(2, material: 5, corners: 3),
        ];

        MapWorld world = MapWorldBuilder.Build(surfaces, Materials, LightmapAtlas.Pack([]), Camera, null);

        world.Batches.Count.ShouldBe(2);
        world.Vertices.Count.ShouldBe(9);

        // Every batch's range must be inside the vertex list and not overlap another's.
        List<WorldBatch> ordered = [.. world.Batches.OrderBy(batch => batch.FirstVertex)];

        ordered[0].FirstVertex.ShouldBe(0);
        (ordered[0].FirstVertex + ordered[0].VertexCount).ShouldBe(ordered[1].FirstVertex);
        (ordered[1].FirstVertex + ordered[1].VertexCount).ShouldBe(world.Vertices.Count);

        // And the material with two surfaces must own six vertices, not three.
        world.Batches.Single(batch => batch.MaterialIndex == 5).VertexCount.ShouldBe(6);
        world.Batches.Single(batch => batch.MaterialIndex == 9).VertexCount.ShouldBe(3);
    }

    [Test]
    public void Build_LightmapCoordinatesLandInsideTheFacesAtlasRectangle()
    {
        // The remap: a face's own 0..1 coordinates have to become coordinates in the shared atlas,
        // or every surface samples the top-left corner of it.
        LightmapAtlas atlas = LightmapAtlas.Pack([Lightmap(8, 8), Lightmap(8, 8)]);

        MapWorld world = MapWorldBuilder.Build([Surface(1, material: 0, corners: 3)], Materials, atlas, Camera, null);

        AtlasRect rectangle = atlas.Rectangles[1];

        foreach (WorldVertex vertex in world.Vertices)
        {
            vertex.LightU.ShouldBeInRange(rectangle.U, rectangle.U + rectangle.Width);
            vertex.LightV.ShouldBeInRange(rectangle.V, rectangle.V + rectangle.Height);
        }
    }

    [Test]
    public void Build_TextureCoordinatesArePassedThroughUnchanged()
    {
        // Tiling is correct and must survive: a wall repeats its texture, so coordinates outside
        // 0..1 are the normal case and clamping them would stretch one texel across the surface.
        MapWorld world = MapWorldBuilder.Build([Surface(0, material: 0, corners: 3, u: 12.5f)], Materials, LightmapAtlas.Pack([]), Camera, null);

        world.Vertices[0].U.ShouldBe(12.5f);
    }

    private static BspSurface Surface(
        int faceIndex,
        int material,
        int corners,
        float normalZ = 1f,
        SurfaceProperties flags = SurfaceProperties.None,
        float u = 0.25f)
    {
        List<SurfaceVertex> vertices = [];

        for (int index = 0; index < corners; index++)
        {
            vertices.Add(new SurfaceVertex(
                index * 100f, index * 50f, 0f, u, 0.5f, index / (float)corners, 0.5f));
        }

        return new BspSurface(
            faceIndex, vertices, material, default, (0f, 0f, normalZ), flags, -1);
    }

    private static BspLightmap Lightmap(int width, int height) =>
        new(width, height, new byte[width * height * 4]);
}
