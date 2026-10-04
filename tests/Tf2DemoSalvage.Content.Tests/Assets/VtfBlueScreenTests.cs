using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>The two blue-screen formats, which five of TF2's sky faces ship in (`sky-hdr` probe, B461).</summary>
/// <remarks>
/// <c>IMAGE_FORMAT_RGB888_BLUESCREEN</c> (9) and <c>IMAGE_FORMAT_BGR888_BLUESCREEN</c> (10) store three bytes a texel, in the
/// order their names give; pure blue is the key, read as transparent. The key's own colour after conversion lives in the
/// closed bitmap library and is not asserted — only that it is the key.
/// </remarks>
public sealed class VtfBlueScreenTests
{
    [Test]
    public void Decode_Rgb888BlueScreen_ReadsRgbAndKeysPureBlue()
    {
        byte[] file = VtfFixture.Build(VtfFormat.Rgb888BlueScreen, 2, 1, 1, [10, 20, 30, 0, 0, 255]);

        byte[] pixels = VtfTexture.Decode(file).Pixels;

        pixels[..4].ShouldBe(new byte[] { 10, 20, 30, 255 });
        pixels[7].ShouldBe((byte)0, "pure blue is the key");
    }

    [Test]
    public void Decode_Bgr888BlueScreen_SwapsAndKeysPureBlue()
    {
        byte[] file = VtfFixture.Build(VtfFormat.Bgr888BlueScreen, 2, 1, 1, [30, 20, 10, 255, 0, 0]);

        byte[] pixels = VtfTexture.Decode(file).Pixels;

        pixels[..4].ShouldBe(new byte[] { 10, 20, 30, 255 });
        pixels[7].ShouldBe((byte)0, "pure blue is the key");
    }
}
