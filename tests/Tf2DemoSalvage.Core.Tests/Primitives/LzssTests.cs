using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests for Valve's LZSS variant, which compresses five of the twenty string tables.
/// </summary>
/// <remarks>
/// The format is a control byte followed by eight items, each either a literal byte or a
/// back-reference into what has already been produced. That self-referential copy is the whole
/// risk: a back-reference can legitimately overlap the bytes it is still writing, so a decoder
/// that copies through a snapshot of the output rather than the live buffer produces a
/// plausible-looking result that is wrong only for repeated runs.
///
/// A literal-only encoder is trivially correct, which makes a round-trip property possible
/// without trusting a hand-built fixture. The back-reference cases are hand-built, because an
/// encoder that chose matches the same way the decoder reads them could agree with a wrong
/// decoder.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified.")]
public sealed class LzssTests
{
    /// <summary>Encodes bytes as literals only — every control bit clear.</summary>
    /// <remarks>
    /// No compression at all, which is legal output and trivially checkable by inspection. Used
    /// as the round-trip oracle so the property does not depend on match selection being right.
    /// </remarks>
    private static byte[] EncodeLiterals(ReadOnlySpan<byte> data)
    {
        List<byte> output = [.. BitConverter.GetBytes(data.Length)];
        int control = -1;
        int item = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (item == 0)
            {
                control = output.Count;
                output.Add(0);      // all bits clear: the items that follow are literals
            }

            output.Add(data[i]);
            item = (item + 1) % 8;
        }

        // The terminator is an item like any other, so its control bit has to be set in
        // whichever control byte it lands in. Appending the bytes without setting that bit -
        // which an earlier version of this did - leaves them to be read as four more literals.
        if (item == 0)
        {
            control = output.Count;
            output.Add(0);
        }

        output[control] |= (byte)(1 << item);
        output.Add(0x00);
        output.Add(0x00);

        return [.. output];
    }

    [Test]
    public void LiteralOnlyStream_RoundTripsAnyInput()
    {
        Gen.Byte.Array[1, 200].Sample(data =>
        {
            byte[] decompressed = Lzss.Decompress(EncodeLiterals(data), data.Length);
            return decompressed.AsSpan().SequenceEqual(data);
        });
    }

    [Test]
    public void BackReference_CopiesFromWhatHasAlreadyBeenProduced()
    {
        // "abc" then a back-reference three bytes back for three bytes, giving "abcabc".
        // Offset and count are packed across two bytes: offset is 12 bits, count the low 4.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(6),
            0b0001_1000,            // literal, literal, literal, match, terminator
            (byte)'a', (byte)'b', (byte)'c',
            .. Match(offset: 3, count: 3),
            0x00, 0x00,             // terminator: a match whose count decodes to one
        ];

        Encoding.ASCII.GetString(Lzss.Decompress(compressed, 6)).ShouldBe("abcabc");
    }

    [Test]
    public void OverlappingBackReference_ReadsBytesAsItWritesThem()
    {
        // The case that separates a correct decoder from one copying via a snapshot: a single
        // byte referenced back one, repeated eight times, must produce "aaaaaaaaa". Copying
        // from a pre-copy view of the buffer yields garbage after the first byte instead.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(9),
            0b0000_0110,            // literal, match, terminator
            (byte)'a',
            .. Match(offset: 1, count: 8),
            0x00, 0x00,
        ];

        Encoding.ASCII.GetString(Lzss.Decompress(compressed, 9)).ShouldBe("aaaaaaaaa");
    }

    [Test]
    public void ControlByte_IsReadLowBitFirst()
    {
        // Item order follows the control byte's low bit upward. Reading it from the top would
        // take the terminator first and produce nothing at all, and any other order puts the
        // match before the literals it refers back to.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(5),
            0b0001_1000,            // literal, literal, literal, match, terminator
            (byte)'a', (byte)'b', (byte)'c',
            .. Match(offset: 3, count: 2),
            0x00, 0x00,
        ];

        Encoding.ASCII.GetString(Lzss.Decompress(compressed, 5)).ShouldBe("abcab");
    }

    [Test]
    public void MatchOfCountOne_TerminatesTheStream()
    {
        // The terminator is a match whose encoded count is zero, meaning one. Nothing after it
        // is read, which is how a stream ends without an explicit length in the body.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(2),
            0b0000_0100,            // literal, literal, then terminator
            (byte)'h', (byte)'i',
            0x00, 0x00,             // encoded count 0 => terminate
            (byte)'X', (byte)'X',   // must never be produced
        ];

        Encoding.ASCII.GetString(Lzss.Decompress(compressed, 2)).ShouldBe("hi");
    }

    [Test]
    public void OutputStopsAtTheDeclaredLength()
    {
        // The declared length is the contract. Producing more than it says means the stream
        // disagrees with its own header, which is corruption rather than a longer answer.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(2),
            0b0000_0000,            // eight literals, but only two were declared
            (byte)'a', (byte)'b', (byte)'c', (byte)'d',
            (byte)'e', (byte)'f', (byte)'g', (byte)'h',
        ];

        Should.Throw<InvalidDataException>(() => Lzss.Decompress(compressed, 2));
    }

    [Test]
    public void TruncatedStream_IsRejectedRatherThanReturningWhatItGot()
    {
        // A partial table decoded as though complete would be read as real entries.
        byte[] compressed = [.. BitConverter.GetBytes(100), 0b0000_0000, (byte)'a'];

        Should.Throw<InvalidDataException>(() => Lzss.Decompress(compressed, 100));
    }

    [Test]
    public void BackReferenceBeforeTheStartOfOutput_IsRejected()
    {
        // Reaching behind the first byte produced. Without the check this reads whatever
        // happens to precede the buffer, which is a silent wrong answer at best.
        byte[] compressed =
        [
            .. BitConverter.GetBytes(4),
            0b0000_0001,            // a match as the very first item
            .. Match(offset: 5, count: 3),
            0x00, 0x00,
        ];

        Should.Throw<InvalidDataException>(() => Lzss.Decompress(compressed, 4));
    }

    [Test]
    public void HeaderTooShortToHoldItsOwnLength_IsRejected()
    {
        Should.Throw<InvalidDataException>(() => Lzss.Decompress([1, 2], 4));
    }

    [Test]
    public void DeclaredLengthDisagreeingWithTheHeader_IsRejected()
    {
        // Two independent statements of the same size - the string table message's field and
        // the LZSS header's own. Disagreement means one of them was misread, and continuing
        // would decode a table at the wrong size.
        byte[] compressed = [.. BitConverter.GetBytes(6), 0x02, (byte)'a', 0x00, 0x00];

        Should.Throw<InvalidDataException>(() => Lzss.Decompress(compressed, 99));
    }

    /// <summary>
    /// Packs a back-reference: twelve bits of offset, then four bits of count minus one.
    /// </summary>
    /// <remarks>
    /// Offset is stored one less than the true distance, since a distance of zero would name
    /// the byte not yet written. Count is stored one less for the same reason a count of one
    /// is unusable — it is the terminator.
    /// </remarks>
    private static byte[] Match(int offset, int count)
    {
        int stored = offset - 1;
        return [(byte)(stored >> 4), (byte)(((stored & 0x0F) << 4) | (count - 1))];
    }
}
