using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Packs values into a bit stream least-significant-bit first — the inverse of <see cref="BitReader"/>.
/// </summary>
/// <remarks>
/// **Promoted out of the test project, where it had been the only bit writer for months.** That
/// was the right place to start and the wrong place to stay: a decoder can only be checked
/// against something, and until now the only things available were hand-built fixtures and a
/// second parser. Fixtures have been the least reliable part of this suite — several layer 2 bugs
/// were in the fixture rather than the decoder — and a round trip removes the expected value from
/// the experiment entirely. Whatever went in has to come out.
///
/// The bit order is the whole contract. Source packs least-significant-bit first within a byte
/// and moves to the next byte when the current one fills, so a value spanning a boundary has its
/// low bits in the earlier byte. Writing that the other way produces bytes that still decode into
/// plausible numbers, which is why the property tests state it as a round trip against
/// <see cref="BitReader"/> rather than as an expected byte array.
///
/// Nothing here pads. A message that states its body length in bits has to be measured and then
/// appended bit by bit, and copying the bytes instead would silently round up to the next byte
/// boundary and desynchronise everything after it.
/// </remarks>
public sealed class BitWriter
{
    private const int BitsPerByte = 8;

    /// <summary>Widest value a single write can carry.</summary>
    private const int MaxBits = 32;

    private readonly List<byte> _bytes = [];
    private int _bitCount;

    /// <summary>Bits written so far.</summary>
    public int BitCount => _bitCount;

    /// <summary>Writes the low <paramref name="bits"/> bits of <paramref name="value"/>.</summary>
    /// <param name="value">Value to write; bits above <paramref name="bits"/> are ignored.</param>
    /// <param name="bits">Width, 1 to 32.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bits"/> is outside 1 to 32.
    /// </exception>
    public BitWriter Write(uint value, int bits)
    {
        if (bits is < 1 or > MaxBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bits),
                bits,
                string.Create(
                    CultureInfo.InvariantCulture, $"A field is 1 to {MaxBits} bits wide."));
        }

        for (int i = 0; i < bits; i++)
        {
            if (_bitCount % BitsPerByte == 0)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                _bytes[^1] |= (byte)(1 << (_bitCount % BitsPerByte));
            }

            _bitCount++;
        }

        return this;
    }

    /// <summary>Writes a single bit.</summary>
    /// <param name="value">The bit.</param>
    /// <returns>This writer, for chaining.</returns>
    public BitWriter WriteBit(bool value) => Write(value ? 1u : 0u, 1);

    /// <summary>
    /// Writes a value in Source's variable-width form: a two-bit selector, then the narrowest of
    /// 4, 8, 12 or 32 bits that holds it.
    /// </summary>
    /// <param name="value">Value to write.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <remarks>
    /// Written from the encoder's side rather than by inverting <see cref="UBitVar"/>'s reader, so
    /// a wrong selector cannot agree with itself across the round trip.
    /// </remarks>
    public BitWriter WriteUBitVar(uint value)
    {
        (uint selector, int bits) = value switch
        {
            < 1u << 4 => (0u, 4),
            < 1u << 8 => (1u, 8),
            < 1u << 12 => (2u, 12),
            _ => (3u, MaxBits),
        };

        return Write(selector, 2).Write(value, bits);
    }

    /// <summary>Writes a NUL-terminated UTF-8 string, the encoding Source uses in bit streams.</summary>
    /// <param name="value">The string.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    /// <remarks>
    /// UTF-8 rather than ASCII, because player names are arbitrary bytes the client chose and TF2
    /// players use that freely. An ASCII encoder does not fail on those — it corrupts a name into
    /// a different plausible name.
    /// </remarks>
    public BitWriter WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            Write(b, BitsPerByte);
        }

        return Write(0, BitsPerByte);
    }

    /// <summary>Writes whole bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>This writer, for chaining.</returns>
    public BitWriter WriteBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            Write(b, BitsPerByte);
        }

        return this;
    }

    /// <summary>Appends the low bits of a buffer without padding to a byte boundary.</summary>
    /// <param name="bytes">Buffer holding the bits, starting at bit zero.</param>
    /// <param name="bits">How many bits of it to append.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bits"/> is negative or exceeds what <paramref name="bytes"/> holds.
    /// </exception>
    /// <remarks>
    /// The operation every length-prefixed message needs. A body is built separately so its bit
    /// count can be written ahead of it, and it then has to land at whatever unaligned offset the
    /// header left behind — so this copies bit by bit rather than byte by byte.
    /// </remarks>
    public BitWriter AppendBits(ReadOnlySpan<byte> bytes, int bits)
    {
        if (bits < 0 || bits > bytes.Length * BitsPerByte)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bits),
                bits,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {bytes.Length}-byte buffer holds {bytes.Length * BitsPerByte} bits."));
        }

        for (int bit = 0; bit < bits; bit++)
        {
            Write((uint)((bytes[bit / BitsPerByte] >> (bit % BitsPerByte)) & 1), 1);
        }

        return this;
    }

    /// <summary>The bytes written, with the final byte zero-padded to its end.</summary>
    /// <returns>A copy of the buffer.</returns>
    public byte[] Build() => [.. _bytes];
}
