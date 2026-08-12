using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>Where one lump sits in the file.</summary>
/// <param name="Offset">Byte offset from the start of the file.</param>
/// <param name="Length">Length in bytes.</param>
/// <param name="Version">Lump format version.</param>
public readonly record struct BspLump(int Offset, int Length, int Version);

/// <summary>
/// A BSP file's header: the identifier, the version, and the directory of 64 lumps.
/// </summary>
/// <remarks>
/// **Every lump is validated against the file before this constructor returns**, because a BSP is
/// untrusted input. Maps arrive from fastdl — supplied by whoever runs the server, reviewed by
/// nobody — and the Source engine has had map-driven remote-code-execution research published
/// against it. See <c>DECISIONS.md</c> D32 for the full rules.
///
/// The directory is 64 pairs of offset and length pointing anywhere in the file, which is exactly
/// the shape of every allocate-before-validate defect this project has already fixed on the demo
/// side: a declared length believed, then used to read or size a buffer. Validating here means
/// every later reader can treat a lump's bounds as a fact rather than a claim.
/// </remarks>
public sealed class BspHeader
{
    /// <summary>Lumps in a Source BSP directory.</summary>
    public const int LumpCount = 64;

    /// <summary>Bytes of header: ident, version, the directory, and the map revision.</summary>
    public const int SizeBytes = 8 + (LumpCount * 16) + 4;

    /// <summary>The four bytes every Source BSP starts with.</summary>
    private const string Ident = "VBSP";

    private readonly BspLump[] _lumps;

    private BspHeader(int version, int mapRevision, BspLump[] lumps)
    {
        Version = version;
        MapRevision = mapRevision;
        _lumps = lumps;
    }

    /// <summary>BSP format version. TF2 ships 20 and 21.</summary>
    public int Version { get; }

    /// <summary>The map's revision number, as the compiler stamped it.</summary>
    public int MapRevision { get; }

    /// <summary>Reads and validates a header.</summary>
    /// <param name="file">The whole file, or at least enough of it to validate against.</param>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidDataException">
    /// Not a BSP, too short, or a lump that does not fit inside the file.
    /// </exception>
    public static BspHeader Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < SizeBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A BSP header is {SizeBytes} bytes and this file is {file.Length}."));
        }

        string ident = Encoding.ASCII.GetString(file[..4]);

        if (!string.Equals(ident, Ident, StringComparison.Ordinal))
        {
            // Named rather than reported as a generic parse failure: the likely causes are a
            // download that returned an error page, or a file that is simply not a map, and both
            // are worth telling apart from a corrupt BSP.
            throw new InvalidDataException(
                $"Expected a BSP starting with '{Ident}' but found '{Sanitise(ident)}'.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(file[4..]);
        BspLump[] lumps = new BspLump[LumpCount];

        for (int index = 0; index < LumpCount; index++)
        {
            int at = 8 + (index * 16);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(file[at..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]);
            int lumpVersion = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 8)..]);

            Validate(index, offset, length, file.Length);
            lumps[index] = new BspLump(offset, length, lumpVersion);
        }

        int revision = BinaryPrimitives.ReadInt32LittleEndian(file[(8 + (LumpCount * 16))..]);
        return new BspHeader(version, revision, lumps);
    }

    /// <summary>Where a lump sits.</summary>
    /// <param name="index">Lump index, 0 to 63.</param>
    /// <returns>The lump's bounds, already validated against the file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the directory.</exception>
    public BspLump Lump(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, LumpCount);

        return _lumps[index];
    }

    private static void Validate(int index, int offset, int length, int fileLength)
    {
        // An unused lump is zero and zero. Most maps use nothing like all 64, so rejecting that
        // would reject every real map.
        if (offset == 0 && length == 0)
        {
            return;
        }

        if (offset < 0 || length < 0)
        {
            // A 32-bit field read as a signed int arrives negative above int.MaxValue, and a
            // negative length slips past a "too large" check - the shape of the Snappy literal
            // defect the fuzzer found on the demo side.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Lump {index} declares offset {offset} and length {length}; neither can be " +
                $"negative."));
        }

        if (offset < SizeBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Lump {index} starts at {offset}, inside the {SizeBytes}-byte header."));
        }

        // `long`, because offset + length overflows int for large values and an overflowed sum
        // comes out NEGATIVE - which passes a naive "fits in the file" test and lets exactly the
        // largest, most damaging values through.
        if ((long)offset + length > fileLength)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Lump {index} claims bytes {offset} to {(long)offset + length} of a " +
                $"{fileLength}-byte file."));
        }
    }

    /// <summary>Makes an unexpected ident printable, since it is attacker-supplied.</summary>
    private static string Sanitise(string ident)
    {
        StringBuilder safe = new(ident.Length);

        foreach (char character in ident)
        {
            safe.Append(char.IsControl(character) ? '?' : character);
        }

        return safe.ToString();
    }
}
