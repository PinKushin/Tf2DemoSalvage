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
    /// <summary>Strings remembered for back-references. Source keeps the last 32.</summary>
    private const int HistorySize = 32;

    private const int HistoryIndexBits = 5;
    private const int SubstringLengthBits = 5;
    private const int UserDataLengthBits = 14;
    private const int TableIdBits = 5;
    private const int UpdateLengthBits = 20;
    private const int CreateLengthBits = 20;

    /// <summary>Protocol at which the create-message length became a varint.</summary>
    private const int VarIntLengthProtocol = 23;

    internal static CreateStringTableMessage ReadCreate(ref BitReader reader, NetDecodeState state)
    {
        string name = NetBitReading.ReadString(ref reader);
        int maxEntries = (int)reader.ReadUInt32(16);
        int entryCount = (int)reader.ReadUInt32(BitsFor(maxEntries) + 1);

        int protocol = state.ServerInfo?.NetworkProtocol ?? 0;
        int lengthBits = protocol > VarIntLengthProtocol
            ? (int)VarInt.ReadUInt32(ref reader)
            : (int)reader.ReadUInt32(CreateLengthBits);

        bool fixedUserData = reader.ReadBit();
        int userDataSizeBits = 0;
        if (fixedUserData)
        {
            // The byte-count field is on the wire but unused: the bit count that follows is
            // what actually sizes a payload. Consumed with a discard rather than stored, so it
            // is clear the skip is deliberate and not a forgotten field.
            _ = reader.ReadUInt32(12);
            userDataSizeBits = (int)reader.ReadUInt32(4);
        }

        bool compressed = reader.ReadBit();

        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);

        if (compressed)
        {
            // The payload is LZSS-compressed. The length prefix has already advanced the outer
            // reader, so this costs one table rather than the rest of the stream.
            return new CreateStringTableMessage(
                name, maxEntries, [], true, "table payload is compressed");
        }

        try
        {
            BitReader bodyReader = new(body);
            IReadOnlyList<StringTableEntry> entries = ReadEntries(
                ref bodyReader, entryCount, maxEntries, fixedUserData, userDataSizeBits);

            return new CreateStringTableMessage(name, maxEntries, entries, false, null);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        {
            // The table's own bits ran out or made no sense. Reported rather than thrown,
            // because the outer stream is still intact and worth continuing.
            return new CreateStringTableMessage(
                name, maxEntries, [], false, $"entry decode failed: {exception.Message}");
        }
    }

    internal static UpdateStringTableMessage ReadUpdate(ref BitReader reader, NetDecodeState state)
    {
        int tableId = (int)reader.ReadUInt32(TableIdBits);
        int entryCount = reader.ReadBit() ? (int)reader.ReadUInt32(16) : 1;
        int lengthBits = (int)reader.ReadUInt32(UpdateLengthBits);

        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);
        int maxEntries = state.StringTableCapacity(tableId);

        if (maxEntries <= 0)
        {
            return new UpdateStringTableMessage(
                tableId, [], "the table this updates has not been seen");
        }

        try
        {
            BitReader bodyReader = new(body);
            return new UpdateStringTableMessage(
                tableId,
                ReadEntries(ref bodyReader, entryCount, maxEntries, false, 0),
                null);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        {
            return new UpdateStringTableMessage(
                tableId, [], $"entry decode failed: {exception.Message}");
        }
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
        int indexBits = BitsFor(maxEntries);
        int lastIndex = -1;

        for (int i = 0; i < entryCount; i++)
        {
            // Consecutive indices are the common case, so a single bit covers them.
            int index = reader.ReadBit() ? lastIndex + 1 : (int)reader.ReadUInt32(indexBits);
            lastIndex = index;

            string? text = null;
            if (reader.ReadBit())
            {
                if (reader.ReadBit())
                {
                    // Shares a prefix with a recent string: take that many bytes from it and
                    // read only the differing tail.
                    int historyIndex = (int)reader.ReadUInt32(HistoryIndexBits);
                    int copyLength = (int)reader.ReadUInt32(SubstringLengthBits);
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

            entries.Add(new StringTableEntry(index, text, userData));
        }

        return entries;
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

    /// <summary>Bits needed to hold an index into a table of <paramref name="capacity"/>.</summary>
    private static int BitsFor(int capacity)
    {
        int bits = 0;
        while (1 << bits < capacity)
        {
            bits++;
        }

        return bits;
    }
}
