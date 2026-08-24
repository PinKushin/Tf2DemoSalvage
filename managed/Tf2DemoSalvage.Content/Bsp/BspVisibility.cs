using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// The map's potentially visible set: which clusters can be seen from which.
/// </summary>
/// <remarks>
/// **What a cluster is, and why it is not a leaf.** `vvis` groups leaves into clusters and computes,
/// for each, a bit per cluster saying whether it could possibly be seen from there. A leaf carries
/// its cluster in <c>dleaf_t.cluster</c>; solid leaves carry −1 and belong to none.
///
/// **The lump is `dvis_t` (`bspfile.h:904`)**: an <c>int numclusters</c> followed by
/// <c>int bitofs[numclusters][2]</c>, where index 0 is the PVS and index 1 the PAS
/// (<c>DVIS_PVS</c> is 0). Each offset is a byte offset from the START of the lump to that
/// cluster's run-length-encoded row.
///
/// **The encoding is Valve's and both halves of it ship with the SDK** — `CompressVis` and
/// `DecompressVis` in `utils/common/bsplib.cpp`, because `vvis` writes the lump. A row is
/// <c>(numclusters + 7) &gt;&gt; 3</c> bytes. Non-zero bytes are literal; a zero byte is followed by
/// a count of how many zero bytes it stands for, counting itself, capped at 255 by the compressor.
///
/// **Read for soundscape selection (B177)**, where the engine considers only the soundscapes in the
/// listener's own cluster — `m_soundscapesInCluster` in `CSoundscapeSystem::LevelInitPostEntity`.
/// Without it every soundscape on the map contends and a placement on the far side can win on a
/// long clear traceline.
///
/// **Every length here is a stranger's.** The lump comes from a map file, and a map is fetched over
/// HTTP from whatever mirror a server points at (D32) — so a malformed offset, a row that never
/// fills, or a zero repeat count must be refused rather than trusted. Valve's own decompressor
/// clamps an overrun and calls <c>Error</c> on a zero repeat; this does the same, as an exception
/// the caller can report.
/// </remarks>
public sealed class BspVisibility
{
    /// <summary>Nothing is visible from anywhere, for a map compiled without vis.</summary>
    public static readonly BspVisibility None = new(ReadOnlyMemory<byte>.Empty, 0);

    private readonly ReadOnlyMemory<byte> _lump;
    private readonly int _clusters;
    private readonly int _row;

    /// <summary>Decompressed rows, kept because a row is asked for repeatedly and costs a walk.</summary>
    /// <remarks>
    /// **One row per cluster the caller actually asks about**, rather than the whole lump up front.
    /// A soundscape pass asks for the listener's cluster and no other, so on a map with thousands
    /// of clusters this decompresses one row of a few hundred bytes rather than all of them.
    /// </remarks>
    private readonly Dictionary<int, byte[]> _rows = [];

    private BspVisibility(ReadOnlyMemory<byte> lump, int clusters)
    {
        _lump = lump;
        _clusters = clusters;
        _row = (clusters + 7) >> 3;
    }

    /// <summary>How many clusters the map has; zero when it carries no visibility data.</summary>
    public int ClusterCount => _clusters;

    /// <summary>Whether the map was compiled with vis at all.</summary>
    /// <remarks>
    /// **Separate from "nothing is visible", and the distinction is load-bearing.** A map with no
    /// visibility data would report every query false, which for a caller that filters by visibility
    /// means filtering everything away — silence, on a map that should simply skip the filter. The
    /// caller has to be able to tell the two apart, so this says which case it is rather than
    /// leaving it to be inferred from a count.
    /// </remarks>
    public bool HasData => _clusters > 0;

    /// <summary>Reads the visibility lump out of a whole map file.</summary>
    /// <param name="file">The map.</param>
    /// <returns>The visibility data, or <see cref="None"/> when the map has none.</returns>
    /// <exception cref="InvalidDataException">The header or the lump is malformed.</exception>
    public static BspVisibility Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return FromLump(BspLumpData.Read(file, header.Lump(BspLumpIndex.Visibility)));
    }

    /// <summary>Reads an already-extracted visibility lump.</summary>
    /// <param name="lump">The lump's bytes.</param>
    /// <returns>The visibility data, or <see cref="None"/> when the lump is empty.</returns>
    /// <exception cref="InvalidDataException">The lump declares more clusters than it can hold.</exception>
    public static BspVisibility FromLump(ReadOnlyMemory<byte> lump)
    {
        if (lump.Length < 4)
        {
            // An empty lump is an ordinary map compiled without vis, not a defect.
            return None;
        }

        int clusters = BinaryPrimitives.ReadInt32LittleEndian(lump.Span);

        if (clusters <= 0)
        {
            return None;
        }

        // **The offset table has to fit before anything else is believed.** `numclusters` is four
        // bytes of a downloaded file, so a value of 2^30 would otherwise be used to index far past
        // the end — checked in long arithmetic because the multiplication itself overflows.
        long table = 4L + ((long)clusters * 8L);

        if (table > lump.Length)
        {
            throw new InvalidDataException(
                $"the visibility lump declares {clusters.ToString(CultureInfo.InvariantCulture)} " +
                $"clusters, whose offset table needs {table.ToString(CultureInfo.InvariantCulture)} " +
                $"bytes of a {lump.Length.ToString(CultureInfo.InvariantCulture)}-byte lump");
        }

        return new BspVisibility(lump, clusters);
    }

    /// <summary>Whether one cluster can potentially see another.</summary>
    /// <param name="from">The cluster being looked out of.</param>
    /// <param name="to">The cluster being looked for.</param>
    /// <returns><c>true</c> when <paramref name="to"/> is in <paramref name="from"/>'s PVS.</returns>
    /// <exception cref="InvalidDataException">The row for <paramref name="from"/> is malformed.</exception>
    /// <remarks>
    /// **A cluster outside the map is an ordinary answer, not an error.** `GetClusterForOrigin`
    /// returns −1 for a point in solid space, and the viewer's free camera is there routinely — so
    /// an out-of-range cluster answers false rather than throwing in the middle of an audio pass.
    ///
    /// **The bit arithmetic is Valve's**: <c>pvs[j &gt;&gt; 3] &amp; (1 &lt;&lt; (j &amp; 7))</c>,
    /// as `LevelInitPostEntity` reads it.
    /// </remarks>
    public bool Visible(int from, int to)
    {
        if (from < 0 || from >= _clusters || to < 0 || to >= _clusters)
        {
            return false;
        }

        byte[] row = RowFor(from);

        return (row[to >> 3] & (1 << (to & 7))) != 0;
    }

    /// <summary>Decompresses one cluster's row, or answers a cached copy.</summary>
    private byte[] RowFor(int cluster)
    {
        if (_rows.TryGetValue(cluster, out byte[]? cached))
        {
            return cached;
        }

        byte[] row = Decompress(cluster);

        _rows[cluster] = row;

        return row;
    }

    /// <summary>Valve's <c>DecompressVis</c>, with its clamp and its refusal.</summary>
    /// <remarks>
    /// Transcribed from `utils/common/bsplib.cpp`:
    /// <code>
    /// do {
    ///     if (*in) { *out++ = *in++; continue; }
    ///     c = in[1];
    ///     if (!c) Error("DecompressVis: 0 repeat");
    ///     in += 2;
    ///     if ((out - decompressed) + c > row) { c = row - (out - decompressed); Warning(...); }
    ///     while (c) { *out++ = 0; c--; }
    /// } while (out - decompressed &lt; row);
    /// </code>
    /// The two guards are the interesting part and both are about malformed input: the clamp stops a
    /// run writing past the row, and the zero-repeat refusal stops a loop that would otherwise make
    /// no progress and walk off the end of the lump.
    /// </remarks>
    private byte[] Decompress(int cluster)
    {
        ReadOnlySpan<byte> lump = _lump.Span;

        int offset = BinaryPrimitives.ReadInt32LittleEndian(lump[(4 + (cluster * 8))..]);

        byte[] row = new byte[_row];

        if (offset < 0 || offset >= lump.Length)
        {
            throw new InvalidDataException(
                $"cluster {cluster.ToString(CultureInfo.InvariantCulture)}'s visibility row starts " +
                $"at {offset.ToString(CultureInfo.InvariantCulture)}, outside the lump");
        }

        int at = offset;
        int written = 0;

        while (written < _row)
        {
            if (at >= lump.Length)
            {
                throw new InvalidDataException(
                    $"cluster {cluster.ToString(CultureInfo.InvariantCulture)}'s visibility row " +
                    "ends before it is full");
            }

            if (lump[at] != 0)
            {
                row[written++] = lump[at++];
                continue;
            }

            if (at + 1 >= lump.Length)
            {
                throw new InvalidDataException(
                    $"cluster {cluster.ToString(CultureInfo.InvariantCulture)}'s visibility row " +
                    "ends on a zero with no repeat count");
            }

            int repeat = lump[at + 1];

            if (repeat == 0)
            {
                // Valve's `Error("DecompressVis: 0 repeat")`. Treating it as "emit nothing" would
                // leave the row unfilled and the cursor advancing two bytes at a time through
                // whatever follows.
                throw new InvalidDataException(
                    $"cluster {cluster.ToString(CultureInfo.InvariantCulture)}'s visibility row " +
                    "has a zero repeat count");
            }

            at += 2;

            // Valve clamps rather than overrunning, and warns. A malformed map is the only way to
            // reach this, so it is silently correct here rather than noisy.
            if (written + repeat > _row)
            {
                repeat = _row - written;
            }

            while (repeat > 0)
            {
                row[written++] = 0;
                repeat--;
            }
        }

        return row;
    }
}
