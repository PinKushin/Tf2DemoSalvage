using System.Collections.Generic;
using System.Text;
using Tf2DemoSalvage.Core.Primitives;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Reading conventions shared by every network message.
/// </summary>
public static class NetBitReading
{
    /// <summary>
    /// Reads a NUL-terminated string, the encoding Source uses throughout its bit streams.
    /// </summary>
    /// <remarks>
    /// Not length-prefixed: the terminator is the only thing that ends it. A decoder that has
    /// lost bit alignment will therefore read until it happens to find a zero byte, which is
    /// why garbage strings are the first visible symptom of a desynchronised stream rather
    /// than an exception.
    /// </remarks>
    public static string ReadString(ref BitReader reader)
    {
        List<byte> bytes = new();

        while (true)
        {
            byte value = reader.ReadByte();
            if (value == 0)
            {
                break;
            }

            bytes.Add(value);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>
    /// Copies <paramref name="bitCount"/> bits out of the reader into their own buffer.
    /// </summary>
    /// <remarks>
    /// A length-prefixed body has to be read as an independent stream, and
    /// <see cref="BitReader"/> cannot be positioned at an arbitrary bit offset. Copying also
    /// contains the damage: a malformed body cannot run past its declared length and corrupt
    /// whatever follows, because the outer reader has already moved on.
    /// </remarks>
    public static byte[] CopyBits(ref BitReader reader, int bitCount)
    {
        // VALIDATED BEFORE THE ALLOCATION, which is the whole point. bitCount comes off the wire
        // - svc_GameEventList and friends declare their body length - so an unchecked value of two
        // billion is a 250 MB allocation per message. The reads below would fail immediately
        // afterwards, but by then the memory has already been taken, and a file full of such
        // messages exhausts the process without a single malformed byte being decoded.
        //
        // Negative is checked as well as too-large: a length above int.MaxValue arrives negative,
        // and `(bitCount + 7) / 8` on a negative is a negative size that throws from the runtime
        // rather than through this parser's own contract.
        if (bitCount < 0 || bitCount > reader.BitsRemaining)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A body of {bitCount} bits was declared, but the packet holds " +
                $"{reader.BitsRemaining}."));
        }

        // Stryker disable once Arithmetic: mutating the rounding only over-allocates. The
        // extra bytes are never written or read, so decoding stays correct.
        byte[] buffer = new byte[(bitCount + 7) / 8];
        int whole = bitCount / 8;

        for (int i = 0; i < whole; i++)
        {
            buffer[i] = reader.ReadByte();
        }

        int remainder = bitCount % 8;
        if (remainder > 0)
        {
            buffer[whole] = (byte)reader.ReadUInt32(remainder);
        }

        return buffer;
    }
}
