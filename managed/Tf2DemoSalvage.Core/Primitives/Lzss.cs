using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Valve's LZSS variant, used to compress string table payloads.
/// </summary>
/// <remarks>
/// A control byte followed by eight items, each read from the control byte's **low bit upward**.
/// A clear bit means a literal; a set bit means a back-reference into what has already been
/// produced, packed as twelve bits of offset then four bits of count.
///
/// Both packed fields are stored one less than their true value, because neither zero is
/// usable: an offset of zero would name the byte not yet written, and a count of one is
/// reserved as the end-of-stream marker. There is no length field in the body — the stream ends
/// when that marker appears.
///
/// **Back-references may overlap the bytes they are still writing**, and legitimately do: a
/// run of the same byte is encoded as one literal plus a reference one byte back. A decoder
/// that copies through a snapshot of the output rather than the live buffer produces a
/// plausible result that is wrong wherever the source repeats — so the copy here is
/// deliberately byte-at-a-time and must stay that way. <c>Span.CopyTo</c> would be a bug.
/// </remarks>
public static class Lzss
{
    /// <summary>Magic identifying an LZSS payload.</summary>
    public static ReadOnlySpan<byte> Magic => "LZSS"u8;

    /// <summary>Magic identifying a Snappy payload, which is not implemented.</summary>
    public static ReadOnlySpan<byte> SnappyMagic => "SNAP"u8;

    /// <summary>Bits of the packed pair given to the offset.</summary>
    private const int OffsetShift = 4;

    /// <summary>Mask selecting the count from the low byte of the packed pair.</summary>
    private const int CountMask = 0x0F;

    /// <summary>Items described by one control byte.</summary>
    private const int ItemsPerControlByte = 8;

    /// <summary>Decompresses a payload, which begins with its own four-byte target length.</summary>
    /// <param name="compressed">Payload, starting after the four-byte magic.</param>
    /// <param name="expectedLength">
    /// Length the containing message declared, checked against the payload's own header.
    /// </param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">
    /// The payload is truncated, disagrees with <paramref name="expectedLength"/>, reaches
    /// behind the start of its output, or produces more than it declared.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedLength)
    {
        if (compressed.Length < sizeof(uint))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"LZSS payload of {compressed.Length} bytes is too short to hold its length."));
        }

        int targetLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed);

        // Two independent statements of the same size: the string table message's field and the
        // payload's own header. Disagreement means one was misread, and decoding on would
        // produce a table of the wrong size rather than an error.
        if (targetLength != expectedLength)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"LZSS header declares {targetLength} bytes but the message declared " +
                $"{expectedLength}."));
        }

        byte[] output = new byte[targetLength];
        int written = 0;
        int read = sizeof(uint);

        while (true)
        {
            if (read >= compressed.Length)
            {
                throw Truncated(written, targetLength);
            }

            int control = compressed[read++];

            for (int item = 0; item < ItemsPerControlByte; item++)
            {
                bool isMatch = (control & 1) != 0;
                control >>= 1;

                if (!isMatch)
                {
                    if (read >= compressed.Length)
                    {
                        throw Truncated(written, targetLength);
                    }

                    Append(output, ref written, compressed[read++], targetLength);
                    continue;
                }

                if (read + 1 >= compressed.Length)
                {
                    throw Truncated(written, targetLength);
                }

                int packed = (compressed[read] << OffsetShift) |
                             (compressed[read + 1] >> OffsetShift);
                int count = (compressed[read + 1] & CountMask) + 1;
                read += 2;

                // A count of one is the end of the stream, not a one-byte copy.
                if (count == 1)
                {
                    if (written != targetLength)
                    {
                        throw Truncated(written, targetLength);
                    }

                    return output;
                }

                int source = written - packed - 1;
                if (source < 0)
                {
                    throw new InvalidDataException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"LZSS back-reference reaches {-source} bytes before the start of the " +
                        $"output, so the stream is corrupt."));
                }

                // Byte at a time, reading the buffer as it grows. A block copy would be wrong:
                // an overlapping reference is how a repeated run is encoded, and it depends on
                // seeing bytes written earlier in this same loop.
                for (int i = 0; i < count; i++)
                {
                    Append(output, ref written, output[source + i], targetLength);
                }
            }
        }
    }

    private static void Append(byte[] output, ref int written, byte value, int targetLength)
    {
        if (written >= targetLength)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"LZSS stream produces more than the {targetLength} bytes it declared."));
        }

        output[written++] = value;
    }

    private static InvalidDataException Truncated(int written, int targetLength) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"LZSS stream ended after {written} of {targetLength} bytes."));
}
