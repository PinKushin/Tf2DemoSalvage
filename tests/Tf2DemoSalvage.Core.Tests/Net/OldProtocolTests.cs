using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The four protocol-conditional rules, exercised on their older side.
/// </summary>
/// <remarks>
/// Every corpus demo is network protocol 24, so these branches have never run against real
/// data — they were written from the reference implementation and taken on trust. The eras they
/// cover are exactly the ones <c>DECISIONS.md</c> D5 says the corpus is missing:
///
/// | Rule | Boundary | Era it crosses |
/// |---|---|---|
/// | <c>svc_ServerInfo</c> replay flag | &gt;15 | Source 2009 |
/// | 16-byte map hash rather than a 4-byte CRC | &gt;17 | Source MP |
/// | <c>svc_Prefetch</c> index width | &gt;22 | late Source MP |
/// | varint rather than fixed table lengths | &gt;23 | Source 2013 |
///
/// **These four are not the whole list, and were once assumed to be.** Valve's
/// <c>proto_version.h</c> enumerates every boundary the engine still honours; see
/// <c>DECISIONS.md</c> D20. A fifth is implemented — the <c>svc_CreateStringTable</c>
/// compression flag at &gt;14 — and lives in <c>StringTableCodecTests</c> next to the table
/// builder it needs. Four more, all sound-related, are unimplemented because this parser steps
/// over the messages that carry them.
///
/// **What is asserted is alignment, not values.** A branch reading the wrong number of bits does
/// not return a wrong answer, it desynchronises everything after it — so every test here puts a
/// <c>net_Tick</c> behind the message under test and checks it still arrives. That is the same
/// property a real old demo would exercise, and it can be checked now rather than when one
/// finally turns up.
/// </remarks>
public sealed class OldProtocolTests
{
    /// <summary>Writes <c>svc_ServerInfo</c> as the given protocol lays it out.</summary>
    private static byte[] ServerInfo(ushort protocol)
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.ServerInfo)
            .Write(protocol, 16)
            .Write(1, 32)                      // server count
            .Write(1, 1)                       // SourceTV
            .Write(1, 1)                       // dedicated
            .Write(0xDEADBEEF, 32)             // max crc
            .Write(362, 16);                   // max classes

        // Protocol 18 replaced the 4-byte CRC with a 16-byte hash.
        int hashBytes = protocol > 17 ? 16 : 4;
        for (int i = 0; i < hashBytes; i++)
        {
            writer.Write((uint)i, 8);
        }

        writer.Write(0, 8)                     // player slot
            .Write(24, 8)                      // max players
            .Write(0x3C888889, 32)             // interval per tick
            .Write((byte)'w', 8);              // platform

        writer.String("tf").String("cp_dustbowl").String("sky_day01_01").String("a server");

        // The replay flag arrived at protocol 16. Writing it at 15 would be a bit the reader
        // must not consume.
        if (protocol > 15)
        {
            writer.Write(0, 1);
        }

        writer.NetTick(4242, 0, 0);
        return writer.Build();
    }

    [Theory]
    [InlineData(15)]                           // Source 2009: 4-byte CRC, no replay flag
    [InlineData(16)]                           // replay flag appears
    [InlineData(18)]                           // 16-byte hash appears
    [InlineData(24)]                           // current
    public void ServerInfo_LeavesTheReaderAlignedAtEveryProtocol(ushort protocol)
    {
        NetMessageReadResult result = NetMessageReader.Read(ServerInfo(protocol));

        // The tick behind it is the whole assertion: it only decodes if ServerInfo consumed
        // exactly the right number of bits for this protocol.
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
        result.StopReason.ShouldBeNull();
    }

    [Theory]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(24)]
    public void ServerInfo_ReadsItsFieldsAtEveryProtocol(ushort protocol)
    {
        ServerInfoMessage info = NetMessageReader.Read(ServerInfo(protocol))
            .Messages.OfType<ServerInfoMessage>().ShouldHaveSingleItem();

        info.NetworkProtocol.ShouldBe(protocol);
        info.MaxClasses.ShouldBe((ushort)362);
        info.Map.ShouldBe("cp_dustbowl");
        info.GameDirectory.ShouldBe("tf");
    }

    [Theory]
    [InlineData(15, 13)]                       // pre-23: a 13-bit index
    [InlineData(22, 13)]
    [InlineData(23, 14)]                       // widened
    [InlineData(24, 14)]
    public void Prefetch_UsesTheWidthItsProtocolDefines(ushort protocol, int indexBits)
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.Prefetch).Write(1234, indexBits);
        writer.NetTick(777, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build(), StateAt(protocol));

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(777);
    }

    [Theory]
    [InlineData(15)]                           // pre-24: a fixed 17-bit length
    [InlineData(23)]
    public void TempEntities_UsesAFixedLengthBeforeProtocol24(ushort protocol)
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.TempEntities).Write(2, 8).Write(16, 17);
        writer.Write(0xBEEF, 16);
        writer.NetTick(888, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build(), StateAt(protocol));

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(888);
    }

    /// <summary>Decode state reporting a given network protocol.</summary>
    private static NetDecodeState StateAt(ushort protocol) => new()
    {
        ServerInfo = new ServerInfoMessage(
            protocol, 0, false, false, 0, 24, [], 0, 0, 0f, 'w',
            string.Empty, string.Empty, string.Empty, string.Empty, false),
    };
}
