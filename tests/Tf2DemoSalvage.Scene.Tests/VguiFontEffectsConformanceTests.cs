using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The glyph post-effects `CWin32Font::GetCharRGBA` applies, as `vguimatsurface.dll` runs them.</summary>
/// <remarks>
/// Renamed in `tf2vguimatsurface`: dropshadow (0x18001c700), outline (0x18001d000), gaussian blur (0x18001c7b0),
/// scanlines (0x18001d190), rotary (0x18001d140). Expected values are worked by hand from the disassembly's arithmetic.
/// </remarks>
public sealed class VguiFontEffectsConformanceTests
{
    [Test]
    public void DropShadow_ATransparentPixel_TakesTheAlphaUpAndLeftOfItInBlack()
    {
        byte[] rgba = Image(3, 3);
        Set(rgba, 3, 1, 1, 255, 255, 255, 255);

        VguiFontEffects.DropShadow(3, 3, rgba, 1);

        Get(rgba, 3, 2, 2).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)255));
        Get(rgba, 3, 1, 1).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255), "an opaque pixel is left alone");
    }

    [Test]
    public void Outline_AroundAWhitePixel_IsOpaqueBlackOnEveryNeighbour()
    {
        byte[] rgba = Image(3, 3);
        Set(rgba, 3, 1, 1, 255, 255, 255, 255);

        VguiFontEffects.Outline(3, 3, rgba, 1);

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                if (x != 1 || y != 1)
                {
                    Get(rgba, 3, x, y).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)255), $"({x}, {y})");
                }
            }
        }
    }

    [Test]
    public void Outline_ABlackOpaquePixel_SeedsNothing()
    {
        // A neighbour counts only when all four of its channels are non-zero — a shadow does not grow an outline.
        byte[] rgba = Image(3, 3);
        Set(rgba, 3, 1, 1, 0, 0, 0, 255);

        VguiFontEffects.Outline(3, 3, rgba, 1);

        Get(rgba, 3, 0, 0).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)0));
    }

    [Test]
    public void Scanlines_EveryRowButEachNth_IsDarkenedToSeventyPercent()
    {
        byte[] rgba = Image(1, 3);
        Set(rgba, 1, 0, 0, 200, 200, 200, 99);
        Set(rgba, 1, 0, 1, 200, 200, 200, 99);
        Set(rgba, 1, 0, 2, 200, 200, 200, 99);

        VguiFontEffects.Scanlines(1, 3, rgba, 2);

        Get(rgba, 1, 0, 0).ShouldBe(((byte)200, (byte)200, (byte)200, (byte)99));
        Get(rgba, 1, 0, 1).ShouldBe(((byte)140, (byte)140, (byte)140, (byte)99), "(int)( 200 × 0.7 ), alpha untouched");
        Get(rgba, 1, 0, 2).ShouldBe(((byte)200, (byte)200, (byte)200, (byte)99));
    }

    [Test]
    public void Scanlines_OfOne_DoNothing()
    {
        byte[] rgba = Image(1, 2);
        Set(rgba, 1, 0, 1, 200, 200, 200, 99);

        VguiFontEffects.Scanlines(1, 2, rgba, 1);

        Get(rgba, 1, 0, 1).ShouldBe(((byte)200, (byte)200, (byte)200, (byte)99));
    }

    [Test]
    public void Rotary_TheMiddleRow_IsOpaqueGrey() =>
        Get(Rotated(), 2, 1, 2).ShouldBe(((byte)0x7f, (byte)0x7f, (byte)0x7f, (byte)0xff));

    /// <remarks>
    /// Blur 1: σ = 0.683, weights ≈ 0.20149, 0.58425, 0.20149. A lone opaque pixel at (2, 1) in a 5 × 3 image becomes
    /// 51 / 148 / 51 across its row, then each column spreads down. The last column, and the last row, see only the
    /// sample a blur-width before them — the engine's window at the far edge is `off − x + wide` long.
    /// </remarks>
    [Test]
    public void Blur_ALonePixel_SpreadsByTheTruncatedGaussian()
    {
        byte[] rgba = Image(5, 3);
        Set(rgba, 5, 2, 1, 255, 255, 255, 255);

        VguiFontEffects.GaussianBlur(5, 3, rgba, 1);

        Alphas(rgba, 5, 0).ShouldBe([0, 10, 29, 10, 0]);
        Alphas(rgba, 5, 1).ShouldBe([0, 29, 86, 29, 0]);
        Alphas(rgba, 5, 2).ShouldBe([0, 10, 29, 10, 0]);
        Get(rgba, 5, 2, 1).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)86), "not additive: RGB is white wherever alpha is");
        Get(rgba, 5, 0, 1).ShouldBe(((byte)0, (byte)0, (byte)0, (byte)0));
    }

    [Test]
    public void Blur_APixelInTheLastColumn_LeavesItsOwnColumnEmpty()
    {
        // At x = 4 of 5 the window is `off − x + wide` = 1 long and starts at x − blur = 3: the pixel never samples itself.
        byte[] rgba = Image(5, 3);
        Set(rgba, 5, 4, 1, 255, 255, 255, 255);

        VguiFontEffects.GaussianBlur(5, 3, rgba, 1);

        Alphas(rgba, 5, 1).ShouldBe([0, 0, 0, 29, 0]);
    }

    private static byte[] Rotated()
    {
        byte[] rgba = Image(2, 4);

        VguiFontEffects.Rotary(2, 4, rgba);

        return rgba;
    }

    private static byte[] Image(int wide, int tall) => new byte[wide * tall * 4];

    private static void Set(byte[] rgba, int wide, int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        int at = ((y * wide) + x) * 4;

        (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (red, green, blue, alpha);
    }

    private static (byte, byte, byte, byte) Get(byte[] rgba, int wide, int x, int y)
    {
        int at = ((y * wide) + x) * 4;

        return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
    }

    private static int[] Alphas(byte[] rgba, int wide, int y)
    {
        int[] row = new int[wide];

        for (int x = 0; x < wide; x++)
        {
            row[x] = rgba[(((y * wide) + x) * 4) + 3];
        }

        return row;
    }
}
