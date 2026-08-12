using System;
using System.Buffers.Binary;
using System.IO;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests that a declared length cannot make the parser allocate before it validates.
/// </summary>
/// <remarks>
/// **This is a denial-of-service surface, not a tidiness one.** Every decoder here reads a length
/// from the file and sizes a buffer with it, and the file is by definition untrusted — reading
/// demos that other parsers reject is this project's whole purpose. A few bytes that declare two
/// billion is a quarter-gigabyte allocation per message, and nothing about it looks like an
/// attack while it is happening: the process simply becomes slow and then dies.
///
/// The rule these tests pin down is **validate against what is actually present, then allocate**.
/// A cross-check between two declared values is not enough, because whoever wrote the file
/// controls both of them.
/// </remarks>
public sealed class AllocationBoundsTests
{
    [Test]
    public void Lzss_CannotDeclareAnOutputNoInputThatSizeCouldProduce()
    {
        // The LZSS header's length and the message's expected length agreed - which is exactly
        // what an author of a malformed file would do, since they write both. Before this bound
        // the agreement was the only check, so 64 MB was allocated from a 16-byte payload.
        const int declared = 64 * 1024 * 1024;
        byte[] compressed = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(compressed, declared);

        // Asserting the MESSAGE, not merely that it throws. Without the bound this still threw -
        // it allocated the 64 MB, ran out of input and reported a truncated stream - so a bare
        // Should.Throw passes on the broken code and measures nothing. The distinguishing
        // observation is WHICH failure happens, and therefore whether the allocation happened
        // at all.
        InvalidDataException error =
            Should.Throw<InvalidDataException>(() => Lzss.Decompress(compressed, declared));

        error.Message.ShouldContain("no LZSS payload");
    }

    [Test]
    public void Lzss_AcceptsARealBackReference()
    {
        // The control, and it matters more than the rejection: the bound has to leave genuine
        // compression alone. Nine bytes of output from a literal plus a back-reference is real
        // expansion and must keep decoding.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(9),
            0b0000_0110,
            (byte)'a',
            .. Match(offset: 1, count: 8),
            0x00, 0x00,
        ];

        System.Text.Encoding.ASCII.GetString(Lzss.Decompress(compressed, 9)).ShouldBe("aaaaaaaaa");
    }

    private static byte[] Match(int offset, int count)
    {
        int stored = offset - 1;
        return [(byte)(stored >> 4), (byte)(((stored & 0x0F) << 4) | (count - 1))];
    }

    [Test]
    public void CopyBits_CannotAllocateForMoreBitsThanTheReaderHolds()
    {
        // svc_GameEventList and friends declare a body length in bits and the body is copied out
        // at that size. Unchecked, a declared two billion bits is a 250 MB allocation that
        // happens BEFORE the first read can fail - so the failure that would have caught it
        // arrives after the damage.
        ShouldRejectCopy(2_000_000_000);
    }

    /// <summary>
    /// Asserts <see cref="NetBitReading.CopyBits"/> refuses a length.
    /// </summary>
    /// <remarks>
    /// Written out rather than using a lambda because <see cref="BitReader"/> is a ref struct and
    /// cannot be captured by one.
    /// </remarks>
    private static void ShouldRejectCopy(int bitCount)
    {
        BitReader reader = new(new byte[8]);

        try
        {
            NetBitReading.CopyBits(ref reader, bitCount);
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new ShouldAssertException(
            $"CopyBits accepted {bitCount} bits against a reader holding 64.");
    }

    [Test]
    public void CopyBits_StillCopiesWhatIsActuallyThere()
    {
        // The control: a length the reader can satisfy must behave exactly as before.
        BitReader reader = new([0xAB, 0xCD, 0xEF]);

        NetBitReading.CopyBits(ref reader, 16).ShouldBe(new byte[] { 0xAB, 0xCD });
    }

    [Test]
    public void CopyBits_RejectsANegativeLength()
    {
        // A bit count read from the wire into an int arrives negative above int.MaxValue.
        // `(bitCount + 7) / 8` on a negative is a negative size, which throws from the runtime
        // rather than from this parser's own contract.
        ShouldRejectCopy(-1);
    }
}
