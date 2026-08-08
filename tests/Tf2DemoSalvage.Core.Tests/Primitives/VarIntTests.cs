using System;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Hand-built byte fixtures for protobuf-style varint decoding, written before the
/// implementation (D6).
/// </summary>
/// <remarks>
/// Encoding under test: seven payload bits per byte, least-significant group first, with the
/// high bit set on every byte except the last. So 300 (0b1_0010_1100) splits into groups
/// 0b010_1100 and 0b10, and encodes as 0xAC 0x02.
///
/// Varints are byte-oriented but not byte-aligned - Source reads them through the same bit
/// reader as everything else, so a varint can begin mid-byte. That case gets its own test.
/// </remarks>
public sealed class VarIntTests
{
    [Theory]
    [InlineData(0u, new byte[] { 0x00 })]
    [InlineData(1u, new byte[] { 0x01 })]
    [InlineData(127u, new byte[] { 0x7F })]
    [InlineData(128u, new byte[] { 0x80, 0x01 })]
    [InlineData(300u, new byte[] { 0xAC, 0x02 })]
    [InlineData(16383u, new byte[] { 0xFF, 0x7F })]
    [InlineData(16384u, new byte[] { 0x80, 0x80, 0x01 })]
    [InlineData(uint.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void ReadUInt32_CanonicalEncodings_DecodeToTheirValue(uint expected, byte[] encoded)
    {
        BitReader reader = new(encoded);

        VarInt.ReadUInt32(ref reader).ShouldBe(expected);
    }

    [Fact]
    public void ReadUInt32_ConsumesOnlyTheBytesTheEncodingNeeds()
    {
        // 0x01 terminates immediately; the 0xFF after it must be left for the next reader.
        BitReader reader = new([0x01, 0xFF]);

        VarInt.ReadUInt32(ref reader).ShouldBe(1u);
        reader.BitsRead.ShouldBe(8);
        reader.BitsRemaining.ShouldBe(8);
    }

    [Fact]
    public void ReadUInt32_TwoInSequence_DecodeIndependently()
    {
        BitReader reader = new([0xAC, 0x02, 0x7F]);

        VarInt.ReadUInt32(ref reader).ShouldBe(300u);
        VarInt.ReadUInt32(ref reader).ShouldBe(127u);
        reader.BitsRemaining.ShouldBe(0);
    }

    [Fact]
    public void ReadUInt32_StartingMidByte_DecodesFromTheCurrentBitPosition()
    {
        // Shift the encoding of 300 (0xAC 0x02) left by four bits, so it starts on a nibble
        // boundary: the stream becomes 0x_C? 0x2A 0x00 with the low nibble of byte 0 unused.
        BitReader reader = new([0xC0, 0x2A, 0x00]);
        reader.ReadUInt32(4).ShouldBe(0x0u);

        VarInt.ReadUInt32(ref reader).ShouldBe(300u);
    }

    [Fact]
    public void ReadUInt32_TruncatedMidEncoding_ThrowsEndOfStream()
    {
        // Continuation bit set on the final available byte: the value is incomplete.
        Should.Throw<EndOfStreamException>(() =>
        {
            BitReader reader = new([0x80]);
            VarInt.ReadUInt32(ref reader);
        });
    }

    [Fact]
    public void ReadUInt32_SixthContinuationByte_ThrowsInvalidData()
    {
        // A 32-bit varint is at most five bytes. A fifth byte that still asks for a sixth is
        // malformed, and must be rejected rather than read on forever.
        InvalidDataException exception = Should.Throw<InvalidDataException>(() =>
        {
            BitReader reader = new([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]);
            VarInt.ReadUInt32(ref reader);
        });

        exception.Message.ShouldContain("5");
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(-1, new byte[] { 0x01 })]
    [InlineData(1, new byte[] { 0x02 })]
    [InlineData(-2, new byte[] { 0x03 })]
    [InlineData(2, new byte[] { 0x04 })]
    [InlineData(int.MaxValue, new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x0F })]
    [InlineData(int.MinValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void ReadInt32_ZigZagEncodings_DecodeToTheirValue(int expected, byte[] encoded)
    {
        BitReader reader = new(encoded);

        VarInt.ReadInt32(ref reader).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0ul, new byte[] { 0x00 })]
    [InlineData(300ul, new byte[] { 0xAC, 0x02 })]
    [InlineData(8589934592ul, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x20 })]
    [InlineData(
        ulong.MaxValue,
        new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    public void ReadUInt64_CanonicalEncodings_DecodeToTheirValue(ulong expected, byte[] encoded)
    {
        BitReader reader = new(encoded);

        VarInt.ReadUInt64(ref reader).ShouldBe(expected);
    }

    [Fact]
    public void ReadUInt64_EleventhContinuationByte_ThrowsInvalidData()
    {
        Should.Throw<InvalidDataException>(() =>
        {
            BitReader reader = new(
                [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]);
            VarInt.ReadUInt64(ref reader);
        });
    }

    [Theory]
    [InlineData(0L, new byte[] { 0x00 })]
    [InlineData(-1L, new byte[] { 0x01 })]
    [InlineData(1L, new byte[] { 0x02 })]
    [InlineData(
        long.MinValue,
        new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    public void ReadInt64_ZigZagEncodings_DecodeToTheirValue(long expected, byte[] encoded)
    {
        BitReader reader = new(encoded);

        VarInt.ReadInt64(ref reader).ShouldBe(expected);
    }

    [Fact]
    public void ReadUInt32_FifthByteWithBitsAboveThirtyTwo_TruncatesLikeValvesWriter()
    {
        // The fifth group carries bits 28-34, but only 28-31 fit. Valve's own reader lets the
        // excess fall off the top rather than rejecting the input, and a demo produced by its
        // writer never contains such an encoding anyway - so this documents the behaviour
        // rather than endorsing the encoding.
        BitReader reader = new([0xFF, 0xFF, 0xFF, 0xFF, 0x7F]);

        VarInt.ReadUInt32(ref reader).ShouldBe(uint.MaxValue);
    }
}
