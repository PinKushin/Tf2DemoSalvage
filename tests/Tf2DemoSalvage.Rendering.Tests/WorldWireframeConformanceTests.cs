using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What the world, brush entities, displacements and decals draw under <c>mat_wireframe</c> (B803), and what fill a
/// material draws with (B808) — `engine.dll` of 2026-10-07, read in `docs/findings/113-mat-wireframe-is-a-material.md`.
/// </summary>
/// <remarks>
/// One 20-unit face seen from straight above fills the 64-pixel frame (as <c>ErrorChequerRenderTests</c> draws it), split
/// into two triangles along its diagonal. Materials, as the engine's VMTs ship them (`platform_misc`): 0 the face, red
/// UnlitGeneric; 1 `debugworldwireframe`, `Wireframe` `$color [0.9 0.5 0.5]` `$ignorez 1` `$decal 1`; 2
/// `debugworldwireframezbuffer`, `[0 1 .5]`, `$ignorez 0`; 3 `debugwireframe`, `[1 1 1]`, `$ignorez 0` `$decal 1`; 4
/// `debugdecalwireframe`, `[1 1 0]`, `$ignorez 1` `$decal 1`; 5 a nearer blue face; 6 a blue `$decal`; 7 a map's own `Wireframe`
/// material, white; 8 a translucent green. The `Wireframe` shader is VertexLitGeneric's helper run unlit over a white base
/// (B788), so an edge is `$color` itself.
/// </remarks>
public sealed class WorldWireframeConformanceTests
{
    private const int Size = 64;

    private static readonly (int R, int G, int B) Red = (255, 0, 0);
    private static readonly (int R, int G, int B) Black = (0, 0, 0);

    /// <summary>`debugworldwireframe`'s `$color [0.9 0.5 0.5]`, in gamma as the VMT writes it.</summary>
    private static readonly (int R, int G, int B) World = (230, 128, 128);

    /// <summary>`debugworldwireframezbuffer`'s `[0 1 .5]`.</summary>
    private static readonly (int R, int G, int B) ZBuffer = (0, 255, 128);

    /// <summary>`debugwireframe`'s `[1 1 1]`.</summary>
    private static readonly (int R, int G, int B) White = (255, 255, 255);

    /// <remarks>
    /// **Mode 0 is the control: the face is red to the edge.** The diagonal pixel and an interior one are both the face's.
    /// </remarks>
    [Test]
    public void DrawWorld_WithoutWireframe_DrawsTheFaceAlone()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 0);

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(Inside.X, Inside.Y)).ShouldBe((Red, Red));
    }

    /// <remarks>
    /// **`mat_wireframe 1` draws the face solid, then overlays its triangles with `debugworldwireframe`** (`FUN_1800e39d0`
    /// draws the chains with their own materials, then `FUN_1800dc720` binds `0x18069b3e0` and redraws every surface,
    /// `0x1800dc7a0`): the diagonal is the material's `[0.9 0.5 0.5]`, the interior the face's red. Before B803 the face
    /// drew its own red in line fill: the interior was the clear black.
    /// </remarks>
    [Test]
    public void DrawWorld_UnderWireframeOne_DrawsTheFaceSolidAndOutlinesItInTheWorldWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 1);

        Near(target.PixelAt(Edge.X, Edge.Y), World, "the diagonal, debugworldwireframe's colour");
        target.PixelAt(Inside.X, Inside.Y).ShouldBe(Red, "the face itself, solid");
    }

    /// <remarks>
    /// **`mat_wireframe 2` binds `debugworldwireframezbuffer`** (`FUN_1800dc720`: `0x18069b3e8` when the ConVar's int is 2):
    /// the diagonal is `[0 1 .5]`.
    /// </remarks>
    [Test]
    public void DrawWorld_UnderWireframeTwo_OutlinesInTheZBufferedWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 2);

        Near(target.PixelAt(Edge.X, Edge.Y), ZBuffer, "the diagonal, debugworldwireframezbuffer's colour");
        target.PixelAt(Inside.X, Inside.Y).ShouldBe(Red);
    }

    /// <remarks>
    /// **Mode 1's edges draw through a nearer face; mode 2's are hidden by it** — `$ignorez 1` against `$ignorez 0`. A blue
    /// face 5 units nearer covers the probe, which lies on the far face's diagonal and off the near face's own (the near
    /// face is split along its other diagonal). Mode 0 is the control: blue.
    /// </remarks>
    [TestCase(0, 0, 0, 255)]
    [TestCase(1, 230, 128, 128)]
    [TestCase(2, 0, 0, 255)]
    public void DrawWorld_AnEdgeBehindANearerFace_ShowsByItsWireframesIgnoreZ(int wireframe, int red, int green, int blue)
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe, nearer: true);

        Near(target.PixelAt(Covered.X, Covered.Y), (red, green, blue), "the far face's diagonal under the near face");
    }

    /// <remarks>
    /// **A displacement draws `debug/debugwireframe` and nothing else** (`FUN_1800c2140`: with `sv_cheats` and `mat_wireframe` it
    /// binds `0x18069b3c8`, draws the mesh and returns without queuing the displacement's own draw): the edge white, the
    /// interior the clear black — not the face's red.
    /// </remarks>
    [Test]
    public void DrawWorld_ADisplacementUnderWireframe_DrawsOnlyTheWhiteWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 1, category: SurfaceCategory.Terrain);

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(Inside.X, Inside.Y)).ShouldBe((White, Black));
    }

    /// <remarks>
    /// **A world decal draws `debugdecalwireframe` in place of its own material** (`FUN_1800c6210` `0x1800c6a21`,
    /// `FUN_18011aa60` `0x18011b07b`): the decal, the face itself here with the blue material, shows yellow edges and leaves
    /// the face's red inside. Without wireframe the control: blue over red.
    /// </remarks>
    [TestCase(0, 0, 0, 255, 0, 0, 255)]
    [TestCase(1, 255, 255, 0, 255, 0, 0)]
    public void DrawWorld_ADecalUnderWireframe_IsReplacedByTheDecalWireframe(
        int wireframe, int edgeRed, int edgeGreen, int edgeBlue, int insideRed, int insideGreen, int insideBlue)
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe, decal: true);

        Near(target.PixelAt(Edge.X, Edge.Y), (edgeRed, edgeGreen, edgeBlue), "the decal's diagonal");
        target.PixelAt(Inside.X, Inside.Y).ShouldBe((insideRed, insideGreen, insideBlue));
    }

    /// <remarks>
    /// **The fill is the drawn material's own flag** (B808): the `Wireframe` shader sets `MATERIAL_VAR_WIREFRAME`
    /// (`wireframe_dx9.cpp:37`), so a face whose own material is `Wireframe` draws edges only with `mat_wireframe` 0 — and
    /// a face's own red stays solid under `mat_wireframe` 1 (the test above). The face's material is the map's own (7), not
    /// one of the debug ones, which have table entries of their own in production and draw only under the switch.
    /// </remarks>
    [Test]
    public void DrawWorld_AFaceWhoseOwnMaterialIsWireframe_DrawsInLineFillWithoutMatWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 0, faceMaterial: 7);

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(Inside.X, Inside.Y)).ShouldBe((White, Black));
    }

    /// <remarks>
    /// **`mat_wireframe 3` outlines each face's own polygon and not its triangles** (`FUN_1800dc720` hands the list to
    /// `FUN_1800e6780` when the value is 3; that builds one `MATERIAL_LINES` list of every surface's edges with three or more
    /// vertices, from the last vertex round to the first, out of the map's vertex-index table, and draws it with
    /// `debugworldwireframe`, `0x18069b3e0`). A square face fanned into two triangles: under 3 its diagonal (32, 32) is the
    /// face's own red and its left edge (16, 32) the material's `[0.9 0.5 0.5]`. Under 1 the diagonal is outlined too, the
    /// control that the probe lies on it.
    /// </remarks>
    [TestCase(1, 230, 128, 128)]
    [TestCase(3, 255, 0, 0)]
    public void DrawWorld_UnderWireframeThree_OutlinesTheFacesPolygonWithoutItsDiagonal(int wireframe, int red, int green, int blue)
    {
        using OffscreenTarget target = Target();
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };

        target.Clear(0f, 0f, 0f);
        target.Wireframe = wireframe;
        target.DrawWorld(FannedSquare(), [new WorldBatch(0, 0, 6)], camera.ToMatrix(), Assets());
        Dump(target);

        Near(target.PixelAt(Edge.X, Edge.Y), (red, green, blue), "the fan's diagonal");
        Near(target.PixelAt(PolygonEdge.X, PolygonEdge.Y), World, "the polygon's left edge");
        target.PixelAt(PolygonInside.X, PolygonInside.Y).ShouldBe(Red, "the face itself, solid");
    }

    /// <remarks>
    /// **A displacement keeps its triangles under 3** (`FUN_1800c2140` draws its `debug/debugwireframe` mesh whatever the
    /// value, and no displacement reaches `FUN_1800dc720`'s list): the diagonal (32, 32) of the same fanned square, drawn as
    /// terrain, is white, though its corners say the diagonal is no face edge.
    /// </remarks>
    [Test]
    public void DrawWorld_ADisplacementUnderWireframeThree_KeepsItsTriangleEdges()
    {
        using OffscreenTarget target = Target();
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };

        target.Clear(0f, 0f, 0f);
        target.Wireframe = 3;
        target.DrawWorld(FannedSquare(), [new WorldBatch(0, 0, 6, Category: SurfaceCategory.Terrain)], camera.ToMatrix(), Assets());

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(PolygonInside.X, PolygonInside.Y)).ShouldBe((White, Black));
    }

    /// <remarks>
    /// **A run that starts past the buffer's first vertex outlines its own face** (B803 review 1, Sonnet): the outline is two
    /// indices a vertex, so the run at vertex 6 starts at index 12. A face out of view fills vertices 0 to 5; the fanned
    /// square is the second run, and its diagonal (32, 32) is the face's red and its left (16, 32) and right (47, 32) edges
    /// the outline's colour. The right edge is the run's last triangle's: an index offset of 6 instead of 12 draws the
    /// run's first half from the face before it and stops short of it.
    /// </remarks>
    [Test]
    public void DrawWorld_UnderWireframeThreeOnASecondRun_OutlinesThatRunsOwnFace()
    {
        using OffscreenTarget target = Target();
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };
        WorldVertex[] away = [.. Face(100f, 110f, 0f, alongDiagonal: true).Select(corner => corner with { FaceEdge = true })];

        target.Clear(0f, 0f, 0f);
        target.Wireframe = 3;
        target.DrawWorld([.. away, .. FannedSquare()], [new WorldBatch(0, 0, 6), new WorldBatch(0, 6, 6)], camera.ToMatrix(), Assets());
        Dump(target);

        Near(target.PixelAt(Edge.X, Edge.Y), Red, "the second run's diagonal, its face's own red");
        Near(target.PixelAt(PolygonEdge.X, PolygonEdge.Y), World, "the second run's left edge");
        Near(target.PixelAt(47, 32), World, "the second run's right edge, its last triangle's, which an offset of 6 leaves out");
    }

    /// <remarks>
    /// **A translucent displacement draws `debug/debugwireframe` alone too** (B803 review 1, Sonnet): the translucent path
    /// `FUN_1800e61d0` hands its displacements to `FUN_1800c73a0`, which reaches the same `FUN_1800c2140` as the opaque ones,
    /// and its outline list is the surfaces alone. So the translucent green displacement is white edges over the clear
    /// black, with no green and no pink.
    /// </remarks>
    [Test]
    public void DrawWorld_ATranslucentDisplacementUnderWireframe_DrawsOnlyTheWhiteWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 1, category: SurfaceCategory.Terrain, faceMaterial: 8);

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(Inside.X, Inside.Y)).ShouldBe((White, Black));
    }

    /// <remarks>
    /// **Each debug wireframe draws with its own material's rasteriser, not a decal pass's** (B803 review 1): shaderlib's
    /// `SetInitialShadowState` (`stdshader_dx9.dll` `0x180063340`) gives a `$decal` material `SHADER_POLYOFFSET_DECAL` and
    /// any other none. A face sloping one unit up per unit across has a blue face 0.05 nearer over it, along the other
    /// diagonal; the probe lies on the far face's diagonal. `debugworldwireframezbuffer` (2, no `$decal`) is depth-tested with
    /// no bias, so the nearer blue hides it; `debugwireframe` (a displacement under 1, `$decal 1`) takes the decal offset,
    /// which pulls a sloped edge 0.05 forward, so it shows white. The value 0 is the control: blue. Before review 1 every
    /// queued run took the overlays' slope bias, and the edge under 2 showed through green.
    /// </remarks>
    [TestCase(0, SurfaceCategory.Brush, 0, 0, 255)]
    [TestCase(2, SurfaceCategory.Brush, 0, 0, 255)]
    [TestCase(1, SurfaceCategory.Terrain, 255, 255, 255)]
    public void DrawWorld_ASlopedEdgeJustBehindANearerFace_TakesItsOwnMaterialsOffset(
        int wireframe, SurfaceCategory category, int red, int green, int blue)
    {
        using OffscreenTarget target = Target();
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };

        target.Clear(0f, 0f, 0f);
        target.Wireframe = wireframe;
        target.DrawWorld(
            [.. Sloped(-4f, 4f, 0f, alongDiagonal: true), .. Sloped(-3f, 3f, 0.05f, alongDiagonal: false)],
            [new WorldBatch(0, 0, 6, Category: category), new WorldBatch(5, 6, 6)],
            camera.ToMatrix(),
            Assets());
        Dump(target);

        Near(target.PixelAt(Covered.X, Covered.Y), (red, green, blue), "the far face's diagonal under the nearer face");
    }

    /// <summary>A square face rising one unit per unit east, lifted, split along either diagonal as <see cref="Face"/> is.</summary>
    private static WorldVertex[] Sloped(float low, float high, float lift, bool alongDiagonal) =>
        [.. Face(low, high, 0f, alongDiagonal).Select(corner => corner with { Depth = corner.X + lift })];

    /// <summary>A square whose edges fall on pixel centres 16 and 47, fanned from its first corner as a BSP face is.</summary>
    /// <remarks>
    /// 64 pixels cover 20 units, so a pixel is 0.3125: ±4.84375 lands on 16.5 and 47.5. Each corner says whether the edge
    /// to the next corner of its triangle is the polygon's: the first triangle's diagonal is its third edge, the second's
    /// its first.
    /// </remarks>
    private static WorldVertex[] FannedSquare()
    {
        const float Low = -4.84375f;
        const float High = 4.84375f;

        WorldVertex Corner(float x, float y, bool faceEdge) =>
            new(x, y, 0f, (x - Low) / (High - Low), (y - Low) / (High - Low), 0f, 0f, 1f, FaceEdge: faceEdge);

        return
        [
            Corner(Low, Low, true), Corner(Low, High, true), Corner(High, High, false),
            Corner(Low, Low, false), Corner(High, High, true), Corner(High, Low, true),
        ];
    }

    /// <summary>A pixel on the fanned square's left edge, halfway along it.</summary>
    private static readonly (int X, int Y) PolygonEdge = (16, 32);

    /// <summary>A pixel inside the fanned square, off its diagonal and its edges.</summary>
    private static readonly (int X, int Y) PolygonInside = (40, 24);

    /// <summary>A pixel on the face's diagonal.</summary>
    private static readonly (int X, int Y) Edge = (32, 32);

    /// <summary>A pixel inside one triangle, well off every edge.</summary>
    private static readonly (int X, int Y) Inside = (48, 16);

    /// <summary>A pixel on the far face's diagonal, under the near face and off its own.</summary>
    private static readonly (int X, int Y) Covered = (28, 28);

    private static OffscreenTarget Target() =>
        Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

    private static void DrawFace(
        OffscreenTarget target,
        int wireframe,
        bool nearer = false,
        SurfaceCategory category = SurfaceCategory.Brush,
        bool decal = false,
        int faceMaterial = 0)
    {
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };
        List<WorldVertex> vertices = [.. Face(-10f, 10f, 0f, alongDiagonal: true)];
        List<WorldBatch> batches = [new WorldBatch(faceMaterial, 0, 6, Category: category)];

        if (nearer)
        {
            vertices.AddRange(Face(-8f, 2f, 5f, alongDiagonal: false));
            batches.Add(new WorldBatch(5, 6, 6));
        }

        WorldVertex[] decalFace = Face(-10f, 10f, 0f, alongDiagonal: true);

        target.Clear(0f, 0f, 0f);
        target.Wireframe = wireframe;
        target.DrawWorld(
            vertices,
            batches,
            camera.ToMatrix(),
            Assets(),
            shotDecals: decal ? (decalFace, [new WorldBatch(6, 0, 6)]) : null);
        Dump(target);
    }

    /// <summary>A square face at a height, split along the diagonal from its low corner or along the other.</summary>
    private static WorldVertex[] Face(float low, float high, float z, bool alongDiagonal)
    {
        WorldVertex Corner(float x, float y) => new(x, y, z, (x - low) / (high - low), (y - low) / (high - low), 0f, 0f, 1f);

        return alongDiagonal
            ? [Corner(low, low), Corner(high, high), Corner(high, low), Corner(low, low), Corner(low, high), Corner(high, high)]
            : [Corner(low, high), Corner(high, low), Corner(low, low), Corner(low, high), Corner(high, high), Corner(high, low)];
    }

    private static MapAssets Assets() =>
        MapAssets.ForMaterials(
            [
                Uniform(255, 0, 0),
                Uniform(255, 255, 255) with { Modulation = (0.9f, 0.5f, 0.5f, 1f), IsDecal = true },
                Uniform(255, 255, 255) with { Modulation = (0f, 1f, 0.5f, 1f) },
                Uniform(255, 255, 255) with { Modulation = (1f, 1f, 1f, 1f), IsDecal = true },
                Uniform(255, 255, 255) with { Modulation = (1f, 1f, 0f, 1f), IsDecal = true },
                Uniform(0, 0, 255),
                Uniform(0, 0, 255) with { IsDecal = true },
                Uniform(255, 255, 255),
                Uniform(0, 255, 0) with { IsTranslucent = true },
            ],
            ["UnlitGeneric", "Wireframe", "Wireframe", "Wireframe", "Wireframe", "UnlitGeneric", "UnlitGeneric", "Wireframe", "UnlitGeneric"],
            engineWireframes: new Dictionary<string, WireframeMaterial>
            {
                [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true),
                [MapAssets.WorldWireframeZBufferPath] = new(2, IgnoresZ: false),
                [MapAssets.DisplacementWireframePath] = new(3, IgnoresZ: false),
                [MapAssets.DecalWireframePath] = new(4, IgnoresZ: true),
            });

    private static MapTexture Uniform(byte r, byte g, byte b) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { r, g, b, (byte)255 }).ToArray()), IsTransparent: false);

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, string because)
    {
        TestContext.Out.WriteLine($"{because}: {actual}, predicted {expected}");
        Math.Abs(actual.R - expected.R).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.G - expected.G).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.B - expected.B).ShouldBeLessThanOrEqualTo(1, because);
    }

    private static void Dump(OffscreenTarget target)
    {
        for (int y = 0; y < Size; y += 2)
        {
            TestContext.Out.WriteLine(string.Concat(Enumerable.Range(0, Size / 2).Select(x =>
            {
                (int r, int g, int b) = target.PixelAt(x * 2, y);

                if (r > 200 && g < 50)
                {
                    return 'R';
                }

                if (b > 200 && r < 50)
                {
                    return 'B';
                }

                return r + g + b == 0 ? '.' : 'w';
            })));
        }
    }
}
