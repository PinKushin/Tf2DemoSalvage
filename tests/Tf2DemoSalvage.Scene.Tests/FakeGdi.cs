using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>GDI's answers, chosen by the test: the font rules are measured against these rather than a real face.</summary>
internal sealed class FakeGdi : IVguiGdi
{
    /// <summary>Families enumerated; null knows every name.</summary>
    public HashSet<string>? Families { get; init; }

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

    /// <summary>Every face `CreateFontA` was asked for, in order.</summary>
    public List<string> Created { get; } = [];

    /// <summary>Every path `AddFontResourceExA` was given.</summary>
    public List<string> Added { get; } = [];

    public bool AddFontResource(string path)
    {
        Added.Add(path);
        return true;
    }

    public bool FamilyExists(string family) => Known && (Families is null || Families.Contains(family));

    public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality)
    {
        (Quality, Charset) = (quality, charset);
        Created.Add(face);

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
