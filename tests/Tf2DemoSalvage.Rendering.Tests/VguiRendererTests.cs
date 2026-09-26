using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Where a <see cref="VguiQuad"/> lands in clip space, and which alpha each corner carries.</summary>
public sealed class VguiRendererTests
{
    [Test]
    public void BuildVertices_AQuadInTheTopLeftQuarter_SpansMinusOneToZeroAndCarriesItsCornerAlphas()
    {
        VguiQuad quad = new(null, 0f, 0f, 50f, 25f, 0f, 0f, 1f, 1f, 255, 0, 0, 10, 20, 30, 40);

        float[] data = VguiRenderer.BuildVertices([quad], 100, 50);

        // (tl, tr, br) and (tl, br, bl), eight floats each: x y s t r g b a.
        data.Length.ShouldBe(6 * 8);
        (data[0], data[1], data[7]).ShouldBe((-1f, 1f, 10f / 255f), "top-left");
        (data[8], data[9], data[15]).ShouldBe((0f, 1f, 20f / 255f), "top-right");
        (data[16], data[17], data[23]).ShouldBe((0f, 0f, 30f / 255f), "bottom-right");
        (data[40], data[41], data[47]).ShouldBe((-1f, 0f, 40f / 255f), "bottom-left");
        data[4].ShouldBe(1f, "red");
    }
}
