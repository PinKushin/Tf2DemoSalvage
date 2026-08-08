using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Synthetic tests for game event decoding.
/// </summary>
/// <remarks>
/// These exist because the corpus cannot reach a real <c>svc_GameEventList</c> yet — it sits
/// behind <c>svc_ServerInfo</c> in the signon stream, and nothing in this layer is skippable
/// until decoded. Building the bytes by hand tests the codec now rather than after ServerInfo
/// lands, and independently checks the assumption that the value-type field is 3 bits.
/// </remarks>
public sealed class GameEventCodecTests
{
    /// <summary>
    /// Writes a <c>svc_GameEventList</c>: 9-bit count, 20-bit body length, then definitions of
    /// [9-bit id][name][(3-bit type)(name)]* terminated by a zero type.
    /// </summary>
    private static byte[] EventList(params (int Id, string Name, (GameEventValueType Type, string Name)[] Fields)[] events)
    {
        var body = new BitWriter();
        foreach ((int id, string name, (GameEventValueType Type, string Name)[] fields) in events)
        {
            body.Write((uint)id, 9).String(name);
            foreach ((GameEventValueType type, string fieldName) in fields)
            {
                body.Write((uint)type, 3).String(fieldName);
            }

            body.Write((uint)GameEventValueType.None, 3);
        }

        byte[] bodyBytes = body.Build();

        var writer = new BitWriter();
        writer.Message(NetMessageType.GameEventList)
            .Write((uint)events.Length, 9)
            .Write((uint)body.BitCount, 20);

        // Re-pack the body bit by bit so it lands at the reader's current, unaligned offset.
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
    }

    [Fact]
    public void GameEventList_DecodesDefinitionsFieldsAndTypes()
    {
        byte[] packet = EventList(
            (25, "player_death",
            [
                (GameEventValueType.Short, "userid"),
                (GameEventValueType.Short, "attacker"),
                (GameEventValueType.String, "weapon"),
            ]),
            (30, "round_start", [(GameEventValueType.Bool, "full_reset")]));

        NetMessageReadResult result = NetMessageReader.Read(packet);

        var list = result.Messages[0].ShouldBeOfType<GameEventListMessage>();
        list.Definitions.Count.ShouldBe(2);

        GameEventDefinition death = list.Definitions[0];
        death.Id.ShouldBe(25);
        death.Name.ShouldBe("player_death");
        death.Fields.Count.ShouldBe(3);
        death.Fields[0].ShouldBe(new GameEventField("userid", GameEventValueType.Short));
        death.Fields[2].ShouldBe(new GameEventField("weapon", GameEventValueType.String));

        list.Definitions[1].Name.ShouldBe("round_start");
        list.Definitions[1].Fields[0].Type.ShouldBe(GameEventValueType.Bool);
    }

    [Fact]
    public void GameEventList_LeavesTheReaderPositionedForTheNextMessage()
    {
        // The whole point of the length prefix: whatever follows must still decode. If the
        // body were consumed by even one bit too few or too many, this net_Tick becomes noise.
        var writer = new BitWriter();
        byte[] listBytes = EventList((1, "e", [(GameEventValueType.Byte, "f")]));
        foreach (byte b in listBytes)
        {
            writer.Write(b, 8);
        }

        NetMessageReadResult result = NetMessageReader.Read(
            AppendNetTick(listBytes, tick: 4242));

        IReadOnlyList<INetMessage> messages = Significant(result);
        messages.Count.ShouldBe(2);
        ((NetTickMessage)messages[1]).Tick.ShouldBe(4242);
    }

    [Fact]
    public void GameEvent_DecodesAgainstADefinitionSeenEarlier()
    {
        var state = new NetDecodeState();

        NetMessageReader.Read(
            EventList((7, "player_hurt",
            [
                (GameEventValueType.Short, "userid"),
                (GameEventValueType.Byte, "health"),
                (GameEventValueType.Bool, "crit"),
            ])),
            state);

        var writer = new BitWriter();
        var body = new BitWriter()
            .Write(7, 9)
            .Write(unchecked((uint)(short)91), 16)
            .Write(63, 8)
            .Write(1, 1);
        byte[] bodyBytes = body.Build();

        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        NetMessageReadResult result = NetMessageReader.Read(writer.Build(), state);

        var fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();
        fired.Name.ShouldBe("player_hurt");
        fired.IsDecoded.ShouldBeTrue();
        fired.Values["userid"].ShouldBe((short)91);
        fired.Values["health"].ShouldBe((byte)63);
        fired.Values["crit"].ShouldBe(true);
    }

    [Fact]
    public void GameEvent_WithoutItsDefinition_IsReportedUndecodedButDoesNotStopTheWalk()
    {
        // The length prefix means one unknown event costs that event, not the packet.
        var body = new BitWriter().Write(99, 9);
        byte[] bodyBytes = body.Build();

        var writer = new BitWriter();
        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        writer.NetTick(11, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        var fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();
        fired.EventId.ShouldBe(99);
        fired.IsDecoded.ShouldBeFalse();
        fired.Values.ShouldBeEmpty();
        ((NetTickMessage)Significant(result)[1]).Tick.ShouldBe(11);
    }

    [Fact]
    public void GameEvent_DecodesEveryValueType()
    {
        var state = new NetDecodeState();
        NetMessageReader.Read(
            EventList((3, "everything",
            [
                (GameEventValueType.String, "s"),
                (GameEventValueType.Float, "f"),
                (GameEventValueType.Long, "l"),
                (GameEventValueType.Short, "sh"),
                (GameEventValueType.Byte, "b"),
                (GameEventValueType.Bool, "bo"),
                (GameEventValueType.UInt64, "u"),
            ])),
            state);

        const ulong big = 0xDEADBEEF_12345678UL;
        var body = new BitWriter()
            .Write(3, 9)
            .String("hi")
            .Write((uint)System.BitConverter.SingleToInt32Bits(1.5f), 32)
            .Write(unchecked((uint)-7), 32)
            .Write(unchecked((uint)(short)-2), 16)
            .Write(200, 8)
            .Write(0, 1)
            .Write((uint)(big & 0xFFFFFFFF), 32)
            .Write((uint)(big >> 32), 32);

        NetMessageReadResult result = NetMessageReader.Read(WrapEvent(body), state);
        var fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();

        fired.Values["s"].ShouldBe("hi");
        fired.Values["f"].ShouldBe(1.5f);
        fired.Values["l"].ShouldBe(-7);
        fired.Values["sh"].ShouldBe((short)-2);
        fired.Values["b"].ShouldBe((byte)200);
        fired.Values["bo"].ShouldBe(false);
        // The 64-bit case is assembled from two 32-bit reads, so a wrong shift or a wrong
        // combining operator would only show up on a value with bits set in both halves.
        fired.Values["u"].ShouldBe(big);
    }

    [Fact]
    public void AddEventDefinitions_NullDefinitions_Throws()
    {
        Should.Throw<System.ArgumentNullException>(
            () => new NetDecodeState().AddEventDefinitions(null!));
    }

    [Fact]
    public void GameEventList_WithNoEvents_DecodesToAnEmptyList()
    {
        NetMessageReadResult result = NetMessageReader.Read(EventList());

        result.Messages[0].ShouldBeOfType<GameEventListMessage>().Definitions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    public void BodyLengthsThatAreNotWholeBytes_StillLeaveTheReaderAligned(int extraBits)
    {
        // CopyBits handles the partial trailing byte, and every survivor from the first
        // mutation run lived in exactly that arithmetic.
        var body = new BitWriter().Write(1, 9);
        for (int i = 0; i < extraBits; i++)
        {
            body.Write(1, 1);
        }

        byte[] bodyBytes = body.Build();
        var writer = new BitWriter();
        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        writer.NetTick(777, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        IReadOnlyList<INetMessage> messages = Significant(result);
        messages.Count.ShouldBe(2);
        ((NetTickMessage)messages[1]).Tick.ShouldBe(777);
    }

    /// <summary>
    /// Messages with padding removed. Byte-aligning a fixture leaves spare zero bits, and
    /// those decode as net_NOP - correct behaviour, but noise when counting real messages.
    /// </summary>
    private static IReadOnlyList<INetMessage> Significant(NetMessageReadResult result) =>
        [.. result.Messages.Where(m => m.Type != NetMessageType.Empty)];

    /// <summary>Wraps an already-built event body in its message type and length prefix.</summary>
    private static byte[] WrapEvent(BitWriter body)
    {
        byte[] bodyBytes = body.Build();
        var writer = new BitWriter();
        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);

        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
    }

    private static byte[] AppendNetTick(byte[] prefix, uint tick)
    {
        var writer = new BitWriter();
        foreach (byte b in prefix)
        {
            writer.Write(b, 8);
        }

        return writer.NetTick(tick, 0, 0).Build();
    }
}
