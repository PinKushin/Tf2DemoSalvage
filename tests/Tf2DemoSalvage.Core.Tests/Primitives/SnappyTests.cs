using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests for Snappy decompression, the scheme TF2 actually uses for its compressed tables.
/// </summary>
/// <remarks>
/// A varint length, then a sequence of elements each introduced by a tag byte whose low two
/// bits pick the kind: a literal run, or a copy with a 1, 2 or 4 byte offset. Every length in
/// the format is stored one less than its true value, since a zero-length element would be
/// meaningless — an off-by-one there produces a shorter output that still parses.
///
/// Copies may overlap what they are still writing, exactly as in LZSS, so the same rule
/// applies: byte at a time, from the live buffer. Compressing a repeated run is precisely what
/// makes that path common rather than exotic.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified.")]
public sealed class SnappyTests
{
    /// <summary>Encodes bytes as one or more literal runs — legal Snappy, and no compression.</summary>
    /// <remarks>
    /// Trivially correct by inspection, so a round trip through it tests the decoder rather
    /// than agreement between two guesses. Copies are covered by hand-built cases instead.
    /// </remarks>
    private static byte[] EncodeLiterals(ReadOnlySpan<byte> data)
    {
        List<byte> output = [];
        WriteVarInt(output, (uint)data.Length);

        if (data.Length <= 60)
        {
            output.Add((byte)((data.Length - 1) << 2));     // tag 00, length in the top six bits
        }
        else if (data.Length <= 256)
        {
            // Over 60, the length moves into trailing bytes and the tag says how many.
            output.Add((byte)(60 << 2));
            output.Add((byte)(data.Length - 1));
        }
        else
        {
            output.Add((byte)(61 << 2));
            output.Add((byte)((data.Length - 1) & 0xFF));
            output.Add((byte)((data.Length - 1) >> 8));
        }

        foreach (byte b in data)
        {
            output.Add(b);
        }

        return [.. output];
    }

    private static void WriteVarInt(List<byte> output, uint value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        output.Add((byte)value);
    }

    [Test]
    public void LiteralRun_RoundTripsAnyInput()
    {
        // Spans both literal encodings: up to 60 bytes inline, beyond that in a trailing byte.
        Gen.Byte.Array[1, 200].Sample(data =>
            Snappy.Decompress(EncodeLiterals(data)).AsSpan().SequenceEqual(data));
    }

    [Test]
    public void ShortLiteral_StoresItsLengthMinusOne()
    {
        // A single byte is length 0 in the tag. Reading it as a literal length rather than
        // length-minus-one drops the byte entirely and still parses.
        byte[] compressed = [1, 0 << 2, (byte)'x'];

        Encoding.ASCII.GetString(Snappy.Decompress(compressed)).ShouldBe("x");
    }

    [Test]
    public void CopyWithOneByteOffset_TakesItsLengthFromTheTag()
    {
        // "abc" then a copy of 4 bytes from 3 back, giving "abcabca" - the copy runs one byte
        // past its source and picks up the byte it just wrote.
        //
        // This tag stores length minus four, so it cannot express a copy shorter than four at
        // all; three would encode as -1 and land in the offset bits. Worth stating because the
        // obvious fixture to write is a three-byte copy.
        byte[] compressed =
        [
            7,
            2 << 2, (byte)'a', (byte)'b', (byte)'c',
            .. Copy1(offset: 3, length: 4),
        ];

        Encoding.ASCII.GetString(Snappy.Decompress(compressed)).ShouldBe("abcabca");
    }

    [Test]
    public void OverlappingCopy_ReadsBytesAsItWritesThem()
    {
        // The case that separates a correct decoder from one copying via a snapshot: one byte
        // repeated ten times, encoded as a literal plus a copy of length 10 from one back.
        byte[] compressed = [11, 0 << 2, (byte)'a', .. Copy1(offset: 1, length: 10)];

        Encoding.ASCII.GetString(Snappy.Decompress(compressed)).ShouldBe("aaaaaaaaaaa");
    }

    [Test]
    public void CopyWithTwoByteOffset_ReadsTheOffsetLittleEndian()
    {
        // A two-byte offset reaches further back than a one-byte copy can. Reading it
        // big-endian would point somewhere else entirely and usually still be in range.
        byte[] literal = new byte[300];
        for (int i = 0; i < literal.Length; i++)
        {
            literal[i] = (byte)(i % 251);
        }

        List<byte> compressed = [];
        WriteVarInt(compressed, 304);
        // 299 does not fit in one trailing byte, so this is the two-byte form: tag 61 means
        // "length occupies the next two bytes", little-endian.
        compressed.Add((byte)(61 << 2));
        compressed.Add((byte)(299 & 0xFF));
        compressed.Add((byte)(299 >> 8));
        compressed.AddRange(literal);
        compressed.Add((byte)(((4 - 1) << 2) | 2));     // tag 10: copy, length 4
        compressed.Add(0x2C);                           // offset 300, little-endian
        compressed.Add(0x01);

        byte[] result = Snappy.Decompress([.. compressed]);

        result.Length.ShouldBe(304);
        result.AsSpan(300, 4).ToArray().ShouldBe(literal.AsSpan(0, 4).ToArray());
    }

    [Test]
    public void CopyWithFourByteOffset_ReadsAllFourBytesLittleEndian()
    {
        // Tag 11. Never exercised until the mutation gate pointed at it - the earlier tests
        // used one and two byte offsets only, so the whole four-byte branch was untested.
        byte[] literal = new byte[300];
        for (int i = 0; i < literal.Length; i++)
        {
            literal[i] = (byte)(i % 251);
        }

        List<byte> compressed = [];
        WriteVarInt(compressed, 305);
        compressed.Add((byte)(61 << 2));
        compressed.Add((byte)(299 & 0xFF));
        compressed.Add((byte)(299 >> 8));
        compressed.AddRange(literal);
        compressed.Add((byte)(((5 - 1) << 2) | 3));     // tag 11: copy, length 5
        compressed.Add(0x2C);                           // offset 300 across four bytes
        compressed.Add(0x01);
        compressed.Add(0x00);
        compressed.Add(0x00);

        byte[] result = Snappy.Decompress([.. compressed]);

        result.Length.ShouldBe(305);
        result.AsSpan(300, 5).ToArray().ShouldBe(literal.AsSpan(0, 5).ToArray());
    }

    [Test]
    public void CopyRunningPastTheDeclaredLength_IsRejected()
    {
        // A copy, rather than a literal, overrunning the declared output. The literal path had
        // a test for this and the copy path did not.
        byte[] compressed = [5, 0 << 2, (byte)'a', .. Copy1(offset: 1, length: 8)];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void TruncatedFourByteOffset_IsRejected()
    {
        byte[] compressed = [8, 0 << 2, (byte)'a', (byte)(((5 - 1) << 2) | 3), 0x01];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void StreamEndingInsideItsLengthPreamble_SaysSo()
    {
        // A varint whose continuation bit promises another byte that never arrives.
        InvalidDataException error =
            Should.Throw<InvalidDataException>(() => Snappy.Decompress([0x80]));

        error.Message.ShouldContain("preamble");
    }

    [Test]
    public void DeclaredLength_IsTheContract()
    {
        // The varint preamble says how long the output is. A stream that produces less has
        // been truncated, and returning the short result would read as a complete table.
        byte[] compressed = [50, 0 << 2, (byte)'x'];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void OutputLongerThanDeclared_IsRejected()
    {
        byte[] compressed = [2, 4 << 2, (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e'];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void CopyReachingBeforeTheStart_IsRejected()
    {
        // Without the check this reads whatever precedes the buffer - a silent wrong answer.
        byte[] compressed = [5, 0 << 2, (byte)'a', .. Copy1(offset: 9, length: 4)];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void ZeroOffsetCopy_IsRejectedRatherThanLooping()
    {
        // An offset of zero names the byte about to be written. Left unchecked it either
        // repeats uninitialised memory or spins, depending on how the copy is written.
        byte[] compressed = [5, 0 << 2, (byte)'a', .. Copy1(offset: 0, length: 4)];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void EmptyInput_IsRejected()
    {
        Should.Throw<InvalidDataException>(() => Snappy.Decompress([]));
    }

    [Test]
    public void TruncatedLiteral_IsRejected()
    {
        byte[] compressed = [10, 9 << 2, (byte)'a', (byte)'b'];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void ALiteralLengthWithItsTopBitSet_IsRejectedRatherThanGoingNegative()
    {
        // Found by the fuzzer on fuzz-box, 2026-08-11, in under sixty seconds of the snappy
        // target's first scheduled-mode run — eleven bytes, reported as
        // "Decompressing 11 bytes threw ArgumentOutOfRangeException rather than refusing them".
        //
        // A four-byte literal length is accumulated into a signed int by shifting each byte into
        // place, so a top byte of 0x80 or above lands in the sign bit and the length comes out
        // NEGATIVE.
        // That is what makes it interesting rather than merely wrong: both guards downstream are
        // written for a length that is too LARGE, and a negative satisfies each of them. Need()
        // sees read + (negative) still inside the buffer, and `written + length > output.Length`
        // is false. The value survives every check and dies in Slice's own argument validation,
        // which is the wrong exception from the wrong layer.
        //
        // 0xFC is the literal tag with the length field at 63, meaning four trailing length
        // bytes. 0x00 0x00 0x00 0x80 is the smallest of those that sets the sign bit.
        byte[] compressed = [10, 0xFC, 0x00, 0x00, 0x00, 0x80, 1, 2, 3, 4, 5];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    [Test]
    public void ALiteralLongerThanTheStream_IsRejected()
    {
        // The control: a length that is genuinely positive and merely too long. This already
        // passes, and must keep passing — it is the bystander proving the fix narrows the
        // negative case without disturbing the ordinary overrun path.
        //
        // It took a correction to become a control. The first version used 0xFFFFFFFF as the
        // "large positive" length, which is not one: it accumulates to 0x7FFFFFFF and then the
        // trailing increment wraps it to int.MinValue, so it failed identically to the test above
        // and measured the same defect twice. 0x00000100 stays positive through the increment.
        byte[] compressed = [10, 0xFC, 0x00, 0x01, 0x00, 0x00, 1, 2, 3];

        Should.Throw<InvalidDataException>(() => Snappy.Decompress(compressed));
    }

    /// <summary>Packs a copy with a one-byte offset: length minus four, then eleven offset bits.</summary>
    private static byte[] Copy1(int offset, int length) =>
    [
        (byte)(((offset >> 8) << 5) | ((length - 4) << 2) | 1),
        (byte)(offset & 0xFF),
    ];
}
