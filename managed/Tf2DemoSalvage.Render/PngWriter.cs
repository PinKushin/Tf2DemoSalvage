using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Writes a PNG, so a rendered frame can be looked at without <c>System.Drawing</c>.
/// </summary>
/// <remarks>
/// **This exists because `System.Drawing.Common` is Windows-only by design** since .NET 7, and it
/// was the single thing keeping the render layer on the `net10.0-windows` framework — used in two
/// places, both of which only ever wanted "put these pixels in a file I can open".
///
/// **Written rather than taken from a package**, because the requirement is genuinely small: one
/// colour type, no interlacing, no palette, filter 0. PNG's container is four chunks and a CRC, and
/// the compression is `ZLibStream` out of the framework. A dependency would be larger than the code
/// and would carry decoding, resizing and format conversion that nothing here needs.
///
/// **The format is RFC 2083**, and the parts that are easy to get subtly wrong are called out at
/// each site below: the byte order is big-endian throughout — the opposite of everything else this
/// project reads — the CRC covers the chunk TYPE as well as its data, and every scanline carries a
/// leading filter byte that is not part of the image.
/// </remarks>
public static class PngWriter
{
    /// <summary>The eight bytes every PNG starts with.</summary>
    /// <remarks>
    /// RFC 2083 §3.1. The high bit on the first byte catches a transfer that stripped it; the
    /// <c>CR LF</c> and lone <c>LF</c> catch a transfer that "helpfully" converted line endings.
    /// </remarks>
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Colour type 6: RGBA, eight bits a channel.</summary>
    private const byte TrueColourWithAlpha = 6;

    /// <summary>Writes 8-bit RGBA pixels as a PNG.</summary>
    /// <param name="path">File to write; its folder is created if needed.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="rgba">
    /// <paramref name="width"/> × <paramref name="height"/> × 4 bytes, red first, top row first.
    /// </param>
    /// <exception cref="ArgumentException">The path is empty or the buffer is the wrong size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int expected = width * height * 4;

        if (rgba.Length != expected)
        {
            // Checked rather than trusted: a buffer one row short writes a PNG that opens and shows
            // garbage in its last rows, which is a worse failure than refusing.
            throw new ArgumentException(
                $"expected {expected} bytes for {width}x{height} RGBA, got {rgba.Length}",
                nameof(rgba));
        }

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        using FileStream file = File.Create(path);

        file.Write(Signature);
        WriteChunk(file, "IHDR", Header(width, height));
        WriteChunk(file, "IDAT", Compress(width, height, rgba));
        WriteChunk(file, "IEND", []);
    }

    /// <summary>The IHDR chunk's payload.</summary>
    private static byte[] Header(int width, int height)
    {
        byte[] header = new byte[13];

        // Big-endian, which is the opposite of every other format this project reads.
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);

        header[8] = 8;                      // bits per channel
        header[9] = TrueColourWithAlpha;    // colour type
        header[10] = 0;                     // compression: deflate, the only value defined
        header[11] = 0;                     // filter method: the only value defined
        header[12] = 0;                     // interlace: none

        return header;
    }

    /// <summary>The image data, filtered and zlib-compressed.</summary>
    /// <remarks>
    /// **Every scanline carries a leading filter byte and it is not part of the image.** Filter 0
    /// means "store the row as-is"; the other four predict each byte from its neighbours and exist
    /// to make the deflate that follows more effective. Omitting the byte shifts every row by one
    /// and produces a picture that is recognisably the right image, skewed — which decoders accept
    /// without complaint.
    ///
    /// Filter 0 throughout, deliberately: these are debug captures, so a slightly larger file is
    /// worth more than the code to choose a filter per row.
    /// </remarks>
    private static byte[] Compress(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int stride = width * 4;

        using MemoryStream compressed = new();

        // ZLibStream, not DeflateStream: PNG's IDAT holds a zlib stream, which is deflate wrapped
        // in a two-byte header and an Adler-32 trailer. DeflateStream writes the payload without
        // them, and the result is rejected by every decoder.
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] row = new byte[stride + 1];

            for (int y = 0; y < height; y++)
            {
                row[0] = 0;
                rgba.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                zlib.Write(row, 0, row.Length);
            }
        }

        return compressed.ToArray();
    }

    /// <summary>Writes one chunk: length, type, data, CRC.</summary>
    private static void WriteChunk(Stream file, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        file.Write(length);

        Span<byte> name = stackalloc byte[4];

        for (int i = 0; i < 4; i++)
        {
            name[i] = (byte)type[i];
        }

        file.Write(name);
        file.Write(data);

        // **The CRC covers the type as well as the data, and not the length.** Computing it over
        // the data alone gives a file that every decoder rejects, and computing it over the length
        // too gives the same — both are easy to write and neither is detectable by eye.
        uint crc = Crc32.Of(name, data);
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        file.Write(check);
    }
}

/// <summary>PNG's CRC-32, as RFC 2083 §15 specifies it.</summary>
/// <remarks>
/// The ordinary CRC-32 with polynomial <c>0xEDB88320</c>, initialised to all ones and complemented
/// at the end. Written out because the framework does not ship one outside the
/// <c>System.IO.Hashing</c> package, and a package for sixteen lines is a poor trade.
/// </remarks>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        uint[] table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            uint c = n;

            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>The CRC of two spans treated as one run of bytes.</summary>
    internal static uint Of(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte b in first)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in second)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
