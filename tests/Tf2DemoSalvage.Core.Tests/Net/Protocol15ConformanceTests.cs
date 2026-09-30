using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Protocol 15 was recorded by two engine builds that wrote different bits, and nothing Valve
/// versioned tells them apart (B440).
/// </summary>
/// <remarks>
/// **What the engine did, from its own headers.** Two things changed between TF2 build 3862
/// (4 June 2009, the protocol-15 client this project measured) and the builds that recorded the two
/// protocol-15 SourceTV demos in B440 — and <c>PROTOCOL_VERSION</c> moved for neither:
///
/// - **The message type field grew from five bits to six.** The engine sizes it so that
///   <c>2^NETMSG_TYPE_BITS &gt; SVC_LASTMSG</c>. The 2007 Orange Box handler list ends at
///   <c>SVC_GetCvarValue</c>, id 31 (hl2sdk <c>orangebox</c>, <c>public/inetmsghandler.h:138</c>),
///   which five bits carry; source-sdk-2013's declares <c>SVC_CmdKeyValues</c> after it
///   (<c>public/inetmsghandler.h:148-149</c>), and id 32 is the first five bits cannot.
/// - **<c>SendPropType</c> gained <c>DPT_VectorXY</c> at 3**, pushing String, Array and DataTable up
///   one (<c>public/dt_common.h:108-114</c> against hl2sdk <c>orangebox</c>
///   <c>public/dt_common.h:103-108</c>). The hl2sdk branch TF2 builds against took it in
///   <c>c789d33e</c> on 14 August 2009, the day after TF2's August 2009 update — two months after
///   build 3862, at the same protocol.
///
/// <c>proto_version.h</c> — which lists every boundary the live engine still branches on — has no
/// constant between <c>PROTOCOL_VERSION_14</c> and <c>PROTOCOL_VERSION_REPLAY</c>
/// (<c>common/proto_version.h:40-47</c>). So a demo header saying 15 cannot say which build wrote
/// it, and the parser has to read that from the demo's own bits.
///
/// **And the container is not where they differ.** <c>demoformat.h</c> declares one
/// <c>demoheader_t</c> and one <c>democmdinfo_t</c> for every recording
/// (<c>public/demofile/demoformat.h:48-61, 78-157</c>) — a SourceTV demo is told apart only by the
/// text <c>"SourceTV Demo"</c> in <c>clientname</c>. The filed hypothesis that SourceTV at this era
/// laid its packets out differently is refuted by the corpus test beside this one, which finds the
/// first signon's <c>svc_ServerInfo</c> starting at bit 0 of the payload.
/// </remarks>
public sealed class Protocol15ConformanceTests
{
    /// <summary>Every protocol boundary the engine still honours.</summary>
    private const string ProtoVersion = "src/common/proto_version.h";

    /// <summary>A handler per message the engine processes.</summary>
    private const string Handlers = "src/public/inetmsghandler.h";

    /// <summary>Where <c>SendPropType</c> is declared.</summary>
    private const string DataTables = "src/public/dt_common.h";

    /// <summary>The protocol both builds announced.</summary>
    private const ushort Protocol = 15;

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void ProtoVersion_Protocol15_HasNoConstantToTellItsBuildsApart()
    {
        // The two constants either side, so this cannot pass on a file it failed to read: an empty
        // extraction has no 14 and no 16 either.
        IReadOnlyDictionary<string, int> versions = SourceSdk.Constants(ProtoVersion);

        versions["PROTOCOL_VERSION_14"].ShouldBe(14);
        versions["PROTOCOL_VERSION_REPLAY"].ShouldBe(16);
        versions.Values.ShouldNotContain((int)Protocol);
    }

    [Test]
    public void MessageTypeField_TheFirstIdFiveBitsCannotCarry_IsSvcCmdKeyValues()
    {
        // The engine declares it; its id is 2^5, one past what five bits hold; and six hold it.
        SourceSdk.Text(Handlers).ShouldNotBeNull().ShouldContain("class SVC_CmdKeyValues;");

        ((int)NetMessageType.GetCvarValue).ShouldBe((1 << NetMessage.OldTypeBits) - 1);
        ((int)NetMessageType.CmdKeyValues).ShouldBe(1 << NetMessage.OldTypeBits);
        ((int)NetMessageType.CmdKeyValues).ShouldBeLessThan(1 << NetMessage.TypeBits);
    }

    [TestCase(NetMessage.OldTypeBits)] // build 3862, June 2009
    [TestCase(NetMessage.TypeBits)]    // the builds after it, at the same protocol
    public void Read_AProtocol15PacketAtEitherWidth_RestatesItsProtocol(int typeBits)
    {
        // Bits laid out by hand from the engine's order rather than by NetMessageWriter, so the
        // fixture and the reader agree only if both are right.
        BitWriter writer = new();
        WriteServerInfo(writer, typeBits);
        writer.Write((uint)NetMessageType.NetTick, typeBits).Write(4242, 32).Write(7, 16).Write(8, 16);

        NetMessageReadResult result = NetMessageReader.Read(
            writer.Build(), new NetDecodeState { NetworkProtocol = Protocol });

        result.StopReason.ShouldBeNull();
        ServerInfoMessage info = result.Messages.OfType<ServerInfoMessage>().ShouldHaveSingleItem();
        info.NetworkProtocol.ShouldBe(Protocol);
        info.MaxClasses.ShouldBe((ushort)249);
        info.Map.ShouldBe("cp_badlands");
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
    }

    [Test]
    public void SendPropType_TheNumberingDtCommonDeclares_IsTheOneModernDemosAreReadIn()
    {
        // Valve's numbering, read from Valve's header rather than typed a second time. Seven
        // members, DPT_Int through DPT_DataTable; the two after them are compiled out.
        IReadOnlyDictionary<string, int> declared = SourceSdk.Enumerators(DataTables, "SendPropType");

        declared.Count.ShouldBe(7, string.Join(", ", declared.Keys));

        foreach ((string member, int value) in declared)
        {
            SendTableParser.MapPropertyType((uint)value, 24).ToString()
                .ShouldBe(member["DPT_".Length..], member);
        }
    }

    [TestCase(false)] // build 3862's numbering: the orangebox list, no DPT_VectorXY
    [TestCase(true)]  // the later builds': dt_common.h's own
    public void Parse_AProtocol15SchemaInEitherNumbering_ReadsTheTypesDtCommonNames(bool vectorXy)
    {
        IReadOnlyDictionary<string, int> declared = SourceSdk.Enumerators(DataTables, "SendPropType");
        declared.Count.ShouldBe(7);

        // One property per type the numbering has, each carrying the code that numbering gives it.
        // Without DPT_VectorXY, every type above it sits one lower (orangebox dt_common.h:103-108).
        List<(string Type, uint Code)> properties =
        [
            .. declared
                .Where(member => vectorXy || member.Key != "DPT_VectorXY")
                .OrderBy(member => member.Value)
                .Select((member, position) => (
                    member.Key["DPT_".Length..],
                    (uint)(vectorXy ? member.Value : position))),
        ];

        DemoSchema schema = SendTableParser.Parse(Schema(properties), Protocol);

        schema.Tables.ShouldHaveSingleItem().Properties.Select(property => property.Type.ToString())
            .ShouldBe(properties.Select(property => property.Type));
        schema.ServerClasses.ShouldHaveSingleItem().TableName.ShouldBe("DT_Protocol15");
    }

    /// <summary>
    /// <c>svc_ServerInfo</c> as a protocol-15 server lays it out, behind a type field of the width
    /// given: a four-byte map CRC rather than the hash protocol 18 brought, and no replay flag.
    /// </summary>
    private static void WriteServerInfo(BitWriter writer, int typeBits)
    {
        writer.Write((uint)NetMessageType.ServerInfo, typeBits)
            .Write(Protocol, 16)
            .Write(11, 32)                     // server count
            .Write(1, 1)                       // SourceTV
            .Write(1, 1)                       // dedicated
            .Write(0xFFFFFFFF, 32)             // client CRC
            .Write(249, 16)                    // max classes
            .Write(0x75608E3D, 32)             // map CRC
            .Write(4, 8)                       // player slot
            .Write(15, 8)                      // max players
            .Write(0x3C75C28F, 32)             // interval per tick, 0.015
            .Write((byte)'l', 8);              // platform

        writer.WriteString("tf").WriteString("cp_badlands").WriteString("sky_badlands_01")
            .WriteString("SourceTV");
    }

    /// <summary>
    /// A <c>dem_datatables</c> payload of one table holding the given properties, and one class.
    /// </summary>
    /// <remarks>
    /// Written from the engine's order — a set bit per table, then needs-decoder, name, a ten-bit
    /// count, and per property a five-bit type, name, sixteen flags and the shape that type takes —
    /// rather than through <c>SyntheticSchema</c>, so the numbers on the wire are the SDK's.
    /// </remarks>
    private static byte[] Schema(List<(string Type, uint Code)> properties)
    {
        BitWriter writer = new();
        writer.WriteBit(true).WriteBit(true).WriteString("DT_Protocol15")
            .Write((uint)properties.Count, 10);

        foreach ((string type, uint code) in properties)
        {
            writer.Write(code, 5).WriteString("m_" + type).Write(0, 16);

            switch (type)
            {
                case "DataTable":
                    writer.WriteString("DT_Referenced");
                    break;

                case "Array":
                    writer.Write(4, 10);
                    break;

                default:
                    writer.Write(0, 32).Write(0x3F800000, 32).Write(12, 7);
                    break;
            }
        }

        writer.WriteBit(false);
        writer.Write(1, 16).Write(0, 16).WriteString("CProtocol15").WriteString("DT_Protocol15");
        return writer.Build();
    }
}
