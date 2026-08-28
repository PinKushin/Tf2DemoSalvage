using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// A map's checksum, as the engine computes it — <c>CRC_MapFile</c>.
/// </summary>
/// <remarks>
/// **This is the number a server sends and a client compares.** `svc_ServerInfo` carries `mapCRC`,
/// and its whole purpose is to let a client tell whether the `.bsp` it has is the one the recording
/// was made against. This project has decoded it since the container work and never compared it to
/// anything.
///
/// **The cost of not comparing it.** A 2017 badlands demo rendered against the 2026
/// `cp_badlands.bsp` produced roller doors drawing as grey rock, players appearing from nowhere, and
/// doors flickering — three defects investigated as regressions, two of which were real bugs in
/// unrelated work and one of which was never code at all. Nothing could separate them except the
/// owner's memory of a different map. See D113 and `docs/findings/41`.
///
/// **The algorithm is published in full** (`utils/common/bsplib.cpp:3774`):
///
/// <code>
/// // CRC across all lumps except for the Entities lump
/// for ( int l = 0; l &lt; HEADER_LUMPS; ++l )
/// {
///     if (l == LUMP_ENTITIES) continue;
///     curLump = &amp;g_pBSPHeader->lumps[l];
///     ... seek to curLump->fileofs, read curLump->filelen bytes, CRC32_ProcessBuffer ...
/// }
/// </code>
///
/// **Three properties of it decide whether a reimplementation agrees**, and all three are asserted
/// by `BspMapCrcConformanceTests`:
///
/// * **The entity lump is excluded**, deliberately, so a server that edits its entity list still
///   matches its clients. A checksum over the whole file would report a mismatch for most
///   competitive servers.
/// * **Lumps are read by their own `fileofs`/`filelen`, in HEADER ORDER** — not sequentially
///   through the file. Lumps are not stored in index order, so a start-to-end read hashes a
///   different byte sequence.
/// * **The bytes are raw.** A Source BSP may hold LZMA-compressed lumps and `CRC_MapFile` neither
///   knows nor cares; decompressing first would disagree with every real client.
/// </remarks>
public static class BspMapChecksum
{
    /// <summary>Both checksums of a map, from one pass over its lumps.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The CRC32 and the MD5, over the same bytes.</returns>
    /// <exception cref="InvalidDataException">The header will not parse.</exception>
    /// <remarks>
    /// **Both, because the demo decides which one can be compared and the map cannot know in
    /// advance.** A CRC32 cannot be converted into an MD5 or the reverse — they are different
    /// functions of the same bytes, and that is the point of a hash — so a uniform comparison across
    /// eras means computing both here and letting the caller ask about whichever the demo carries.
    ///
    /// **That is not a divergence from Valve.** Each era's number is still Valve's own, computed
    /// Valve's way over Valve's lump selection; only the choice of which to compare is ours, and it
    /// is forced by the demo rather than chosen.
    ///
    /// **One walk feeds both**, so the cost is one read of the map rather than two, and neither
    /// accumulator can disagree with the other about which bytes it saw.
    /// </remarks>
    public static (uint Crc, byte[] Md5) OfMap(ReadOnlyMemory<byte> file)
    {
        Crc32 crc = new();

#pragma warning disable CA5351, S4790
        using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
#pragma warning restore CA5351, S4790

        foreach (Range lump in HashedLumps(file))
        {
            ReadOnlySpan<byte> bytes = file.Span[lump];

            crc.Append(bytes);

            byte[] chunk = bytes.ToArray();

            md5.TransformBlock(chunk, 0, chunk.Length, null, 0);
        }

        md5.TransformFinalBlock([], 0, 0);

        return (
            BinaryPrimitives.ReadUInt32LittleEndian(crc.GetCurrentHash()),
            md5.Hash ?? []);
    }

    /// <summary>The byte ranges both checksums cover, in the order they cover them.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>One range per hashed lump.</returns>
    /// <exception cref="InvalidDataException">The header will not parse.</exception>
    /// <remarks>
    /// **One walk, two accumulators, and the duplication it replaces was a real weakness in the
    /// evidence.** The CRC and the MD5 had a copy of this loop each. The MD5 is verified end to end
    /// against `cp_process_f12` and its demo — but that only proved the MD5's copy, so a divergence
    /// in the CRC's would have been invisible. Sharing the walk means the MD5's match is evidence
    /// for both.
    ///
    /// **A lump whose range falls outside the file is skipped rather than throwing.** A truncated or
    /// padded `.bsp` is a broken map, and answering a checksum that will simply fail to match is
    /// more useful than refusing to open it — the caller's response to both is the same warning.
    /// </remarks>
    private static IEnumerable<Range> HashedLumps(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        for (int lump = 0; lump < BspHeader.LumpCount; lump++)
        {
            if (lump == EntitiesLump)
            {
                continue;
            }

            BspLump at = header.Lump(lump);

            if (at.Offset < 0 || at.Length <= 0 || (long)at.Offset + at.Length > file.Length)
            {
                continue;
            }

            yield return new Range(at.Offset, at.Offset + at.Length);
        }
    }

    /// <summary>Valve's <c>LUMP_ENTITIES</c>, the one lump left out of the checksum.</summary>
    /// <remarks>
    /// Named rather than written as a bare zero at the comparison, because "skip lump 0" reads like
    /// an off-by-one guard and this is a deliberate exclusion with a reason.
    /// </remarks>
    private const int EntitiesLump = 0;

    /// <summary>The CRC-32 of a buffer, in the variant the engine uses.</summary>
    /// <param name="bytes">The bytes to hash.</param>
    /// <returns>The checksum.</returns>
    /// <remarks>
    /// **Standard reflected CRC-32, and that is established rather than assumed.**
    /// `checksum_crc.cpp` initialises and finalises with `0xFFFFFFFF`, steps with
    /// `table[b ^ (byte)crc] ^ (crc >> 8)`, and its table's second entry is `0x77073096` — the
    /// reflected polynomial `0xEDB88320`. That is CRC-32/ISO-HDLC, which
    /// <see cref="System.IO.Hashing.Crc32"/> computes exactly.
    ///
    /// **`Crc32.Hash` returns the digest LITTLE-ENDIAN**, so it is read back as such. Reading it
    /// the other way byte-swaps every value and makes every map look mismatched — a failure that
    /// would look like the check working.
    /// </remarks>
    public static uint Crc32Of(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Crc32.Hash(bytes));

}
