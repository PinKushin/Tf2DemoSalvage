using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The visibility lump's run-length encoding, against Valve's own compressor and decompressor.
/// </summary>
/// <remarks>
/// **Both halves are published, in `utils/common/bsplib.cpp`.** `CompressVis` and `DecompressVis`
/// ship with the SDK because `vvis` writes the lump, so this is not a reading of a format — it is a
/// transcription of the code that produces it.
///
/// That makes a round trip possible, which matters here more than usual: a hand-built fixture can
/// only prove the decoder agrees with whatever the fixture's author believed
/// (`docs/memory/fixtures-are-the-weak-point.md`). <see cref="Compress"/> below is Valve's
/// `CompressVis` transcribed, so a row that survives compress-then-decompress agrees with the
/// ENGINE's encoding rather than with this test's idea of it.
///
/// **Why this exists at all: B177.** Soundscape selection has to consider only the soundscapes in
/// the listener's visibility cluster, the way `CSoundscapeSystem::LevelInitPostEntity` does. Nothing
/// in this project read the visibility lump before — `BspLumpIndex.Visibility` was defined and
/// unused.
/// </remarks>
public sealed class BspVisibilityConformanceTests
{
    /// <summary>Valve's <c>CompressVis</c>, transcribed so a round trip means something.</summary>
    /// <remarks>
    /// `bsplib.cpp`: each byte is emitted; a ZERO byte is followed by a repeat count of how many
    /// zero bytes follow it in total, capped at 255. Note that the zero byte itself is counted, so
    /// the pair <c>00 01</c> is one zero byte and not two.
    /// </remarks>
    private static byte[] Compress(byte[] row)
    {
        // A while loop rather than Valve's `for`, which advances its own index inside the body and
        // then steps back — legal C and an analyzer error here (S127). The behaviour is identical;
        // only the bookkeeping moved.
        List<byte> destination = [];
        int at = 0;

        while (at < row.Length)
        {
            destination.Add(row[at]);

            if (row[at] != 0)
            {
                at++;
                continue;
            }

            int repeat = 1;

            at++;

            while (at < row.Length && row[at] == 0 && repeat < 255)
            {
                repeat++;
                at++;
            }

            destination.Add((byte)repeat);
        }

        return [.. destination];
    }

    /// <summary>A lump holding one cluster's PVS, laid out as <c>dvis_t</c> requires.</summary>
    /// <remarks>
    /// `bspfile.h:904` — <c>int numclusters</c> then <c>int bitofs[numclusters][2]</c>, PVS at
    /// index 0 and PAS at index 1 (<c>DVIS_PVS</c> is 0). The offsets are from the START of the
    /// lump, which is the detail a reader gets wrong first and which produces a plausible wrong
    /// answer rather than a failure.
    /// </remarks>
    private static byte[] Lump(int clusters, params byte[][] rows)
    {
        List<byte> lump = [];
        int table = 4 + (clusters * 8);

        lump.AddRange(BitConverter.GetBytes(clusters));

        List<byte> data = [];
        List<int> offsets = [];

        foreach (byte[] row in rows)
        {
            offsets.Add(table + data.Count);
            data.AddRange(Compress(row));
        }

        // **Every cluster needs an offset, including the ones these tests do not supply a row for.**
        // A map always writes numclusters rows; the tests care about one or two, so the rest point
        // at the last supplied row rather than at nothing. Getting this wrong throws inside the
        // helper and looks exactly like a reader bug — it did once.
        for (int cluster = 0; cluster < clusters; cluster++)
        {
            int offset = offsets[Math.Min(cluster, offsets.Count - 1)];

            lump.AddRange(BitConverter.GetBytes(offset));
            lump.AddRange(BitConverter.GetBytes(offset));
        }

        lump.AddRange(data);

        return [.. lump];
    }

    [Test]
    public void Visible_AClusterSeeingOnlyItself_ReportsExactlyThat()
    {
        // Eight clusters is one byte of row, so the bit arithmetic and the row length are both
        // exercised without the two being able to mask each other.
        BspVisibility visibility = BspVisibility.FromLump(Lump(8, [0b0000_0001]));

        visibility.ClusterCount.ShouldBe(8);
        visibility.Visible(0, 0).ShouldBeTrue("cluster 0's own bit is set");

        for (int cluster = 1; cluster < 8; cluster++)
        {
            visibility.Visible(0, cluster).ShouldBeFalse($"cluster {cluster} is not in the row");
        }
    }

    [Test]
    public void Visible_ABitInALaterByte_UsesValvesIndexing()
    {
        // **`pvs[j >> 3] & (1 << (j & 7))`**, which is how `LevelInitPostEntity` reads it. Cluster
        // 11 is bit 3 of byte 1 — a value that distinguishes this from every plausible alternative,
        // where cluster 8 (bit 0 of byte 1) would not: it survives swapping the shift and the mask.
        BspVisibility visibility = BspVisibility.FromLump(Lump(16, [0b0000_0000, 0b0000_1000]));

        visibility.Visible(0, 11).ShouldBeTrue("cluster 11 is bit 3 of byte 1");
        visibility.Visible(0, 8).ShouldBeFalse();
        visibility.Visible(0, 3).ShouldBeFalse("the same bit in byte 0 must not be confused with it");
        visibility.Visible(0, 12).ShouldBeFalse();
    }

    [Test]
    public void Visible_ARowOfEveryPattern_SurvivesValvesOwnCompressor()
    {
        // **The round trip, and the reason this suite is not a pile of fixtures.** Every byte value
        // appears, so every branch of the encoding is taken: literals, a single zero, and runs of
        // zeros of assorted lengths.
        byte[] row = new byte[64];

        for (int at = 0; at < row.Length; at++)
        {
            // A deliberately lumpy pattern: isolated zeros, short runs, and long runs, rather than
            // an even split that a decoder could get right by accident.
            bool zero = at % 7 == 0 || at % 11 < 4;

            row[at] = zero ? (byte)0 : (byte)at;
        }

        BspVisibility visibility = BspVisibility.FromLump(Lump(row.Length * 8, [row]));

        for (int cluster = 0; cluster < row.Length * 8; cluster++)
        {
            bool expected = (row[cluster >> 3] & (1 << (cluster & 7))) != 0;

            visibility.Visible(0, cluster)
                .ShouldBe(expected, $"cluster {cluster} after a compress/decompress round trip");
        }
    }

    [Test]
    public void Visible_ARunLongerThanTheRow_IsClampedRatherThanOverrunning()
    {
        // Valve clamps and warns rather than writing past the row:
        //   if ((out - decompressed) + c > row) { c = row - (out - decompressed); Warning(...); }
        // A reader without the clamp corrupts whatever follows the buffer, which on a malformed map
        // is attacker-controlled (D32) — so this is a hostile-input case, not a tidiness one.
        // Built by hand rather than through Lump(), because the point is a run the COMPRESSOR would
        // never emit: 255 zero bytes for a row that holds one. Eight clusters is a one-byte row, and
        // the offset table is 4 + 8*8 = 68 bytes, so the data starts there.
        List<byte> lump = [.. BitConverter.GetBytes(8)];

        for (int cluster = 0; cluster < 8; cluster++)
        {
            lump.AddRange(BitConverter.GetBytes(68));
            lump.AddRange(BitConverter.GetBytes(68));
        }

        lump.AddRange([0x00, 0xff]);

        BspVisibility visibility = BspVisibility.FromLump(lump.ToArray());

        visibility.Visible(0, 0).ShouldBeFalse("a run of zeros means nothing is visible");
        visibility.Visible(0, 7).ShouldBeFalse();
    }

    [Test]
    public void FromLump_AZeroRepeatCount_IsRefusedRatherThanLoopingForever()
    {
        // **Valve calls `Error("DecompressVis: 0 repeat")` and stops.** A reader that instead
        // treats it as "emit nothing" makes no progress on that iteration and reads the next two
        // bytes, and on a row that never fills, walks off the end of the lump — a hang or a crash
        // on a malformed map rather than a rejection.
        // **The offset table has to be complete or this never reaches the decompressor.** An
        // earlier version of this test wrote one offset pair for eight clusters, so `FromLump`
        // rejected it on SIZE and the test passed green while measuring nothing about repeat
        // counts. The exception type was right and the reason was wrong, which is the failure a
        // `Should.Throw` invites.
        List<byte> lump = [.. BitConverter.GetBytes(8)];

        for (int cluster = 0; cluster < 8; cluster++)
        {
            lump.AddRange(BitConverter.GetBytes(68));
            lump.AddRange(BitConverter.GetBytes(68));
        }

        lump.AddRange([0x00, 0x00]);

        BspVisibility visibility = BspVisibility.FromLump(lump.ToArray());

        // Constructing it must succeed — the lump is structurally fine and only the ROW is corrupt,
        // which is not discovered until that row is decompressed.
        visibility.ClusterCount.ShouldBe(8);

        Should.Throw<InvalidDataException>(() => visibility.Visible(0, 0))
            .Message.ShouldContain("repeat", Case.Insensitive);
    }

    [Test]
    public void FromLump_AnEmptyLump_HasNoClustersAndSeesNothing()
    {
        // A map compiled without vis has an empty lump, and it is not an error. Answering FALSE
        // everywhere would hide every soundscape on such a map; the caller has to be able to tell
        // "no visibility data" from "nothing is visible", so this reports zero clusters and the
        // caller decides.
        BspVisibility visibility = BspVisibility.FromLump(ReadOnlyMemory<byte>.Empty);

        visibility.ClusterCount.ShouldBe(0);
        visibility.HasData.ShouldBeFalse();
    }

    [Test]
    public void Visible_AClusterOutsideTheMap_IsFalseRatherThanThrowing()
    {
        // −1 is what `GetClusterForOrigin` returns for a point in solid space, and the viewer's
        // free camera goes there constantly. It must be an ordinary answer, not an exception in the
        // middle of an audio pass.
        BspVisibility visibility = BspVisibility.FromLump(Lump(8, [0b1111_1111]));

        visibility.Visible(-1, 0).ShouldBeFalse();
        visibility.Visible(0, -1).ShouldBeFalse();
        visibility.Visible(0, 999).ShouldBeFalse();
        visibility.Visible(999, 0).ShouldBeFalse();
    }
}
