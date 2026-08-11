using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Decodes <c>svc_CreateStringTable</c> and <c>svc_UpdateStringTable</c>.
/// </summary>
/// <remarks>
/// Entries are encoded against a rolling history of the last 32 strings, so a name that shares
/// a prefix with an earlier one transmits only the differing tail. That makes the decoder
/// stateful within a single table: getting one entry wrong corrupts every later entry that
/// back-references it, rather than failing locally.
/// </remarks>
internal static class StringTableCodec
{
    /// <summary>Width of the four-byte magic naming a payload's compression scheme.</summary>
    private const int MagicBytes = 4;

    /// <summary>Strings remembered for back-references. Source keeps the last 32.</summary>
    private const int HistorySize = 32;

    private const int HistoryIndexBits = 5;
    private const int SubstringLengthBits = 5;
    private const int UserDataLengthBits = 14;
    private const int TableIdBits = 5;
    private const int UpdateLengthBits = 20;
    private const int CreateLengthBits = 20;

    /// <summary>Protocol at which the create-message length became a varint.</summary>
    internal const int VarIntLengthProtocol = 23;

    /// <summary>Last protocol that sent no compression flag on a create message.</summary>
    /// <remarks>
    /// From Valve's <c>proto_version.h</c>, still shipped in the current TF2 SDK because the
    /// engine keeps reading old demos: <c>PROTOCOL_VERSION_14</c> is annotated "create string
    /// tables compression flag". That file names the last build *without* each change —
    /// <c>PROTOCOL_VERSION_17</c> is "MD5 in map version" and the MD5 appears at 18 — so the
    /// flag arrives at 15.
    ///
    /// This matters on the era axis rather than in theory. TF2 shipped on the Orange Box engine
    /// in October 2007, which is pre-15, so TF2's own 2007–2008 demos carry no flag here. Reading
    /// the bit anyway shifts every string table by one, and string tables are load-bearing.
    /// </remarks>
    internal const int CompressionFlagProtocol = 14;

    internal static CreateStringTableMessage ReadCreate(ref BitReader reader, NetDecodeState state)
    {
        string name = NetBitReading.ReadString(ref reader);
        int maxEntries = (int)reader.ReadUInt32(16);
        int entryCount = (int)reader.ReadUInt32(WireWidths.StringTableEntryCount(maxEntries));

        int protocol = state.ServerInfo?.NetworkProtocol ?? 0;
        int lengthBits = protocol > VarIntLengthProtocol
            ? (int)VarInt.ReadUInt32(ref reader)
            : (int)reader.ReadUInt32(CreateLengthBits);

        bool fixedUserData = reader.ReadBit();
        int userDataSizeBits = 0;
        int? userDataSizeBytes = null;
        if (fixedUserData)
        {
            // The byte-count field is on the wire but the decoder has no use for it: the bit
            // count that follows is what actually sizes a payload. Kept rather than discarded
            // because a message cannot be rebuilt without every field it carried.
            userDataSizeBytes = (int)reader.ReadUInt32(12);
            userDataSizeBits = (int)reader.ReadUInt32(4);
        }

        bool compressed = protocol > CompressionFlagProtocol && reader.ReadBit();

        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);
        CreateStringTableWire wire = new(
            entryCount, lengthBits, body, userDataSizeBytes, userDataSizeBits);

        if (compressed)
        {
            try
            {
                body = Decompress(body);
            }
            catch (InvalidDataException exception)
            {
                // Reported rather than thrown: the outer reader has already stepped past this
                // table's bits, so one unreadable table does not cost the rest of the stream.
                return new CreateStringTableMessage(
                    name, maxEntries, [], true, $"decompression failed: {exception.Message}",
                    wire);
            }
        }

        try
        {
            BitReader bodyReader = new(body);
            IReadOnlyList<StringTableEntry> entries = ReadEntries(
                ref bodyReader, entryCount, maxEntries, fixedUserData, userDataSizeBits);

            return new CreateStringTableMessage(
                name, maxEntries, entries, compressed, null, wire);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        {
            // The table's own bits ran out or made no sense. Reported rather than thrown,
            // because the outer stream is still intact and worth continuing.
            return new CreateStringTableMessage(
                name, maxEntries, [], false, $"entry decode failed: {exception.Message}", wire);
        }
    }

    internal static UpdateStringTableMessage ReadUpdate(ref BitReader reader, NetDecodeState state)
    {
        int tableId = (int)reader.ReadUInt32(TableIdBits);
        int entryCount = reader.ReadBit() ? (int)reader.ReadUInt32(16) : 1;
        int lengthBits = (int)reader.ReadUInt32(UpdateLengthBits);

        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);
        UpdateStringTableWire wire = new(entryCount, lengthBits, body);
        int maxEntries = state.StringTableCapacity(tableId);

        if (maxEntries <= 0)
        {
            return new UpdateStringTableMessage(
                tableId, [], "the table this updates has not been seen", wire);
        }

        try
        {
            BitReader bodyReader = new(body);
            return new UpdateStringTableMessage(
                tableId,
                ReadEntries(ref bodyReader, entryCount, maxEntries, false, 0),
                null,
                wire);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        {
            return new UpdateStringTableMessage(
                tableId, [], $"entry decode failed: {exception.Message}", wire);
        }
    }

    /// <summary>
    /// Unwraps a compressed table payload: two sizes, a four-byte magic, then the payload.
    /// </summary>
    /// <remarks>
    /// Two schemes appear in the wild and the magic decides, not the era. <c>SNAP</c> is
    /// Snappy, which every compressed table in the current corpus uses; <c>LZSS</c> is Valve's
    /// older scheme. An unknown magic is reported by its bytes rather than guessed at, because
    /// decompressing with the wrong scheme still produces bytes and those bytes still parse as
    /// entries.
    /// </remarks>
    private static byte[] Decompress(ReadOnlySpan<byte> body)
    {
        BitReader reader = new(body);
        int decompressedSize = (int)reader.ReadUInt32(32);
        int compressedSize = (int)reader.ReadUInt32(32);

        Span<byte> magic = stackalloc byte[MagicBytes];
        for (int i = 0; i < MagicBytes; i++)
        {
            magic[i] = reader.ReadByte();
        }

        bool isLzss = magic.SequenceEqual(Lzss.Magic);
        bool isSnappy = magic.SequenceEqual(Lzss.SnappyMagic);

        if (!isLzss && !isSnappy)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"unknown compression magic 0x{Convert.ToHexString(magic)}"));
        }

        if (compressedSize < MagicBytes || compressedSize - MagicBytes > body.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"compressed size {compressedSize} does not fit in {body.Length} bytes"));
        }

        // The magic counts toward the compressed size, so the payload is what remains after it.
        byte[] payload = new byte[compressedSize - MagicBytes];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = reader.ReadByte();
        }

        // Every compressed table in the current corpus is Snappy; LZSS is the older scheme and
        // is kept because the magic is what decides, not the era.
        byte[] decompressed = isLzss
            ? Lzss.Decompress(payload, decompressedSize)
            : Snappy.Decompress(payload);

        if (decompressed.Length != decompressedSize)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"decompressed to {decompressed.Length} bytes, not the {decompressedSize} " +
                $"the table declared"));
        }

        return decompressed;
    }

    /// <summary>
    /// Reads table entries, resolving back-references against a rolling 32-string history.
    /// </summary>
    private static List<StringTableEntry> ReadEntries(
        ref BitReader reader,
        int entryCount,
        int maxEntries,
        bool fixedUserData,
        int userDataSizeBits)
    {
        List<StringTableEntry> entries = new(entryCount);
        List<string> history = new(HistorySize);
        int indexBits = WireWidths.StringTableIndex(maxEntries);
        int lastIndex = -1;

        for (int i = 0; i < entryCount; i++)
        {
            // Consecutive indices are the common case, so a single bit covers them.
            bool follows = reader.ReadBit();
            int index = follows ? lastIndex + 1 : (int)reader.ReadUInt32(indexBits);
            lastIndex = index;

            int historyIndex = -1;
            int copyLength = 0;

            string? text = null;
            if (reader.ReadBit())
            {
                if (reader.ReadBit())
                {
                    // Shares a prefix with a recent string: take that many bytes from it and
                    // read only the differing tail.
                    historyIndex = (int)reader.ReadUInt32(HistoryIndexBits);
                    copyLength = (int)reader.ReadUInt32(SubstringLengthBits);
                    string source = historyIndex < history.Count ? history[historyIndex] : string.Empty;
                    // Stryker disable once Equality: at copyLength == source.Length both
                    // branches yield the same string, because source[..source.Length] is
                    // source. Equivalent mutant, not a missing boundary test.
                    string prefix = copyLength <= source.Length
                        ? source[..copyLength]
                        : source;

                    text = prefix + NetBitReading.ReadString(ref reader);
                }
                else
                {
                    text = NetBitReading.ReadString(ref reader);
                }

                history.Add(text);
                if (history.Count > HistorySize)
                {
                    history.RemoveAt(0);
                }
            }

            byte[] userData = [];
            if (reader.ReadBit())
            {
                // A fixed-size table states its payload width in *bits*; a variable one
                // states a byte count. Reading the wrong unit desynchronises the table
                // rather than failing, so the distinction matters more than it looks.
                userData = fixedUserData
                    ? NetBitReading.CopyBits(ref reader, userDataSizeBits)
                    : ReadBytes(ref reader, (int)reader.ReadUInt32(UserDataLengthBits));
            }

            entries.Add(new StringTableEntry(
                index, text, userData, follows, historyIndex, copyLength));
        }

        return entries;
    }

    /// <summary>Writes table entries back, exactly as they were sent.</summary>
    /// <param name="entries">The entries.</param>
    /// <param name="maxEntries">The table's capacity, which sizes an explicit index.</param>
    /// <param name="fixedUserData">Whether payloads are a fixed width.</param>
    /// <param name="userDataSizeBits">That width, in bits.</param>
    /// <returns>The body's bits, and how many of them are meaningful.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <c>null</c>.</exception>
    /// <remarks>
    /// The rolling history is rebuilt here rather than carried, because it is derivable: it is the
    /// last 32 strings written, in order. What is not derivable — which of them an entry reused
    /// and how much of it — travels on the entry itself.
    /// </remarks>
    internal static (byte[] Body, int BitCount) WriteEntries(
        IReadOnlyList<StringTableEntry> entries,
        int maxEntries,
        bool fixedUserData,
        int userDataSizeBits)
    {
        ArgumentNullException.ThrowIfNull(entries);

        BitWriter writer = new();
        List<string> history = new(HistorySize);
        int indexBits = WireWidths.StringTableIndex(maxEntries);

        foreach (StringTableEntry entry in entries)
        {
            writer.WriteBit(entry.FollowsPrevious);

            // A one-entry table needs no index field at all - floor(log2(1)) is zero - and the
            // reader consumes nothing there, so writing a bit would insert one.
            if (!entry.FollowsPrevious && indexBits > 0)
            {
                writer.Write((uint)entry.Index, indexBits);
            }

            if (entry.Text is null)
            {
                writer.WriteBit(false);
            }
            else
            {
                writer.WriteBit(true).WriteBit(entry.HistoryIndex >= 0);

                if (entry.HistoryIndex >= 0)
                {
                    string source = entry.HistoryIndex < history.Count
                        ? history[entry.HistoryIndex]
                        : string.Empty;

                    int prefix = Math.Min(entry.CopyLength, source.Length);

                    writer.Write((uint)entry.HistoryIndex, HistoryIndexBits)
                        .Write((uint)entry.CopyLength, SubstringLengthBits)
                        .WriteString(entry.Text[prefix..]);
                }
                else
                {
                    writer.WriteString(entry.Text);
                }

                history.Add(entry.Text);
                if (history.Count > HistorySize)
                {
                    history.RemoveAt(0);
                }
            }

            if (entry.UserData.Count == 0)
            {
                writer.WriteBit(false);
                continue;
            }

            writer.WriteBit(true);
            byte[] payload = [.. entry.UserData];

            if (fixedUserData)
            {
                writer.AppendBits(payload, userDataSizeBits);
                continue;
            }

            writer.Write((uint)payload.Length, UserDataLengthBits).WriteBytes(payload);
        }

        return (writer.Build(), writer.BitCount);
    }

    private static byte[] ReadBytes(ref BitReader reader, int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = reader.ReadByte();
        }

        return bytes;
    }

}
