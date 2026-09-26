using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Text on the surface: where each glyph's quad lands, and where the glyph lives in a page.</summary>
/// <remarks>
/// vguimatsurface.dll, renamed in `tf2vguimatsurface`: `DrawUnicodeChar` (0x18000e350) through
/// `DrawGetUnicodeCharRenderInfo` (0x18000ab20) — the quad at pen + a, <c>b + 2</c> by <c>tall + 2</c>, the pen then on
/// by a + b + c; `DrawPrintText` (0x18000b990) — quads at <c>floor( a + 0.6 )</c> from the pen, the pen on by
/// <c>floor( a + b + c + 0.6 )</c>, whitespace advancing without a quad; `CFontTextureCache_GetTextureForChars`
/// (0x180005d70) and its allocator (0x180005610) — a <c>max( b, 1 ) + 2</c> by <c>max( tall, 1 ) + 2</c> cell in a
/// 1024-square page for its height bucket, rows packed with a one-texel gap.
/// </remarks>
public sealed class VguiTextConformanceTests
{
    [Test]
    public void DrawUnicodeChar_OneGlyph_IsAtPenPlusAAndAdvancesByTheWholeWidth()
    {
        VguiDrawList list = Current(out VguiFontAmalgam font);

        list.DrawSetTextFont(font);
        list.DrawSetTextColor((255, 255, 255, 255));
        list.DrawSetTextPos(3, 4);
        list.DrawUnicodeChar('A');

        VguiQuad quad = list.Quads.ShouldHaveSingleItem();
        (quad.X0, quad.Y0, quad.X1, quad.Y1).ShouldBe((14f, 24f, 26f, 46f), "panel (10, 20) + pen (3, 4) + a 1; b + 2 by tall + 2");
        (quad.S0, quad.T0, quad.S1, quad.T1).ShouldBe((0f, 0f, 12f / 1024f, 22f / 1024f));
        list.DrawGetTextPos().ShouldBe((16, 4));
    }

    [Test]
    public void DrawPrintText_ASpace_AdvancesWithoutAQuad()
    {
        VguiDrawList list = Current(out VguiFontAmalgam font);

        list.DrawSetTextFont(font);
        list.DrawSetTextColor((255, 255, 255, 255));
        list.DrawSetTextPos(3, 4);
        list.DrawPrintText("A B");

        list.Quads.Count.ShouldBe(2);
        list.Quads[0].X0.ShouldBe(14f, "13 + floor( 1 + 0.6 )");
        list.Quads[1].X0.ShouldBe(40f, "13 + 13 + 13 + 1");
        list.Quads[1].S0.ShouldBe(13f / 1024f, "the second cell sits one texel after the first's 12");
        list.DrawGetTextPos().ShouldBe((42, 4));
    }

    [Test]
    public void DrawUnicodeChar_AtZeroTextAlpha_DrawsNothing()
    {
        VguiDrawList list = Current(out VguiFontAmalgam font);

        list.DrawSetTextFont(font);
        list.DrawSetTextColor((255, 255, 255, 0));
        list.DrawUnicodeChar('A');

        list.Quads.ShouldBeEmpty();
    }

    [Test]
    public void DrawSetTextColor_BakesTheAlphaMultiplierIn()
    {
        VguiDrawList list = Current(out VguiFontAmalgam font);

        list.AlphaMultiplier = 0.5f;
        list.DrawSetTextFont(font);
        list.DrawSetTextColor((9, 8, 7, 201));
        list.AlphaMultiplier = 1f;
        list.DrawUnicodeChar('A');

        list.Quads[0].AlphaTopLeft.ShouldBe((byte)100);
    }

    [Test]
    public void GlyphPage_HoldsTheRasterisedGlyphAtItsCell()
    {
        VguiDrawList list = Current(out VguiFontAmalgam font);

        list.DrawSetTextFont(font);
        list.DrawSetTextColor((255, 255, 255, 255));
        list.DrawUnicodeChar('A');

        (int wide, _, byte[] rgba, _) = list.Pages[list.Quads[0].Texture!];
        int at = ((0 * wide) + 0) * 4;

        (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]).ShouldBe(((byte)30, (byte)20, (byte)10, (byte)22), "the fake's ExtTextOut pixel: (int)( 30 × .34 + 20 × .55 + 10 × .11 )");
    }

    private static VguiDrawList Current(out VguiFontAmalgam font)
    {
        VguiFontManager manager = new(new FakeGdi { Colour = (30, 20, 10) });
        VguiPanel panel = new(null, "Panel") { X = 10, Y = 20, Wide = 500, Tall = 300 };

        font = manager.CreateFont();
        manager.SetFontGlyphSet(font, new VguiFont("Tahoma", 20, 400, 0, 0, 0, 1f, 1f), 0, 0);
        VguiLayout.InternalSolveTraverse(panel);

        VguiDrawList list = new(_ => (0, 0), manager);

        list.PushMakeCurrent(panel, useInset: false);

        return list;
    }
}
