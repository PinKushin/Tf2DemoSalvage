using System;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Byte-level fixtures for the Source-engine bit stream reader (D6: primitives get their own
/// unit tests before implementation, not just end-to-end coverage from the corpus).
///
/// Bit order under test is Source's <c>bf_read</c> convention: bits are consumed
/// least-significant-first within each byte, bytes in file order. So the first bit read from
/// 0xAC (0b1010_1100) is bit 0 = 0, and reading 4 bits yields the low nibble 0xC.
/// </summary>
public sealed class BitReaderTests
{
    [Fact]
    public void ReadBit_SingleByte_YieldsBitsLeastSignificantFirst()
    {
        var reader = new BitReader([0xAC]);

        bool[] actual = new bool[8];
        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = reader.ReadBit();
        }

        // 0xAC = 1010_1100 -> LSB-first: 0,0,1,1,0,1,0,1
        actual.ShouldBe([false, false, true, true, false, true, false, true]);
    }

    [Fact]
    public void ReadUInt32_FourBitsTwice_ReadsLowNibbleThenHighNibble()
    {
        var reader = new BitReader([0xAC]);

        reader.ReadUInt32(4).ShouldBe(0xCu);
        reader.ReadUInt32(4).ShouldBe(0xAu);
    }

    [Fact]
    public void ReadUInt32_SpanningByteBoundary_StitchesLowBitsFromNextByte()
    {
        var reader = new BitReader([0xAC, 0x3F]);
        reader.ReadUInt32(4).ShouldBe(0xCu);

        // Next 8 bits = high nibble of 0xAC (0xA) then low nibble of 0x3F (0xF) -> 0xFA.
        reader.ReadUInt32(8).ShouldBe(0xFAu);
    }

    [Fact]
    public void ReadUInt32_ThirtyTwoBits_ReadsLittleEndianWord()
    {
        var reader = new BitReader([0x01, 0x02, 0x03, 0x04]);

        reader.ReadUInt32(32).ShouldBe(0x04030201u);
    }

    [Fact]
    public void ReadUInt32_ZeroBits_ReturnsZeroAndConsumesNothing()
    {
        var reader = new BitReader([0xAC]);

        reader.ReadUInt32(0).ShouldBe(0u);
        reader.BitsRead.ShouldBe(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void ReadUInt32_BitCountOutOfRange_Throws(int bitCount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            var reader = new BitReader([0xAC, 0x3F, 0x00, 0x00, 0x00]);
            reader.ReadUInt32(bitCount);
        });
    }

    [Fact]
    public void ReadBit_PastEndOfBuffer_Throws()
    {
        Should.Throw<EndOfStreamException>(() =>
        {
            var reader = new BitReader([0xAC]);
            reader.ReadUInt32(8);
            reader.ReadBit();
        });
    }

    [Fact]
    public void ReadUInt32_RequestExceedsRemainingBits_ThrowsWithoutPartialRead()
    {
        Should.Throw<EndOfStreamException>(() =>
        {
            var reader = new BitReader([0xAC]);
            reader.ReadUInt32(9);
        });
    }

    [Fact]
    public void BitsRead_AndBitsRemaining_TrackPositionAcrossReads()
    {
        var reader = new BitReader([0xAC, 0x3F]);
        reader.BitsRemaining.ShouldBe(16);

        reader.ReadUInt32(5);

        reader.BitsRead.ShouldBe(5);
        reader.BitsRemaining.ShouldBe(11);
    }

    [Fact]
    public void ReadByte_Unaligned_ReadsEightBitsFromCurrentPosition()
    {
        var reader = new BitReader([0xAC, 0x3F]);
        reader.ReadBit();

        // Bits 1..8 of 0x3FAC = 0b0011_1111_1010_1100 -> 0b1101_0110 = 0xD6.
        reader.ReadByte().ShouldBe((byte)0xD6);
    }

    [Fact]
    public void Constructor_EmptyBuffer_HasNoBitsRemaining()
    {
        var reader = new BitReader([]);

        reader.BitsRemaining.ShouldBe(0);
        reader.BitsRead.ShouldBe(0);
    }
}
