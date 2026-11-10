namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>DrawScreenSpaceRectangle</c>'s edge texcoords (`materialsystem.dll` <c>0x18007e980</c>, tier2 <c>renderutils.cpp</c>,
/// read in disassembly): <c>( x0 + 0.5 - step / 2 ) / w</c> to <c>( x1 + 0.5 + step / 2 ) / w</c>, step the texel run per
/// pixel.
/// </summary>
public sealed class ScreenRectangleConformanceTests
{
    /// <remarks>
    /// `DrawUnderwaterOverlay`'s plain rectangle, <c>0, 0, 1, 1, 1, 1</c>, over 64 pixels: step 1/63, so the edges are
    /// 0.5 − 0.5/63 and 1.5 + 0.5/63 — the texture shifted by half and wrapped, not 0 to 1.
    /// </remarks>
    [Test]
    public void Edges_ThePlainRectangleOver64Pixels_RunHalfATextureOn()
    {
        (float left, float top, float right, float bottom) = ScreenRectangle.Edges(64, 64, 0f, 0f, 1f, 1f, 1, 1);

        left.ShouldBe(0.5f - (0.5f / 63f), 1e-6f);
        right.ShouldBe(1.5f + (0.5f / 63f), 1e-6f);
        (top, bottom).ShouldBe((left, right));
    }

    /// <remarks>
    /// The frame copy's rectangle, <c>0, 0, 1023, 1023, 1024, 1024</c>, over 64 by 32 pixels: step 1023/63 across and
    /// 1023/31 down, so the left edge is (0.5 − 1023/126)/1024 and the bottom (1023.5 + 1023/62)/1024.
    /// </remarks>
    [Test]
    public void Edges_TheFrameCopyRectangle_WidenByHalfAStepEachSide()
    {
        (float left, float top, float right, float bottom) = ScreenRectangle.Edges(64, 32, 0f, 0f, 1023f, 1023f, 1024, 1024);

        left.ShouldBe((0.5f - (1023f / 126f)) / 1024f, 1e-6f);
        right.ShouldBe((1023.5f + (1023f / 126f)) / 1024f, 1e-6f);
        top.ShouldBe((0.5f - (1023f / 62f)) / 1024f, 1e-6f);
        bottom.ShouldBe((1023.5f + (1023f / 62f)) / 1024f, 1e-6f);
    }

    /// <remarks>One pixel across has no step: the texel's centre on both edges.</remarks>
    [Test]
    public void Edges_OnePixelWide_IsTheTexelsCentre()
    {
        ScreenRectangle.Edges(1, 1, 3f, 3f, 3f, 3f, 8, 8).ShouldBe((3.5f / 8f, 3.5f / 8f, 3.5f / 8f, 3.5f / 8f));
    }
}
