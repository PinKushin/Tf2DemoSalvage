using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Tests the BSP header and its lump directory.
/// </summary>
/// <remarks>
/// **A BSP is untrusted input** — maps arrive from fastdl, supplied by whoever runs the server and
/// reviewed by nobody, and the Source engine has had map-driven RCE research published against it
/// (<c>DECISIONS.md</c> D32). The header is 64 pairs of file offset and length pointing anywhere
/// in the file, which is the same shape as every allocate-before-validate defect this project has
/// already fixed on the demo side.
///
/// So most of these tests are about rejection rather than about reading, and they are written
/// before the reader exists rather than after.
/// </remarks>
public sealed class BspHeaderTests
{
    private const int LumpCount = 64;
    private const int HeaderSize = 8 + (LumpCount * 16) + 4;

    /// <summary>Builds a header whose lumps can be adjusted per test.</summary>
    private static byte[] Header(
        string ident = "VBSP",
        int version = 21,
        int lumpIndex = 0,
        int lumpOffset = HeaderSize,
        int lumpLength = 0,
        int totalSize = HeaderSize)
    {
        byte[] file = new byte[Math.Max(totalSize, HeaderSize)];

        Encoding.ASCII.GetBytes(ident).CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), version);

        int at = 8 + (lumpIndex * 16);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), lumpOffset);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 4), lumpLength);

        return file;
    }

    [Test]
    public void BspHeader_TheIdentAndVersion_AreRead()
    {
        BspHeader header = BspHeader.Parse(Header(version: 20));

        header.Version.ShouldBe(20);
    }

    [Test]
    public void BspHeader_AFileThatIsNotABsp_IsRejected()
    {
        // A demo, a text file, or an HTML error page saved by a failed download - all plausible
        // things to find where a map was expected.
        Should.Throw<InvalidDataException>(() => BspHeader.Parse(Header(ident: "HTML")));
    }

    [Test]
    public void BspHeader_AFileTooShortForAHeader_IsRejected()
    {
        Should.Throw<InvalidDataException>(() => BspHeader.Parse(new byte[100]));
    }

    [Test]
    public void BspHeader_ALumpPastTheEndOfTheFile_IsRejected()
    {
        // The central D32 rule. A length is a number in the file, and believing it is how a
        // parser is made to read - or allocate - whatever the author chose.
        byte[] file = Header(lumpIndex: 5, lumpOffset: HeaderSize, lumpLength: 1_000_000);

        Should.Throw<InvalidDataException>(() => BspHeader.Parse(file));
    }

    [Test]
    public void BspHeader_ALumpOffsetInsideTheHeader_IsRejected()
    {
        // Pointing a lump at the header itself makes the directory describe its own bytes, which
        // no honest compiler emits and which invites a reader into a loop.
        byte[] file = Header(lumpIndex: 2, lumpOffset: 16, lumpLength: 16, totalSize: HeaderSize + 64);

        Should.Throw<InvalidDataException>(() => BspHeader.Parse(file));
    }

    [Test]
    public void BspHeader_ANegativeOffsetOrLength_IsRejected()
    {
        // A 32-bit field read as a signed int arrives negative above int.MaxValue, and a negative
        // length sails past a "too large" check - the exact shape of the Snappy defect the fuzzer
        // found on the demo side.
        Should.Throw<InvalidDataException>(
            () => BspHeader.Parse(Header(lumpIndex: 1, lumpOffset: -8, lumpLength: 16)));

        Should.Throw<InvalidDataException>(
            () => BspHeader.Parse(Header(lumpIndex: 1, lumpOffset: HeaderSize, lumpLength: -16)));
    }

    [Test]
    public void BspHeader_OffsetPlusLength_IsCheckedWithoutOverflowing()
    {
        // Two large positive numbers whose sum wraps: computed in int, the sum is negative and
        // passes a naive "fits in the file" test. This is the same overflow that let a Snappy
        // literal length through, and it has to be computed in long.
        byte[] file = Header(
            lumpIndex: 3, lumpOffset: int.MaxValue - 8, lumpLength: 64, totalSize: HeaderSize + 128);

        Should.Throw<InvalidDataException>(() => BspHeader.Parse(file));
    }

    [Test]
    public void BspHeader_AnEmptyLump_IsLegal()
    {
        // Most maps use nothing like all 64 lumps; an unused one is zero offset and zero length,
        // and rejecting that would reject every real map.
        BspHeader header = BspHeader.Parse(Header(lumpIndex: 7, lumpOffset: 0, lumpLength: 0));

        header.Lump(7).Length.ShouldBe(0);
    }

    [Test]
    public void BspHeader_ALumpThatFits_IsReadable()
    {
        byte[] file = Header(
            lumpIndex: 4, lumpOffset: HeaderSize, lumpLength: 32, totalSize: HeaderSize + 32);

        BspHeader header = BspHeader.Parse(file);

        header.Lump(4).Offset.ShouldBe(HeaderSize);
        header.Lump(4).Length.ShouldBe(32);
    }

    [Test]
    public void BspHeader_ALumpOutsideTheDirectory_IsRejected()
    {
        BspHeader header = BspHeader.Parse(Header());

        Should.Throw<ArgumentOutOfRangeException>(() => header.Lump(LumpCount));
        Should.Throw<ArgumentOutOfRangeException>(() => header.Lump(-1));
    }
}
