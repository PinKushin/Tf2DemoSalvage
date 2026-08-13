using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One file inside a VPK: where it lives and how long it is.</summary>
/// <param name="ArchiveIndex">Numbered archive holding it, or <see cref="VpkArchive.InDirectoryFile"/>.</param>
/// <param name="Offset">Offset of the archived part.</param>
/// <param name="Length">Length of the archived part, which may be zero.</param>
/// <param name="Preload">Bytes stored inline in the directory, which come first.</param>
public readonly record struct VpkEntry(
    int ArchiveIndex, uint Offset, uint Length, ReadOnlyMemory<byte> Preload)
{
    /// <summary>Total size of the file.</summary>
    public long Size => Preload.Length + Length;
}

/// <summary>
/// Reads Valve's VPK archives — where the game keeps its materials and textures.
/// </summary>
/// <remarks>
/// **The point of this class is to stop reinventing what the game already ships.** Face colours,
/// materials and textures are not things to approximate: TF2 has them, in
/// <c>tf2_textures_dir.vpk</c> and <c>tf2_misc_dir.vpk</c>, and a viewer that means to show the map
/// as the game does has to read them.
///
/// A VPK is a directory file plus numbered archives. The directory is small — 1.5 MB of tree
/// against a hundred megabytes per archive — so the whole tree is read once and the archives are
/// touched only for the files actually wanted.
///
/// <code>
///   header (v1 12 bytes, v2 28)
///   tree:  extension \0  { path \0  { filename \0  entry } \0 } \0
///   entry: uint32 crc, uint16 preloadBytes, uint16 archiveIndex,
///          uint32 offset, uint32 length, uint16 0xFFFF, then preloadBytes of data
/// </code>
///
/// Three details that are easy to get wrong and produce nothing rather than an error:
///
/// - **A path of <c>" "</c> means the root**, not a folder called space.
/// - **Preload bytes are part of the file** and come *before* the archived part. A small file can
///   be entirely preload, with a length of zero — reading only the archive gives an empty file.
/// - **Archive index 0x7FFF means the directory file itself**, at an offset past the tree.
///
/// **Map pakfiles are the same shape of problem but hostile** (D32), so offsets and lengths are
/// checked against the file they claim to be in rather than trusted.
/// </remarks>
public sealed class VpkArchive
{
    /// <summary>Archive index meaning "in the directory file, after the tree".</summary>
    public const int InDirectoryFile = 0x7FFF;

    /// <summary>The four bytes every VPK starts with.</summary>
    private const uint Signature = 0x55AA1234;

    private const int HeaderBytesV1 = 12;
    private const int HeaderBytesV2 = 28;

    /// <summary>Marks the end of a directory entry.</summary>
    private const ushort EntryTerminator = 0xFFFF;

    /// <summary>
    /// The separator inside a VPK path.
    /// </summary>
    /// <remarks>
    /// **Not an OS path separator.** A VPK's internal paths are slash-separated by the format, on
    /// every platform, so using <c>Path.DirectorySeparatorChar</c> here would build keys that never
    /// match on Linux. Named so that is visible rather than looking like an oversight.
    /// </remarks>
    private const char ArchiveSeparator = '/';

    private readonly Dictionary<string, VpkEntry> _entries;
    private readonly string? _directoryPath;
    private readonly long _dataStart;

    private VpkArchive(
        Dictionary<string, VpkEntry> entries, string? directoryPath, long dataStart, int version)
    {
        _entries = entries;
        _directoryPath = directoryPath;
        _dataStart = dataStart;
        Version = version;
    }

    /// <summary>VPK format version, 1 or 2.</summary>
    public int Version { get; }

    /// <summary>How many files the directory lists.</summary>
    public int Count => _entries.Count;

    /// <summary>Every path in the archive.</summary>
    public IEnumerable<string> Paths => _entries.Keys;

    /// <summary>Reads a VPK directory.</summary>
    /// <param name="directory">The <c>_dir.vpk</c> bytes.</param>
    /// <param name="path">Path it came from, used to find the numbered archives.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file is not a VPK, or its tree is malformed.</exception>
    public static VpkArchive Read(ReadOnlyMemory<byte> directory, string? path = null)
    {
        ReadOnlySpan<byte> span = directory.Span;

        if (span.Length < HeaderBytesV1)
        {
            throw new InvalidDataException(
                $"A VPK needs at least {HeaderBytesV1} bytes but this is {span.Length}.");
        }

        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(span);

        if (signature != Signature)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"0x{signature:X8} is not the VPK signature 0x{Signature:X8}."));
        }

        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        if (version is not (1 or 2))
        {
            throw new InvalidDataException($"VPK version {version} is not supported.");
        }

        int treeSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        int headerBytes = version == 1 ? HeaderBytesV1 : HeaderBytesV2;

        if (treeSize < 0 || (long)headerBytes + treeSize > span.Length)
        {
            throw new InvalidDataException(
                $"A VPK tree of {treeSize} bytes does not fit in a {span.Length}-byte directory.");
        }

        Dictionary<string, VpkEntry> entries = ReadTree(
            directory.Slice(headerBytes, treeSize), out _);

        return new VpkArchive(entries, path, headerBytes + treeSize, version);
    }

    /// <summary>Opens a VPK from disk.</summary>
    /// <param name="path">Path to a <c>_dir.vpk</c>.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static VpkArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Read(File.ReadAllBytes(path), path);
    }

    /// <summary>Looks a file up.</summary>
    /// <param name="path">Path inside the archive, such as <c>materials/concrete/x.vmt</c>.</param>
    /// <param name="entry">The entry, when found.</param>
    /// <returns>Whether the archive holds it.</returns>
    /// <remarks>
    /// Case-insensitive and slash-agnostic, because material names come out of a BSP written by
    /// a map compiler on someone else's machine and their case is not dependable.
    /// </remarks>
    public bool TryFind(string path, out VpkEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _entries.TryGetValue(Normalise(path), out entry);
    }

    /// <summary>Reads a file's bytes.</summary>
    /// <param name="path">Path inside the archive.</param>
    /// <returns>The file, or null if the archive does not hold it.</returns>
    /// <exception cref="InvalidDataException">The entry points outside its archive.</exception>
    /// <remarks>
    /// Preload first, then the archived part. A file may be entirely one or the other.
    /// </remarks>
    public byte[]? ReadFile(string path)
    {
        if (!TryFind(path, out VpkEntry entry))
        {
            return null;
        }

        byte[] bytes = new byte[entry.Size];
        entry.Preload.Span.CopyTo(bytes);

        if (entry.Length == 0)
        {
            return bytes;
        }

        string source = ArchivePath(entry.ArchiveIndex);
        long start = entry.ArchiveIndex == InDirectoryFile ? _dataStart + entry.Offset : entry.Offset;

        using FileStream stream = File.OpenRead(source);

        if (start + entry.Length > stream.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{path}' claims {entry.Length} bytes at {start} of a {stream.Length}-byte archive."));
        }

        stream.Seek(start, SeekOrigin.Begin);
        stream.ReadExactly(bytes, entry.Preload.Length, (int)entry.Length);

        return bytes;
    }

    /// <summary>Builds the path of a numbered archive.</summary>
    /// <remarks>
    /// <c>tf2_textures_dir.vpk</c> holds the tree and <c>tf2_textures_014.vpk</c> holds the data,
    /// so the suffix is swapped rather than appended.
    /// </remarks>
    private string ArchivePath(int index)
    {
        if (_directoryPath is null)
        {
            throw new InvalidOperationException(
                "This VPK was read from memory, so its numbered archives cannot be located.");
        }

        if (index == InDirectoryFile)
        {
            return _directoryPath;
        }

        string folder = Path.GetDirectoryName(_directoryPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(_directoryPath);

        if (name.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return Path.Combine(
            folder, string.Create(CultureInfo.InvariantCulture, $"{name}_{index:D3}.vpk"));
    }

    private static Dictionary<string, VpkEntry> ReadTree(
        ReadOnlyMemory<byte> tree, out int consumed)
    {
        Dictionary<string, VpkEntry> entries = new(StringComparer.Ordinal);
        ReadOnlySpan<byte> span = tree.Span;
        int at = 0;

        while (true)
        {
            string extension = ReadString(span, ref at);

            if (extension.Length == 0)
            {
                break;
            }

            while (true)
            {
                string folder = ReadString(span, ref at);

                if (folder.Length == 0)
                {
                    break;
                }

                while (true)
                {
                    string name = ReadString(span, ref at);

                    if (name.Length == 0)
                    {
                        break;
                    }

                    // 18 bytes of fixed entry before any preload data.
                    if (at + 18 > span.Length)
                    {
                        throw new InvalidDataException(
                            $"A VPK entry for '{name}' runs past the end of the tree.");
                    }

                    at += 4; // CRC, not checked: this is a local file the game already trusts.
                    ushort preloadBytes = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
                    ushort archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 2)..]);
                    uint offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 4)..]);
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 8)..]);
                    ushort terminator = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 12)..]);
                    at += 14;

                    if (terminator != EntryTerminator)
                    {
                        throw new InvalidDataException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"A VPK entry for '{name}' ends with 0x{terminator:X4} rather than " +
                            $"0x{EntryTerminator:X4}; the tree is not being read correctly."));
                    }

                    if (at + preloadBytes > span.Length)
                    {
                        throw new InvalidDataException(
                            $"'{name}' claims {preloadBytes} preload bytes past the end of the tree.");
                    }

                    ReadOnlyMemory<byte> preload = tree.Slice(at, preloadBytes);
                    at += preloadBytes;

                    // A folder of " " is the archive root, not a directory named space.
                    string path = folder == " "
                        ? name + "." + extension
                        : folder + ArchiveSeparator + name + "." + extension;

                    entries[Normalise(path)] = new VpkEntry(archiveIndex, offset, length, preload);
                }
            }
        }

        consumed = at;
        return entries;
    }

    private static string ReadString(ReadOnlySpan<byte> span, ref int at)
    {
        int end = at;

        while (end < span.Length && span[end] != 0)
        {
            end++;
        }

        if (end >= span.Length)
        {
            throw new InvalidDataException("A VPK tree ends inside a string.");
        }

        // UTF-8 rather than ASCII: community content carries non-English paths, and an ASCII read
        // turns those into a plausible wrong name rather than failing.
        string value = Encoding.UTF8.GetString(span[at..end]);
        at = end + 1;
        return value;
    }

    private static string Normalise(string path) =>
        path.Replace('\\', '/').Trim('/').ToUpperInvariant();
}
