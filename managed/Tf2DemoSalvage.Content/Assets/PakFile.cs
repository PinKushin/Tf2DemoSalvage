using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The zip a map carries inside itself, holding the materials and models it ships with.
/// </summary>
/// <remarks>
/// **This is where a community map's own content lives.** Of the 211 materials
/// <c>cp_process_final</c> paints its surfaces with, 54 are not in the game's archives at all —
/// they are in lump 40, a zip embedded in the map. A reader that only looks in the game's VPKs
/// draws a quarter of a community map untextured and has no idea it is missing anything.
///
/// **A hand-written zip reader rather than <see cref="ZipArchive"/>, and for a measured reason.**
/// Valve compresses pakfile entries with LZMA — zip method 14 — and .NET refuses it outright:
///
/// <code>
///   System.IO.InvalidDataException: The archive entry was compressed using LZMA
///                                   and is not supported.
/// </code>
///
/// Deflate and stored entries go through the framework; LZMA entries go through the same decoder
/// the compressed BSP lumps use. The zip variant wraps the stream in a small header of its own:
/// two bytes of SDK version, two of property length, then the five property bytes.
///
/// **The pakfile is hostile input (D32).** It comes from whoever built the map, which for a
/// downloaded map is a stranger, so every offset is checked against the lump and every declared
/// size against a cap before anything is allocated.
/// </remarks>
public sealed class PakFile
{
    /// <summary>Index of the pakfile lump.</summary>
    public const int LumpIndex = BspLumpIndex.PakFile;

    /// <summary>Largest file this will extract, in bytes.</summary>
    /// <remarks>
    /// A zip states its own uncompressed size, so that number is the attacker's choice. 128 MB is
    /// far above any real material or model and well below anything that hurts.
    /// </remarks>
    private const int MaximumEntryBytes = 128 * 1024 * 1024;

    private const uint CentralDirectorySignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint LocalHeaderSignature = 0x04034B50;

    private const ushort MethodStored = 0;
    private const ushort MethodDeflate = 8;
    private const ushort MethodLzma = 14;

    private readonly ReadOnlyMemory<byte> _zip;
    private readonly Dictionary<string, PakEntry> _entries;

    private PakFile(ReadOnlyMemory<byte> zip, Dictionary<string, PakEntry> entries)
    {
        _zip = zip;
        _entries = entries;
    }

    /// <summary>How many files the pakfile holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Every path inside it.</summary>
    public IEnumerable<string> Paths => _entries.Keys;

    /// <summary>Reads the pakfile out of a map.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The pakfile, which may be empty.</returns>
    /// <exception cref="InvalidDataException">The lump is not a readable zip.</exception>
    public static PakFile ReadFrom(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return Read(BspLumpData.Read(file, header.Lump(LumpIndex)));
    }

    /// <summary>Reads a zip.</summary>
    /// <param name="zip">The zip's bytes.</param>
    /// <returns>The pakfile.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a readable zip.</exception>
    public static PakFile Read(ReadOnlyMemory<byte> zip)
    {
        Dictionary<string, PakEntry> entries = new(StringComparer.OrdinalIgnoreCase);

        if (zip.Length < 22)
        {
            // Too short to hold an end-of-central-directory record, so there is nothing in it.
            return new PakFile(zip, entries);
        }

        ReadOnlySpan<byte> span = zip.Span;
        int end = FindEndOfCentralDirectory(span);

        if (end < 0)
        {
            return new PakFile(zip, entries);
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[(end + 10)..]);
        int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(end + 16)..]);
        int at = start;

        for (int index = 0; index < count; index++)
        {
            if (at < 0 || at + 46 > span.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(span[at..]) != CentralDirectorySignature)
            {
                break;
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 10)..]);
            uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 20)..]);
            uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 24)..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 28)..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 30)..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 32)..]);
            uint localHeader = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 42)..]);

            if (at + 46 + nameLength > span.Length)
            {
                break;
            }

            // UTF-8: community maps carry non-English asset paths.
            string name = Encoding.UTF8.GetString(span.Slice(at + 46, nameLength));

            if (name.Length > 0 && !name.EndsWith('/'))
            {
                entries[Normalise(name)] = new PakEntry(method, localHeader, compressed, uncompressed);
            }

            at += 46 + nameLength + extraLength + commentLength;
        }

        return new PakFile(zip, entries);
    }

    /// <summary>Whether the pakfile holds a path.</summary>
    /// <param name="path">Path inside the zip.</param>
    /// <returns>Whether it is there.</returns>
    public bool Contains(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _entries.ContainsKey(Normalise(path));
    }

    /// <summary>Extracts a file.</summary>
    /// <param name="path">Path inside the zip.</param>
    /// <returns>The file's bytes, or null if it is not there.</returns>
    /// <exception cref="InvalidDataException">The entry is malformed or too large.</exception>
    public byte[]? ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_entries.TryGetValue(Normalise(path), out PakEntry entry))
        {
            return null;
        }

        if (entry.Uncompressed > MaximumEntryBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{path}' declares {entry.Uncompressed:N0} bytes, beyond the " +
                $"{MaximumEntryBytes:N0}-byte limit."));
        }

        ReadOnlySpan<byte> span = _zip.Span;
        int at = (int)entry.LocalHeader;

        if (at < 0 || at + 30 > span.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(span[at..]) != LocalHeaderSignature)
        {
            throw new InvalidDataException($"'{path}' does not start with a local file header.");
        }

        // The local header repeats the name and extra lengths, and they are NOT required to match
        // the central directory's - the data starts after the local copies.
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 26)..]);
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 28)..]);
        int data = at + 30 + nameLength + extraLength;

        if (data < 0 || (long)data + entry.Compressed > span.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{path}' claims {entry.Compressed:N0} bytes at {data} of a {span.Length:N0}-byte zip."));
        }

        ReadOnlyMemory<byte> payload = _zip.Slice(data, (int)entry.Compressed);

        return entry.Method switch
        {
            MethodStored => payload.ToArray(),
            MethodDeflate => Inflate(payload, (int)entry.Uncompressed),
            MethodLzma => Unpack(payload, (int)entry.Uncompressed),
            _ => throw new InvalidDataException(
                $"'{path}' uses zip compression method {entry.Method}, which is not supported."),
        };
    }

    private static byte[] Inflate(ReadOnlyMemory<byte> payload, int size)
    {
        using MemoryStream source = new(payload.ToArray(), writable: false);
        using DeflateStream stream = new(source, CompressionMode.Decompress);
        using MemoryStream output = new(size);

        stream.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Decompresses a zip LZMA entry.</summary>
    /// <remarks>
    /// The zip variant is the raw LZMA stream behind a four-byte preamble: two bytes of SDK
    /// version, then a two-byte property length which is always five. That preamble is why feeding
    /// the entry straight to an LZMA decoder produces nonsense rather than an error.
    /// </remarks>
    private static byte[] Unpack(ReadOnlyMemory<byte> payload, int size)
    {
        const int PreambleBytes = 4;
        const int PropertyBytes = 5;

        if (payload.Length < PreambleBytes + PropertyBytes)
        {
            throw new InvalidDataException(
                $"An LZMA zip entry needs {PreambleBytes + PropertyBytes} bytes of header.");
        }

        int properties = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span[2..]);

        if (properties != PropertyBytes)
        {
            throw new InvalidDataException(
                $"An LZMA zip entry declares {properties} property bytes rather than {PropertyBytes}.");
        }

        return ValveLzma.Decode(
            payload.Span.Slice(PreambleBytes, PropertyBytes),
            payload.Span[(PreambleBytes + PropertyBytes)..],
            size);
    }

    /// <summary>Finds the end-of-central-directory record, which is at the end of the file.</summary>
    /// <remarks>
    /// Scanned backwards because the record carries a variable-length comment after it, so its
    /// position is not fixed. The comment can be 64 KB, which bounds the search.
    /// </remarks>
    private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> span)
    {
        int earliest = Math.Max(0, span.Length - 22 - 0xFFFF);

        for (int at = span.Length - 22; at >= earliest; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(span[at..]) == EndOfCentralDirectorySignature)
            {
                return at;
            }
        }

        return -1;
    }

    private static string Normalise(string path) =>
        path.Replace('\\', '/').Trim('/').ToUpperInvariant();

    private readonly record struct PakEntry(
        ushort Method, uint LocalHeader, uint Compressed, uint Uncompressed);
}
