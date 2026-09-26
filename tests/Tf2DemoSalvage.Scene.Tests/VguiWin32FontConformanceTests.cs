using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CWin32Font`'s rules over GDI, as `vguimatsurface.dll` applies them.</summary>
/// <remarks>
/// Renamed in `tf2vguimatsurface`: `CWin32Font_Create` (0x180017b70), `CWin32Font_GetCharABCWidths` (0x180017ec0),
/// `CWin32Font_GetCharRGBA` (0x1800181e0). GDI itself is faked: these are the numbers the font does with GDI's answers.
/// </remarks>
public sealed class VguiWin32FontConformanceTests
{
    [Test]
    public void Create_OutlineAndDropShadow_AddThreeToTheHeightAndTwoToTheBitmapWidth()
    {
        FakeGdi gdi = new();

        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(VguiFont.Outline | VguiFont.DropShadow))!;

        (font.Height, font.Ascent, font.MaxCharWidth).ShouldBe((20 + 1 + 2, 16, 12));
        gdi.Bitmap.ShouldBe((12 + 2, 23));
    }

    [Test]
    public void Create_AFamilyGdiDoesNotKnow_Fails() =>
        VguiWin32Font.Create(new FakeGdi { Known = false }, Font(0)).ShouldBeNull();

    [TestCase(VguiFont.Antialias, 4, 0)]
    [TestCase(0, 3, 0)]
    [TestCase(VguiFont.Symbol, 3, 2)]
    public void Create_Flags_ChooseQualityAndCharset(int flags, int quality, int charset)
    {
        FakeGdi gdi = new();

        VguiWin32Font.Create(gdi, Font(flags));

        (gdi.Quality, gdi.Charset).ShouldBe((quality, charset));
    }

    [Test]
    public void GetCharAbcWidths_TheFirstCall_IsGdisNumbersAndTheSecondTheWidenedOnes()
    {
        // Only the cache is widened (0x180017ec0): the call that fills it hands back what GDI said.
        VguiWin32Font font = VguiWin32Font.Create(new FakeGdi(), Font(VguiFont.Outline | VguiFont.DropShadow, blur: 2))!;

        font.GetCharAbcWidths('A').ShouldBe((1, 10, 2));
        font.GetCharAbcWidths('A').ShouldBe((1 - 2 - 1, 10 + ((1 + 2) * 2) + 1, 2 - 2 - 1 - 1));
    }

    [Test]
    public void GetCharAbcWidths_WhenGdiHasNone_IsTheTextExtentThenTheMaxWidth()
    {
        FakeGdi gdi = new() { Abc = null, Extent = 7 };
        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(0))!;

        font.GetCharAbcWidths('x').ShouldBe((0, 7, 0));

        gdi.Extent = null;
        font.GetCharAbcWidths('y').ShouldBe((0, 12, 0));
    }

    [Test]
    public void GetCharRgba_Antialiased_ReadsTheGray8LevelsAsSixtyFourths()
    {
        // Black box 3 × 1, pitch 4; origin y 16 against ascent 16 puts it on row 0; b is 10, so no centring.
        FakeGdi gdi = new() { Gray = new VguiGrayGlyph([0, 32, 64, 9], 3, 1, 16) };
        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(VguiFont.Antialias))!;
        byte[] rgba = new byte[4 * 2 * 4];

        font.GetCharRgba('A', 4, 2, rgba);

        Pixel(rgba, 4, 0, 0).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)0));
        Pixel(rgba, 4, 1, 0).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)127), "32 / 64 × 255, truncated");
        Pixel(rgba, 4, 2, 0).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
        gdi.Drawn.ShouldBeFalse();
    }

    [Test]
    public void GetCharRgba_AboveLatin1WithoutCustom_DrawsWithExtTextOutEvenWhenAntialiased()
    {
        FakeGdi gdi = new() { Gray = new VguiGrayGlyph([64], 1, 1, 16) };
        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(VguiFont.Antialias))!;

        font.GetCharRgba('А', 4, 2, new byte[4 * 2 * 4]);

        gdi.Drawn.ShouldBeTrue();
    }

    [Test]
    public void GetCharRgba_ExtTextOut_KeepsTheBitmapsColourAndWeighsItIntoAlpha()
    {
        // Memory order is blue, green, red; the engine weighs bytes 0, 1, 2 by 0.34, 0.55, 0.11 in that order.
        FakeGdi gdi = new() { Colour = (10, 20, 30) };
        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(0))!;
        byte[] rgba = new byte[4 * 2 * 4];

        font.GetCharRgba('A', 4, 2, rgba);

        Pixel(rgba, 4, 0, 0).ShouldBe(((byte)10, (byte)20, (byte)30, (byte)17), "(int)( 10 × .34 + 20 × .55 + 30 × .11 ) = 17");
        gdi.PenX.ShouldBe(-1, "the pen starts at −a");
    }

    [Test]
    public void GetCharRgba_ATab_HasNoColour()
    {
        FakeGdi gdi = new() { Colour = (10, 20, 30) };
        VguiWin32Font font = VguiWin32Font.Create(gdi, Font(0))!;
        byte[] rgba = new byte[4 * 2 * 4];

        font.GetCharRgba('\t', 4, 2, rgba);

        Pixel(rgba, 4, 0, 0).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)0));
    }

    private static VguiFont Font(int flags, int blur = 0) => new("Face", 20, 400, blur, 0, flags, 1f, 1f);

    private static (byte, byte, byte, byte) Pixel(byte[] rgba, int wide, int x, int y)
    {
        int at = ((y * wide) + x) * 4;

        return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
    }

    private sealed class FakeGdi : IVguiGdi
    {
        public bool Known { get; init; } = true;

        public (int A, int B, int C)? Abc { get; init; } = (1, 10, 2);

        public int? Extent { get; set; }

        public VguiGrayGlyph? Gray { get; init; }

        public (byte Blue, byte Green, byte Red) Colour { get; init; }

        public (int Wide, int Tall) Bitmap { get; private set; }

        public int Quality { get; private set; }

        public int Charset { get; private set; }

        public bool Drawn { get; private set; }

        public int PenX { get; private set; }

        public bool AddFontResource(string path) => true;

        public bool FamilyExists(string family) => Known;

        public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality)
        {
            (Quality, Charset) = (quality, charset);

            return new VguiGdiFont(face, 20, 16, 12);
        }

        public void CreateBitmap(VguiGdiFont font, int wide, int tall) => Bitmap = (wide, tall);

        public (int A, int B, int C)? GetCharAbcWidths(VguiGdiFont font, char character) => Abc;

        public int? GetTextExtent(VguiGdiFont font, char character) => Extent;

        public VguiGrayGlyph? GetGlyphOutlineGray8(VguiGdiFont font, int character) => Gray;

        public byte[] DrawGlyph(VguiGdiFont font, char character, int penX, int clearWide, int clearTall)
        {
            (Drawn, PenX) = (true, penX);

            List<byte> bytes = [];

            for (int pixel = 0; pixel < Bitmap.Wide * Bitmap.Tall; pixel++)
            {
                bytes.AddRange([Colour.Blue, Colour.Green, Colour.Red, 0]);
            }

            return [.. bytes];
        }
    }
}
