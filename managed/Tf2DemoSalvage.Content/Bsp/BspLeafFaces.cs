using System;
using System.Buffers.Binary;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// The LEAFFACES lump: which faces each leaf touches, as one flat array of face indices.
/// </summary>
/// <remarks>
/// **The indirection that makes leaf-based visibility possible.** A `dleaf_t` carries
/// `firstleafface` and `numleaffaces`, which are a range into THIS array; each entry is an index
/// into the FACES lump. Without it a leaf knows where it is and nothing about what is drawn there.
///
/// **A face appears once per leaf that touches it, so entries repeat.** A wall spanning a doorway
/// is listed by the leaves on both sides. That is deliberate — visibility is per leaf and a surface
/// straddling two of them is visible from either — and it is why a gather over visible leaves must
/// stamp each face as it takes it. Valve does exactly that in `R_BuildWorldLists` rather than
/// storing a face once and asking which leaves see it.
///
/// **`unsigned short` per entry**, so a map may hold up to 65,535 faces addressed this way. Reading
/// them signed silently turns the upper half of a large map's faces into negative indices, which
/// then either throw or index backwards depending on the caller — the sort of fault that appears
/// only on big maps and reads as corruption.
/// </remarks>
public sealed class BspLeafFaces
{
    private readonly ReadOnlyMemory<byte> _lump;

    /// <summary>A map with no leaf-face lump, which reports no faces anywhere.</summary>
    /// <remarks>
    /// **Empty rather than null**, so a caller need not decide what a missing lump means every time
    /// it asks. A map without this lump cannot be culled by leaf, and the caller that notices is the
    /// one that should say so — not every read site.
    /// </remarks>
    public static readonly BspLeafFaces None = new(ReadOnlyMemory<byte>.Empty);

    private BspLeafFaces(ReadOnlyMemory<byte> lump) => _lump = lump;

    /// <summary>How many entries the lump holds.</summary>
    public int Count => _lump.Length / sizeof(ushort);

    /// <summary>Whether the map carried the lump at all.</summary>
    public bool HasData => Count > 0;

    /// <summary>Reads the lump from a whole map file.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The lump, or <see cref="None"/> when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or the lump is malformed.</exception>
    public static BspLeafFaces Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return FromLump(BspLumpData.Read(file, header.Lump(BspLumpIndex.LeafFaces)));
    }

    /// <summary>Wraps a lump already in hand.</summary>
    /// <param name="lump">The decompressed LEAFFACES bytes.</param>
    /// <returns>The reader.</returns>
    /// <remarks>
    /// **An odd trailing byte is kept rather than refused.** The count divides, so a lump one byte
    /// long simply holds no entries; refusing would turn a harmless padding difference into a map
    /// that will not open. Every read is bounds-checked in any case.
    /// </remarks>
    public static BspLeafFaces FromLump(ReadOnlyMemory<byte> lump) => new(lump);

    /// <summary>The face index at one position in the flat array.</summary>
    /// <param name="index">A position, as a leaf's <c>firstleafface</c> plus an offset.</param>
    /// <returns>The face index, or −1 when the position is outside the lump.</returns>
    /// <remarks>
    /// **−1 for out of range rather than an exception**, because the range comes from a leaf and a
    /// leaf's range comes from the file. A truncated or mismatched lump is a broken map, and a
    /// viewer that draws the rest of it is better than one that refuses to open it — the caller
    /// skipping a −1 loses one surface where a throw loses the map.
    /// </remarks>
    public int Face(int index)
    {
        if (index < 0)
        {
            return -1;
        }

        int at = index * sizeof(ushort);

        return at + sizeof(ushort) <= _lump.Length
            ? BinaryPrimitives.ReadUInt16LittleEndian(_lump.Span[at..])
            : -1;
    }
}
