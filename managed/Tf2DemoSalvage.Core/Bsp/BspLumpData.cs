using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>
/// The bytes of one BSP lump, decompressed if the map stored it that way.
/// </summary>
/// <remarks>
/// **Every lump of a shipped TF2 map is LZMA-compressed, and nothing in the lump directory says
/// so.** The compression is announced by a 17-byte header at the start of the lump's own bytes,
/// which means a reader that does not look for it reads compressed data as structs and gets
/// numbers rather than errors. Measured on <c>cp_process_final.bsp</c>: the faces lump read raw
/// gave face 0 a plane index of 23,116 against 1,824 planes.
///
/// The arithmetic gives it away without decoding anything, and this is the general form of the
/// rule in <c>length-arithmetic-identifies-a-layout</c>. A lump of fixed-size structures has a
/// length that is a whole multiple of that size. The faces lump is 147,154 bytes and
/// <c>dface_t</c> is 56; 147,154 / 56 is 2,627.75. Decompressed it is 773,976, which is exactly
/// 13,821 faces.
///
/// <code>
///   struct lzma_header_t {
///       unsigned int  id;            // 'LZMA', little-endian on disk
///       unsigned int  actualSize;    // decompressed length
///       unsigned int  lzmaSize;      // compressed length, excluding this header
///       unsigned char properties[5];
///   };
/// </code>
///
/// Note what is *absent*: the eight-byte uncompressed-size field of a standard <c>.lzma</c> file.
/// <c>actualSize</c> replaces it, which is why the decoder here is told its output length rather
/// than reading one.
///
/// **A map is hostile input (D32), and both sizes in that header come out of the file.** They are
/// checked before anything is allocated.
/// </remarks>
internal static class BspLumpData
{
    /// <summary>Size of <c>lzma_header_t</c>.</summary>
    private const int CompressionHeaderBytes = 17;

    /// <summary>
    /// The largest decompressed lump this reader will produce.
    /// </summary>
    /// <remarks>
    /// **The declared size is attacker-controlled, so it cannot be used to size a buffer.** Four
    /// bytes allow 4 GB from a lump of a few hundred, which is a decompression bomb and the same
    /// allocate-before-validate shape already fixed in <c>Lzss</c> and <c>CopyBits</c>.
    ///
    /// 256 MB is far above anything real — the largest lump in a shipped TF2 map is the pakfile,
    /// and the geometry lumps this reader touches run to a couple of megabytes — while staying
    /// small enough that a refusal costs nothing.
    /// </remarks>
    private const int MaximumDecompressedBytes = 256 * 1024 * 1024;

    /// <summary>'LZMA' as it appears at the front of a compressed lump.</summary>
    private static ReadOnlySpan<byte> CompressionMagic => "LZMA"u8;

    /// <summary>Reads one lump, decompressing it if it is compressed.</summary>
    /// <param name="file">The whole map file.</param>
    /// <param name="lump">The directory entry naming the lump.</param>
    /// <returns>The lump's bytes, ready to be read as structures.</returns>
    /// <exception cref="InvalidDataException">
    /// The lump does not lie within the file, or its compression header is not decodable.
    /// </exception>
    public static ReadOnlyMemory<byte> Read(ReadOnlyMemory<byte> file, BspLump lump)
    {
        if (lump.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (lump.Offset < 0 || lump.Length < 0 ||
            (long)lump.Offset + lump.Length > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A lump at offset {lump.Offset} of {lump.Length} bytes does not fit in a " +
                $"{file.Length}-byte file."));
        }

        ReadOnlyMemory<byte> raw = file.Slice(lump.Offset, lump.Length);

        // Too short to hold the header is too short to be compressed, whatever it starts with.
        if (raw.Length < CompressionHeaderBytes ||
            !raw.Span[..CompressionMagic.Length].SequenceEqual(CompressionMagic))
        {
            return raw;
        }

        ReadOnlySpan<byte> header = raw.Span;
        uint actualSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        uint packedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);

        if (actualSize > MaximumDecompressedBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A compressed lump declares {actualSize:N0} decompressed bytes, beyond the " +
                $"{MaximumDecompressedBytes:N0}-byte limit this reader will allocate."));
        }

        if (packedSize > raw.Length - CompressionHeaderBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A compressed lump declares {packedSize:N0} packed bytes but only " +
                $"{raw.Length - CompressionHeaderBytes:N0} follow its header."));
        }

        return Lzma.Decode(
            header.Slice(12, Lzma.PropertiesBytes),
            raw.Span.Slice(CompressionHeaderBytes, (int)packedSize),
            (int)actualSize);
    }

    /// <summary>Reads a lump and checks it holds whole structures of a given size.</summary>
    /// <param name="file">The whole map file.</param>
    /// <param name="lump">The directory entry naming the lump.</param>
    /// <param name="stride">Bytes per structure.</param>
    /// <param name="what">What the lump holds, for the error message.</param>
    /// <returns>The lump's bytes.</returns>
    /// <exception cref="InvalidDataException">The length is not a whole number of structures.</exception>
    /// <remarks>
    /// **`==` rather than `&lt;=`, and this check is what identifies compression.** Dividing the
    /// length by the stride and taking the floor silently discards a trailing partial structure,
    /// so a lump that is not what it claims still yields a plausible count — which is exactly how
    /// compressed lumps read as garbage geometry instead of raising anything.
    /// </remarks>
    public static ReadOnlyMemory<byte> ReadStructures(
        ReadOnlyMemory<byte> file, BspLump lump, int stride, string what)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);

        ReadOnlyMemory<byte> data = Read(file, lump);

        if (data.Length % stride != 0)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The {what} lump is {data.Length:N0} bytes, which is not a whole number of " +
                $"{stride}-byte entries."));
        }

        return data;
    }
}
