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
    public static uint Read(ref BitReader reader) => Read(ref reader, out _);

    /// <summary>Reads one value, reporting the payload width it was sent at.</summary>
    /// <param name="reader">Reader positioned at the selector.</param>
    /// <param name="payloadBits">The width the sender chose.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="System.IO.EndOfStreamException">The buffer is exhausted.</exception>
    /// <remarks>
    /// **The width is not implied by the value.** Nothing stops a sender using a wider bucket than
    /// the value needs, and TF2 demonstrably does: about a tenth of entity snapshots carry an
    /// index at twelve payload bits where eight would hold it. Both decode to the same number, so
    /// only a re-encode can see the difference - which is why this is reported rather than assumed
    /// (RISKS B25).
    /// </remarks>
    public static uint Read(ref BitReader reader, out int payloadBits)
    {
        int selector = (int)reader.ReadUInt32(SelectorBits);
        payloadBits = PayloadBits[selector];
        return reader.ReadUInt32(payloadBits);
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
    public static void Write(BitWriter writer, uint value) => Write(writer, value, 0);

    /// <summary>Writes one value at a stated payload width.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The value.</param>
    /// <param name="payloadBits">
    /// The width to use, or 0 for the narrowest that holds the value. A width too small for the
    /// value is ignored in favour of the narrowest that fits, because the alternative is writing
    /// a number that reads back as a different one.
    /// </param>
    /// <exception cref="System.ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void Write(BitWriter writer, uint value, int payloadBits)
    {
        System.ArgumentNullException.ThrowIfNull(writer);

        (uint selector, int bits) = value switch
        {
            < 1u << 4 => (0u, 4),
            < 1u << 8 => (1u, 8),
            < 1u << 12 => (2u, 12),
            _ => (3u, MaxPayloadBits),
        };

        if (payloadBits > bits)
        {
            selector = (uint)PayloadBits.IndexOf(payloadBits);
            bits = payloadBits;
        }

        writer.Write(selector, SelectorBits).Write(value, bits);
    }

    /// <summary>Widest payload the encoding carries.</summary>
    private const int MaxPayloadBits = 32;

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
