using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// A protocol-15 demo from each of the two builds that recorded one, read through the whole
/// pipeline (B440).
/// </summary>
/// <remarks>
/// **Protocol 15 does not decide the width of a message's type field.** TF2 build 3862 (June 2009)
/// wrote five bits; the builds after it went on announcing protocol 15 and wrote six, which is what
/// the two SourceTV demos B440 is about carry. So the header's protocol cannot choose, and the demo
/// has to — <c>Protocol15ConformanceTests</c> has the engine's side.
///
/// **What decides it is the first packet's own <c>svc_ServerInfo</c>**, which restates the protocol
/// the header gives. Read at the wrong width it does not: a six-bit ServerInfo read at five puts the
/// type's top bit into the protocol field, and protocol 15 comes back as 30 — the number B440's trace
/// printed. Only the header is written independently of the packet, so agreement with it is the
/// test, and every case below that decides a width is a variation on it.
///
/// **Demos are built with <see cref="SyntheticDemo"/>, and every packet after the first as a reader
/// reads it once ServerInfo has arrived** (<see cref="SyntheticDemo.PacketAfter"/>,
/// <see cref="SyntheticDemo.SixBitPacket"/>): the later packet carries <c>svc_Prefetch</c> and
/// <c>svc_TempEntities</c>, whose widths come from ServerInfo's protocol, so a packet written without
/// it would be read nine bits off and the test would pass on nothing
/// (<c>docs/memory/a-synthetic-packet-without-serverinfo-is-protocol-0.md</c>).
/// </remarks>
public sealed class Protocol15TypeWidthTests
{
    /// <summary>The protocol both builds announced.</summary>
    private const ushort Protocol = 15;

    [Test]
    public void Read_AProtocol15DemoWrittenAtSixBits_ReturnsEveryMessageSent()
    {
        // The later builds. The second packet carries no ServerInfo of its own, so it only reads if
        // the width the first one settled is still in force.
        ServerInfoMessage info = Info(Protocol);
        byte[] demo = SyntheticDemo.From(
            Protocol,
            SyntheticDemo.SixBitPacket(null, 0, Signon(info)),
            SyntheticDemo.SixBitPacket(info, 1, Later()));

        EverythingSent(SyntheticDemo.MessagesIn(demo));
    }

    [Test]
    public void Read_AProtocol15DemoWrittenAtFiveBits_ReturnsEveryMessageSent()
    {
        // Build 3862, the control: the protocol-15 demo this project could always read, and the one
        // gcor's 2009 POV is. Whatever decides the width must not take this one to six.
        ServerInfoMessage info = Info(Protocol);
        byte[] demo = SyntheticDemo.From(
            Protocol,
            SyntheticDemo.Packet(Protocol, 0, Signon(info)),
            SyntheticDemo.PacketAfter(info, 1, Later()));

        EverythingSent(SyntheticDemo.MessagesIn(demo));
    }

    [TestCase(true, NetMessage.TypeBits)]
    [TestCase(false, NetMessage.OldTypeBits)]
    public void MessageTypeBits_AfterTheFirstPacket_IsTheWidthItWasWrittenAt(bool sixBits, int expected)
    {
        ServerInfoMessage info = Info(Protocol);
        DemoCommand first = sixBits
            ? SyntheticDemo.SixBitPacket(null, 0, info)
            : SyntheticDemo.Packet(Protocol, 0, info);

        NetDecodeState state = new() { NetworkProtocol = Protocol };
        _ = NetMessageReader.Read(first.Payload.Span, state);

        state.MessageTypeBits.ShouldBe(expected);
    }

    [Test]
    public void MessageTypeBits_WhenTheFirstPacketCarriesNoServerInfo_IsFive()
    {
        // Nothing in this packet can say which build wrote it, so it is read the way build 3862 —
        // the protocol-15 build this project measured first — wrote it. The tick is the control
        // that the read happened at all.
        DemoCommand first = SyntheticDemo.Packet(Protocol, 0, new NetTickMessage(4242, 0, 0));

        NetDecodeState state = new() { NetworkProtocol = Protocol };
        NetMessageReadResult result = NetMessageReader.Read(first.Payload.Span, state);

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
        state.MessageTypeBits.ShouldBe(NetMessage.OldTypeBits);
    }

    [Test]
    public void MessageTypeBits_WhenServerInfoRestatesAnotherProtocol_IsFive()
    {
        // Six bits read this packet's ServerInfo cleanly — and it says 24 in a demo whose header
        // says 15. Reading A ServerInfo is not the evidence; restating the header's protocol is,
        // because the header is the one thing written independently of the packet.
        DemoCommand first = SyntheticDemo.SixBitPacket(null, 0, Info(24));

        NetDecodeState state = new() { NetworkProtocol = Protocol };
        _ = NetMessageReader.Read(first.Payload.Span, state);

        state.MessageTypeBits.ShouldBe(NetMessage.OldTypeBits);
    }

    [TestCase((ushort)14, NetMessage.OldTypeBits)]
    [TestCase((ushort)16, NetMessage.TypeBits)]
    public void MessageTypeBits_AtAProtocolWithOneWidth_IsThatWidthWhateverThePacketSays(
        ushort protocol, int expected)
    {
        // **Only 15 is asked.** Every build at 14 and below wrote five bits and every build at 16
        // and above six, both measured; a packet at the other width there is not another build, so
        // it must not move the width. Each packet here restates its protocol at the width it was
        // written at — the thing that decides 15 — and is written at the width its protocol never
        // had.
        DemoCommand first = protocol > Protocol
            ? SyntheticDemo.Packet(Protocol, 0, Info(protocol))
            : SyntheticDemo.SixBitPacket(null, 0, Info(protocol));

        NetDecodeState state = new() { NetworkProtocol = protocol };
        _ = NetMessageReader.Read(first.Payload.Span, state);

        state.MessageTypeBits.ShouldBe(expected);
    }

    [TestCase(true, NetMessage.TypeBits)]
    [TestCase(false, NetMessage.OldTypeBits)]
    public void Write_ServerInfoAtTheWidthTheReaderSettled_ReproducesItsBits(bool sixBits, int typeBits)
    {
        // A writer told the width the first packet settled writes ServerInfo back bit for bit at
        // either build's width — the writer side of B451, whose census forgot to tell it.
        ServerInfoMessage info = Info(Protocol);
        DemoCommand first = sixBits
            ? SyntheticDemo.SixBitPacket(null, 0, Signon(info))
            : SyntheticDemo.Packet(Protocol, 0, Signon(info));
        NetDecodeState read = new() { NetworkProtocol = Protocol };
        NetMessageReadResult result = NetMessageReader.Read(first.Payload.Span, read);

        NetDecodeState write = new() { NetworkProtocol = Protocol, MessageTypeBits = read.MessageTypeBits };
        BitWriter writer = new();
        NetMessageWriter.TryWrite(writer, result.Messages[0], write).ShouldBeTrue();

        // The fixture is written by the same writer, so the width is pinned independently of it.
        int length = result.MessageStartBits[1] - result.MessageStartBits[0];
        writer.BitCount.ShouldBe(length);
        BitReader typeField = new(writer.Build());
        typeField.ReadUInt32(typeBits).ShouldBe((uint)NetMessageType.ServerInfo);
        typeField.ReadUInt32(16).ShouldBe((uint)Protocol);
        BitReader expected = new(first.Payload.Span);
        BitReader actual = new(writer.Build());
        for (int bit = 0; bit < length; bit++)
        {
            actual.ReadBit().ShouldBe(expected.ReadBit(), $"bit {bit}");
        }
    }

    /// <summary>What a SourceTV signon opens with: ServerInfo, then a tick and the replicated cvars.</summary>
    private static INetMessage[] Signon(ServerInfoMessage info) =>
    [
        info,
        new NetTickMessage(4242, 7, 8),
        new SetConVarMessage([new KeyValuePair<string, string>("tv_transmitall", "1")]),
    ];

    /// <summary>
    /// A later packet, carrying the two messages whose widths come from ServerInfo's protocol: a
    /// 13-bit sound index and a 17-bit effect length, both below their protocols' boundaries.
    /// </summary>
    private static INetMessage[] Later() =>
    [
        new NetTickMessage(4243, 0, 0),
        new PrefetchMessage(1234),
        new TempEntitiesMessage(2, 16, new byte[] { 0xEF, 0xBE }),
    ];

    /// <summary>Asserts the demo read back to exactly what <see cref="Signon"/> and <see cref="Later"/> sent.</summary>
    /// <remarks>
    /// <c>net_NOP</c> is left out: a packet is padded to a byte, and padding at least as wide as a
    /// type field reads as one — a fact about the padding, not about either build.
    /// </remarks>
    private static void EverythingSent(IReadOnlyList<INetMessage> messages)
    {
        List<INetMessage> read = [.. messages.Where(message => message is not NetEmptyMessage)];

        read.Select(message => message.Type).ShouldBe(
        [
            NetMessageType.ServerInfo, NetMessageType.NetTick, NetMessageType.SetConVar,
            NetMessageType.NetTick, NetMessageType.Prefetch, NetMessageType.TempEntities,
        ]);

        ServerInfoMessage info = read[0].ShouldBeOfType<ServerInfoMessage>();
        info.NetworkProtocol.ShouldBe(Protocol);
        info.IsSourceTv.ShouldBeTrue();
        info.MaxClasses.ShouldBe((ushort)249);
        info.Map.ShouldBe("cp_badlands");
        info.ServerName.ShouldBe("SourceTV");

        NetTickMessage tick = read[1].ShouldBeOfType<NetTickMessage>();
        tick.Tick.ShouldBe(4242);
        tick.HostFrameTimeRaw.ShouldBe((ushort)7);
        tick.HostFrameTimeStdDevRaw.ShouldBe((ushort)8);

        read[2].ShouldBeOfType<SetConVarMessage>().Variables
            .ShouldBe([new KeyValuePair<string, string>("tv_transmitall", "1")]);

        read[3].ShouldBeOfType<NetTickMessage>().Tick.ShouldBe(4243);
        read[4].ShouldBeOfType<PrefetchMessage>().SoundIndex.ShouldBe(1234);

        TempEntitiesMessage effects = read[5].ShouldBeOfType<TempEntitiesMessage>();
        effects.Count.ShouldBe(2);
        effects.BodyBits.ShouldBe(16);
        effects.Body.ToArray().ShouldBe(new byte[] { 0xEF, 0xBE });
    }

    /// <summary>
    /// A SourceTV server's <c>svc_ServerInfo</c> at the given protocol, with the values B440's CEVO
    /// demo carries.
    /// </summary>
    /// <remarks>
    /// The map field is four bytes below protocol 18 and a sixteen-byte hash from it on — the
    /// reader sizes it from this message's own protocol, so the fixture has to agree.
    /// </remarks>
    private static ServerInfoMessage Info(ushort protocol) => new(
        NetworkProtocol: protocol,
        ServerCount: 11,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0xFFFF_FFFF,
        MaxClasses: 249,
        MapHash: protocol > 17
            ? [.. Enumerable.Range(1, 16).Select(value => (byte)value)]
            : [0x3D, 0x8E, 0x60, 0x75],
        PlayerSlot: 4,
        MaxPlayers: 15,
        IntervalPerTick: 0.015f,
        Platform: 'l',
        GameDirectory: "tf",
        Map: "cp_badlands",
        Skybox: "sky_badlands_01",
        ServerName: "SourceTV",
        IsReplay: false);
}
