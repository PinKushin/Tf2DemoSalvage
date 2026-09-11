using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A texture's MAPPING size — the header's own width and height — kept apart from the size of the
/// level that was decoded (B390).
/// </summary>
/// <remarks>
/// **What the engine calls the mapping size, settled in the disassembly rather than guessed from the
/// name.** In TF2's x64 `materialsystem.dll`, `SLoadTextureBitsFromFile` (named by its own telemetry
/// string, `ctexture.cpp`) reads the file HEADER ONLY — `Unserialize( buf, true, 0 )`, no mips skipped —
/// and copies `Width()`, `Height()`, `MipCount()` and `Depth()` into the four shorts at `CTexture+0x28`.
/// `CTexture::GetMappingWidth` is the second slot of `CTexture`'s vtable and is one instruction,
/// `MOVZX EAX, word ptr [RCX + 0x28]`; `GetActualWidth`, two slots later, reads `+0x30` instead. So the
/// mapping size is the file as authored and the actual size is what survived the mip skip — two fields,
/// never one derived from the other.
///
/// **Why it matters here:** `CEngineSprite::Init` sizes every sprite by
/// `m_material[0]->GetMappingWidth()` (`spritemodel.cpp:294-295`). Taking the decoded size instead drew
/// a sprite smaller by exactly the factor the texture-quality cap dropped.
/// </remarks>
public sealed class VtfMappingSizeConformanceTests
{
    [Test]
    public void MappingSize_AtFullSize_IsTheDecodedSize()
    {
        VtfTexture texture = VtfTexture.Read(EightByFour());

        texture.Width.ShouldBe(8, "the control: no level was dropped");
        texture.MappingWidth.ShouldBe(8);
        texture.MappingHeight.ShouldBe(4);
    }

    /// <remarks>
    /// **Capped at four texels, the reader decodes level 1**, the smallest whose longest edge still
    /// reaches the cap: 4x2. The header said 8x4, and that is what `GetMappingWidth` returns.
    /// </remarks>
    [Test]
    public void MappingSize_WhenTheCapDropsALevel_IsStillTheHeadersSize()
    {
        VtfTexture texture = VtfTexture.Read(EightByFour(), maximumSize: 4);

        texture.Level.ShouldBe(1, "the control: the cap did drop a level");
        texture.Width.ShouldBe(4);
        texture.Height.ShouldBe(2);
        texture.MappingWidth.ShouldBe(8);
        texture.MappingHeight.ShouldBe(4);
    }

    /// <remarks>
    /// **The block path returns through its own constructor call**, so it needs its own test: an 8x8
    /// DXT1 texture with four mips, capped at four, hands over its 4x4 level's blocks.
    /// </remarks>
    [Test]
    public void MappingSize_ForABlockTextureAtAReducedLevel_IsStillTheHeadersSize()
    {
        byte[] file = VtfFixture.Build(
            VtfFormat.Dxt1, width: 8, height: 8, mips: 4, images: new byte[8 + 8 + 8 + 32]);

        VtfTexture texture = VtfTexture.Read(file, maximumSize: 4);

        texture.Width.ShouldBe(4, "the control: the cap did drop a level");
        texture.MappingWidth.ShouldBe(8);
        texture.MappingHeight.ShouldBe(8);
    }

    /// <remarks>
    /// **Why the header's size is carried rather than recomputed.** A 6x3 file's level 1 is 3x1, since
    /// <c>3 &gt;&gt; 1</c> is 1; shifting back gives 6x2, not 6x3. <c>levelSize &lt;&lt; Level</c> recovers the
    /// header only for a power of two, and TF2 ships non-power-of-two textures.
    /// </remarks>
    [Test]
    public void MappingSize_ForANonPowerOfTwoHeader_IsNotRecoverableFromTheLevel()
    {
        byte[] file = VtfFixture.Build(
            VtfFormat.Bgr888, width: 6, height: 3, mips: 2, images: new byte[(3 * 1 * 3) + (6 * 3 * 3)]);

        VtfTexture texture = VtfTexture.Read(file, maximumSize: 3);

        (texture.Height << texture.Level).ShouldBe(2, "the control: shifting back loses the odd row");
        texture.MappingWidth.ShouldBe(6);
        texture.MappingHeight.ShouldBe(3);
    }

    /// <summary>An 8x4 BGR888 file with all four levels: 1x1, 2x1, 4x2 and 8x4, smallest first.</summary>
    private static byte[] EightByFour() =>
        VtfFixture.Build(
            VtfFormat.Bgr888,
            width: 8,
            height: 4,
            mips: 4,
            images: new byte[((1 * 1) + (2 * 1) + (4 * 2) + (8 * 4)) * 3]);
}
