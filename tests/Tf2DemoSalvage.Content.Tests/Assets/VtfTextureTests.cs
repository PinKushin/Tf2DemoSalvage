using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Decoding the game's own textures.
/// </summary>
/// <remarks>
/// **The mip ordering is the defect this file exists for.** A VTF stores its chain smallest first,
/// so reading at the start of the image section yields a 1x1 texture that decodes perfectly and
/// looks like a solid colour. That is an error which produces a picture rather than an exception,
/// and no amount of staring at a rendered map would identify it — every surface simply looks
/// flat-shaded.
///
/// The DXT fixtures are built by hand from the block layout, which is small enough to predict
/// exactly: two 16-bit endpoints and sixteen two-bit selectors. Every colour asserted below is
/// arithmetic, not an observation of what the decoder happened to produce.
/// </remarks>
public sealed class VtfTextureTests
{
    [Test]
    public void Decode_NotAVtf_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => VtfTexture.Decode(new byte[128]));
    }

    [Test]
    public void Decode_Bgra_SwapsToRgba()
    {
        // BGRA is the common uncompressed format, and the swap is the whole content of reading it.
        // A test on a grey pixel could not tell swapped from unswapped, so the colour is lopsided.
        byte[] pixel = [0x10, 0x20, 0x30, 0xFF];
        byte[] file = Vtf(VtfFormat.Bgra8888, width: 1, height: 1, mips: 1, images: pixel);

        VtfTexture texture = VtfTexture.Decode(file);

        texture.Pixels[0].ShouldBe((byte)0x30, "red should come from the third byte");
        texture.Pixels[1].ShouldBe((byte)0x20);
        texture.Pixels[2].ShouldBe((byte)0x10, "blue should come from the first byte");
        texture.Pixels[3].ShouldBe((byte)0xFF);
    }

    [Test]
    public void Decode_Dxt1_ExpandsTheEndpointsExactly()
    {
        // One block, selector 0 everywhere, so every pixel is endpoint zero. Pure red in 565 is
        // 0xF800, and five bits of full scale expand to 255 rather than 248.
        byte[] block = Dxt1Block(first: 0xF800, second: 0x0000, indices: 0x00000000);
        byte[] file = Vtf(VtfFormat.Dxt1, width: 4, height: 4, mips: 1, images: block);

        VtfTexture texture = VtfTexture.Decode(file);

        texture.Width.ShouldBe(4);
        texture.Height.ShouldBe(4);
        texture.Pixels[0].ShouldBe((byte)255);
        texture.Pixels[1].ShouldBe((byte)0);
        texture.Pixels[2].ShouldBe((byte)0);
        texture.Pixels[3].ShouldBe((byte)255);
    }

    [Test]
    public void Decode_Dxt1_InterpolatesTheThirdColour()
    {
        // Selector 2 with first > second is the two-thirds mix, which is where a decoder that
        // used the one-bit-alpha halfway mix instead would differ: 170 rather than 128.
        byte[] block = Dxt1Block(first: 0xF800, second: 0x0000, indices: 0xAAAAAAAA);
        byte[] file = Vtf(VtfFormat.Dxt1, width: 4, height: 4, mips: 1, images: block);

        VtfTexture texture = VtfTexture.Decode(file);

        // (2 * 255 + 0) / 3 = 170.
        texture.Pixels[0].ShouldBe((byte)170);
    }

    [Test]
    public void Decode_Dxt1_WithoutOrderedEndpoints_MakesTheFourthIndexTransparent()
    {
        // first <= second selects the one-bit-alpha form, where index 3 is transparent black. A
        // decoder that ignored the comparison would return opaque colour here.
        byte[] block = Dxt1Block(first: 0x0000, second: 0xF800, indices: 0xFFFFFFFF);
        byte[] file = Vtf(VtfFormat.Dxt1, width: 4, height: 4, mips: 1, images: block);

        VtfTexture texture = VtfTexture.Decode(file);

        texture.Pixels[3].ShouldBe((byte)0, "index 3 must be transparent in the one-bit-alpha form");
    }

    [Test]
    public void Decode_ChoosesTheFullSizeMipByDefault()
    {
        // Two mips: 8x8 at level 0 and 4x4 at level 1, stored SMALLEST FIRST. The small one is
        // pure blue and the large one pure red, so reading the wrong level is unmistakable.
        byte[] small = Dxt1Block(first: 0x001F, second: 0x0000, indices: 0);
        byte[] large = Dxt1Blocks(count: 4, first: 0xF800, second: 0x0000);
        byte[] file = Vtf(VtfFormat.Dxt1, width: 8, height: 8, mips: 2, images: [.. small, .. large]);

        VtfTexture texture = VtfTexture.Decode(file);

        texture.Width.ShouldBe(8);
        texture.Level.ShouldBe(0);
        texture.Pixels[0].ShouldBe((byte)255, "the full-size mip is red");
        texture.Pixels[2].ShouldBe((byte)0);
    }

    [Test]
    public void Decode_WithASizeLimit_TakesTheSmallerMip()
    {
        // The reason the limit exists: an overhead view of a whole map does not need 1024-pixel
        // textures, and Valve already generated the smaller ones.
        byte[] small = Dxt1Block(first: 0x001F, second: 0x0000, indices: 0);
        byte[] large = Dxt1Blocks(count: 4, first: 0xF800, second: 0x0000);
        byte[] file = Vtf(VtfFormat.Dxt1, width: 8, height: 8, mips: 2, images: [.. small, .. large]);

        VtfTexture texture = VtfTexture.Decode(file, maximumSize: 4);

        texture.Width.ShouldBe(4);
        texture.Level.ShouldBe(1);
        texture.Pixels[2].ShouldBe((byte)255, "the smaller mip is blue");
    }

    [Test]
    public void Decode_ATruncatedFile_FailsAsBadData()
    {
        byte[] file = Vtf(VtfFormat.Dxt1, width: 64, height: 64, mips: 1, images: new byte[8]);

        Should.Throw<InvalidDataException>(() => VtfTexture.Decode(file));
    }

    [Test]
    public void Decode_AnUnknownFormat_IsRefusedRatherThanGuessed()
    {
        // A guess here decodes to plausible colours, which is worse than failing.
        byte[] file = Vtf((VtfFormat)63, width: 4, height: 4, mips: 1, images: new byte[64]);

        Should.Throw<InvalidDataException>(() => VtfTexture.Decode(file));
    }

    /// <summary>Builds a DXT1 block: two endpoints and sixteen two-bit selectors.</summary>
    [Test]
    public void Decode_ATextureWithNoFlags_ReportsNoneAndIsNotABumpMap()
    {
        // The control. Every other fixture in this file leaves the flags field zero, so this pins
        // what that means before the next test asserts a flag was seen.
        VtfTexture texture = VtfTexture.Decode(
            Vtf(VtfFormat.Dxt1, 4, 4, 1, Dxt1Blocks(1, 0xFFFF, 0xFFFF)));

        texture.Flags.ShouldBe(0u);
        texture.IsSelfShadowBump.ShouldBeFalse();
    }

    [Test]
    public void Decode_ATextureFlaggedAsASelfShadowBump_SaysSo()
    {
        // **The engine overrides a material's stated detail blend mode from this flag.** Valve's
        // helper reads the detail texture's flags and forces mode 10 or 11 regardless of what
        // $detailblendmode says, so a material that names mode 0 and points at an ssbump does not
        // draw as mode 0. Without this the surface gets a mod2x of a normal map, which is a
        // plausible-looking pattern rather than an error.
        VtfTexture texture = VtfTexture.Decode(
            Vtf(VtfFormat.Dxt1, 4, 4, 1, Dxt1Blocks(1, 0xFFFF, 0xFFFF), flags: 0x08000000));

        texture.IsSelfShadowBump.ShouldBeTrue();
    }

    [Test]
    public void Decode_ATextureWithOtherFlagsSet_IsNotMistakenForASelfShadowBump()
    {
        // A neighbouring bit rather than an arbitrary one: 0x04000000 sits directly below the
        // ssbump bit, so an off-by-one shift or a "flags are non-zero" test passes on it.
        VtfTexture texture = VtfTexture.Decode(
            Vtf(VtfFormat.Dxt1, 4, 4, 1, Dxt1Blocks(1, 0xFFFF, 0xFFFF), flags: 0x04000000));

        texture.Flags.ShouldBe(0x04000000u);
        texture.IsSelfShadowBump.ShouldBeFalse();
    }

    private static byte[] Dxt1Block(ushort first, ushort second, uint indices)
    {
        byte[] block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, first);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), second);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), indices);
        return block;
    }

    private static byte[] Dxt1Blocks(int count, ushort first, ushort second)
    {
        byte[] blocks = new byte[count * 8];

        for (int index = 0; index < count; index++)
        {
            Dxt1Block(first, second, 0).CopyTo(blocks, index * 8);
        }

        return blocks;
    }

    /// <summary>Builds a VTF with no thumbnail, so the image data starts at the header's end.</summary>
    private static byte[] Vtf(
        VtfFormat format, int width, int height, int mips, byte[] images, uint flags = 0)
    {
        const int HeaderSize = 80;

        byte[] file = new byte[HeaderSize + images.Length];

        Encoding.ASCII.GetBytes("VTF\0").CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 7);   // major version
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 2);   // minor version
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24), 1);  // frames
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(52), (int)format);
        file[56] = (byte)mips;

        // No thumbnail: format -1, and zero dimensions.
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(57), -1);

        images.CopyTo(file, HeaderSize);
        return file;
    }
}
