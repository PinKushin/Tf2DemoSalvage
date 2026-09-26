using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What `CMatSystemSurface` turns a panel's draw calls into, in screen pixels.</summary>
/// <remarks>
/// Closed code, renamed in `tf2vguimatsurface`: `PushMakeCurrent` (0x180011fb0) moves the origin to the panel's absolute
/// position, plus its left and top inset when asked, but clips to the panel's clip rectangle either way; `DrawSetColor`
/// (0x18000ca70) bakes the alpha multiplier in when the colour is set; each draw skips at zero alpha, translates, and
/// clips on the CPU (0x1800039a0) — texture coordinates interpolated to the cut, a rectangle turned inside out rejected;
/// `DrawFilledRectFade` (0x18000a1f0) scales its two alphas by the colour's and does not re-interpolate them when cut;
/// `DrawOutlinedRect` (0x18000b2e0) is four filled rectangles.
/// </remarks>
public sealed class VguiDrawListConformanceTests
{
    [Test]
    public void DrawFilledRect_InAPanelWithInset_IsOffsetByPositionAndInsetButClippedToThePanel()
    {
        VguiDrawList list = Current(out _, useInset: true);

        list.DrawSetColor((1, 2, 3, 255));
        list.DrawFilledRect(0, 0, 100, 5);

        list.Quads.ShouldHaveSingleItem().ShouldBe(Solid(12, 23, 60, 28, 255));
    }

    [Test]
    public void DrawSetColor_BakesTheAlphaMultiplierInWhenSet()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.AlphaMultiplier = 0.5f;
        list.DrawSetColor((1, 2, 3, 201));
        list.AlphaMultiplier = 1f;
        list.DrawFilledRect(0, 0, 1, 1);

        list.Quads[0].AlphaTopLeft.ShouldBe((byte)100, "(int)( 201 × 0.5 )");
    }

    [Test]
    public void DrawFilledRect_AtZeroAlpha_DrawsNothing()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.DrawSetColor((1, 2, 3, 0));
        list.DrawFilledRect(0, 0, 5, 5);

        list.Quads.ShouldBeEmpty();
    }

    [Test]
    public void DrawTexturedRect_CutByTheClip_InterpolatesItsTextureCoordinates()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.DrawSetColor((255, 255, 255, 255));
        list.DrawSetTexture("vgui/thing");
        list.DrawTexturedRect(-20, 0, 20, 10);

        VguiQuad quad = list.Quads.ShouldHaveSingleItem();
        (quad.X0, quad.X1).ShouldBe((10f, 30f), "the panel's clip starts at its absolute x of 10");
        (quad.S0, quad.S1).ShouldBe((0.5f, 1f));
        quad.Texture.ShouldBe("vgui/thing");
    }

    [Test]
    public void DrawFilledRect_EntirelyOutsideTheClip_IsRejected()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.DrawSetColor((1, 1, 1, 255));
        list.DrawFilledRect(100, 0, 110, 5);

        list.Quads.ShouldBeEmpty();
    }

    [Test]
    public void DrawFilledRectFade_Horizontal_RunsLeftToRightScaledByTheColoursAlpha()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.DrawSetColor((1, 1, 1, 128));
        list.DrawFilledRectFade(0, 0, 10, 10, 255, 100, horizontal: true);

        VguiQuad quad = list.Quads.ShouldHaveSingleItem();
        (quad.AlphaTopLeft, quad.AlphaTopRight, quad.AlphaBottomRight, quad.AlphaBottomLeft)
            .ShouldBe(((byte)128, (byte)50, (byte)50, (byte)128), "(uint)( alpha × 128 / 255 )");
    }

    [Test]
    public void DrawOutlinedRect_IsTopBottomLeftRight()
    {
        VguiDrawList list = Current(out _, useInset: false);

        list.DrawSetColor((1, 1, 1, 255));
        list.DrawOutlinedRect(0, 0, 10, 8);

        list.Quads.Count.ShouldBe(4);
        (list.Quads[0].X0, list.Quads[0].Y0, list.Quads[0].X1, list.Quads[0].Y1).ShouldBe((10f, 20f, 20f, 21f));
        (list.Quads[1].X0, list.Quads[1].Y0, list.Quads[1].X1, list.Quads[1].Y1).ShouldBe((10f, 27f, 20f, 28f));
        (list.Quads[2].X0, list.Quads[2].Y0, list.Quads[2].X1, list.Quads[2].Y1).ShouldBe((10f, 21f, 11f, 27f));
        (list.Quads[3].X0, list.Quads[3].Y0, list.Quads[3].X1, list.Quads[3].Y1).ShouldBe((19f, 21f, 20f, 27f));
    }

    /// <summary>A list with a 50 × 30 panel at (10, 20), inset (2, 3, 4, 5), made current.</summary>
    private static VguiDrawList Current(out VguiPanel panel, bool useInset)
    {
        panel = new VguiPanel(null, "Panel") { X = 10, Y = 20, Wide = 50, Tall = 30, Inset = (2, 3, 4, 5) };
        VguiLayout.InternalSolveTraverse(panel);

        VguiDrawList list = new(_ => (64, 64));

        list.PushMakeCurrent(panel, useInset);

        return list;
    }

    private static VguiQuad Solid(float x0, float y0, float x1, float y1, byte alpha) =>
        new(null, x0, y0, x1, y1, 0f, 0f, 0f, 0f, 1, 2, 3, alpha, alpha, alpha, alpha);
}
