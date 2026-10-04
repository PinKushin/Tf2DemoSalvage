using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

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

    /// <summary>No terrain reader: these fixtures have no displacements to read terrain for.</summary>
    private static BspTerrain? Map => null;

    // A `TopDownCamera Camera` fixture sat here until 2026-08-26 (D98) and was passed to every
    // `Build` call. `Build` never read it.

    [Test]
    public void Build_NoSurfaces_ProducesNothing()
    {
        MapWorld world = MapWorldBuilder.Build(
            Map,
            [], Materials, LightmapAtlas.Pack([]), null);

        world.Vertices.ShouldBeEmpty();
        world.Batches.ShouldBeEmpty();
    }

    [Test]
    public void Build_AQuad_BecomesTwoTriangles()
    {
        MapWorld world = MapWorldBuilder.Build(
            Map,
            [Surface(0, material: 3, corners: 4)], Materials, LightmapAtlas.Pack([]), null);

        world.Vertices.Count.ShouldBe(6);
        world.Batches.Count.ShouldBe(1);
        world.Batches[0].MaterialIndex.ShouldBe(3);
        world.Batches[0].VertexCount.ShouldBe(6);
    }

    [Test]
    public void Build_DownwardFacingSurfaces_AreKept()
    {
        // **This test used to assert the opposite, and it was pinning a workaround in place.**
        //
        // Dropping every downward-facing surface at build time was free while the only camera
        // looked straight down: a face pointing away from an overhead view can never be seen from
        // one, and calling it "the engine's own backface culling" made it sound principled.
        //
        // It is not what the engine does. Valve culls per frame, against the view frustum and the
        // PVS, from wherever the camera actually is. Culling once, by the sign of a normal, is
        // equivalent only for a camera that never moves — and the moment a free camera existed it
        // was deleting ceilings, undersides, and any wall whose normal tipped slightly below
        // horizontal.
        //
        // It also produced a whole evening of chasing "floating decals": an overlay pinned to a
        // culled face draws correctly in mid-air with the wall that belongs behind it simply gone.
        //
        // Backface culling still happens, in the rasteriser, per frame, which is where a decision
        // that depends on the camera belongs.
        MapWorld world = MapWorldBuilder.Build(
            Map,
            [Surface(0, material: 0, corners: 3, normalZ: -1f)], Materials, LightmapAtlas.Pack([]),
            null);

        world.Vertices.Count.ShouldBe(3);
    }

    [Test]
    public void Build_InvisibleDisplacement_IsDroppedButToolsBlackIsKept()
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
            new("tools/toolsblack", (0f, 0f, 0f), 32, 32),
        ];

        MapWorld world = MapWorldBuilder.Build(
            Map,
            [
                Surface(0, material: 0, corners: 3),
                Surface(1, material: 1, corners: 3),
                Surface(2, material: 2, corners: 3),
            ],
            materials,
            LightmapAtlas.Pack([]),
            null);

        // **toolsblack is kept, and that is the point of this test now.** It shares the tools/
        // path and is an ordinary drawn surface - 80 visible faces with no flags on
        // cp_process_final, covering 4.8 million square units. Dropping it with its siblings left
        // holes that read as dark blobs.
        world.Batches.Count.ShouldBe(2, "only the invisible displacement should be dropped");
        world.Batches.ShouldNotContain(batch => batch.MaterialIndex == 0);
        world.Batches.ShouldContain(batch => batch.MaterialIndex == 1);
        world.Batches.ShouldContain(batch => batch.MaterialIndex == 2);
    }

    [Test]
    public void Build_ToolSurfaces_AreDropped()
    {
        // Drawing nodraw would put solid slabs across the map.
        MapWorld world = MapWorldBuilder.Build(
            Map,
            [Surface(0, material: 0, corners: 3, flags: SurfaceProperties.NoDraw)], Materials, LightmapAtlas.Pack([]),
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

        MapWorld world = MapWorldBuilder.Build(
            Map,
            surfaces, Materials, LightmapAtlas.Pack([]), null);

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

        MapWorld world = MapWorldBuilder.Build(
            Map,
            [Surface(1, material: 0, corners: 3)], Materials, atlas, null);

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
        MapWorld world = MapWorldBuilder.Build(
            Map,
            [Surface(0, material: 0, corners: 3, u: 12.5f)], Materials, LightmapAtlas.Pack([]), null);

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

    [Test]
    public void Build_ANonDisplacementFace_IsWhiteWithBlendZero()
    {
        // engine.dll 0x1800f4d40 writes a brush face's colour as white, alpha 0 for a material with
        // `$basetexture2` (0x00FFFFFF) and 1 otherwise. That alpha is LightmappedGeneric's blend factor
        // (`lightmappedgeneric_vs20.fxc:250` → `ps2_3_x.h:314`), so a two-texture material on a plain
        // brush draws its FIRST texture; without `$basetexture2` the second is bound to the first and
        // the factor cannot be seen. Zero therefore matches both cases (B329).
        MapWorld world = MapWorldBuilder.Build(
            null,
            [OverlayRenderOrderConformanceTests.Floor()],
            [new BspMaterial("nature/blendgrassdirt", (0.5f, 0.5f, 0.5f), 64, 64)],
            LightmapAtlas.Pack([]),
            null,
            []);

        world.Vertices.ShouldNotBeEmpty();
        world.Vertices.Select(vertex => (vertex.Alpha, vertex.Red, vertex.Green, vertex.Blue)).Distinct()
            .ShouldBe([(0f, 1f, 1f, 1f)]);
    }

    private static BspLightmap Lightmap(int width, int height) =>
        new(width, height, new byte[width * height * 4]);
}
