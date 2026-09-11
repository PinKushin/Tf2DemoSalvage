using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Tests.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A decoded VTF as a material's texture slot (B390).</summary>
public sealed class MapTextureTests
{
    /// <remarks>
    /// **Both sizes, from one decode.** An 8x4 file capped at four texels decodes its 4x2 level; the
    /// slot must upload 4x2 and still say it was authored at 8x4, because that is what the engine's
    /// <c>GetMappingWidth</c> answers and what a sprite is sized by.
    /// </remarks>
    [Test]
    public void Of_AVtfDecodedBelowItsHeader_CarriesBothSizes()
    {
        byte[] file = VtfFixture.Build(
            VtfFormat.Bgr888,
            width: 8,
            height: 4,
            mips: 4,
            images: new byte[((1 * 1) + (2 * 1) + (4 * 2) + (8 * 4)) * 3]);

        MapTexture texture = MapTexture.Of(VtfTexture.Read(file, maximumSize: 4));

        (texture.Width, texture.Height).ShouldBe((4, 2), "the control: the cap did drop a level");
        (texture.MappingWidth, texture.MappingHeight).ShouldBe((8, 4));
    }
}
