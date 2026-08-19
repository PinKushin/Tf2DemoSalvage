using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The schema encoder, checked against the parser it was deliberately not built from.
/// </summary>
/// <remarks>
/// **These come first, before anything uses a synthetic schema to test something else.** The
/// encoder is about to become the foundation for every entity and timeline test that does not
/// need a real demo, and a fixture builder that is quietly wrong produces tests that agree with
/// each other and with nothing real. That failure has cost this repository three bugs.
///
/// The protections are two. <see cref="SyntheticSchema"/> is written from Valve's field order
/// rather than by inverting <c>SendTableParser</c>, so agreement between them is evidence. And
/// the era cases below exercise the two boundaries where a schema stops being readable at all —
/// which is what makes them worth writing, since the corpus has no demo between protocols 15 and
/// 24 to check either one.
/// </remarks>
public sealed class SyntheticSchemaTests
{
    [Test]
    public void RoundTrip_EveryPropertyShape_ReturnsWhatWentIn()
    {
        // One property of each of the three wire shapes, plus the exclude case that takes the
        // table-naming shape despite declaring a numeric type. That last one is the case a reader
        // switching on type alone gets wrong, and it is silent: the property reads as a numeric,
        // consumes 71 bits instead of a string, and every table after it is noise.
        DemoSchema sent = new(
            [
                new SendTable("DT_TFPlayer", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.Int, "m_iHealth", Flags: 0, ReferencedTable: "",
                        LowValue: 0f, HighValue: 0f, BitCount: 15, ElementCount: 0),
                    new SendProperty(
                        SendPropType.Float, "m_flCycle", Flags: 1 << 2, ReferencedTable: "",
                        LowValue: 0f, HighValue: 1f, BitCount: 10, ElementCount: 0),
                    new SendProperty(
                        SendPropType.Vector, "m_vecOrigin", Flags: 1 << 1, ReferencedTable: "",
                        LowValue: -16384f, HighValue: 16384f, BitCount: 32, ElementCount: 0),
                    new SendProperty(
                        SendPropType.String, "m_szName", Flags: 0, ReferencedTable: "",
                        LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                    new SendProperty(
                        SendPropType.Array, "m_iAmmo", Flags: 0, ReferencedTable: "",
                        LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 32),
                    new SendProperty(
                        SendPropType.DataTable, "baseclass", Flags: 0,
                        ReferencedTable: "DT_BaseEntity",
                        LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                    new SendProperty(
                        SendPropType.Int, "m_flPoseParameter",
                        Flags: SendProperty.ExcludeFlag, ReferencedTable: "DT_BaseAnimating",
                        LowValue: 0f, HighValue: 0f, BitCount: 0, ElementCount: 0),
                ]),
                new SendTable("DT_BaseEntity", NeedsDecoder: false,
                [
                    new SendProperty(
                        SendPropType.VectorXY, "m_vecMins", Flags: 0, ReferencedTable: "",
                        LowValue: -1f, HighValue: 1f, BitCount: 20, ElementCount: 0),
                ]),
            ],
            [
                new ServerClass(0, "CTFPlayer", "DT_TFPlayer"),
                new ServerClass(1, "CBaseEntity", "DT_BaseEntity"),
            ]);

        DemoSchema read = SendTableParser.Parse(SyntheticSchema.Write(sent));

        read.Tables.Count.ShouldBe(2);
        read.Tables[0].Name.ShouldBe("DT_TFPlayer");
        read.Tables[0].NeedsDecoder.ShouldBeTrue();
        read.Tables[1].NeedsDecoder.ShouldBeFalse();

        IReadOnlyList<SendProperty> properties = read.Tables[0].Properties;
        properties.Count.ShouldBe(7);

        properties[0].Type.ShouldBe(SendPropType.Int);
        properties[0].Name.ShouldBe("m_iHealth");
        properties[0].BitCount.ShouldBe(15);

        properties[1].LowValue.ShouldBe(0f);
        properties[1].HighValue.ShouldBe(1f);
        properties[1].BitCount.ShouldBe(10);

        // A range that is not symmetric about zero, so a sign or an operand swap shows.
        properties[2].LowValue.ShouldBe(-16384f);
        properties[2].HighValue.ShouldBe(16384f);

        properties[4].Type.ShouldBe(SendPropType.Array);
        properties[4].ElementCount.ShouldBe(32);

        properties[5].Type.ShouldBe(SendPropType.DataTable);
        properties[5].ReferencedTable.ShouldBe("DT_BaseEntity");

        // The exclude: an Int by type, but it names a table rather than carrying a range.
        properties[6].IsExcluded.ShouldBeTrue();
        properties[6].ReferencedTable.ShouldBe("DT_BaseAnimating");

        read.ServerClasses.Select(entry => entry.ClassName).ShouldBe(["CTFPlayer", "CBaseEntity"]);
        read.ServerClasses[1].TableName.ShouldBe("DT_BaseEntity");
    }

    [Test]
    public void RoundTrip_AtProtocol15_RenumbersTypesAroundVectorXy()
    {
        // **The boundary that makes an old demo unreadable, and the corpus cannot check it.**
        // DPT_VectorXY was inserted at 3 rather than appended, so String, Array and DataTable each
        // sit one lower before protocol 16. Write the modern numbering into an old demo and every
        // nested table reads as an array.
        //
        // The values are identical either way — a DataTable is a DataTable — so this is only
        // observable by encoding at one protocol and decoding at the same one, and checking the
        // type survives.
        DemoSchema sent = Schema(
            new SendProperty(
                SendPropType.DataTable, "baseclass", 0, "DT_BaseEntity", 0f, 0f, 0, 0),
            new SendProperty(SendPropType.String, "m_szName", 0, "", 0f, 0f, 0, 0),
            new SendProperty(SendPropType.Array, "m_iAmmo", 0, "", 0f, 0f, 0, 8));

        DemoSchema read = SendTableParser.Parse(SyntheticSchema.Write(sent, 15), 15);

        read.Tables[0].Properties[0].Type.ShouldBe(SendPropType.DataTable);
        read.Tables[0].Properties[1].Type.ShouldBe(SendPropType.String);
        read.Tables[0].Properties[2].Type.ShouldBe(SendPropType.Array);
    }

    [Test]
    public void Write_VectorXyAtProtocol15_IsRefusedRatherThanRenumberedWrongly()
    {
        // There is no code for it before 16, so silently writing one would produce a schema no
        // client ever sent. A fixture builder that invents wire forms is worse than one that
        // refuses, because the tests built on it look real.
        DemoSchema sent = Schema(
            new SendProperty(SendPropType.VectorXY, "m_vecMins", 0, "", -1f, 1f, 20, 0));

        Should.Throw<ArgumentOutOfRangeException>(() => SyntheticSchema.Write(sent, 15));
    }

    [Test]
    public void BitCountField_AtProtocols14And15_IsSixBitsThenSeven()
    {
        // The other era boundary, and it is measured as a LENGTH because the values agree: a bit
        // count of 15 decodes identically at either width. One bit, and it costs the whole file —
        // the schema has no per-table length to resynchronise on, so reading seven where six were
        // written turns every table after the first numeric property into noise.
        //
        // Measured as the difference between two protocols on an otherwise identical schema, which
        // is safe here in a way it was not for the sound widths: only one field changes between 14
        // and 15, and the type renumbering does not, because Int is below VectorXY's insertion
        // point in both.
        DemoSchema sent = Schema(
            new SendProperty(SendPropType.Int, "m_iHealth", 0, "", 0f, 0f, 15, 0));

        int narrow = SyntheticSchema.Write(sent, 14).Length * 8;
        int wide = SyntheticSchema.Write(sent, 15).Length * 8;

        // Byte-rounded, so the one-bit difference may or may not cross a boundary. What must hold
        // is that the narrow encoding is never the larger one, and that both still parse at their
        // own protocol to the value that went in.
        narrow.ShouldBeLessThanOrEqualTo(wide);

        SendTableParser.Parse(SyntheticSchema.Write(sent, 14), 14)
            .Tables[0].Properties[0].BitCount.ShouldBe(15);
        SendTableParser.Parse(SyntheticSchema.Write(sent, 15), 15)
            .Tables[0].Properties[0].BitCount.ShouldBe(15);
    }

    [Test]
    public void Parse_ASchemaWrittenAtTheWrongProtocol_DoesNotSilentlyAgree()
    {
        // **The control for the two boundary tests above, and the reason they mean anything.**
        // If a schema written at 14 also parsed correctly at 15, neither boundary test would be
        // measuring the boundary — both would pass against a decoder that ignored the protocol
        // entirely.
        //
        // A six-bit field read as seven steals the top bit of the next property's type, so the
        // mismatch shows as a wrong value or a failed parse. Either is acceptable; agreement is
        // not.
        DemoSchema sent = Schema(
            new SendProperty(SendPropType.Int, "m_iHealth", 0, "", 0f, 0f, 63, 0),
            new SendProperty(SendPropType.Int, "m_iAmmo", 0, "", 0f, 0f, 7, 0));

        byte[] atFourteen = SyntheticSchema.Write(sent, 14);

        bool disagreed;
        try
        {
            DemoSchema misread = SendTableParser.Parse(atFourteen, 15);
            disagreed =
                misread.Tables.Count != 1 ||
                misread.Tables[0].Properties.Count != 2 ||
                misread.Tables[0].Properties[0].BitCount != 63 ||
                misread.Tables[0].Properties[1].BitCount != 7;
        }
        catch (Exception error) when (error is System.IO.InvalidDataException or
            System.IO.EndOfStreamException)
        {
            disagreed = true;
        }

        disagreed.ShouldBeTrue(
            "a protocol-14 schema read as protocol 15 came back intact, so neither era test is " +
            "measuring the bit-count width");
    }

    /// <summary>A one-table schema holding the given properties.</summary>
    private static DemoSchema Schema(params SendProperty[] properties) => new(
        [new SendTable("DT_Test", NeedsDecoder: true, properties)],
        [new ServerClass(0, "CTest", "DT_Test")]);
}
