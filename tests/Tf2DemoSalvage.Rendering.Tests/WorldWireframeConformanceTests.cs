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
/// `debugdecalwireframe`, `[1 1 0]`, `$ignorez 1` `$decal 1`; 5 a nearer blue face; 6 a blue `$decal`. The `Wireframe` shader is
/// VertexLitGeneric's helper run unlit over a white base (B788), so an edge is `$color` itself.
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
    /// a face's own red stays solid under `mat_wireframe` 1 (the test above).
    /// </remarks>
    [Test]
    public void DrawWorld_AFaceWhoseOwnMaterialIsWireframe_DrawsInLineFillWithoutMatWireframe()
    {
        using OffscreenTarget target = Target();

        DrawFace(target, wireframe: 0, faceMaterial: 3);

        (target.PixelAt(Edge.X, Edge.Y), target.PixelAt(Inside.X, Inside.Y)).ShouldBe((White, Black));
    }

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
            ],
            ["UnlitGeneric", "Wireframe", "Wireframe", "Wireframe", "Wireframe", "UnlitGeneric", "UnlitGeneric"],
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
