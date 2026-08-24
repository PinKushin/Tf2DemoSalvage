using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Packing glyphs into one texture, and placing a string across it.
/// </summary>
/// <remarks>
/// **This is the half of the HUD that is arithmetic, and it is deliberately separable** (D84). The
/// owner asked for the rasteriser to sit behind an interface — *"we want to be able to test this so
/// a interface is probably not a bad idea, its SOLID if nothing else"* — and the payoff is here:
/// every number below is checked without GDI, without a font installed, and without a device.
///
/// What is being protected is worth naming, because it is not the glyph pictures. A wrong advance
/// width is a bug that shows as text that crawls or overlaps; a one-pixel difference in
/// antialiasing is not a bug at all. This suite covers the first kind exhaustively and the second
/// not at all, which is the right division.
/// </remarks>
public sealed class GlyphAtlasTests
{
    /// <summary>
    /// A rasteriser with no font behind it, producing glyphs whose size is derived from the
    /// character so every assertion below can predict an exact number.
    /// </summary>
    /// <remarks>
    /// **Deliberately irregular.** Every glyph a different width, and an advance that is not the
    /// width, because a fake where everything is 8x8 would let a packer that ignored the metrics
    /// pass — the "wrong condition" failure from the testing standards, where correct and broken
    /// predict the same observation.
    /// </remarks>
    private sealed class ShapedRasteriser : IGlyphRasteriser
    {
        /// <summary>Width of the glyph for a character: 'a' is 1 wide, 'b' is 2, and so on.</summary>
        public static int WidthOf(char character) => (character - 'a') + 1;

        /// <summary>Advance is one more than the width, so the two cannot be confused.</summary>
        public static int AdvanceOf(char character) => WidthOf(character) + 1;

        public int LineHeight(SchemeFont font) => font.Tall + 2;

        public RasterisedGlyph Rasterise(SchemeFont font, char character)
        {
            int width = WidthOf(character);
            int height = font.Tall;

            byte[] pixels = new byte[width * height * GlyphAtlas.BytesPerPixel];

            // Filled with the character itself, so a packer that copies the wrong glyph into a slot
            // is caught by reading the pixels back rather than only by their size.
            Array.Fill(pixels, (byte)character);

            return new RasterisedGlyph
            {
                Metrics = new GlyphMetrics(width, height, LeftBearing: 0, TopBearing: 0, AdvanceOf(character)),
                Pixels = pixels,
            };
        }
    }

    private static readonly SchemeFont TenTall = new() { Name = "Fake", Tall = 10 };

    private static GlyphAtlas Build(string characters, int maximumWidth = 512) =>
        GlyphAtlas.Build(new ShapedRasteriser(), TenTall, characters, maximumWidth);

    [Test]
    public void Build_EveryRequestedCharacter_IsInTheAtlas()
    {
        GlyphAtlas atlas = Build("abcd");

        foreach (char character in "abcd")
        {
            atlas.TryGlyph(character, out AtlasGlyph glyph)
                .ShouldBeTrue($"'{character}' was asked for");

            glyph.Width.ShouldBe(ShapedRasteriser.WidthOf(character));
            glyph.Advance.ShouldBe(ShapedRasteriser.AdvanceOf(character));
        }
    }

    [Test]
    public void TryGlyph_ACharacterNobodyAskedFor_IsAbsent()
    {
        // Not a blank glyph of zero width, which would draw nothing and silently swallow every
        // character outside the set. Absent, so the caller decides -- a HUD that meets an unexpected
        // character has either the wrong character set or a bug, and both want reporting.
        Build("abcd").TryGlyph('z', out _).ShouldBeFalse();
    }

    /// <summary>
    /// Glyphs do not overlap one another in the packed texture.
    /// </summary>
    /// <remarks>
    /// **The defect this exists for is invisible in a screenshot of ordinary text**: two glyphs
    /// sharing a pixel column show as a faint smear on one character, which reads as a font
    /// artefact rather than as a packing bug. Checked as rectangles rather than by eye.
    /// </remarks>
    [Test]
    public void Build_ThePackedGlyphs_DoNotOverlap()
    {
        GlyphAtlas atlas = Build("abcdefghij", maximumWidth: 16);

        List<AtlasGlyph> placed = [];

        foreach (char character in "abcdefghij")
        {
            atlas.TryGlyph(character, out AtlasGlyph glyph).ShouldBeTrue();
            placed.Add(glyph);
        }

        for (int one = 0; one < placed.Count; one++)
        {
            for (int two = one + 1; two < placed.Count; two++)
            {
                bool apart =
                    placed[one].X + placed[one].Width <= placed[two].X ||
                    placed[two].X + placed[two].Width <= placed[one].X ||
                    placed[one].Y + placed[one].Height <= placed[two].Y ||
                    placed[two].Y + placed[two].Height <= placed[one].Y;

                apart.ShouldBeTrue($"glyphs {one} and {two} share pixels");
            }
        }
    }

    [Test]
    public void Build_EveryGlyph_LiesInsideTheTexture()
    {
        GlyphAtlas atlas = Build("abcdefghij", maximumWidth: 16);

        foreach (char character in "abcdefghij")
        {
            atlas.TryGlyph(character, out AtlasGlyph glyph).ShouldBeTrue();

            glyph.X.ShouldBeGreaterThanOrEqualTo(0);
            glyph.Y.ShouldBeGreaterThanOrEqualTo(0);
            (glyph.X + glyph.Width).ShouldBeLessThanOrEqualTo(atlas.Width);
            (glyph.Y + glyph.Height).ShouldBeLessThanOrEqualTo(atlas.Height);
        }
    }

    /// <summary>
    /// A row that runs out of width wraps to the next one.
    /// </summary>
    /// <remarks>
    /// The condition is chosen so the two answers differ: 'a' through 'e' are 1+2+3+4+5 = 15 pixels
    /// wide in total, so a 16-pixel texture fits them all on one row and an 8-pixel one cannot. A
    /// packer that never wrapped would put every glyph on row zero and run off the edge, which the
    /// bounds test above would also catch — so this one asserts the ROW, not just containment.
    /// </remarks>
    [Test]
    public void Build_MoreGlyphsThanFitOneRow_WrapsToTheNextRow()
    {
        GlyphAtlas wide = Build("abcde", maximumWidth: 16);
        GlyphAtlas narrow = Build("abcde", maximumWidth: 8);

        wide.Height.ShouldBeLessThan(narrow.Height, "the narrow atlas needs more rows");

        narrow.TryGlyph('a', out AtlasGlyph first).ShouldBeTrue();
        narrow.TryGlyph('e', out AtlasGlyph last).ShouldBeTrue();

        last.Y.ShouldBeGreaterThan(first.Y, "'e' cannot share a row with 'a' in eight pixels");
    }

    /// <summary>
    /// The pixels of a glyph land where the atlas says they are.
    /// </summary>
    /// <remarks>
    /// **The control that catches a packer copying to the wrong offset.** Every other test here
    /// reads only the rectangles the packer reports, so a packer that recorded correct rectangles
    /// and then blitted somewhere else would pass all of them. The fake fills each glyph with its
    /// own character code, so reading one pixel back names which glyph actually landed there.
    /// </remarks>
    [Test]
    public void Build_ThePixelsAtAGlyphsRectangle_AreThatGlyphs()
    {
        GlyphAtlas atlas = Build("abcd", maximumWidth: 16);

        foreach (char character in "abcd")
        {
            atlas.TryGlyph(character, out AtlasGlyph glyph).ShouldBeTrue();

            byte sample = atlas.Pixels[
                (((glyph.Y * atlas.Width) + glyph.X) * GlyphAtlas.BytesPerPixel)];

            sample.ShouldBe((byte)character, $"the pixels at '{character}' belong to another glyph");
        }
    }

    [Test]
    public void Measure_AString_IsTheSumOfItsAdvances()
    {
        GlyphAtlas atlas = Build("abcd");

        // 2 + 3 + 4 + 5 = 14, and note this is the sum of ADVANCES not of widths (1+2+3+4 = 10),
        // which is what makes the fake's deliberate gap between the two worth having.
        TextLayout.Measure(atlas, "abcd").ShouldBe(14);
    }

    [Test]
    public void Measure_AnEmptyString_IsZero()
    {
        TextLayout.Measure(Build("abcd"), string.Empty).ShouldBe(0);
    }

    /// <summary>
    /// A character the atlas does not carry advances by nothing rather than throwing.
    /// </summary>
    /// <remarks>
    /// A HUD line is built from live data — a player's name, a map name — and one unexpected
    /// character must not take the frame down. Skipped rather than substituted, because a
    /// substitute glyph is a lie about what the data said.
    /// </remarks>
    [Test]
    public void Measure_AStringWithAnUnknownCharacter_SkipsIt()
    {
        GlyphAtlas atlas = Build("abcd");

        TextLayout.Measure(atlas, "azb").ShouldBe(TextLayout.Measure(atlas, "ab"));
    }

    [Test]
    public void Place_AString_AdvancesEachGlyphByThePreviousAdvance()
    {
        GlyphAtlas atlas = Build("abcd");

        PlacedGlyph[] placed = [.. TextLayout.Place(atlas, "abc", 100, 50)];

        placed.Length.ShouldBe(3);

        // 'a' at the origin, then 'b' at +2 (a's advance), then 'c' at +2+3.
        placed[0].X.ShouldBe(100);
        placed[1].X.ShouldBe(102);
        placed[2].X.ShouldBe(105);

        placed.Select(glyph => glyph.Y).ShouldAllBe(y => y == 50);
    }

    /// <summary>
    /// Bearings offset a glyph from the pen without moving the pen.
    /// </summary>
    /// <remarks>
    /// **The distinction a naive layout gets wrong.** The pen advances by `Advance`; where the
    /// glyph's box is DRAWN relative to the pen is `LeftBearing`/`TopBearing`. Conflating them
    /// makes every character with a negative left bearing — an italic 'f', a 'j' — sit a pixel or
    /// two off, cumulatively.
    /// </remarks>
    [Test]
    public void Place_AGlyphWithBearings_OffsetsTheGlyphButNotThePen()
    {
        GlyphAtlas atlas = GlyphAtlas.Build(new BearingRasteriser(), TenTall, "ab", 512);

        PlacedGlyph[] placed = [.. TextLayout.Place(atlas, "ab", 0, 0)];

        // Each glyph is drawn three right and two down from its pen position...
        placed[0].X.ShouldBe(3);
        placed[0].Y.ShouldBe(2);

        // ...but the pen still moved by the advance alone, so 'b' starts at 10 + 3.
        placed[1].X.ShouldBe(13);
    }

    /// <summary>Every glyph five wide, advancing ten, drawn three right and two down.</summary>
    private sealed class BearingRasteriser : IGlyphRasteriser
    {
        public int LineHeight(SchemeFont font) => font.Tall;

        public RasterisedGlyph Rasterise(SchemeFont font, char character) => new()
        {
            Metrics = new GlyphMetrics(5, font.Tall, LeftBearing: 3, TopBearing: 2, Advance: 10),
            Pixels = new byte[5 * font.Tall * GlyphAtlas.BytesPerPixel],
        };
    }
}
