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
    /// <summary>
    /// Largest output-to-input ratio a well-formed Snappy stream can reach.
    /// </summary>
    /// <remarks>
    /// A copy tag is two bytes and can reproduce at most 64, so 64 is the ceiling on what one
    /// byte of input can become, with a margin for the preamble. This is a sanity bound on a
    /// declared length, not a limit on legitimate data.
    /// </remarks>
    private const long MaxExpansionRatio = 64;

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
        uint declaredLength = ReadVarInt(compressed, ref read);

        // **The declared length is data, and it was being believed.** Found by fuzzing on
        // 2026-08-11: a stream may declare any 32-bit size, and casting it straight to int
        // produces either a negative length or one past int.MaxValue, both of which reach
        // `new byte[]` and throw OverflowException - an undocumented failure escaping the
        // parser rather than a refusal. String tables arrive Snappy-compressed off the network,
        // so a demo can carry this.
        //
        // The bound is the input's own size rather than a constant: Snappy's maximum compression
        // ratio is bounded by its copy encoding, so an output vastly larger than the input it
        // came from is a malformed stream regardless of what the preamble claims.
        if (declaredLength > (uint)int.MaxValue ||
            declaredLength > (uint)compressed.Length * MaxExpansionRatio)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A Snappy stream of {compressed.Length} bytes declares {declaredLength} bytes " +
                $"of output, which no stream that size can produce."));
        }

        int targetLength = (int)declaredLength;
        byte[] output = new byte[targetLength];
        int written = 0;

        // Checked at the TOP of the loop rather than the bottom, comparing each entry against
        // the last, so a `continue` or an early branch inside the body cannot skip the check.
        DecodeProgress progress = new("a Snappy stream", read - 1);

        while (read < compressed.Length)
        {
            progress.Advanced(read);

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

            // Accumulated as UNSIGNED, which is what the format actually specifies. Done in a
            // signed int, a fourth byte of 0x80 or above lands in the sign bit and the length
            // comes out negative — and a negative length is not caught by anything downstream,
            // because every guard here is written against a length that is too LARGE. Need()
            // sees read plus a negative as still inside the buffer, and the output-capacity
            // check is false for any negative. The value reaches Slice and dies in its argument
            // validation: an ArgumentOutOfRangeException from the framework where this type's
            // contract promises InvalidDataException.
            //
            // Found by the fuzzer on fuzz-box 2026-08-11, in under a minute, on the snappy
            // target's first run under the scheduled fuzz mode.
            uint accumulated = 0;
            for (int i = 0; i < extraBytes; i++)
            {
                accumulated |= (uint)compressed[read + i] << (i * 8);
            }

            // The increment below is part of the encoding — a stored length is one less than the
            // real one — so the check has to leave room for it. int.MaxValue - 1 is the largest
            // value that can survive it without wrapping, and anything near that is corrupt data
            // regardless: Need() would refuse it a line later against a buffer this size.
            if (accumulated > int.MaxValue - 1)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A Snappy literal declares a length of {accumulated} bytes, which cannot be " +
                    $"a real length in a {compressed.Length}-byte stream."));
            }

            length = (int)accumulated;
            read += extraBytes;
        }

        length++;
        Need(compressed, read, length);

        // Long for the same reason as Need: written plus a length near int.MaxValue wraps
        // negative, and a negative is never greater than the output's length.
        if ((long)written + length > output.Length)
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
        // **Widened to long, because the sum is what overflowed.** A length read from the stream
        // can be int.MaxValue, and int.MaxValue plus a read offset wraps to a negative - which is
        // not greater than the buffer length, so this guard passed and the value went on to die
        // in Slice's argument validation instead.
        //
        // Every guard here is written as "is this number too large", and that shape cannot catch
        // a number that becomes small by wrapping. Doing the comparison in a type the sum fits in
        // is the fix, and it belongs here rather than at any one call site: this is the check all
        // of them share.
        if ((long)read + count > compressed.Length)
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
