using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Snappy decompression — the scheme TF2 uses for its compressed string tables.
/// </summary>
/// <remarks>
/// A varint giving the uncompressed length, then elements each introduced by a tag byte whose
/// low two bits pick the kind:
///
/// | Tag | Kind | Length | Offset |
/// |---|---|---|---|
/// | 00 | literal | tag &gt;&gt; 2, plus one; 60+ means the length is in trailing bytes | — |
/// | 01 | copy | bits 2-4, plus four | eleven bits: tag's top three, then one byte |
/// | 10 | copy | tag &gt;&gt; 2, plus one | two bytes, little-endian |
/// | 11 | copy | tag &gt;&gt; 2, plus one | four bytes, little-endian |
///
/// **Every length is stored one less than its true value**, since a zero-length element would
/// be meaningless. Getting that wrong shortens the output without making it unparseable.
///
/// **Copies may overlap the bytes they are still writing.** A repeated run compresses to one
/// literal plus a copy from one byte back, so this is the common case rather than an edge one.
/// The copy is therefore byte-at-a-time from the live buffer; a block copy would be a bug, the
/// same one <see cref="Lzss"/> documents.
///
/// Implemented here rather than taken from a package. The format is small and fully specified,
/// the project's premise is understanding what it decodes, and a decompressor that is subtly
/// wrong yields bytes that still parse as string table entries.
/// </remarks>
public static class Snappy
{
    /// <summary>Literal lengths at or above this are stored in trailing bytes.</summary>
    private const int LiteralLengthInTrailingBytes = 60;

    /// <summary>Tag kinds, in the tag byte's low two bits.</summary>
    private const int TagLiteral = 0;
    private const int TagCopyOneByteOffset = 1;
    private const int TagCopyTwoByteOffset = 2;

    /// <summary>Decompresses a Snappy stream.</summary>
    /// <param name="compressed">The stream, beginning with its varint length.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">
    /// The stream is truncated, disagrees with its declared length, or contains a copy that
    /// reaches before the start of its output.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        int read = 0;
        int targetLength = (int)ReadVarInt(compressed, ref read);
        byte[] output = new byte[targetLength];
        int written = 0;

        while (read < compressed.Length)
        {
            int tag = compressed[read++];

            switch (tag & 0x03)
            {
                case TagLiteral:
                    ReadLiteral(compressed, ref read, tag, output, ref written);
                    break;

                case TagCopyOneByteOffset:
                {
                    // Length and the offset's high bits share the tag byte: length in bits 2-4
                    // (plus four), the offset's top three bits in 5-7.
                    Need(compressed, read, 1);
                    int length = ((tag >> 2) & 0x07) + 4;
                    int offset = (((tag >> 5) & 0x07) << 8) | compressed[read++];
                    Copy(output, ref written, offset, length, targetLength);
                    break;
                }

                case TagCopyTwoByteOffset:
                {
                    Need(compressed, read, 2);

                    // Stryker disable once Bitwise: tag comes from a byte, so it is never
                    // negative and >>> is identical to >>. Equivalent mutant.
                    int length = (tag >> 2) + 1;
                    int offset = BinaryPrimitives.ReadUInt16LittleEndian(compressed[read..]);
                    read += 2;
                    Copy(output, ref written, offset, length, targetLength);
                    break;
                }

                default:
                {
                    Need(compressed, read, 4);
                    int length = (tag >> 2) + 1;
                    int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed[read..]);
                    read += 4;
                    Copy(output, ref written, offset, length, targetLength);
                    break;
                }
            }
        }

        if (written != targetLength)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Snappy stream ended after {written} of {targetLength} declared bytes."));
        }

        return output;
    }

    private static void ReadLiteral(
        ReadOnlySpan<byte> compressed, ref int read, int tag, byte[] output, ref int written)
    {
        int length = tag >> 2;

        if (length >= LiteralLengthInTrailingBytes)
        {
            // 60..63 mean the real length occupies that many bytes beyond 59, little-endian.
            int extraBytes = length - LiteralLengthInTrailingBytes + 1;
            Need(compressed, read, extraBytes);

            length = 0;
            for (int i = 0; i < extraBytes; i++)
            {
                length |= compressed[read + i] << (i * 8);
            }

            read += extraBytes;
        }

        length++;
        Need(compressed, read, length);

        if (written + length > output.Length)
        {
            throw TooLong(output.Length);
        }

        compressed.Slice(read, length).CopyTo(output.AsSpan(written));
        written += length;
        read += length;
    }

    private static void Copy(
        byte[] output, ref int written, int offset, int length, int targetLength)
    {
        // Zero would name the byte about to be written. Unchecked it repeats whatever the
        // buffer happens to hold, which is a silent wrong answer rather than a failure.
        if (offset <= 0 || offset > written)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Snappy copy offset {offset} is outside the {written} bytes produced so far."));
        }

        if (written + length > targetLength)
        {
            throw TooLong(targetLength);
        }

        // Byte at a time, from the buffer as it grows. A block copy would be wrong: an
        // overlapping copy is how a repeated run is encoded and depends on bytes written
        // earlier in this same loop.
        int source = written - offset;
        for (int i = 0; i < length; i++)
        {
            output[written++] = output[source + i];
        }
    }

    private static uint ReadVarInt(ReadOnlySpan<byte> compressed, ref int read)
    {
        uint value = 0;
        int shift = 0;

        while (true)
        {
            if (read >= compressed.Length)
            {
                throw new InvalidDataException(
                    "Snappy stream ended inside its length preamble.");
            }

            byte current = compressed[read++];
            value |= (uint)(current & 0x7F) << shift;

            if ((current & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }
    }

    private static void Need(ReadOnlySpan<byte> compressed, int read, int count)
    {
        if (read + count > compressed.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Snappy stream needs {count} bytes at offset {read} but holds " +
                $"{compressed.Length}."));
        }
    }

    private static InvalidDataException TooLong(int targetLength) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"Snappy stream produces more than the {targetLength} bytes it declared."));
}
