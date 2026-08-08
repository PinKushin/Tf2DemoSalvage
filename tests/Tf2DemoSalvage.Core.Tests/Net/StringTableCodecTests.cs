using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Synthetic tests for string table decoding.
/// </summary>
/// <remarks>
/// Written after the codec rather than before it, which was a mistake: the first mutation run
/// left 53 survivors in this file alone, because corpus tests exercise only the paths the three
/// demos happen to use. Entries are encoded against a rolling history, so a wrong branch here
/// corrupts every later entry that back-references it rather than failing locally — exactly the
/// kind of bug end-to-end tests hide.
/// </remarks>
public sealed class StringTableCodecTests
{
    /// <summary>Builds a table entry stream matching the wire encoding.</summary>
    private sealed class TableBuilder(int maxEntries)
    {
        private readonly BitWriter _writer = new();
        private int _count;

        public int Count => _count;

        public int MaxEntries { get; } = maxEntries;

        /// <summary>An entry whose index follows on from the previous one.</summary>
        public TableBuilder Next(string? text, byte[]? userData = null) =>
            Entry(null, text, userData);

        /// <summary>An entry at an explicit index.</summary>
        public TableBuilder At(int index, string? text, byte[]? userData = null) =>
            Entry(index, text, userData);

        /// <summary>An entry reusing the first <paramref name="copy"/> bytes of a history item.</summary>
        public TableBuilder Substring(int historyIndex, int copy, string tail)
        {
            _writer.Write(1, 1);          // index follows
            _writer.Write(1, 1);          // has text
            _writer.Write(1, 1);          // is a substring
            _writer.Write((uint)historyIndex, 5);
            _writer.Write((uint)copy, 5);
            _writer.String(tail);
            _writer.Write(0, 1);          // no user data
            _count++;
            return this;
        }

        private TableBuilder Entry(int? index, string? text, byte[]? userData)
        {
            if (index is null)
            {
                _writer.Write(1, 1);
            }
            else
            {
                _writer.Write(0, 1);
                _writer.Write((uint)index.Value, BitsFor(MaxEntries));
            }

            if (text is null)
            {
                _writer.Write(0, 1);
            }
            else
            {
                _writer.Write(1, 1).Write(0, 1).String(text);
            }

            if (userData is null)
            {
                _writer.Write(0, 1);
            }
            else
            {
                _writer.Write(1, 1).Write((uint)userData.Length, 14);
                foreach (byte b in userData)
                {
                    _writer.Write(b, 8);
                }
            }

            _count++;
            return this;
        }

        public byte[] Bits() => _writer.Build();

        public int BitCount => _writer.BitCount;

        internal static int BitsFor(int capacity)
        {
            int bits = 0;
            while (1 << bits < capacity)
            {
                bits++;
            }

            return bits;
        }
    }

    /// <summary>Wraps table bits in a svc_CreateStringTable message.</summary>
    private static byte[] Create(
        TableBuilder table,
        string name = "userinfo",
        bool compressed = false,
        ushort protocol = 24)
    {
        BitWriter writer = new();
        CreateInto(writer, table, name, compressed, protocol);
        return writer.Build();
    }

    /// <summary>
    /// Writes a create message into an existing stream. Separate from <see cref="Create"/>
    /// because appending one byte-aligned message after another does not work: the padding is
    /// 0-7 bits and a message type field is 6, so the reader would consume a type field
    /// spanning the padding and whatever follows. Anything testing what comes *after* a
    /// message has to share one continuous bit stream.
    /// </summary>
    private static void CreateInto(
        BitWriter writer,
        TableBuilder table,
        string name = "userinfo",
        bool compressed = false,
        ushort protocol = 24)
    {
        writer.Message(NetMessageType.CreateStringTable);
        writer.String(name).Write((uint)table.MaxEntries, 16);
        writer.Write((uint)table.Count, TableBuilder.BitsFor(table.MaxEntries) + 1);

        // Protocol 24 sends the length as a varint rather than a fixed 20-bit field.
        if (protocol > 23)
        {
            WriteVarInt(writer, (uint)table.BitCount);
        }
        else
        {
            writer.Write((uint)table.BitCount, 20);
        }

        writer.Write(0, 1);                        // not fixed user data size
        writer.Write(compressed ? 1u : 0u, 1);
        AppendBits(writer, table.Bits(), table.BitCount);
    }

    private static void WriteVarInt(BitWriter writer, uint value)
    {
        while (value >= 0x80)
        {
            writer.Write((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }

        writer.Write(value, 8);
    }

    private static void AppendBits(BitWriter writer, byte[] bytes, int bitCount)
    {
        for (int bit = 0; bit < bitCount; bit++)
        {
            writer.Write((uint)((bytes[bit / 8] >> (bit % 8)) & 1), 1);
        }
    }

    /// <summary>Reads with a ServerInfo already in state, so the protocol branch is taken.</summary>
    private static NetMessageReadResult ReadWithProtocol(byte[] packet, ushort protocol = 24)
    {
        NetDecodeState state = new()
        {
            ServerInfo = new ServerInfoMessage(
                protocol, 0, false, true, 0, 275, [], 0, 24, 0.015f, 'l',
                "tf", "cp_process_final", "sky", "server", false),
        };

        return NetMessageReader.Read(packet, state);
    }

    [Fact]
    public void Create_DecodesNameCapacityAndPlainEntries()
    {
        TableBuilder table = new TableBuilder(256).Next("alpha").Next("beta").Next("gamma");

        NetMessageReadResult result = ReadWithProtocol(Create(table, name: "userinfo"));

        CreateStringTableMessage message =
            result.Messages[0].ShouldBeOfType<CreateStringTableMessage>();
        message.Name.ShouldBe("userinfo");
        message.MaxEntries.ShouldBe(256);
        message.IsDecoded.ShouldBeTrue();
        message.Entries.Select(e => e.Text).ShouldBe(["alpha", "beta", "gamma"]);
        message.Entries.Select(e => e.Index).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Create_ExplicitIndex_IsHonouredAndResetsTheRunningIndex()
    {
        TableBuilder table = new TableBuilder(256).Next("a").At(40, "b").Next("c");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries.Select(e => e.Index).ShouldBe([0, 40, 41]);
    }

    [Fact]
    public void Create_SubstringEntry_ReusesThePrefixFromHistory()
    {
        // The compression that makes this decoder stateful: "decals/concrete/shot2" is sent as
        // "take 20 bytes of history entry 0, then '2'".
        TableBuilder table = new TableBuilder(512)
            .Next("decals/concrete/shot1")
            .Substring(historyIndex: 0, copy: 20, tail: "2");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[1].Text.ShouldBe("decals/concrete/shot2");
    }

    [Fact]
    public void Create_SubstringCopyLongerThanTheSource_UsesTheWholeSource()
    {
        // Malformed rather than impossible. Clamping keeps the table readable instead of
        // throwing away everything after it.
        TableBuilder table = new TableBuilder(512)
            .Next("ab")
            .Substring(historyIndex: 0, copy: 31, tail: "c");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[1].Text.ShouldBe("abc");
    }

    [Fact]
    public void Create_SubstringReferencingAnAbsentHistoryEntry_YieldsTheTailAlone()
    {
        TableBuilder table = new TableBuilder(512).Substring(historyIndex: 9, copy: 4, tail: "x");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[0].Text.ShouldBe("x");
    }

    [Fact]
    public void Create_EntryWithoutText_HasNullTextRatherThanEmpty()
    {
        // An update can carry user data for an existing entry without resending its name.
        // Null and empty mean different things here.
        TableBuilder table = new TableBuilder(64).Next(null, userData: [1, 2, 3]);

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[0].Text.ShouldBeNull();
        message.Entries[0].UserData.ShouldBe([(byte)1, (byte)2, (byte)3]);
    }

    [Fact]
    public void Create_EntryWithoutUserData_HasEmptyUserData()
    {
        TableBuilder table = new TableBuilder(64).Next("only-text");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[0].UserData.ShouldBeEmpty();
    }

    [Fact]
    public void Create_CompressedPayload_IsDecompressedAndDecoded()
    {
        // Five of the twenty tables arrive compressed, instancebaseline among them, so this is
        // the path that decides whether entity baselines are reachable at all. The entries are
        // asserted by name: decompressing to the wrong bytes still yields *some* entries.
        TableBuilder table = new TableBuilder(64).Next("alpha").Next("beta");

        CreateStringTableMessage message = ReadWithProtocol(CreateCompressed(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.IsCompressed.ShouldBeTrue();
        message.IsDecoded.ShouldBeTrue();
        message.Entries.Select(e => e.Text).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public void Create_SnappyCompression_IsNamedRatherThanGuessedAt()
    {
        // Decompressing with the wrong scheme still produces bytes, and those bytes still
        // parse as entries - so an unimplemented scheme has to be named, not attempted.
        TableBuilder table = new TableBuilder(64).Next("alpha");

        CreateStringTableMessage message =
            ReadWithProtocol(CreateCompressed(table, magic: "SNAP"u8.ToArray()))
                .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.IsDecoded.ShouldBeFalse();
        message.UndecodedReason.ShouldNotBeNull().ShouldContain("Snappy");
    }

    [Fact]
    public void Create_UnknownCompressionMagic_IsReportedWithItsBytes()
    {
        TableBuilder table = new TableBuilder(64).Next("alpha");

        CreateStringTableMessage message =
            ReadWithProtocol(CreateCompressed(table, magic: [0xDE, 0xAD, 0xBE, 0xEF]))
                .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.IsDecoded.ShouldBeFalse();
        message.UndecodedReason.ShouldNotBeNull().ShouldContain("DEADBEEF");
    }

    [Fact]
    public void Create_CorruptCompressedPayload_CostsOnlyItsOwnTable()
    {
        // The length prefix is what makes this survivable: the outer reader has already moved
        // past the table, so a broken payload must not take the rest of the stream with it.
        TableBuilder table = new TableBuilder(64).Next("alpha");
        byte[] payload = [.. BitConverter.GetBytes(999), 0x00, 0x41];   // truncated
        BitWriter writer = new();
        CompressedInto(writer, table, "userinfo", "LZSS"u8.ToArray(), payload);
        writer.NetTick(4321, 0, 0);

        NetMessageReadResult result = ReadWithProtocol(writer.Build());

        result.Messages[0].ShouldBeOfType<CreateStringTableMessage>()
            .UndecodedReason.ShouldNotBeNull().ShouldContain("decompression failed");
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4321);
    }

    /// <summary>Builds a create message whose payload is LZSS-wrapped.</summary>
    private static byte[] CreateCompressed(
        TableBuilder table, string name = "userinfo", byte[]? magic = null)
    {
        BitWriter writer = new();
        CompressedInto(writer, table, name, magic ?? "LZSS"u8.ToArray(),
            EncodeLiterals(table.Bits().AsSpan(0, (table.BitCount + 7) / 8)));
        return writer.Build();
    }

    private static void CompressedInto(
        BitWriter writer, TableBuilder table, string name, byte[] magic, byte[] payload)
    {
        int decompressedSize = (table.BitCount + 7) / 8;
        byte[] body =
        [
            .. BitConverter.GetBytes(decompressedSize),
            .. BitConverter.GetBytes(payload.Length + magic.Length),
            .. magic,
            .. payload,
        ];

        writer.Message(NetMessageType.CreateStringTable);
        writer.String(name).Write((uint)table.MaxEntries, 16);
        writer.Write((uint)table.Count, TableBuilder.BitsFor(table.MaxEntries) + 1);
        WriteVarInt(writer, (uint)(body.Length * 8));
        writer.Write(0, 1);                        // not fixed user data size
        writer.Write(1, 1);                        // compressed
        AppendBits(writer, body, body.Length * 8);
    }

    /// <summary>
    /// LZSS with every item a literal — legal output, and trivially correct by inspection.
    /// </summary>
    /// <remarks>
    /// A compressor that chose back-references the way the decoder reads them could agree with
    /// a wrong decoder. Emitting only literals removes that risk: the bytes are visible in the
    /// fixture. <see cref="Primitives.LzssTests"/> covers back-references directly.
    /// </remarks>
    private static byte[] EncodeLiterals(ReadOnlySpan<byte> data)
    {
        List<byte> output = [.. BitConverter.GetBytes(data.Length)];
        int control = -1;
        int item = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (item == 0)
            {
                control = output.Count;
                output.Add(0);
            }

            output.Add(data[i]);
            item = (item + 1) % 8;
        }

        if (item == 0)
        {
            control = output.Count;
            output.Add(0);
        }

        // The terminator is an item too, so its control bit has to be set.
        output[control] |= (byte)(1 << item);
        output.Add(0x00);
        output.Add(0x00);

        return [.. output];
    }

    [Fact]
    public void Create_LeavesTheReaderPositionedForTheNextMessage()
    {
        // The length prefix is what makes an undecodable table survivable, so it has to be
        // consumed exactly - whether or not the entries were read.
        TableBuilder table = new TableBuilder(256).Next("alpha").Next("beta");
        BitWriter writer = new();
        CreateInto(writer, table);
        writer.NetTick(1234, 0, 0);

        NetMessageReadResult result = ReadWithProtocol(writer.Build());

        result.Messages[0].ShouldBeOfType<CreateStringTableMessage>();
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1234);
    }

    [Fact]
    public void Create_TwentyBitLengthAtOlderProtocols()
    {
        // Protocol 23 and below use a fixed 20-bit length rather than a varint. No specimen
        // behind this branch - the corpus is entirely protocol 24 - so it is pinned, not
        // verified.
        TableBuilder table = new TableBuilder(64).Next("alpha");

        CreateStringTableMessage message =
            ReadWithProtocol(Create(table, protocol: 23), protocol: 23)
                .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries.Select(e => e.Text).ShouldBe(["alpha"]);
    }

    [Fact]
    public void Create_UncompressedTable_ReportsItselfAsSuch()
    {
        TableBuilder table = new TableBuilder(64).Next("alpha");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.IsCompressed.ShouldBeFalse();
        message.UndecodedReason.ShouldBeNull();
    }

    [Fact]
    public void Create_FixedUserDataSize_ReadsPayloadsInBitsNotBytes()
    {
        // A fixed-size table states its payload width in bits. Reading a 14-bit byte count
        // instead - the variable-size encoding - desynchronises everything after it.
        BitWriter table = new();
        table.Write(1, 1).Write(1, 1).Write(0, 1).String("slot0").Write(1, 1).Write(0b1011, 4);
        table.Write(1, 1).Write(1, 1).Write(0, 1).String("slot1").Write(1, 1).Write(0b0110, 4);

        BitWriter writer = new BitWriter().Message(NetMessageType.CreateStringTable);
        writer.String("fixed").Write(64, 16).Write(2, TableBuilder.BitsFor(64) + 1);
        WriteVarInt(writer, (uint)table.BitCount);
        writer.Write(1, 1)        // fixed user data size
            .Write(1, 12)         // byte count, informational only
            .Write(4, 4)          // four bits per payload
            .Write(0, 1);         // not compressed
        AppendBits(writer, table.Build(), table.BitCount);

        CreateStringTableMessage message = ReadWithProtocol(writer.Build())
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries.Select(e => e.Text).ShouldBe(["slot0", "slot1"]);
        message.Entries[0].UserData.ShouldBe([(byte)0b1011]);
        message.Entries[1].UserData.ShouldBe([(byte)0b0110]);
    }

    [Fact]
    public void Create_SubstringCopyExactlyTheSourceLength_TakesAllOfIt()
    {
        // Boundary: copy length equal to the source length must take the whole string, not
        // fall into the clamped branch or drop a character.
        TableBuilder table = new TableBuilder(512)
            .Next("abcd")
            .Substring(historyIndex: 0, copy: 4, tail: "e");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[1].Text.ShouldBe("abcde");
    }

    [Fact]
    public void Create_SubstringIndexEqualToHistoryLength_IsTreatedAsAbsent()
    {
        // Boundary the other side: history has one entry, so index 1 is one past the end.
        TableBuilder table = new TableBuilder(512)
            .Next("abc")
            .Substring(historyIndex: 1, copy: 3, tail: "z");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[1].Text.ShouldBe("z");
    }

    [Fact]
    public void Create_HistoryEvictsOldestBeyondThirtyTwoEntries()
    {
        // The history window is 32. After 33 strings the first has been evicted, so index 0
        // now refers to what was originally the second string.
        TableBuilder table = new(2048);
        for (int i = 0; i < 33; i++)
        {
            table.Next($"entry{i:D2}");
        }

        table.Substring(historyIndex: 0, copy: 7, tail: "X");

        CreateStringTableMessage message = ReadWithProtocol(Create(table))
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Entries[^1].Text.ShouldBe("entry01X");
    }

    [Fact]
    public void Create_WithABodyTooShortForItsEntries_ReportsWhyRatherThanThrowing()
    {
        // Create's twin of the update-side truncation test: one declared entry, zero body
        // bits. The length prefix already advanced the outer reader, so the failure is
        // reported on the message rather than thrown - and the report says exactly where the
        // bits ran out.
        BitWriter writer = new BitWriter().Message(NetMessageType.CreateStringTable);
        writer.String("broken").Write(64, 16);
        writer.Write(1, TableBuilder.BitsFor(64) + 1);  // one entry claimed...
        WriteVarInt(writer, 0);                         // ...in a zero-bit body
        writer.Write(0, 1)      // not fixed user data size
            .Write(0, 1);       // not compressed

        CreateStringTableMessage message = ReadWithProtocol(writer.Build())
            .Messages[0].ShouldBeOfType<CreateStringTableMessage>();

        message.Name.ShouldBe("broken");
        message.MaxEntries.ShouldBe(64);
        message.IsDecoded.ShouldBeFalse();
        message.Entries.ShouldBeEmpty();
        // Not compressed: the two undecodable paths are reported differently on purpose, and a
        // consumer that saw this flag set would blame compression for a truncation.
        message.IsCompressed.ShouldBeFalse();
        message.UndecodedReason.ShouldBe(
            "entry decode failed: Requested 1 bits at bit offset 0, but only 0 bits remain.");
    }

    [Fact]
    public void Update_CarriesUserDataUsingTheVariableLengthEncoding()
    {
        TableBuilder table = new TableBuilder(256).Next("who", userData: [9, 8]);

        BitWriter writer = new BitWriter().Message(NetMessageType.UpdateStringTable);
        writer.Write(0, 5).Write(0, 1).Write((uint)table.BitCount, 20);
        AppendBits(writer, table.Bits(), table.BitCount);

        NetDecodeState state = new();
        state.AddStringTable(256);

        UpdateStringTableMessage message = NetMessageReader.Read(writer.Build(), state)
            .Messages[0].ShouldBeOfType<UpdateStringTableMessage>();

        message.IsDecoded.ShouldBeTrue();
        message.Entries[0].Text.ShouldBe("who");
        message.Entries[0].UserData.ShouldBe([(byte)9, (byte)8]);
    }

    [Fact]
    public void Update_WithABodyTooShortForItsEntries_ReportsWhyRatherThanThrowing()
    {
        // The body claims more entries than its bits can hold. The outer stream is still
        // intact - the length prefix already moved past it - so this is reported, not thrown.
        BitWriter writer = new BitWriter().Message(NetMessageType.UpdateStringTable);
        writer.Write(0, 5)
            .Write(1, 1)      // an explicit count follows
            .Write(50, 16)    // fifty entries...
            .Write(8, 20)     // ...in eight bits
            .Write(0xFF, 8);

        NetDecodeState state = new();
        state.AddStringTable(256);

        UpdateStringTableMessage message = NetMessageReader.Read(writer.Build(), state)
            .Messages[0].ShouldBeOfType<UpdateStringTableMessage>();

        message.IsDecoded.ShouldBeFalse();
        message.UndecodedReason.ShouldNotBeNull().ShouldContain("entry decode failed");
    }

    [Fact]
    public void Update_WithoutItsTable_IsReportedRatherThanGuessed()
    {
        BitWriter writer = new BitWriter().Message(NetMessageType.UpdateStringTable);
        writer.Write(9, 5)      // a table id never created
            .Write(0, 1)        // single entry
            .Write(8, 20)       // length
            .Write(0, 8);

        UpdateStringTableMessage message = NetMessageReader.Read(writer.Build())
            .Messages[0].ShouldBeOfType<UpdateStringTableMessage>();

        message.TableId.ShouldBe(9);
        message.IsDecoded.ShouldBeFalse();
        message.Entries.ShouldBeEmpty();
        message.UndecodedReason.ShouldNotBeNull().ShouldContain("not been seen");
    }

    [Fact]
    public void StringTableCapacity_IsRememberedPerTableAndZeroForUnknownIds()
    {
        NetDecodeState state = new();
        state.AddStringTable(128);
        state.AddStringTable(2048);

        state.StringTableCapacity(0).ShouldBe(128);
        state.StringTableCapacity(1).ShouldBe(2048);
        state.StringTableCapacity(2).ShouldBe(0);
        state.StringTableCapacity(-1).ShouldBe(0);
    }
}
