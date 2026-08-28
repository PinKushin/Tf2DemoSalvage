using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Which byte selection produces the checksum a 2007 server actually sent.
/// </summary>
/// <remarks>
/// **A search, run because the answer is known and the input is known.** The 2007 granary demo
/// carries `0x534EEB7C`, and the owner recorded it on the client whose `cp_granary.bsp` sits in
/// `F:\tf2-builds\tf2-2007` — so the map is not in question and the selection is.
///
/// **This replaced a wrong conclusion.** The failure was first written up as the archived client's
/// map having been repacked, on the reasoning that the lump walk is shared with the MD5 path and the
/// MD5 matches `cp_process_f12` exactly. The owner corrected it: *"if the demo you are using to
/// compare is the era specimine in the gcor, i recorded it with that client so it has to be the same
/// map"*. Both halves of that reasoning were sound and the conclusion was still wrong — the MD5 match
/// proves the MODERN selection, and says nothing about what a 2007 engine did.
///
/// **`CRC_MapFile` may not be the right citation at all.** Its only caller in the published tree is
/// `SwapBSPFile`, the Xbox 360 conversion tool — not the server. The engine's own map checksum is
/// closed, so this searches rather than reads.
///
/// Explicit: it reports which candidate matched, if any.
/// </remarks>
[Explicit("Diagnostic: searches byte selections for the one a 2007 server checksummed.")]
public sealed class MapCrcVariantSearchDiagnostic
{
    private const string Map =
        @"F:\tf2-builds\tf2-2007\Team Fortress 2\tf\maps\cp_granary.bsp";

    [Test]
    public void SearchForTheSelectionThatMatches()
    {
        if (!File.Exists(Map))
        {
            Assert.Ignore("the 2007 build is not on this machine.");
            return;
        }

        byte[] file = File.ReadAllBytes(Map);

        DemoTimeline demo = TimelineCache.For(Corpus.Demo("tf2-2007-build3258-pov-cp_granary"));

        uint wanted = demo.MapCrc.ShouldNotBeNull("the 2007 demo carries a checksum");

        // **The OTHER four bytes, because it might be the real one.** On old protocols
        // `svc_ServerInfo` carries both a 32-bit field this project calls `mapCrc` AND a four-byte
        // one it calls `mapHash`, and the tick interval decoding as 0.015 proves both are read at
        // the right offsets. Which of the two the engine compares against a map is an assumption
        // that has never been checked — so search for both.
        uint alternate = 0;

        if (demo.MapHash is { Count: 4 } hash)
        {
            alternate = ((uint)hash[3] << 24) | ((uint)hash[2] << 16) |
                        ((uint)hash[1] << 8) | hash[0];

            Console.WriteLine($"the other four bytes, little-endian: {alternate:X8}");
        }

        Console.WriteLine($"looking for {wanted:X8} or {alternate:X8} in {Path.GetFileName(Map)}");
        Console.WriteLine();

        BspHeader header = BspHeader.Parse(file);

        Console.WriteLine($"bsp version {header.Version}, revision {header.MapRevision}");

        Console.WriteLine($"file is {file.Length} bytes");

        for (int lump = 0; lump < 12; lump++)
        {
            BspLump at = header.Lump(lump);

            Console.WriteLine(
                $"  lump {lump,2}  offset {at.Offset,10}  length {at.Length,10}  v{at.Version}");
        }

        Console.WriteLine();

        foreach ((string name, Func<Crc32, byte[], BspHeader, bool> feed) in Candidates())
        {
            Crc32 crc = new();

            if (!feed(crc, file, header))
            {
                continue;
            }

            uint got = BinaryPrimitives.ReadUInt32LittleEndian(crc.GetCurrentHash());

            // **The engine never calls CRC32_Final.** Decompiled from the 2007 engine: the
            // accumulator goes straight from CRC32_ProcessBuffer into the comparison with the
            // server's value, with no `*pulCRC ^= CRC32_XOR_VALUE`. A standard CRC-32 applies that
            // final inversion, so the engine's number is the complement of ours.
            uint unfinalised = ~got;

            string verdict = string.Empty;

            if (got == wanted)
            {
                verdict = "*** MATCH (mapCrc field) *** ";
            }
            else if (unfinalised == wanted)
            {
                verdict = "*** MATCH (mapCrc field, un-finalised) *** ";
            }
            else if (got == alternate)
            {
                verdict = "*** MATCH (the OTHER four bytes) *** ";
            }
            else if (unfinalised == alternate)
            {
                verdict = "*** MATCH (the OTHER four bytes, un-finalised) *** ";
            }

            Console.WriteLine($"  {got:X8} / ~{unfinalised:X8}  {verdict}{name}");
        }

        Console.WriteLine();
        Console.WriteLine("(no MATCH line means none of these selections is the one)");
    }

    /// <summary>Every plausible byte selection, each as a way of feeding the accumulator.</summary>
    private static IEnumerable<(string Name, Func<Crc32, byte[], BspHeader, bool> Feed)> Candidates()
    {
        yield return ("the whole file", static (crc, file, _) =>
        {
            crc.Append(file);
            return true;
        });

        yield return ("every lump, index order, entities INCLUDED", static (crc, file, header) =>
        {
            for (int lump = 0; lump < BspHeader.LumpCount; lump++)
            {
                Feed(crc, file, header.Lump(lump));
            }

            return true;
        });

        yield return ("every lump but entities, index order", static (crc, file, header) =>
        {
            for (int lump = 0; lump < BspHeader.LumpCount; lump++)
            {
                if (lump != 0)
                {
                    Feed(crc, file, header.Lump(lump));
                }
            }

            return true;
        });

        yield return ("every lump but entities, FILE order", static (crc, file, header) =>
        {
            List<BspLump> lumps = [];

            for (int lump = 0; lump < BspHeader.LumpCount; lump++)
            {
                if (lump != 0)
                {
                    lumps.Add(header.Lump(lump));
                }
            }

            lumps.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

            foreach (BspLump lump in lumps)
            {
                Feed(crc, file, lump);
            }

            return true;
        });

        yield return ("every lump, FILE order, entities included", static (crc, file, header) =>
        {
            List<BspLump> lumps = [];

            for (int lump = 0; lump < BspHeader.LumpCount; lump++)
            {
                lumps.Add(header.Lump(lump));
            }

            lumps.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

            foreach (BspLump lump in lumps)
            {
                Feed(crc, file, lump);
            }

            return true;
        });

        // **"34 of the 35 standard lumps"**, which is a different count from the header's 64. An
        // old BSP may hold data in lumps a modern reader would find empty, so the ceiling matters.
        foreach (int ceiling in (int[])[35, 40, 48, 56, 64])
        {
            int stop = ceiling;

            yield return ($"lumps 1..{stop - 1}, index order", (crc, file, header) =>
            {
                for (int lump = 1; lump < stop; lump++)
                {
                    Feed(crc, file, header.Lump(lump));
                }

                return true;
            });

            yield return ($"lumps 0..{stop - 1}, index order", (crc, file, header) =>
            {
                for (int lump = 0; lump < stop; lump++)
                {
                    Feed(crc, file, header.Lump(lump));
                }

                return true;
            });
        }

        // **Exhaustive over one extra exclusion.** The published description — every lump but the
        // entities — does not reproduce the number, so the engine excludes something else as well.
        // PAKFILE and the two lighting variants are the obvious suspects, and trying all of them
        // costs nothing and rules out the whole class rather than one guess at a time.
        for (int skip = 1; skip < BspHeader.LumpCount; skip++)
        {
            int without = skip;

            yield return ($"every lump but entities AND lump {without}", (crc, file, header) =>
            {
                for (int lump = 1; lump < BspHeader.LumpCount; lump++)
                {
                    if (lump != without)
                    {
                        Feed(crc, file, header.Lump(lump));
                    }
                }

                return true;
            });
        }

        // **The FILE minus the entity range, which is not the same as concatenating lumps.** A BSP
        // has padding between lumps; a lump walk skips it and a "whole file, but skip these bytes"
        // implementation keeps it. Both descriptions read as "all lumps except entities" in prose.
        yield return ("whole file, skipping the entity byte range", static (crc, file, header) =>
        {
            BspLump entities = header.Lump(0);

            if (entities.Offset <= 0 || entities.Length <= 0 ||
                (long)entities.Offset + entities.Length > file.Length)
            {
                return false;
            }

            crc.Append(file.AsSpan(0, entities.Offset));
            crc.Append(file.AsSpan(entities.Offset + entities.Length));

            return true;
        });

        yield return ("whole file, entity range ZEROED in place", static (crc, file, header) =>
        {
            BspLump entities = header.Lump(0);

            if (entities.Offset <= 0 || entities.Length <= 0 ||
                (long)entities.Offset + entities.Length > file.Length)
            {
                return false;
            }

            byte[] blanked = (byte[])file.Clone();

            Array.Clear(blanked, entities.Offset, entities.Length);

            crc.Append(blanked);

            return true;
        });

        // Each lump on its own: if the answer is one lump's CRC, that says the engine checksums
        // something far narrower than any description suggests.
        for (int only = 0; only < BspHeader.LumpCount; only++)
        {
            int single = only;

            yield return ($"lump {single} alone", (crc, file, header) =>
            {
                BspLump at = header.Lump(single);

                if (at.Length <= 0)
                {
                    return false;
                }

                Feed(crc, file, at);

                return true;
            });
        }

        yield return ("the file after the header", static (crc, file, _) =>
        {
            if (file.Length <= BspHeader.SizeBytes)
            {
                return false;
            }

            crc.Append(file.AsSpan(BspHeader.SizeBytes));
            return true;
        });

        yield return ("the header only", static (crc, file, _) =>
        {
            crc.Append(file.AsSpan(0, Math.Min(BspHeader.SizeBytes, file.Length)));
            return true;
        });
    }

    private static void Feed(Crc32 crc, byte[] file, BspLump lump)
    {
        if (lump.Offset >= 0 && lump.Length > 0 && (long)lump.Offset + lump.Length <= file.Length)
        {
            crc.Append(file.AsSpan(lump.Offset, lump.Length));
        }
    }
}
