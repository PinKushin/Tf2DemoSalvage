using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Reading a lump that may or may not be compressed.
/// </summary>
/// <remarks>
/// **TF2 ships its maps with every lump LZMA-compressed, and reading them raw produces plausible
/// garbage rather than an error.** Measured on the shipped <c>cp_process_final.bsp</c>: the faces
/// lump is 147,154 bytes on disk and 773,976 decompressed, and reading the compressed bytes as
/// <c>dface_t</c> gave face 0 a plane index of 23,116 out of 1,824 planes.
///
/// The arithmetic settles it before any decoding happens: 147,154 does not divide by 56, and the
/// edge and surfedge lumps do not divide by 4. A lump of fixed-size structs whose length is not a
/// multiple of that size is not a lump of those structs. Decompressed, every one divides exactly.
///
/// The compressed fixture comes from liblzma; see <c>LzmaTests</c> for why the fixtures in this
/// area are generated rather than hand-written.
/// </remarks>
public sealed class BspLumpDataTests
{
    /// <summary>What <see cref="CompressedLump"/> decompresses to.</summary>
    private const int CompressedPayloadLength = 3000;

    [Test]
    public void Read_UncompressedLump_ReturnsTheBytesUntouched()
    {
        // The control. Not every lump in every map is compressed, and a map built without
        // compression must still read.
        byte[] payload = Raw(400);
        byte[] file = FileWith(payload);

        ReadOnlyMemory<byte> read = BspLumpData.Read(file, new BspLump(BspHeader.SizeBytes, payload.Length, 0));

        read.ToArray().ShouldBe(payload);
    }

    [Test]
    public void Read_CompressedLump_ReturnsTheDecompressedBytes()
    {
        byte[] lump = Convert.FromHexString(CompressedLump);
        byte[] file = FileWith(lump);

        ReadOnlyMemory<byte> read = BspLumpData.Read(
            file, new BspLump(BspHeader.SizeBytes, lump.Length, 0));

        read.ToArray().ShouldBe(Blocks(CompressedPayloadLength));
    }

    [Test]
    public void Read_CompressedLump_IsShorterOnDiskThanInMemory()
    {
        // Guards the experiment rather than the code: if the fixture were not actually compressed,
        // both branches of Read would return the same bytes and the test above would pass whether
        // or not decompression ever happened.
        Convert.FromHexString(CompressedLump).Length.ShouldBeLessThan(CompressedPayloadLength);
    }

    [Test]
    public void Read_LumpAtANonZeroOffset_ReadsFromTheRightPlace()
    {
        // A control against reading from the start of the file regardless of what was asked for.
        // Every lump but the first sits at a non-zero offset in a real map.
        byte[] first = Raw(64);
        byte[] second = Raw(128, seed: 99);
        byte[] file = [.. new byte[BspHeader.SizeBytes], .. first, .. second];

        ReadOnlyMemory<byte> read = BspLumpData.Read(
            file, new BspLump(BspHeader.SizeBytes + first.Length, second.Length, 0));

        read.ToArray().ShouldBe(second);
    }

    [Test]
    public void Read_DeclaredSizeBeyondTheCap_IsRefused()
    {
        // A decompression bomb. The declared size is read from the file and used to size the
        // output buffer, so a hostile map can ask for gigabytes from a few hundred bytes — the
        // same allocate-before-validate shape already fixed in Lzss and CopyBits.
        byte[] bomb = Convert.FromHexString(CompressedLump);
        BinaryPrimitives.WriteUInt32LittleEndian(bomb.AsSpan(4), 3_000_000_000);

        byte[] file = FileWith(bomb);

        Should.Throw<InvalidDataException>(
            () => BspLumpData.Read(file, new BspLump(BspHeader.SizeBytes, bomb.Length, 0)));
    }

    [Test]
    public void Read_PackedSizeLargerThanTheLump_IsRefused()
    {
        // The packed size is a second length in the same header, and nothing makes the two agree.
        // Trusting it would read past the end of this lump and into the next one.
        byte[] lying = Convert.FromHexString(CompressedLump);
        BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(8), (uint)lying.Length + 5000);

        byte[] file = FileWith(lying);

        Should.Throw<InvalidDataException>(
            () => BspLumpData.Read(file, new BspLump(BspHeader.SizeBytes, lying.Length, 0)));
    }

    [Test]
    public void Read_LumpShorterThanTheCompressionHeader_IsTreatedAsRaw()
    {
        // Not every four bytes that could be a magic number is one. A lump too short to hold the
        // 17-byte header cannot be compressed, whatever it starts with.
        byte[] tiny = Encoding.ASCII.GetBytes("LZMA");
        byte[] file = FileWith(tiny);

        BspLumpData.Read(file, new BspLump(BspHeader.SizeBytes, tiny.Length, 0))
            .ToArray().ShouldBe(tiny);
    }

    [Test]
    public void Read_EmptyLump_ReturnsNothing()
    {
        BspLumpData.Read(new byte[BspHeader.SizeBytes], new BspLump(0, 0, 0)).Length.ShouldBe(0);
    }

    [Test]
    public void Read_LumpRunningPastTheEndOfTheFile_IsRefused()
    {
        // BspHeader rejects this at parse time, but Read is reachable with a lump built by hand
        // and must not index outside the file it was handed.
        Should.Throw<InvalidDataException>(() => BspLumpData.Read(
            new byte[BspHeader.SizeBytes + 10], new BspLump(BspHeader.SizeBytes, 5000, 0)));
    }

    /// <summary>Uncompressed lump bytes.</summary>
    private static byte[] Raw(int length, int seed = 7)
    {
        byte[] bytes = new byte[length];

        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)((index / 8) + seed);
        }

        return bytes;
    }

    /// <summary>The payload <see cref="CompressedLump"/> holds; see <c>LzmaTests</c>.</summary>
    private static byte[] Blocks(int length)
    {
        byte[] bytes = new byte[length];

        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)(((index % 37) * 11) + 3);
        }

        return bytes;
    }

    /// <summary>Wraps a lump payload in a file with a header-sized gap before it.</summary>
    private static byte[] FileWith(byte[] lump) => [.. new byte[BspHeader.SizeBytes], .. lump];

    /// <summary>3000 bytes compressed to 85 by liblzma, wrapped in Valve's lump header.</summary>
    private const string CompressedLump =
        "4c5a4d41b80b0000440000005d00000100000183fa0aaf5ca8db11fa4c277574" +
        "4528cd9afa665a2efafe6bf81af328b4a2e746be6c441657c4232f0ea8085234" +
        "b866b6ed5e5103f1cc8d822778241e1ffff7ea0000";
}
