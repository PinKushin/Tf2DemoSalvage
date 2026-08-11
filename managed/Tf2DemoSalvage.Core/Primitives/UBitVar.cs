using System;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Source's variable-width unsigned integer: a two-bit selector choosing a 4, 8, 12 or 32-bit
/// payload.
/// </summary>
/// <remarks>
/// A different trade from <see cref="VarInt"/>, and the two are not interchangeable. A varint
/// is byte-granular and unbounded; this is bit-granular with four fixed widths, so a small
/// value costs six bits rather than eight. Entity index deltas use it because they are usually
/// tiny and there are tens of thousands of them per demo.
/// </remarks>
public static class UBitVar
{
    /// <summary>Width of the selector that chooses the payload width.</summary>
    public const int SelectorBits = 2;

    /// <summary>Payload widths, indexed by selector value.</summary>
    private static ReadOnlySpan<int> PayloadBits => [4, 8, 12, 32];

    /// <summary>Reads one variable-width value.</summary>
    /// <param name="reader">Reader positioned at the selector.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="System.IO.EndOfStreamException">The buffer is exhausted.</exception>
    public static uint Read(ref BitReader reader)
    {
        int selector = (int)reader.ReadUInt32(SelectorBits);
        return reader.ReadUInt32(PayloadBits[selector]);
    }

    /// <summary>Writes one variable-width value.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <remarks>
    /// The narrowest payload that holds the value, which is not merely an optimisation: a decoder
    /// reads whatever width the selector names, so a wider encoding of the same number is a
    /// different bit stream. Byte-exact re-encoding needs the canonical choice.
    /// </remarks>
    public static void Write(BitWriter writer, uint value)
    {
        System.ArgumentNullException.ThrowIfNull(writer);

        (uint selector, int bits) = value switch
        {
            < 1u << 4 => (0u, 4),
            < 1u << 8 => (1u, 8),
            < 1u << 12 => (2u, 12),
            _ => (3u, 32),
        };

        writer.Write(selector, SelectorBits).Write(value, bits);
    }

    /// <summary>Bits a value occupies once encoded, including the selector.</summary>
    /// <param name="value">Value to measure.</param>
    /// <returns>Total encoded width in bits.</returns>
    /// <remarks>
    /// Exposed for tests and diagnostics: it makes "this encoding is smaller for small values"
    /// checkable rather than asserted.
    /// </remarks>
    public static int EncodedBits(uint value) => SelectorBits + value switch
    {
        < 1u << 4 => 4,
        < 1u << 8 => 8,
        < 1u << 12 => 12,
        _ => 32,
    };
}
