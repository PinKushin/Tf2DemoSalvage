using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which pixels a 3D triangle covers: TF2's rasteriser puts a pixel's centre on its integer coordinate (B686).
/// </summary>
/// <remarks>
/// **The engine's rule, written as the expectation.** TF2 draws through Direct3D 9, whose viewport maps clip space onto
/// pixel coordinates with pixel <c>i</c>'s centre AT <c>i</c> ("Directly Mapping Texels to Pixels (Direct3D 9)"); Direct3D
/// 10 and later put it at <c>i + 0.5</c>. shaderapidx9 hands the viewport over as integers and unchanged
/// (<c>FUN_180015f10</c>, the only <c>IDirect3DDevice9::SetViewport</c> call, vtable <c>+0x178</c>), so nothing in the
/// engine moves a 3D vertex to compensate, and DXVK reproduces D3D9 by offsetting every viewport half a pixel
/// (<c>D3D9DeviceEx::BindViewportAndScissor</c>, <c>cf = 0.5f</c>). Valve's own GL layer does the same in clip space
/// (<c>cglmprogram.h:240-242</c>, "Apply half pixel offset to output vertices to account for the pixel center difference
/// between D3D9 and OpenGL").
///
/// A square from x = 4.25 to 11.25 pixels, and the same down: a pixel is covered when its D3D9 centre <c>i</c> is inside,
/// so columns and rows 5 to 11. Direct3D 11 unshifted covers 4 to 10 — both edges differ, so either sign of error fails.
/// The positions are exact binary fractions, and no centre sits on an edge, so no fill-rule tie decides anything.
/// </remarks>
public sealed class PixelCentreConformanceTests
{
    private const int Size = 16;

    /// <summary>The square's edges, in pixels from the target's top-left corner.</summary>
    private const float Near = 4.25f;

    private const float Far = 11.25f;

    /// <summary>Columns 5 to 11 lit: the integer centres inside 4.25 to 11.25.</summary>
    private static readonly bool[] Covered = [.. Enumerable.Range(0, Size).Select(i => i >= 5 && i <= 11)];

    [Test]
    public void DrawWorld_ASquareWithEdgesAtAQuarterPixel_CoversThePixelsWhoseD3D9CentresItContains()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        List<WorldVertex> square = Square();

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(square, [new WorldBatch(0, 0, square.Count)], ModulateToneMapConformanceTests.Identity, Assets);

        Profile(target).ShouldBe(Expected());
    }

    [Test]
    public void DrawModelPose_ASquareWithEdgesAtAQuarterPixel_CoversThePixelsWhoseD3D9CentresItContains()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        List<WorldVertex> square = Square();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            square,
            [new WorldBatch(0, 0, square.Count)],
            ModulateToneMapConformanceTests.Identity,
            ModulateToneMapConformanceTests.Identity,
            Assets,
            bothSides: true);

        Profile(target).ShouldBe(Expected());
    }

    private static readonly MapAssets Assets =
        MapAssets.ForMaterials([ModulateToneMapConformanceTests.Grey(255, noCull: true)], ["UnlitGeneric"]);

    /// <summary>Row 8 across, then column 8 down, as covered or not.</summary>
    private static string Profile(OffscreenTarget target)
    {
        string across = new([.. Enumerable.Range(0, Size).Select(x => Lit(target.PixelAt(x, 8)))]);
        string down = new([.. Enumerable.Range(0, Size).Select(y => Lit(target.PixelAt(8, y)))]);

        TestContext.Out.WriteLine($"ACROSS {across} DOWN {down}");

        return across + " " + down;
    }

    private static string Expected()
    {
        string line = new([.. Covered.Select(on => on ? '#' : '.')]);

        return line + " " + line;
    }

    private static char Lit((int Red, int Green, int Blue) pixel) => pixel.Red > 64 ? '#' : '.';

    /// <summary>The square in clip space, wound both ways so no cull can hide it.</summary>
    private static List<WorldVertex> Square()
    {
        float left = ClipX(Near);
        float right = ClipX(Far);
        float top = ClipY(Near);
        float bottom = ClipY(Far);

        WorldVertex Corner(float x, float y) => new(x, y, 0.5f, 0.5f, 0.5f, 0f, 0f, 0f);

        WorldVertex a = Corner(left, top);
        WorldVertex b = Corner(right, top);
        WorldVertex c = Corner(right, bottom);
        WorldVertex d = Corner(left, bottom);

        return [a, b, c, a, c, d, a, c, b, a, d, c];
    }

    private static float ClipX(float pixels) => (pixels / Size * 2f) - 1f;

    private static float ClipY(float pixels) => 1f - (pixels / Size * 2f);
}
