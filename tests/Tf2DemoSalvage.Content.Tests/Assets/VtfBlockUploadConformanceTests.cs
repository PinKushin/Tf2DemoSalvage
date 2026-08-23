using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Handing DXT blocks to the GPU untouched, rather than expanding them first (B149).
/// </summary>
/// <remarks>
/// **DXT1, DXT3 and DXT5 are BC1, BC2 and BC3**, which Direct3D samples natively — they are a GPU
/// format, not an archive format. Expanding them to RGBA on the CPU spends real time to produce
/// something four to eight times larger to upload, and it was costing this viewer 16.87 s of CPU
/// over 3,208 textures on one `cp_badlands` open.
///
/// The owner had asked for this to be on the GPU and it was not done:
///
/// > *"i told the AI that was doing the decompressing to unload everything it could on the gpu and
/// > it must have ignored me … thats fning source SDK and video game dev 101 though, you offload
/// > everything you can to the gpu"*
///
/// **The block arithmetic is the whole of the format side**, and it is fixed by S3TC: a block covers
/// 4x4 texels, BC1 stores one in 8 bytes and BC2/BC3 in 16, and a level that is not a multiple of
/// four is padded up to whole blocks. A row of blocks — not of pixels — is what Direct3D wants as
/// the pitch, which is the detail that turns a wrong upload into a skewed image rather than an
/// error.
///
/// **The mip order comes from this reader's own walk**, which the file already documents:
/// *"Smallest mip first. Level mipCount-1 is 1x1, level 0 is full size, so the wanted level's data
/// sits after every level below it — all of its frames and all of their faces."* Direct3D wants the
/// opposite order, largest first, so the chain has to be reversed rather than copied.
/// </remarks>
public sealed class VtfBlockUploadConformanceTests
{
    [Test]
    public void Levels_ADxt1Texture_AreBlocksRatherThanPixels()
    {
        // A 4x4 DXT1 texture is one block: 8 bytes. Decoded it would be 4*4*4 = 64 bytes of RGBA,
        // which is the eight-fold expansion this change exists to avoid.
        byte[] file = Vtf(VtfFormat.Dxt1, width: 4, height: 4, mips: 1, images: Block(8));

        VtfTexture texture = VtfTexture.Read(file);

        texture.IsBlockCompressed.ShouldBeTrue();
        texture.Levels.Count.ShouldBe(1);
        texture.Levels[0].Length.ShouldBe(8);
    }

    [Test]
    public void Levels_ANonBlockFormat_StillDecodesToPixels()
    {
        // **The control, and the reason this is a branch rather than a replacement.** Not every VTF
        // is DXT — `BGR888` and `RGBA8888` are real and appear in the game's own content — and those
        // have no GPU-native form to hand over, so they keep the existing path.
        byte[] file = Vtf(VtfFormat.Bgr888, width: 2, height: 2, mips: 1, images: new byte[2 * 2 * 3]);

        VtfTexture texture = VtfTexture.Read(file);

        texture.IsBlockCompressed.ShouldBeFalse();
        texture.Pixels.Length.ShouldBe(2 * 2 * 4);
    }

    [Test]
    public void Levels_ADxt5Texture_UsesSixteenByteBlocks()
    {
        // BC3 carries an interpolated alpha block alongside the colour block, hence sixteen bytes
        // against BC1's eight. Getting this wrong reads half a texture and skews the rest.
        byte[] file = Vtf(VtfFormat.Dxt5, width: 4, height: 4, mips: 1, images: Block(16));

        VtfTexture texture = VtfTexture.Read(file);

        texture.Levels[0].Length.ShouldBe(16);
    }

    [Test]
    public void Levels_ASizeThatIsNotAWholeNumberOfBlocks_RoundsUp()
    {
        // **A 5x5 texture is 2x2 blocks, not 1.25x1.25.** Sizes that are not multiples of four are
        // ordinary — `$basetexture` on signs and overlays especially — and truncating instead of
        // rounding up under-reads the last row and column of blocks.
        byte[] file = Vtf(VtfFormat.Dxt1, width: 5, height: 5, mips: 1, images: Block(8 * 4));

        VtfTexture texture = VtfTexture.Read(file);

        texture.Levels[0].Length.ShouldBe(8 * 4, "two blocks across by two down");
    }

    [Test]
    public void Levels_AMipChain_IsLargestFirst()
    {
        // **The file stores smallest first and Direct3D wants largest first.** Subresource zero is
        // the top level, so a chain copied in file order uploads a 1x1 image as the full-size mip —
        // which draws a flat colour over everything and looks like a missing texture rather than a
        // reversed list.
        //
        // 8x8 DXT1 with three mips: 8x8 (4 blocks, 32 bytes), 4x4 (1 block, 8), 2x2 and 1x1 (1
        // block each, padded up). Written smallest first, as the format requires.
        byte[] images =
        [
            .. Block(8),        // 1x1
            .. Block(8),        // 2x2
            .. Block(8),        // 4x4
            .. Block(32),       // 8x8
        ];

        byte[] file = Vtf(VtfFormat.Dxt1, width: 8, height: 8, mips: 4, images: images);

        VtfTexture texture = VtfTexture.Read(file);

        texture.Levels.Count.ShouldBe(4);
        texture.Levels[0].Length.ShouldBe(32, "level zero is the full 8x8");
        texture.Levels[^1].Length.ShouldBe(8, "and the last is the 1x1");
    }

    [Test]
    public void Levels_ABlockFormat_SkipsTheCpuDecodeEntirely()
    {
        // **The point of the change, stated as a fact about the result rather than a shape.**
        // Keeping the blocks while still decoding would pass every test above and save nothing at
        // all — exactly the sort of no-op this project has shipped before with a green suite.
        //
        // **Asserted on `Pixels` rather than on `VtfTexture.DecodeCost`, and the first version used
        // the counter.** That counter is static and this suite runs its fixtures in parallel, so
        // another test decoding at the same moment moved it — a test whose result depended on what
        // else happened to be running. An empty pixel buffer says the same thing and says it about
        // this call alone.
        VtfTexture texture =
            VtfTexture.Read(Vtf(VtfFormat.Dxt1, width: 4, height: 4, mips: 1, images: Block(8)));

        texture.Pixels.ShouldBeEmpty("a block format is handed over, not expanded");
        texture.Levels[0].Length.ShouldBe(8, "and the blocks are what came out of the file");
    }

    /// <summary>Bytes standing in for compressed blocks; the contents are never interpreted.</summary>
    private static byte[] Block(int bytes) => new byte[bytes];

    /// <summary>A minimal VTF header followed by image data, matching what the reader parses.</summary>
    private static byte[] Vtf(VtfFormat format, int width, int height, int mips, byte[] images)
    {
        const int HeaderSize = 64;

        byte[] file = new byte[HeaderSize + images.Length];

        file[0] = (byte)'V';
        file[1] = (byte)'T';
        file[2] = (byte)'F';
        file[3] = 0;

        WriteInt(file, 12, HeaderSize);
        WriteShort(file, 16, width);
        WriteShort(file, 18, height);
        WriteInt(file, 20, 0);
        WriteShort(file, 24, 1);
        WriteInt(file, 52, (int)format);
        file[56] = (byte)mips;
        WriteInt(file, 57, -1);

        images.CopyTo(file, HeaderSize);
        return file;
    }

    private static void WriteInt(byte[] into, int at, int value) =>
        BitConverter.GetBytes(value).CopyTo(into, at);

    private static void WriteShort(byte[] into, int at, int value) =>
        BitConverter.GetBytes((ushort)value).CopyTo(into, at);
}
