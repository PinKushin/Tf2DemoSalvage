using System.Linq;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Tests for the <c>dem_datatables</c> payload — the entity schema embedded in every demo.
/// Written before the parser.
/// </summary>
/// <remarks>
/// This is the payload the whole project premise rests on: a demo describes its own entity
/// layout, so a parser never has to agree with any particular TF2 build. Getting it wrong is
/// not a local failure — the tables are read as one continuous bit stream with no per-table
/// length, so a single wrong field width turns every table after it into noise.
///
/// The flags field is the trap. It is transmitted with
/// <c>SPROP_NUMFLAGBITS_NETWORKED</c> (16) bits, not <c>SPROP_NUMFLAGBITS</c> (17) — the 17th
/// flag is server-side only. 17 is the more prominently named constant.
/// </remarks>
public sealed class SendTableParserTests
{
    private const int ExcludeFlag = 1 << 6;

    /// <summary>Writes one send table in wire order.</summary>
    private static void WriteTable(
        BitWriter writer,
        string name,
        bool needsDecoder,
        params (SendPropType Type, string Name, int Flags, object? Extra)[] props)
    {
        writer.Write(1, 1);                       // another table follows
        writer.Write(needsDecoder ? 1u : 0u, 1);
        writer.String(name);
        writer.Write((uint)props.Length, 10);

        foreach ((SendPropType type, string propName, int flags, object? maybeExtra) in props)
        {
            writer.Write((uint)type, 5).String(propName).Write((uint)flags, 16);

            // Every shape below needs the extra payload, so a null one is a broken fixture
            // rather than a case to handle. Saying that once, here, is what lets the three
            // branches drop their null-forgiving operators: `!` asserts the compiler is wrong
            // without showing why, and Sonar's S8969 correctly objects to at least one of them.
            object extra = maybeExtra
                ?? throw new System.ArgumentException($"'{propName}' needs an Extra value.", nameof(props));

            if (type == SendPropType.DataTable || (flags & ExcludeFlag) != 0)
            {
                writer.String((string)extra);
            }
            else if (type == SendPropType.Array)
            {
                writer.Write((uint)(int)extra, 10);
            }
            else
            {
                (float low, float high, int bits) = ((float, float, int))extra;
                writer.Write((uint)System.BitConverter.SingleToInt32Bits(low), 32);
                writer.Write((uint)System.BitConverter.SingleToInt32Bits(high), 32);
                writer.Write((uint)bits, 7);
            }
        }
    }

    /// <summary>Closes the table list and writes the trailing server class list.</summary>
    private static byte[] Finish(BitWriter writer, params (int Id, string Class, string Table)[] classes)
    {
        writer.Write(0, 1);                       // no more tables
        writer.Write((uint)classes.Length, 16);

        foreach ((int id, string className, string tableName) in classes)
        {
            writer.Write((uint)id, 16).String(className).String(tableName);
        }

        return writer.Build();
    }

    [Fact]
    public void Parse_SingleTableWithNumericProp_ReadsEveryField()
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_BasePlayer", needsDecoder: true,
            (SendPropType.Int, "m_iHealth", 1, (0f, 1024f, 11)));
        byte[] payload = Finish(writer, (0, "CBasePlayer", "DT_BasePlayer"));

        DemoSchema schema = SendTableParser.Parse(payload);

        schema.Tables.Count.ShouldBe(1);
        SendTable table = schema.Tables[0];
        table.Name.ShouldBe("DT_BasePlayer");
        table.NeedsDecoder.ShouldBeTrue();
        table.Properties.Count.ShouldBe(1);

        SendProperty property = table.Properties[0];
        property.Name.ShouldBe("m_iHealth");
        property.Type.ShouldBe(SendPropType.Int);
        property.Flags.ShouldBe(1);
        property.LowValue.ShouldBe(0f);
        property.HighValue.ShouldBe(1024f);
        property.BitCount.ShouldBe(11);
    }

    [Fact]
    public void Parse_DataTableProp_CarriesTheReferencedTableName()
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_TFPlayer", needsDecoder: false,
            (SendPropType.DataTable, "baseclass", 0, "DT_BasePlayer"));
        byte[] payload = Finish(writer, (0, "CTFPlayer", "DT_TFPlayer"));

        SendProperty property = SendTableParser.Parse(payload).Tables[0].Properties[0];

        property.Type.ShouldBe(SendPropType.DataTable);
        property.ReferencedTable.ShouldBe("DT_BasePlayer");
        property.BitCount.ShouldBe(0);
    }

    [Fact]
    public void Parse_ExcludeProp_CarriesTheTableItExcludesFrom()
    {
        // An exclusion removes an inherited property. It reads a table name rather than
        // numeric range, whatever its declared type says.
        BitWriter writer = new();
        WriteTable(writer, "DT_TFPlayer", needsDecoder: false,
            (SendPropType.Int, "m_iHealth", ExcludeFlag, "DT_BasePlayer"));
        byte[] payload = Finish(writer, (0, "CTFPlayer", "DT_TFPlayer"));

        SendProperty property = SendTableParser.Parse(payload).Tables[0].Properties[0];

        property.IsExcluded.ShouldBeTrue();
        property.ReferencedTable.ShouldBe("DT_BasePlayer");
    }

    [Fact]
    public void Parse_ArrayProp_CarriesItsElementCount()
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_Team", needsDecoder: false,
            (SendPropType.Array, "m_iPlayers", 0, 24));
        byte[] payload = Finish(writer, (0, "CTeam", "DT_Team"));

        SendProperty property = SendTableParser.Parse(payload).Tables[0].Properties[0];

        property.Type.ShouldBe(SendPropType.Array);
        property.ElementCount.ShouldBe(24);
    }

    [Fact]
    public void Parse_NumericProp_HasNoReferencedTable()
    {
        // Only datatable and exclude properties name a table. Leaving stale text here would
        // make a numeric property look like a nested one.
        BitWriter writer = new();
        WriteTable(writer, "DT_A", needsDecoder: false,
            (SendPropType.Float, "f", 0, (0f, 1f, 8)),
            (SendPropType.Array, "arr", 0, 4));
        byte[] payload = Finish(writer, (0, "CA", "DT_A"));

        SendTable table = SendTableParser.Parse(payload).Tables[0];

        table.Properties[0].ReferencedTable.ShouldBeEmpty();
        table.Properties[1].ReferencedTable.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(ExcludeFlag, true)]
    [InlineData(ExcludeFlag | 1, true)]
    [InlineData(1 << 5, false)]
    public void IsExcluded_ReadsOnlyItsOwnFlag(int flags, bool expected)
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_A", needsDecoder: false,
            (SendPropType.Int, "p", flags, (flags & ExcludeFlag) != 0 ? "DT_Base" : (object)(0f, 1f, 8)));
        byte[] payload = Finish(writer, (0, "CA", "DT_A"));

        SendTableParser.Parse(payload).Tables[0].Properties[0].IsExcluded.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1 << 10, true)]
    [InlineData((1 << 10) | 1, true)]
    [InlineData(1 << 9, false)]
    public void ChangesOften_ReadsOnlyItsOwnFlag(int flags, bool expected)
    {
        // This flag reorders the flattened property list, and entity deltas index into that
        // list. Misreading it would silently read the wrong fields - see RISKS.md B4.
        BitWriter writer = new();
        WriteTable(writer, "DT_A", needsDecoder: false,
            (SendPropType.Int, "p", flags, (0f, 1f, 8)));
        byte[] payload = Finish(writer, (0, "CA", "DT_A"));

        SendTableParser.Parse(payload).Tables[0].Properties[0].ChangesOften.ShouldBe(expected);
    }

    [Fact]
    public void Parse_MultipleTablesAndProps_KeepsWireOrder()
    {
        // Order is the contract: entity deltas index into the flattened property list, so a
        // reordering here silently reads the wrong fields rather than failing.
        BitWriter writer = new();
        WriteTable(writer, "DT_A", needsDecoder: false,
            (SendPropType.Float, "a1", 0, (0f, 1f, 8)),
            (SendPropType.Vector, "a2", 0, (-1f, 1f, 12)));
        WriteTable(writer, "DT_B", needsDecoder: true,
            (SendPropType.String, "b1", 0, (0f, 0f, 0)));
        byte[] payload = Finish(writer, (0, "CA", "DT_A"), (1, "CB", "DT_B"));

        DemoSchema schema = SendTableParser.Parse(payload);

        schema.Tables.Select(t => t.Name).ShouldBe(["DT_A", "DT_B"]);
        schema.Tables[0].Properties.Select(p => p.Name).ShouldBe(["a1", "a2"]);
        schema.Tables[1].Properties[0].Type.ShouldBe(SendPropType.String);
    }

    [Fact]
    public void Parse_ServerClasses_LinkClassIdsToTables()
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_TFPlayer", needsDecoder: false,
            (SendPropType.Int, "m_iHealth", 1, (0f, 1f, 8)));
        byte[] payload = Finish(writer,
            (0, "CTFPlayer", "DT_TFPlayer"),
            (275, "CObjectSentrygun", "DT_ObjectSentrygun"));

        DemoSchema schema = SendTableParser.Parse(payload);

        schema.ServerClasses.Count.ShouldBe(2);
        schema.ServerClasses[1].Id.ShouldBe(275);
        schema.ServerClasses[1].ClassName.ShouldBe("CObjectSentrygun");
        schema.ServerClasses[1].TableName.ShouldBe("DT_ObjectSentrygun");
    }

    [Fact]
    public void Parse_LooksUpTablesByName()
    {
        BitWriter writer = new();
        WriteTable(writer, "DT_A", needsDecoder: false, (SendPropType.Int, "x", 1, (0f, 1f, 4)));
        WriteTable(writer, "DT_B", needsDecoder: false, (SendPropType.Int, "y", 1, (0f, 1f, 4)));
        byte[] payload = Finish(writer, (0, "CA", "DT_A"));

        DemoSchema schema = SendTableParser.Parse(payload);

        schema.FindTable("DT_B").ShouldNotBeNull().Properties[0].Name.ShouldBe("y");
        schema.FindTable("DT_MISSING").ShouldBeNull();
    }

    [Fact]
    public void Parse_NoTablesAtAll_YieldsAnEmptySchema()
    {
        DemoSchema schema = SendTableParser.Parse(Finish(new BitWriter()));

        schema.Tables.ShouldBeEmpty();
        schema.ServerClasses.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_FlagsUseSixteenBitsNotSeventeen()
    {
        // The trap this whole file exists to guard. Flags are transmitted with
        // SPROP_NUMFLAGBITS_NETWORKED (16); the 17th flag is server-side only. Reading 17
        // would steal a bit from the next field and desynchronise every table that follows,
        // so the second table decoding correctly is the real assertion here.
        const int allSixteen = 0xFFFF;

        BitWriter writer = new();
        WriteTable(writer, "DT_Flags", needsDecoder: false,
            (SendPropType.Int, "loud", allSixteen & ~ExcludeFlag, (0f, 1f, 5)));
        WriteTable(writer, "DT_After", needsDecoder: false,
            (SendPropType.Int, "still_here", 1, (2f, 3f, 6)));
        byte[] payload = Finish(writer, (0, "CFlags", "DT_Flags"));

        DemoSchema schema = SendTableParser.Parse(payload);

        schema.Tables[0].Properties[0].Flags.ShouldBe(allSixteen & ~ExcludeFlag);
        schema.Tables[1].Properties[0].Name.ShouldBe("still_here");
        schema.Tables[1].Properties[0].BitCount.ShouldBe(6);
    }

    [Theory]
    [InlineData(24, 7)]
    [InlineData(15, 7)]
    [InlineData(14, 6)]
    public void BitCountField_NarrowsBelowProtocol15(ushort protocol, int expectedWidth)
    {
        // Measured, not read from a header. Valve's proto_version.h does not mention this - the
        // same blind spot as the message type width (B17) and the SendPropType renumbering (B18).
        //
        // A protocol-14 demo desynchronised after exactly one property, and the raw bits said why:
        // its stream at bit 597 was the protocol-15 stream at bit 598, one bit earlier. The only
        // field that can account for one bit in `type(5) + name + flags(16) + low(32) + high(32)
        // + bits(N)` is the last one.
        //
        // Cross-checked against an unrelated part of the same file: at six bits the schema yields
        // 216 server classes, and svc_ServerInfo independently reports max_classes 216. At seven
        // it yields one table and nonsense.
        //
        // The width is asserted through a round trip rather than by reading a constant, so this
        // measures the decoder rather than restating its source.
        BitWriter writer = new();
        writer.Write(1, 1)                                     // a table follows
              .Write(0, 1)                                     // needsDecoder
              .String("DT_Test")
              .Write(1, 10);                                   // one property
        writer.Write((uint)SendPropType.Int, 5)
              .String("m_nValue")
              .Write(0, 16)                                    // flags
              .Write(0, 32)                                    // low
              .Write(0, 32)                                    // high
              .Write(33, expectedWidth);                      // bit count
        writer.Write(0, 1);                                    // no more tables
        writer.Write(0, 16);                                   // no classes

        DemoSchema schema = SendTableParser.Parse(writer.Build(), protocol);

        schema.Tables.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem()
            .BitCount.ShouldBe(33);
    }

    [Theory]
    [InlineData(24, 6, SendPropType.DataTable)]    // current: VectorXY occupies 3
    [InlineData(24, 5, SendPropType.Array)]
    [InlineData(24, 4, SendPropType.String)]
    [InlineData(15, 5, SendPropType.DataTable)]    // 2009: no VectorXY, everything shifts down
    [InlineData(15, 4, SendPropType.Array)]
    [InlineData(15, 3, SendPropType.String)]
    [InlineData(15, 2, SendPropType.Vector)]       // 2009, below the insertion point: unshifted
    [InlineData(15, 1, SendPropType.Float)]
    [InlineData(15, 0, SendPropType.Int)]
    public void PropertyType_IsNumberedByEra(ushort protocol, uint wireType, SendPropType expected)
    {
        // Valve's dt_common.h, compared between the orangebox branch (2009) and the tf2 branch:
        //
        //   2009     Int=0 Float=1 Vector=2 String=3 Array=4 DataTable=5
        //   current  Int=0 Float=1 Vector=2 VectorXY=3 String=4 Array=5 DataTable=6
        //
        // DPT_VectorXY was inserted at 3 and pushed the three above it up by one. Like the
        // message type width (RISKS B17) this is absent from proto_version.h, so it cannot be
        // found by reading Valve's own list of era differences - only by decoding a demo old
        // enough to carry it.
        //
        // Int, Float and Vector look pointless here because they are unmoved, and they were
        // deliberately left out on that reasoning. That was the wrong call, and mutation testing
        // found it: the old-protocol path is `wireType < VectorXY ? wireType : wireType + 1`, and
        // every shifted case above takes the second branch. Forcing that branch unconditionally
        // therefore changed nothing any of them could see, and the mutant survived.
        //
        // The unmoved types are the only inputs that exercise the guard at all. They distinguish
        // "below the insertion point, so unshifted" from "shift everything", which is a different
        // question from the one the shifted rows ask.
        SendTableParser.MapPropertyType(wireType, protocol).ShouldBe(expected);
    }

}
