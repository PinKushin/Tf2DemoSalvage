using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// A protocol-15 <c>dem_datatables</c> from each of the two builds that wrote one (B440).
/// </summary>
/// <remarks>
/// **Two numberings share protocol 15.** Build 3862 (June 2009) numbered <c>SendPropType</c> the
/// Orange Box way, String 3 to DataTable 5; the builds after it numbered it with
/// <c>DPT_VectorXY</c> at 3 and DataTable at 6, and still announced 15. Every VectorXY-numbered
/// schema the corpus holds — the two protocol-15 SourceTV demos, and every demo at 16 and above —
/// reads under the old numbering to one table and a class count of 26,207, which is the number B440
/// was filed with; build 3862's reads under the new one to 60,833.
///
/// **So the schema says which numbering wrote it by which one reads it WHOLE**: every table, then a
/// class list that ends within the payload's last byte. Every schema in the corpus that parses ends
/// within seven bits. A reading that stops short of that has misread — the class count it found is
/// a number from the middle of something else — however plausible the tables before it looked.
///
/// Only protocol 15 is read both ways: 14 and below were written the old way and 16 and above the
/// new, both measured, so trying the other numbering there would be guessing where nothing is in
/// doubt.
/// </remarks>
public sealed class Protocol15SchemaTests
{
    /// <summary>The protocol both builds announced.</summary>
    private const ushort Protocol = 15;

    [Test]
    public void Parse_ASchemaTheLaterBuildsWrote_ReadsEveryTypeWithVectorXy()
    {
        DemoSchema read = SendTableParser.Parse(
            SyntheticSchema.Write(Later(), Protocol, vectorXyNumbering: true), Protocol);

        SendTable table = read.Tables.ShouldHaveSingleItem();
        table.Properties.Select(property => property.Type).ShouldBe(
        [
            SendPropType.DataTable, SendPropType.VectorXY, SendPropType.String,
            SendPropType.Array, SendPropType.Float,
        ]);
        table.Properties[0].ReferencedTable.ShouldBe("DT_BaseEntity");
        table.Properties[1].BitCount.ShouldBe(20);
        table.Properties[3].ElementCount.ShouldBe(8);
        table.Properties[4].BitCount.ShouldBe(10);
        read.ServerClasses.ShouldHaveSingleItem().ClassName.ShouldBe("CTFPlayer");
    }

    [Test]
    public void Parse_ASchemaBuild3862Wrote_ReadsEveryTypeWithoutVectorXy()
    {
        // The control: the numbering gcor's 2009 POV carries. It has no VectorXY to write.
        DemoSchema read = SendTableParser.Parse(
            SyntheticSchema.Write(June2009(), Protocol, vectorXyNumbering: false), Protocol);

        SendTable table = read.Tables.ShouldHaveSingleItem();
        table.Properties.Select(property => property.Type).ShouldBe(
            [SendPropType.DataTable, SendPropType.String, SendPropType.Array, SendPropType.Float]);
        table.Properties[0].ReferencedTable.ShouldBe("DT_BaseEntity");
        table.Properties[2].ElementCount.ShouldBe(8);
        read.ServerClasses.ShouldHaveSingleItem().ClassName.ShouldBe("CTFPlayer");
    }

    [Test]
    public void Parse_ALaterSchemaTheOldNumberingReadsWithBitsLeftOver_IsReadWithVectorXy()
    {
        // **The case where the old numbering does not throw.** A String's code in the new numbering
        // is the old numbering's Array, which takes ten bits where a String takes seventy-one. With
        // the range starting at zero those ten bits are zero, the next bit — "another table?" — is
        // zero, and so is the sixteen-bit class count after it: one table, one empty array, no
        // classes, no error. Everything after that is left unread, and that is the only sign.
        DemoSchema sent = new(
            [
                new SendTable("DT_Test", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.String, "m_szName", Flags: 0, ReferencedTable: "",
                        LowValue: 0f, HighValue: 1f, BitCount: 12, ElementCount: 0),
                ]),
            ],
            [new ServerClass(0, "CTest", "DT_Test")]);

        DemoSchema read = SendTableParser.Parse(
            SyntheticSchema.Write(sent, Protocol, vectorXyNumbering: true), Protocol);

        read.Tables.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem().Type
            .ShouldBe(SendPropType.String);
        read.ServerClasses.ShouldHaveSingleItem().TableName.ShouldBe("DT_Test");
    }

    [Test]
    public void Parse_ASchemaBothNumberingsReadWhole_IsReadTheWayBuild3862WroteIt()
    {
        // **The order, which nothing above can see.** Code 3 is String to build 3862 and VectorXY
        // to the builds after it, and both are the numeric shape — so a schema whose only type at 3
        // or above is this one reads whole either way. It is read as the build this project measured
        // first. No real schema can be this one: every TF2 table nests another, and DataTable is 5
        // in one numbering and 6 in the other.
        DemoSchema sent = new(
            [
                new SendTable("DT_Test", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.String, "m_szName", Flags: 0, ReferencedTable: "",
                        LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                ]),
            ],
            [new ServerClass(0, "CTest", "DT_Test")]);

        SendTableParser.Parse(SyntheticSchema.Write(sent, Protocol, vectorXyNumbering: false), Protocol)
            .Tables.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem().Type
            .ShouldBe(SendPropType.String);
    }

    [Test]
    public void Parse_AProtocol15SchemaNeitherNumberingReadsWhole_NamesBothReadings()
    {
        // Cut in half, so it runs out mid-table whichever way it is read. The refusal has to say both
        // were tried — reporting one would read as "this demo's schema is truncated" when it may
        // equally be a numbering nobody has seen yet.
        byte[] whole = SyntheticSchema.Write(Later(), Protocol, vectorXyNumbering: true);

        InvalidDataException refusal = Should.Throw<InvalidDataException>(
            () => SendTableParser.Parse(whole.AsSpan(0, whole.Length / 2), Protocol));

        refusal.Message.ShouldContain("DPT_VectorXY");
        refusal.Message.ShouldContain("ends mid-table");
    }

    [Test]
    public void Parse_ASchemaBuild3862WroteAtProtocol16_IsNotReadTheOldWay()
    {
        // **Only protocol 15 is read both ways.** At 16 every build numbered with VectorXY — the 2011
        // pair is the measurement — so an old-numbered schema there is a misread, not another build.
        byte[] old = SyntheticSchema.Write(June2009(), 16, vectorXyNumbering: false);

        DemoSchema? read;
        try
        {
            read = SendTableParser.Parse(old, 16);
        }
        catch (InvalidDataException)
        {
            read = null;
        }

        // Refusing it and misreading it are both what the new numbering does to it; reading it back
        // as it went in is the one outcome that means the old numbering was tried.
        (read?.Tables.Count == 1 &&
            read.Tables[0].Properties.Select(property => property.Type).SequenceEqual(
                [SendPropType.DataTable, SendPropType.String, SendPropType.Array, SendPropType.Float]))
            .ShouldBeFalse();
    }

    /// <summary>A table a build after June 2009 could send: every shape, VectorXY among them.</summary>
    private static DemoSchema Later() => new(
        [
            new SendTable("DT_TFPlayer", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.DataTable, "baseclass", Flags: 0, ReferencedTable: "DT_BaseEntity",
                    LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                new SendProperty(
                    SendPropType.VectorXY, "m_vecOrigin", Flags: 0, ReferencedTable: "",
                    LowValue: -16384f, HighValue: 16384f, BitCount: 20, ElementCount: 0),
                new SendProperty(
                    SendPropType.String, "m_szName", Flags: 0, ReferencedTable: "",
                    LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                new SendProperty(
                    SendPropType.Array, "m_iAmmo", Flags: 0, ReferencedTable: "",
                    LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 8),
                new SendProperty(
                    SendPropType.Float, "m_flCycle", Flags: 0, ReferencedTable: "",
                    LowValue: 0f, HighValue: 1f, BitCount: 10, ElementCount: 0),
            ]),
        ],
        [new ServerClass(0, "CTFPlayer", "DT_TFPlayer")]);

    /// <summary>The same table as build 3862 could send it, which had no VectorXY.</summary>
    private static DemoSchema June2009() => new(
        [
            new SendTable("DT_TFPlayer", NeedsDecoder: true,
                [.. Later().Tables[0].Properties.Where(property => property.Type != SendPropType.VectorXY)]),
        ],
        [new ServerClass(0, "CTFPlayer", "DT_TFPlayer")]);
}
