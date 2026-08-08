using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The three messages that were truncating packets, and the proof that they no longer do.
/// </summary>
/// <remarks>
/// Network messages carry no length prefix, so an unimplemented type cannot be stepped over —
/// the reader has to stop and abandon the rest of the packet. That cost 131 of the first 200
/// packets in <c>z1800.dem</c> and silently dropped the <c>svc_PacketEntities</c> messages
/// behind them, which is what <c>RISKS.md</c> B13 turned out to be.
///
/// So the assertion that matters in every test here is not the message's own fields. It is that
/// **a later message is still reached**, because that is the property that was broken.
/// </remarks>
public sealed class SkippableMessageTests
{
    /// <summary>Protocol 24, where TempEntities uses a varint length and Prefetch is 14 bits.</summary>
    private const ushort Protocol = 24;

    [Fact]
    public void Prefetch_IsFourteenBitsAtProtocol24_AndTheNextMessageSurvives()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.Prefetch).Write(9001, 14);
        writer.NetTick(555, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(555);
    }

    [Fact]
    public void Sounds_Unreliable_ReadsAnEightBitCountAndSixteenBitLength()
    {
        // Unreliable: a count byte, then a 16-bit length, then that many bits of payload.
        BitWriter writer = new();
        writer.Message(NetMessageType.Sounds)
            .Write(0, 1)            // not reliable
            .Write(3, 8)            // three sounds
            .Write(20, 16);         // twenty bits of payload
        writer.Write(0xABCDE, 20);
        writer.NetTick(777, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(777);
    }

    [Fact]
    public void Sounds_Reliable_TakesTheOtherShapeEntirely()
    {
        // Reliable inverts two fields at once: the count is implied rather than sent, and the
        // length shrinks to eight bits. Reading the unreliable shape here would consume the
        // wrong number of bits and lose whatever follows.
        BitWriter writer = new();
        writer.Message(NetMessageType.Sounds)
            .Write(1, 1)            // reliable
            .Write(12, 8);          // twelve bits of payload, no count field
        writer.Write(0xABC, 12);
        writer.NetTick(888, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(888);
    }

    [Fact]
    public void TempEntities_UsesAVarIntLengthAtProtocol24_AndTheNextMessageSurvives()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.TempEntities).Write(2, 8);
        WriteVarInt(writer, 24);    // twenty-four bits of payload
        writer.Write(0xFEDCBA, 24);
        writer.NetTick(999, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(999);
    }

    [Fact]
    public void AllThreeInOnePacket_StillReachTheMessageBehindThem()
    {
        // The real shape of the problem: a gameplay packet carries several of these before its
        // entity snapshot, so any one of them stopping the reader loses the snapshot.
        BitWriter writer = new();
        writer.Message(NetMessageType.Prefetch).Write(1, 14);
        writer.Message(NetMessageType.Sounds).Write(0, 1).Write(1, 8).Write(8, 16);
        writer.Write(0x5A, 8);
        writer.Message(NetMessageType.TempEntities).Write(1, 8);
        WriteVarInt(writer, 16);
        writer.Write(0xBEEF, 16);
        writer.NetTick(4242, 0, 0);

        NetMessageReadResult result = ReadResult(writer);

        result.StoppedAt.ShouldBeNull();
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
    }

    [Fact]
    public void SetView_IsElevenBits()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.SetView).Write(1500, 11);
        writer.NetTick(111, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(111);
    }

    [Fact]
    public void SignOnState_IsAByteAndAThirtyTwoBitCount()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.SignOnState).Write(6, 8).Write(42, 32);
        writer.NetTick(222, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(222);
    }

    [Fact]
    public void VoiceInit_OmitsTheSampleRateUnlessQualityIs255()
    {
        // The rate is only on the wire for quality 255; older messages imply it from the codec
        // name. Reading sixteen bits unconditionally would eat whatever follows.
        BitWriter writer = new();
        writer.Message(NetMessageType.VoiceInit).String("vaudio_celt").Write(5, 8);
        writer.NetTick(333, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(333);
    }

    [Fact]
    public void VoiceInit_ReadsTheSampleRateWhenQualityIs255()
    {
        // The other half of the same branch. Skipping the rate here would leave sixteen bits
        // to be read as a message type and body.
        BitWriter writer = new();
        writer.Message(NetMessageType.VoiceInit).String("vaudio_speex")
            .Write(255, 8).Write(22050, 16);
        writer.NetTick(444, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(444);
    }

    private static System.Collections.Generic.IReadOnlyList<INetMessage> Read(BitWriter writer) =>
        ReadResult(writer).Messages;

    private static NetMessageReadResult ReadResult(BitWriter writer)
    {
        // ServerInfo supplies the protocol, which decides TempEntities' length encoding and
        // Prefetch's width - so it has to be in state before these messages are read.
        NetDecodeState state = new()
        {
            ServerInfo = new ServerInfoMessage(
                Protocol, 0, false, false, 0, 24, [], 0, 0, 0f, 'l', string.Empty,
                string.Empty, string.Empty, string.Empty, false),
        };

        return NetMessageReader.Read(writer.Build(), state);
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
}
