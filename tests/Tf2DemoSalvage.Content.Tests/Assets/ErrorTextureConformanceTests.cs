using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The material system's <c>error</c> texture: <c>CCheckerboardTexture( 4, 0x80000000, 0xffff00ff )</c> over 32 × 32
/// (`materialsystem.dll` <c>0x1800550e0</c>, <c>0x180056880</c>, read in disassembly).
/// </summary>
public sealed class ErrorTextureConformanceTests
{
    /// <remarks>
    /// The generator writes magenta where <c>(4 &amp; (x ^ y)) == 0</c>: texel (0, 0) and (4, 4) are magenta, (4, 0) and
    /// (0, 4) black at alpha 128, and (3, 0) still magenta, so a square is four texels, not eight.
    /// </remarks>
    [TestCase(0, 0, true)]
    [TestCase(3, 0, true)]
    [TestCase(4, 0, false)]
    [TestCase(0, 4, false)]
    [TestCase(4, 4, true)]
    [TestCase(31, 31, true)]
    [TestCase(8, 3, true)]
    [TestCase(12, 3, false)]
    public void Rgba_ATexel_IsTheCheckerboardsColour(int x, int y, bool magenta)
    {
        byte[] pixels = ErrorTexture.Rgba();
        int at = ((y * ErrorTexture.Size) + x) * 4;

        (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]).ShouldBe(
            magenta ? ((byte)255, (byte)0, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)128));
    }

    /// <remarks>Thirty-two square, one level: the procedural texture has <c>NOMIP</c>.</remarks>
    [Test]
    public void Image_TheTexture_IsOne32By32RgbaLevel()
    {
        TextureImage image = ErrorTexture.Image();

        (image.Format, image.Levels.Count, image.Top.Length).ShouldBe((VtfFormat.Rgba8888, 1, 32 * 32 * 4));
    }
}
