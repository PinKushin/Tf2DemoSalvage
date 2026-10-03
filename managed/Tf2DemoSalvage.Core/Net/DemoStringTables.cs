using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One table out of a <c>dem_stringtables</c> block: its name and every entry it held.</summary>
/// <param name="Name">The table's name, e.g. <c>instancebaseline</c>.</param>
/// <param name="Entries">Its server-side entries, indexed by position.</param>
public sealed record DemoStringTable(string Name, IReadOnlyList<StringTableEntry> Entries);

/// <summary>
/// Reads a <c>dem_stringtables</c> block: every string table as it stood when recording began (B452).
/// </summary>
/// <remarks>
/// **A recording that starts mid-match has missed every table update before it**, and this block is
/// how the demo carries them: the demo player hands it to the client's string table container on
/// playback, which rebuilds each table from it. A point-of-view demo's <c>instancebaseline</c> for
/// <c>CTFPlayer</c> lives ONLY here — the signon's create predates the class's first baseline — and
/// without it the player's first full update deltas against nothing and drops every field equal to
/// the baseline: <c>m_PlayerFog.m_hCtrl</c>, <c>m_skybox3d</c>, <c>m_flStepSize</c>.
///
/// Layout (cross-checked with demostf/parser's <c>StringTablePacket</c>; measured on the corpus):
/// a byte of table count; per table its name, a 16-bit entry count, and per entry its string and a
/// bit announcing a 16-bit byte length plus that many bytes of user data; then a bit announcing the
/// client-side entries, in the same entry form behind their own 16-bit count. Client-side entries
/// are the client's own and are read past, not returned.
/// </remarks>
public static class DemoStringTables
{
    /// <summary>Reads every table in the block.</summary>
    /// <param name="payload">The command's payload.</param>
    /// <returns>The tables, in the order the block lists them.</returns>
    /// <exception cref="System.IO.EndOfStreamException">The block ends inside a table.</exception>
    public static IReadOnlyList<DemoStringTable> Read(ReadOnlySpan<byte> payload)
    {
        BitReader reader = new(payload);
        int tableCount = reader.ReadByte();
        List<DemoStringTable> tables = new(tableCount);

        for (int table = 0; table < tableCount; table++)
        {
            string name = NetBitReading.ReadString(ref reader);
            List<StringTableEntry> entries = ReadEntries(ref reader);

            if (reader.ReadBit())
            {
                ReadEntries(ref reader);
            }

            tables.Add(new DemoStringTable(name, entries));
        }

        return tables;
    }

    /// <summary>Every table in the block, as the create that would have built it.</summary>
    /// <param name="payload">The command's payload.</param>
    /// <returns>One decoded, uncompressed create per table, in block order.</returns>
    /// <remarks>
    /// **The engine rebuilds every client table from the block**, so each is handed to whatever a
    /// create feeds — baselines, precaches, the roster — by the one route a create already takes.
    /// The block carries no capacity, so <c>MaxEntries</c> is zero; no consumer of a create reads it.
    /// </remarks>
    public static IReadOnlyList<CreateStringTableMessage> AsCreates(ReadOnlySpan<byte> payload) =>
        [.. Read(payload).Select(table =>
            new CreateStringTableMessage(table.Name, 0, table.Entries, IsCompressed: false, UndecodedReason: null))];

    private static List<StringTableEntry> ReadEntries(ref BitReader reader)
    {
        int count = (int)reader.ReadUInt32(16);
        List<StringTableEntry> entries = new(count);

        for (int index = 0; index < count; index++)
        {
            string text = NetBitReading.ReadString(ref reader);
            byte[] data = reader.ReadBit()
                ? NetBitReading.CopyBits(ref reader, (int)reader.ReadUInt32(16) * 8)
                : [];

            entries.Add(new StringTableEntry(index, text, data));
        }

        return entries;
    }
}
