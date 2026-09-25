using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>An HDR cubemap bake goes to the GPU as the half floats it is stored as.</summary>
/// <remarks>
/// `c&lt;x&gt;_&lt;y&gt;_&lt;z&gt;.hdr.vtf` is `IMAGE_FORMAT_RGBA16161616F`: eight bytes a texel in linear light, which Direct3D samples
/// as `R16G16B16A16_FLOAT`. The pitch is a row of texels, not of blocks — a block pitch would skew every face.
/// </remarks>
public sealed class HdrCubemapUploadTests
{
    [Test]
    public void BlockFormat_HalfFloats_IsR16G16B16A16Float() =>
        WorldRenderer.BlockFormat(VtfFormat.Rgba16161616F, srgb: false)
            .ShouldBe(Silk.NET.DXGI.Format.FormatR16G16B16A16Float);

    [TestCase(32, 0, 256)]
    [TestCase(32, 1, 128)]
    [TestCase(32, 5, 8)]
    [TestCase(32, 6, 8)]
    public void BlockPitch_HalfFloats_IsEightBytesATexel(int width, int level, int expected) =>
        WorldRenderer.BlockPitch(VtfFormat.Rgba16161616F, width, level).ShouldBe(expected);

    [Test]
    public void IsNative_HalfFloats_GoesToTheGpuUntouched() =>
        new TextureImage(VtfFormat.Rgba16161616F, [new byte[8]]).IsNative.ShouldBeTrue();

    [Test]
    public void IsNative_Rgba_IsWidenedAndMipped() =>
        TextureImage.Rgba(new byte[4]).IsNative.ShouldBeFalse();
}
