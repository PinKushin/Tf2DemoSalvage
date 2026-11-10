using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A material that resolved to nothing draws the texture manager's <c>error</c> texture (B713).</summary>
public sealed class ErrorChequerRenderTests
{
    private const int Size = 64;

    /// <remarks>
    /// **Squares of four texels, eight across the 32-texel texture** (`ErrorTexture`, `materialsystem.dll` <c>0x1800550e0</c>,
    /// <c>0x180056880</c>). One face whose texcoords run 0 to 1 fills the 64-pixel frame from straight above, so a square is
    /// eight pixels: pixels 20 and 28 fall in neighbouring squares, one magenta and one black, whichever way the view maps
    /// the face. The chequer drawn until 2026-11-10 had squares of sixteen pixels, which put both in one square.
    /// </remarks>
    [Test]
    public void DrawWorld_AMaterialThatResolvedToNothing_IsTheEnginesFourTexelChequer()
    {
        MapAssets map = MapAssets.ForMaterials([null], ["LightmappedGeneric"]);
        FreeCamera camera = new() { Origin = (0f, 0f, 10f), Angles = (90f, 0f, 0f), Aspect = 1f };
        WorldVertex[] face =
        [
            new(-10f, -10f, 0f, 0f, 0f, 0f, 0f, 1f),
            new(10f, 10f, 0f, 1f, 1f, 0f, 0f, 1f),
            new(10f, -10f, 0f, 1f, 0f, 0f, 0f, 1f),
            new(-10f, -10f, 0f, 0f, 0f, 0f, 0f, 1f),
            new(-10f, 10f, 0f, 0f, 1f, 0f, 0f, 1f),
            new(10f, 10f, 0f, 1f, 1f, 0f, 0f, 1f),
        ];

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(face, [new WorldBatch(0, 0, face.Length)], camera.ToMatrix(), map);

        (int Red, int Green, int Blue) first = target.PixelAt(20, 20);
        (int Red, int Green, int Blue) second = target.PixelAt(28, 20);

        TestContext.Out.WriteLine($"PIXEL 20,20 {first}; PIXEL 28,20 {second}");

        (int Red, int Green, int Blue) magenta = first.Red > second.Red ? first : second;
        (int Red, int Green, int Blue) black = first.Red > second.Red ? second : first;

        magenta.Red.ShouldBeGreaterThan(40, "one of the two is a magenta square");
        magenta.Green.ShouldBeLessThan(magenta.Red / 4);
        black.Red.ShouldBeLessThan(8, "and the other a black one");
    }
}
