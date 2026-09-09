using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Where a 7.3 VTF keeps its images — the resource table, not the header size (B373).
/// </summary>
/// <remarks>
/// **Synthetic, and the fixture is built so the WRONG answer is a different colour.** The file below
/// puts a green block exactly where the legacy arithmetic lands and a red block where the resource
/// table points, so a reader using the computed offset returns green and one reading the table
/// returns red. Neither is an error, neither throws, and both produce a picture — which is precisely
/// why this went unnoticed on shipped textures for weeks.
///
/// **What it looked like in the game:** `effects/smoke/smokelit.vtf` carries a 1,432-byte sheet
/// resource between its thumbnail and its pixels, so the computed offset landed on a table of UV
/// floats. Mips are stored smallest first, so the largest mip was barely shifted — the smoke puffs
/// kept their silhouettes and filled with rainbow noise. Measured after the fix, its mean channel
/// spread over visible pixels fell from 112.5 to 10.8 and its largest from 255 to 25.
/// </remarks>
[TestFixture]
public sealed class VtfResourceOffsetConformanceTests
{
    /// <summary>Where <c>numResources</c> sits.</summary>
    private const int CountAt = 0x44;

    /// <summary>Where the resource entries begin.</summary>
    private const int EntriesAt = 0x50;

    /// <summary><c>VTF_LEGACY_RSRC_LOW_RES_IMAGE</c>.</summary>
    private const uint LowResource = 0x01;

    /// <summary><c>VTF_LEGACY_RSRC_IMAGE</c>.</summary>
    private const uint ImageResource = 0x30;

    /// <summary><c>IMAGE_FORMAT_DXT1</c>.</summary>
    private const int Dxt1 = 13;

    [Test]
    public void Decode_A73FileWithAResourceBeforeItsImage_ReadsWhereTheTableSays()
    {
        // The whole finding in one assertion. Anything between the thumbnail and the pixels moves
        // the pixels, and only the table knows by how much.
        VtfTexture image = VtfTexture.Decode(File(withPadding: true));

        image.Width.ShouldBe(4);
        image.Height.ShouldBe(4);

        (int red, int green, int blue) = First(image);

        red.ShouldBeGreaterThan(200);
        green.ShouldBeLessThan(60);
        blue.ShouldBeLessThan(60);
    }

    [Test]
    public void Decode_A73FileWithNothingBetween_AgreesWithTheComputedOffset()
    {
        // **The control.** With no padding resource the table's answer and the arithmetic coincide,
        // so this passes both before and after the fix — which is what makes the test above evidence
        // about the OFFSET rather than about the fixture being readable at all.
        (int red, int green, int blue) = First(VtfTexture.Decode(File(withPadding: false)));

        red.ShouldBeGreaterThan(200);
        green.ShouldBeLessThan(60);
        blue.ShouldBeLessThan(60);
    }

    [Test]
    public void Decode_AResourceWhoseDataIsInline_IsNotFollowedAsAnOffset()
    {
        // `RSRCF_HAS_NO_DATA_CHUNK` puts four bytes of DATA in the offset field. `smokelit` stores
        // its CRC that way, and a CRC read as a file offset is a number in the billions — so an
        // entry that is not skipped either throws or silently renames the image's location.
        byte[] file = File(withPadding: true);

        // Turn the padding entry into an inline one carrying a huge value, and claim it is the
        // image. A reader that honours the flag ignores it and still finds the real image.
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(EntriesAt + 8), ImageResource | (0x02u << 24));

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(EntriesAt + 12), 0xDEADBEEF);

        (int red, int green, int blue) = First(VtfTexture.Decode(file));

        red.ShouldBeGreaterThan(200);
        green.ShouldBeLessThan(60);
        blue.ShouldBeLessThan(60);
    }

    /// <summary>The first pixel's colour.</summary>
    private static (int Red, int Green, int Blue) First(VtfTexture image) =>
        (image.Pixels[0], image.Pixels[1], image.Pixels[2]);

    /// <summary>
    /// A 4×4 DXT1 texture whose pixels are RED, with a GREEN block sitting where the computed
    /// offset would land.
    /// </summary>
    /// <param name="withPadding">
    /// Whether to place a third resource between the thumbnail and the image, which is what makes
    /// the two offsets disagree.
    /// </param>
    private static byte[] File(bool withPadding)
    {
        int entries = withPadding ? 3 : 2;
        int headerSize = EntriesAt + (entries * 8);

        int thumbnailAt = headerSize;
        int paddingAt = thumbnailAt + 8;
        int imageAt = withPadding ? paddingAt + 8 : paddingAt;

        byte[] file = new byte[imageAt + 8];

        file[0] = (byte)'V';
        file[1] = (byte)'T';
        file[2] = (byte)'F';

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)headerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(52), Dxt1);

        file[56] = 1;

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(57), Dxt1);

        file[61] = 4;
        file[62] = 4;

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(CountAt), entries);

        Entry(file, 0, LowResource, (uint)thumbnailAt);

        if (withPadding)
        {
            // Something that is neither image — a sheet, a CRC chunk, anything. Its ID does not
            // matter; its EXISTENCE is the whole point.
            Entry(file, 1, 0x10, (uint)paddingAt);
            Entry(file, 2, ImageResource, (uint)imageAt);
        }
        else
        {
            Entry(file, 1, ImageResource, (uint)imageAt);
        }

        // The thumbnail, and then the trap: a green block exactly where header + thumbnail lands.
        Block(file, thumbnailAt, red: false);
        Block(file, paddingAt, red: false);
        Block(file, imageAt, red: true);

        return file;
    }

    /// <summary>Writes one resource entry.</summary>
    private static void Entry(byte[] file, int index, uint type, uint payload)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(EntriesAt + (index * 8)), type);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(EntriesAt + (index * 8) + 4), payload);
    }

    /// <summary>One solid-colour DXT1 block: both endpoints the same, every index zero.</summary>
    private static void Block(byte[] file, int at, bool red)
    {
        // 5:6:5 — red is 0xF800, green is 0x07E0.
        ushort colour = red ? (ushort)0xF800 : (ushort)0x07E0;

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), colour);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 2), colour);
    }
}
