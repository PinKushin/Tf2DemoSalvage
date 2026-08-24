using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Fonts;

namespace Tf2DemoSalvage.Fonts.Tests;

/// <summary>
/// The GDI rasteriser, tested only on what genuinely needs a real font.
/// </summary>
/// <remarks>
/// **The division of labour is deliberate and is half of why the interface exists** (D84). Every
/// number a HUD depends on — where a glyph lands, how wide a string is, how the atlas packs — is
/// tested in `Content.Tests` against a fake rasteriser, with no font installed and no GDI. What is
/// left here is what a fake cannot answer: does a real face actually produce ink, and does the
/// scheme's `outline` reach the pixels.
///
/// **Nothing here asserts an exact bitmap**, and that is not laziness. Pixel parity with TF2 needs
/// Valve's own post-processing, which is closed — `vguimatsurface` is not in `source-sdk-2013` — so
/// an exact-pixel assertion would be pinning OUR approximation in place and calling it parity. The
/// properties below are the ones that are true of any correct implementation.
/// </remarks>
public sealed class GdiGlyphRasteriserTests
{
    /// <summary>TF2's own meter font: `platform/Resource/SourceScheme.res`.</summary>
    private static SchemeFont Meter => new()
    {
        Name = "Lucida Console",
        Tall = 10,
        Weight = 0,
        Outline = true,
    };

    /// <summary>The same face without the outline, as the control.</summary>
    private static SchemeFont Plain => Meter with { Outline = false };

    /// <summary>Skips when the machine does not have the family, rather than measuring a fallback.</summary>
    /// <remarks>
    /// **A missing family falls back to the platform default rather than throwing**, which is what
    /// GDI does and what the rasteriser deliberately copies. That makes it invisible to an
    /// assertion — the test would pass, having measured a different font. So it is checked
    /// explicitly and skipped, per `docs/memory/a-skip-is-not-a-pass-or-a-failure.md`.
    /// </remarks>
    [SetUp]
    public void RequireTheFamily()
    {
        bool present = FontFamily.Families.Any(family =>
            family.Name.Equals("Lucida Console", StringComparison.OrdinalIgnoreCase));

        if (!present)
        {
            Assert.Ignore("Lucida Console is not installed, so any measurement would be of a fallback");
        }
    }

    private static int Opaque(RasterisedGlyph glyph)
    {
        int count = 0;

        for (int at = 3; at < glyph.Pixels.Length; at += GlyphAtlas.BytesPerPixel)
        {
            if (glyph.Pixels.Span[at] > 0)
            {
                count++;
            }
        }

        return count;
    }

    [Test]
    public void Rasterise_APrintableCharacter_ProducesInk()
    {
        using GdiGlyphRasteriser rasteriser = new();

        Opaque(rasteriser.Rasterise(Plain, 'W')).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A space draws nothing and still moves the pen.
    /// </summary>
    /// <remarks>
    /// **The control that separates "it drew something" from "it drew the right something".** Every
    /// other test here would pass against a rasteriser that filled the whole box, and this one would
    /// not. It also pins the advance/ink split at the one character where they are furthest apart.
    /// </remarks>
    [Test]
    public void Rasterise_ASpace_DrawsNothingButStillAdvances()
    {
        using GdiGlyphRasteriser rasteriser = new();

        RasterisedGlyph space = rasteriser.Rasterise(Plain, ' ');

        Opaque(space).ShouldBe(0, "a space has no ink");
        space.Metrics.Advance.ShouldBeGreaterThan(0, "a space still moves the pen");
    }

    /// <summary>
    /// Lucida Console is monospaced, so every advance is the same.
    /// </summary>
    /// <remarks>
    /// **A real property of the real font, which is what makes this a measurement rather than a
    /// restatement.** A rasteriser that returned the glyph's ink width instead of its advance would
    /// pass every other test in this file and fail this one immediately: 'i' has far less ink than
    /// 'W'.
    ///
    /// It is also the reason `MeasureString` is called with `GenericTypographic`. The default format
    /// adds padding for selection highlighting, and that padding is not constant, so this assertion
    /// fails against it.
    /// </remarks>
    [Test]
    public void Rasterise_AMonospacedFace_GivesEveryCharacterTheSameAdvance()
    {
        using GdiGlyphRasteriser rasteriser = new();

        int[] advances = [.. "iWl.@1"
            .Select(character => rasteriser.Rasterise(Plain, character).Metrics.Advance)];

        advances.Distinct().Count().ShouldBe(
            1, $"Lucida Console is monospaced, got {string.Join(", ", advances)}");
    }

    /// <summary>
    /// The scheme's outline reaches the pixels, and its absence does too.
    /// </summary>
    /// <remarks>
    /// **Both directions, because a one-sided assertion cannot tell an outline from a bigger box.**
    /// The outlined glyph must have strictly more covered pixels than the plain one — the dilation
    /// adds a ring — and the plain one must have none of the black-but-opaque pixels that only an
    /// outline produces.
    ///
    /// Black-but-opaque is the signature: alpha above zero with all three colour channels at zero
    /// happens nowhere in a white glyph, at any antialiasing level, because the colour IS the
    /// coverage there.
    /// </remarks>
    [Test]
    public void Rasterise_WithOutlineSet_AddsBlackOpaquePixelsTheControlDoesNotHave()
    {
        using GdiGlyphRasteriser rasteriser = new();

        RasterisedGlyph outlined = rasteriser.Rasterise(Meter, 'W');
        RasterisedGlyph plain = rasteriser.Rasterise(Plain, 'W');

        static int BlackButOpaque(RasterisedGlyph glyph)
        {
            int count = 0;

            for (int at = 0; at + 3 < glyph.Pixels.Length; at += GlyphAtlas.BytesPerPixel)
            {
                ReadOnlySpan<byte> pixel = glyph.Pixels.Span;

                if (pixel[at + 3] > 0 && pixel[at] == 0 && pixel[at + 1] == 0 && pixel[at + 2] == 0)
                {
                    count++;
                }
            }

            return count;
        }

        BlackButOpaque(outlined).ShouldBeGreaterThan(0, "the outline is black and covers");
        BlackButOpaque(plain).ShouldBe(0, "nothing in a plain white glyph is black and covering");

        Opaque(outlined).ShouldBeGreaterThan(Opaque(plain), "the outline is a ring around the glyph");
    }

    /// <summary>
    /// An outlined glyph is offset back so the body still sits where the pen is.
    /// </summary>
    /// <remarks>
    /// The body is drawn one pixel inside its box to leave room for the ring, so the box must be
    /// placed one pixel back. Getting this wrong shifts every outlined HUD element by a pixel
    /// against every non-outlined one — visible only when the two are side by side, which is
    /// exactly when nobody is looking for it.
    /// </remarks>
    [Test]
    public void Rasterise_WithOutlineSet_OffsetsTheBoxBackByThePadding()
    {
        using GdiGlyphRasteriser rasteriser = new();

        rasteriser.Rasterise(Meter, 'W').Metrics.LeftBearing.ShouldBe(-1);
        rasteriser.Rasterise(Meter, 'W').Metrics.TopBearing.ShouldBe(-1);

        rasteriser.Rasterise(Plain, 'W').Metrics.LeftBearing.ShouldBe(0);
        rasteriser.Rasterise(Plain, 'W').Metrics.TopBearing.ShouldBe(0);
    }

    [Test]
    public void LineHeight_ForTheMeterFont_LeavesRoomForTheOutline()
    {
        using GdiGlyphRasteriser rasteriser = new();

        rasteriser.LineHeight(Meter).ShouldBe(rasteriser.LineHeight(Plain) + 2);
    }

    [Test]
    public void Rasterise_AfterDisposal_Throws()
    {
        GdiGlyphRasteriser rasteriser = new();
        rasteriser.Dispose();

        Should.Throw<ObjectDisposedException>(() => rasteriser.Rasterise(Plain, 'W'));
    }
}
