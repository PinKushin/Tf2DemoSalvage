using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One entry of the game lump directory: a four-character id and where its payload is.</summary>
/// <param name="Id">The id as it appears in the file, big-endian text such as <c>sprp</c>.</param>
/// <param name="Flags">The entry's flags; bit 0 is the compression flag.</param>
/// <param name="Version">The payload's version, which decides its stride.</param>
/// <param name="Offset">Where the payload begins in the file.</param>
/// <param name="StoredLength">The length the directory declares — DECOMPRESSED when compressed.</param>
/// <param name="PackedEnd">Where the bytes actually present end: the next entry's offset.</param>
public readonly record struct BspGameLumpEntry(
    int Id, int Flags, int Version, int Offset, int StoredLength, int PackedEnd)
{
    /// <summary>The id as the four characters it spells.</summary>
    /// <remarks>
    /// **Stored big-endian, which is why it reads backwards from an integer.** `MAKEID('s','p','r','p')`
    /// packs the first character into the high byte, so the id of the static prop lump is
    /// <c>0x73707270</c> and prints as <c>sprp</c> only when read from the top down.
    /// </remarks>
    public string Name =>
        new([(char)((Id >> 24) & 0xFF), (char)((Id >> 16) & 0xFF),
             (char)((Id >> 8) & 0xFF), (char)(Id & 0xFF)]);
}

/// <summary>
/// The game lump directory — lump 35, which holds several sub-lumps of its own.
/// </summary>
/// <remarks>
/// **`dgamelump_t`, `bspfile.h:975`**: an <c>int</c> count followed by that many
/// <c>{ int id; unsigned short flags; unsigned short version; int fileofs; int filelen; }</c>,
/// sixteen bytes each.
///
/// **The stored length is the DECOMPRESSED length when an entry is compressed**, so the bytes
/// actually present run to the NEXT entry's offset rather than for `filelen` bytes. That is the
/// one thing about this directory that cannot be worked out from a single entry, and it is why the
/// whole directory must be read before any payload is.
///
/// **Extracted from <see cref="BspStaticProps"/> so a second reader cannot disagree with it**
/// (B360). The static prop lump is one entry of several: TF2 maps also carry <c>dprp</c>, the
/// detail props, and a reader that walked the directory a second time would be a second chance to
/// get the packed-end rule wrong.
/// </remarks>
public static class BspGameLumps
{
    /// <summary>Bytes per directory entry — <c>dgamelump_t</c>.</summary>
    public const int EntryBytes = 16;

    /// <summary>Every sub-lump the map declares, in directory order.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The entries, which may be empty for a map with no game lumps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="InvalidDataException">The directory does not fit in its own lump.</exception>
    public static IReadOnlyList<BspGameLumpEntry> Directory(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);
        BspLump game = header.Lump(BspLumpIndex.GameLump);

        if (game.Length <= 0)
        {
            return [];
        }

        ReadOnlySpan<byte> directory = file.Slice(game.Offset, game.Length).Span;

        if (directory.Length < sizeof(int))
        {
            throw new InvalidDataException("The game lump is too short to hold its own count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(directory);

        if (count < 0 || sizeof(int) + ((long)count * EntryBytes) > directory.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The game lump declares {count:N0} sub-lumps, which do not fit in its " +
                $"{directory.Length:N0} bytes."));
        }

        List<BspGameLumpEntry> entries = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = directory.Slice(
                sizeof(int) + (index * EntryBytes), EntryBytes);

            entries.Add(new BspGameLumpEntry(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[12..]),
                NextOffset(directory, count, index, file.Length)));
        }

        return entries;
    }

    /// <summary>One entry's bytes, decompressed if it was packed.</summary>
    /// <param name="file">The whole map file.</param>
    /// <param name="entry">The entry, from <see cref="Directory"/>.</param>
    /// <returns>The payload, trimmed to the length the directory declares.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="InvalidDataException">The entry does not lie inside the file.</exception>
    /// <remarks>
    /// Through <see cref="BspLumpData"/> like every other lump, which recognises the LZMA header by
    /// its magic rather than by the flag — one place that knows what a compressed lump looks like.
    /// </remarks>
    public static ReadOnlyMemory<byte> Payload(
        ReadOnlyMemory<byte> file, BspGameLumpEntry entry)
    {
        if (entry.Offset < 0 || entry.PackedEnd <= entry.Offset || entry.PackedEnd > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Game lump '{entry.Name}' lies at {entry.Offset:N0} to {entry.PackedEnd:N0} of a " +
                $"{file.Length:N0}-byte file."));
        }

        ReadOnlyMemory<byte> payload = BspLumpData.Read(
            file, new BspLump(entry.Offset, entry.PackedEnd - entry.Offset, entry.Version));

        return payload.Length > entry.StoredLength && entry.StoredLength > 0
            ? payload[..entry.StoredLength]
            : payload;
    }

    /// <summary>Where the sub-lump after this one begins, or the end of the file.</summary>
    /// <remarks>
    /// **The SMALLEST offset greater than this one, across every entry — not the next entry in
    /// directory order.** The two readings agree on every map measured: `koth_harvest_final`,
    /// `cp_process_f12` and `cp_granary` all list their sub-lumps in ascending offset order. So
    /// this is not carrying a known bug; it is declining to ASSUME an ordering the format does not
    /// promise, which is what <see cref="BspStaticProps"/> did before the walk was shared (B360).
    ///
    /// **It was rewritten as an order-based scan during that move**, and the difference was caught
    /// by reading the code being deleted rather than by any test — neither reading would have
    /// failed on a shipped map. Recorded because the next person to simplify it will reach for the
    /// same shortcut.
    ///
    /// **The last entry runs to the end of the FILE, not of the game lump.** Payloads live outside
    /// the directory's own bounds — the directory holds offsets into the whole map — so clamping to
    /// the game lump's length truncates whichever sub-lump lies last. That entry's apparent packed
    /// length is then the whole tail of the file: `dplh` on granary declares four bytes and appears
    /// to run for 24,769,230. Harmless while nothing reads it, and a trap for whoever first does.
    /// </remarks>
    private static int NextOffset(
        ReadOnlySpan<byte> directory, int count, int index, int fileLength)
    {
        int mine = BinaryPrimitives.ReadInt32LittleEndian(
            directory[(sizeof(int) + (index * EntryBytes) + 8)..]);

        int next = fileLength;

        for (int other = 0; other < count; other++)
        {
            if (other == index)
            {
                continue;
            }

            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                directory[(sizeof(int) + (other * EntryBytes) + 8)..]);

            if (offset > mine && offset < next)
            {
                next = offset;
            }
        }

        return next;
    }
}
