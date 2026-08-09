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

    [Fact]
    public void UserMessage_IsTypeThenAnElevenBitLength()
    {
        // The message that was costing both SourceTV demos their run: it appears at packet 336
        // in each, and losing that packet's snapshot broke every delta after it.
        BitWriter writer = new();
        writer.Message(NetMessageType.UserMessage)
            .Write(4, 8)            // user message type
            .Write(24, 11);         // twenty-four bits of payload
        writer.Write(0xABCDEF, 24);
        writer.NetTick(1212, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1212);
    }

    [Fact]
    public void EntityMessage_CarriesAnIndexAndClassBeforeItsLength()
    {
        // Index and class id come first, so a reader that went straight for the length would
        // take twenty of their bits as the length and run off the end.
        BitWriter writer = new();
        writer.Message(NetMessageType.EntityMessage)
            .Write(1500, 11)        // entity index
            .Write(246, 9)          // class id
            .Write(16, 11);         // sixteen bits of payload
        writer.Write(0xBEEF, 16);
        writer.NetTick(1313, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1313);
    }

    [Fact]
    public void VoiceData_IsTwoBytesThenASixteenBitLength()
    {
        // 420 of these appear in z1800, which is a Mumble-era demo carrying voice comms. Each
        // one stopped its packet and cost the snapshot behind it.
        BitWriter writer = new();
        writer.Message(NetMessageType.VoiceData)
            .Write(3, 8)            // client
            .Write(1, 8)            // proximity
            .Write(32, 16);         // thirty-two bits of voice payload
        writer.Write(0xDEADBEEF, 32);
        writer.NetTick(1414, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1414);
    }

    [Fact]
    public void SetPause_IsASingleBit()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.SetPause).Write(1, 1);
        writer.NetTick(1515, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1515);
    }

    [Fact]
    public void FixAngle_IsAFlagAndThreeSixteenBitAngles()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.FixAngle)
            .Write(1, 1).Write(16384, 16).Write(8192, 16).Write(0, 16);
        writer.NetTick(1616, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1616);
    }

    [Fact]
    public void File_IsAnIdAThenNameAndAFlag()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.File).Write(7, 32).String("maps/cp_process.bsp").Write(1, 1);
        writer.NetTick(1717, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1717);
    }

    [Fact]
    public void GetCvarValue_IsACookieAndAName()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.GetCvarValue).Write(99, 32).String("sv_cheats");
        writer.NetTick(1818, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1818);
    }

    [Fact]
    public void Menu_LengthIsInBytesNotBits()
    {
        // The one trap in this group: Menu and CmdKeyValues state their payload length in
        // bytes, while every other length in this format is in bits. Reading it as bits
        // consumes an eighth of the payload and leaves the rest to be read as messages.
        BitWriter writer = new();
        writer.Message(NetMessageType.Menu).Write(3, 16).Write(2, 16);
        writer.Write(0xBEEF, 16);              // two bytes of payload
        writer.NetTick(1919, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(1919);
    }

    [Fact]
    public void CmdKeyValues_LengthIsAlsoInBytes()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.CmdKeyValues).Write(3, 32);
        writer.Write(0xAB, 8).Write(0xCD, 8).Write(0xEF, 8);
        writer.NetTick(2020, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(2020);
    }

    [Fact]
    public void BspDecal_ReadsOnlyThePresentCoordinateAxes()
    {
        // The only variable-width message in this group. Three presence bits choose which axes
        // follow, and each present axis is SPROP_COORD - the same encoding entity positions
        // use. Reading three coordinates unconditionally would consume bits that are not there.
        BitWriter writer = new();
        writer.Message(NetMessageType.BspDecal)
            .Write(1, 1).Write(0, 1).Write(1, 1);   // x present, y absent, z present
        WriteCoord(writer, 5, 16);                  // x
        WriteCoord(writer, 3, 0);                   // z
        writer.Write(12, 9);                        // texture index, 9 bits
        writer.Write(1, 1);                         // entity and model indices follow
        writer.Write(34, 11).Write(56, 13);
        writer.Write(0, 1);                         // low priority
        writer.NetTick(2121, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(2121);
    }

    [Fact]
    public void BspDecal_WithoutAnEntity_OmitsBothIndices()
    {
        // The flag that cost a day. Entity and model indices are present only when a bit says
        // so - a world decal carries neither - and an earlier version read three fixed 16-bit
        // fields here because the reference parser's struct declares them as u16. The struct is
        // its in-memory shape; its reader uses 9, 11 and 13 bits. RISKS B16.
        BitWriter writer = new();
        writer.Message(NetMessageType.BspDecal).Write(1, 1).Write(0, 1).Write(0, 1);
        WriteCoord(writer, 5, 16);
        writer.Write(12, 9);                        // texture index
        writer.Write(0, 1);                         // no entity, so no indices follow
        writer.Write(0, 1);                         // low priority
        writer.NetTick(2323, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(2323);
    }

    [Fact]
    public void BspDecal_WithNoAxesPresent_ReadsNoCoordinatesAtAll()
    {
        BitWriter writer = new();
        writer.Message(NetMessageType.BspDecal).Write(0, 1).Write(0, 1).Write(0, 1);
        writer.Write(12, 9).Write(0, 1).Write(0, 1);   // texture, no entity, low priority
        writer.NetTick(2222, 0, 0);

        Read(writer).OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(2222);
    }

    /// <summary>Writes one SPROP_COORD value: presence bits, sign, integer minus one, fraction.</summary>
    private static void WriteCoord(BitWriter writer, int intPart, int frac)
    {
        writer.Write(intPart != 0 ? 1u : 0u, 1).Write(frac != 0 ? 1u : 0u, 1);
        if (intPart != 0 || frac != 0)
        {
            writer.Write(0, 1);
            if (intPart != 0)
            {
                writer.Write((uint)(intPart - 1), 14);
            }

            if (frac != 0)
            {
                writer.Write((uint)frac, 5);
            }
        }
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
