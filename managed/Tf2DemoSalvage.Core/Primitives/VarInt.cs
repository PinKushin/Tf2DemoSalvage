using System;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Protobuf-style variable-length integer decoding, layered over <see cref="BitReader"/>.
/// </summary>
/// <remarks>
/// Seven payload bits per byte, least-significant group first, with the high bit set on every
/// byte except the last. 300 encodes as 0xAC 0x02.
///
/// Deliberately separate from <see cref="BitReader"/> rather than folded into it: the reader's
/// job is positioned access to bits, this is an encoding on top of that. A caller decoding a
/// fixed-width field never needs varints, and this type never needs to know how bits are packed.
///
/// Byte-oriented but not byte-aligned - Source reads varints through the same bit reader as
/// everything else, so an encoding may begin at any bit offset.
///
/// <para><b>Unverified for our corpus.</b> This encoding is not original to Source - GoldSrc
/// (1998) and Source (2004) both predate protobuf's 2008 release, and the original netcode used
/// hand-rolled bit packing. <c>ReadVarInt32</c> arrives in <c>bitbuf.h</c> in the protobuf
/// adoption era (~2011-12) and rides into the Source 2013 SDK TF2 builds from. Whether network
/// protocol 24 (the 2015 era of <c>z1800.dem</c>) actually uses it on the paths we decode is an
/// open question in <c>FORMAT_NOTES.md</c>, not a settled fact. Confirm against real packet bytes
/// before relying on it; do not assume its presence just because the primitive exists.</para>
/// </remarks>
public static class VarInt
{
    private const int GroupBits = 7;
    private const uint PayloadMask = 0x7F;
    private const uint ContinuationFlag = 0x80;

    /// <summary>
    /// Groups needed to carry 32 bits at 7 bits each: ceil(32 / 7).
    /// </summary>
    internal const int MaxGroups32 = 5;

    /// <summary>
    /// Groups needed to carry 64 bits at 7 bits each: ceil(64 / 7).
    /// </summary>
    internal const int MaxGroups64 = 10;

    /// <summary>Writes an unsigned 32-bit varint.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Seven payload bits per byte, low group first, with the high bit set on every byte except
    /// the last. The encoding is canonical here — the fewest groups that hold the value — which
    /// matters for a round trip rather than for correctness: a decoder accepts a padded encoding,
    /// so writing one back would decode to the right number and produce different bytes.
    /// </remarks>
    public static void WriteUInt32(BitWriter writer, uint value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        while (value >= ContinuationFlag)
        {
            writer.Write((value & PayloadMask) | ContinuationFlag, 8);
            value >>= GroupBits;
        }

        writer.Write(value, 8);
    }

    /// <summary>Reads an unsigned 32-bit varint.</summary>
    /// <param name="reader">Reader positioned at the first byte of the encoding.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">
    /// The buffer ended part-way through the encoding.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The encoding asks for more than <see cref="MaxGroups32"/> bytes, so it cannot be a 32-bit
    /// varint and would otherwise read on indefinitely.
    /// </exception>
    public static uint ReadUInt32(ref BitReader reader)
    {
        uint result = 0;

        for (int group = 0; group < MaxGroups32; group++)
        {
            uint octet = reader.ReadByte();

            // The final group carries bits 28-34, of which only 28-31 fit. C#'s shift drops the
            // excess, which is what Valve's own reader does; its writer never emits such an
            // encoding, so this only ever affects malformed input.
            result |= (octet & PayloadMask) << (GroupBits * group);

            if ((octet & ContinuationFlag) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException(string.Create(
            CultureInfo.InvariantCulture,
            $"A 32-bit varint is at most {MaxGroups32} bytes, but the encoding at bit offset " +
            $"{reader.BitsRead} asks for more."));
    }

    /// <summary>Reads a zig-zag encoded signed 32-bit varint.</summary>
    /// <param name="reader">Reader positioned at the first byte of the encoding.</param>
    /// <returns>The decoded value.</returns>
    /// <remarks>
    /// Zig-zag maps signed values onto unsigned ones so that small magnitudes stay small in
    /// either direction: 0, -1, 1, -2, 2 encode as 0, 1, 2, 3, 4. Without it every negative
    /// number would set the high bit and cost the full five bytes.
    /// </remarks>
    /// <exception cref="EndOfStreamException">
    /// The buffer ended part-way through the encoding.
    /// </exception>
    /// <exception cref="InvalidDataException">The encoding is too long to be a 32-bit varint.</exception>
    public static int ReadInt32(ref BitReader reader) => DecodeZigZag(ReadUInt32(ref reader));

    /// <summary>Reads an unsigned 64-bit varint.</summary>
    /// <param name="reader">Reader positioned at the first byte of the encoding.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">
    /// The buffer ended part-way through the encoding.
    /// </exception>
    /// <exception cref="InvalidDataException">The encoding is too long to be a 64-bit varint.</exception>
    public static ulong ReadUInt64(ref BitReader reader)
    {
        ulong result = 0;

        for (int group = 0; group < MaxGroups64; group++)
        {
            uint octet = reader.ReadByte();
            result |= (ulong)(octet & PayloadMask) << (GroupBits * group);

            if ((octet & ContinuationFlag) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException(string.Create(
            CultureInfo.InvariantCulture,
            $"A 64-bit varint is at most {MaxGroups64} bytes, but the encoding at bit offset " +
            $"{reader.BitsRead} asks for more."));
    }

    /// <summary>Reads a zig-zag encoded signed 64-bit varint.</summary>
    /// <param name="reader">Reader positioned at the first byte of the encoding.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">
    /// The buffer ended part-way through the encoding.
    /// </exception>
    /// <exception cref="InvalidDataException">The encoding is too long to be a 64-bit varint.</exception>
    public static long ReadInt64(ref BitReader reader) => DecodeZigZag(ReadUInt64(ref reader));

    /// <summary>Writes a signed 32-bit varint.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Zig-zag first, so small negatives stay short: -1 becomes 1 rather than 0xFFFFFFFF, which
    /// would otherwise cost the full five groups.
    /// </remarks>
    public static void WriteInt32(BitWriter writer, int value) =>
        WriteUInt32(writer, EncodeZigZag(value));

    // Stryker disable once Bitwise: the operand is unsigned, so >> is already a logical shift
    // and >>> is the same operation. Equivalent mutant, not a missing test.
    private static int DecodeZigZag(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    // Stryker disable once Bitwise: unsigned operand, as above.
    private static long DecodeZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    /// <summary>Folds a signed value onto an unsigned one, small magnitudes staying small.</summary>
    private static uint EncodeZigZag(int value) => (uint)((value << 1) ^ (value >> 31));
}
