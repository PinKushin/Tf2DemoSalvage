using System;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Forward-only reader over a Source-engine bit stream.
/// </summary>
/// <remarks>
/// Bit order matches Valve's <c>bf_read</c>: bits are consumed least-significant-first within
/// each byte, bytes in file order. Reading 32 bits therefore yields the little-endian word, and
/// reading 4 bits from 0xAC yields the low nibble 0xC.
///
/// This is a <c>ref struct</c> so it can sit directly on a caller's slice of a demo packet with
/// no copy and no allocation - packet payloads are decoded in tight loops and every allocation
/// here would multiply across tens of thousands of ticks.
/// </remarks>
public ref struct BitReader
{
    private const int BitsPerByte = 8;
    private const int MaxBitsPerRead = 32;

    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    /// <summary>Creates a reader positioned at the first bit of <paramref name="data"/>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> is large enough that its bit count would overflow an
    /// <see cref="int"/>. Demo payloads are chunked well below this, so hitting it means the
    /// caller passed something that is not a packet.
    /// </exception>
    public BitReader(ReadOnlySpan<byte> data)
    {
        if (data.Length > int.MaxValue / BitsPerByte)
        {
            throw new ArgumentException(
                "Buffer is too large for a bit-addressable reader.", nameof(data));
        }

        _data = data;
        _bitPosition = 0;
    }

    /// <summary>Number of bits consumed so far.</summary>
    public readonly int BitsRead => _bitPosition;

    /// <summary>Number of bits still available.</summary>
    public readonly int BitsRemaining => (_data.Length * BitsPerByte) - _bitPosition;

    /// <summary>Reads a single bit.</summary>
    /// <exception cref="EndOfStreamException">The buffer is exhausted.</exception>
    public bool ReadBit() => ReadUInt32(1) != 0;

    /// <summary>Reads eight bits. Does not require byte alignment.</summary>
    /// <exception cref="EndOfStreamException">Fewer than eight bits remain.</exception>
    public byte ReadByte() => (byte)ReadUInt32(BitsPerByte);

    /// <summary>
    /// Reads <paramref name="bitCount"/> bits (0-32) as an unsigned value, zero-extended.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bitCount"/> is negative or greater than 32.
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// Fewer than <paramref name="bitCount"/> bits remain. Nothing is consumed in that case, so
    /// the reader stays usable for error reporting rather than being left mid-field.
    /// </exception>
    public uint ReadUInt32(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitCount, MaxBitsPerRead);

        if (bitCount == 0)
        {
            return 0;
        }

        if (bitCount > BitsRemaining)
        {
            throw new EndOfStreamException(
                $"Requested {bitCount} bits at bit offset {_bitPosition}, but only " +
                $"{BitsRemaining} bits remain.");
        }

        int bytePosition = _bitPosition / BitsPerByte;
        int bitOffset = _bitPosition % BitsPerByte;

        // At most 5 bytes can straddle a 32-bit read that starts unaligned, so a 64-bit window
        // is always wide enough to hold the span before shifting the field down.
        int bytesTouched = ((bitOffset + bitCount) + (BitsPerByte - 1)) / BitsPerByte;
        ulong window = 0;
        for (int i = 0; i < bytesTouched; i++)
        {
            window |= (ulong)_data[bytePosition + i] << (i * BitsPerByte);
        }

        _bitPosition += bitCount;

        ulong value = window >> bitOffset;
        return bitCount == MaxBitsPerRead
            ? (uint)value
            : (uint)(value & ((1UL << bitCount) - 1));
    }
}
