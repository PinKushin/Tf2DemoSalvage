using System;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CircularProgressBar::DrawCircleSegment` (vgui2/vgui_controls/CircularProgressBar.cpp:209-353).</summary>
public sealed class VguiCircularProgressBarConformanceTests
{
    [Test]
    public void CircleSegment_ClockwiseAQuarter_IsTwoWholeWedgesToTheRightEdge()
    {
        Span<VguiVertex> triangles = stackalloc VguiVertex[24];

        int count = VguiCircularProgressBar.CircleSegment(0.25f, true, 100, 100, triangles);

        count.ShouldBe(2);
        (triangles[1].X, triangles[1].Y, triangles[2].X, triangles[2].Y).ShouldBe((50f, 0f, 100f, 0f), "the first eighth: top centre to top right");
        (triangles[5].X, triangles[5].Y).ShouldBe((100f, 50f), "the second ends at the right edge's middle");
    }

    /// <remarks>By hand: 3/16 is half of segment 1, an odd one, so its 22.5 degrees are counted back from the right edge's middle: y = 50 - 50 tan( pi/8 ).</remarks>
    [Test]
    public void CircleSegment_ClockwiseThreeSixteenths_EndsPartWayDownTheRightEdge()
    {
        Span<VguiVertex> triangles = stackalloc VguiVertex[24];

        int count = VguiCircularProgressBar.CircleSegment(3f / 16f, true, 100, 100, triangles);

        count.ShouldBe(2);
        triangles[5].X.ShouldBe(100f, 0.001f);
        triangles[5].Y.ShouldBe(50f - (50f * MathF.Tan(MathF.PI / 8f)), 0.001f);
    }

    [Test]
    public void CircleSegment_NothingFilled_DrawsNoTriangle()
    {
        Span<VguiVertex> triangles = stackalloc VguiVertex[24];

        VguiCircularProgressBar.CircleSegment(0f, true, 100, 100, triangles).ShouldBe(0);
    }

    [Test]
    public void CircleSegment_FullCircle_IsEightWedges()
    {
        Span<VguiVertex> triangles = stackalloc VguiVertex[24];

        VguiCircularProgressBar.CircleSegment(1f, true, 100, 100, triangles).ShouldBe(8);
    }
}
