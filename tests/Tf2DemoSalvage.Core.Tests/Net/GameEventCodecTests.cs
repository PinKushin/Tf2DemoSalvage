using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;
using static Tf2DemoSalvage.Core.Tests.Net.GameEventFixtures;
using Tf2DemoSalvage.Core.Primitives;

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

        GameEventListMessage list = result.Messages[0].ShouldBeOfType<GameEventListMessage>();
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
        BitWriter writer = new();
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
        NetDecodeState state = new();

        NetMessageReader.Read(
            EventList((7, "player_hurt",
            [
                (GameEventValueType.Short, "userid"),
                (GameEventValueType.Byte, "health"),
                (GameEventValueType.Bool, "crit"),
            ])),
            state);

        BitWriter writer = new();
        BitWriter body = new BitWriter()
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

        GameEventMessage fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();
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
        BitWriter body = new BitWriter().Write(99, 9);
        byte[] bodyBytes = body.Build();

        BitWriter writer = new();
        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bodyBytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        writer.NetTick(11, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        GameEventMessage fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();
        fired.EventId.ShouldBe(99);
        fired.IsDecoded.ShouldBeFalse();
        fired.Values.ShouldBeEmpty();
        ((NetTickMessage)Significant(result)[1]).Tick.ShouldBe(11);
    }

    [Fact]
    public void GameEvent_DecodesEveryValueType()
    {
        NetDecodeState state = new();
        NetMessageReader.Read(
            EventList((3, "everything",
            [
                (GameEventValueType.String, "s"),
                (GameEventValueType.Float, "f"),
                (GameEventValueType.Long, "l"),
                (GameEventValueType.Short, "sh"),
                (GameEventValueType.Byte, "b"),
                (GameEventValueType.Bool, "bo"),
            ])),
            state);

        BitWriter body = new BitWriter()
            .Write(3, 9)
            .String("hi")
            .Write((uint)System.BitConverter.SingleToInt32Bits(1.5f), 32)
            .Write(unchecked((uint)-7), 32)
            .Write(unchecked((uint)(short)-2), 16)
            .Write(200, 8)
            .Write(0, 1);

        NetMessageReadResult result = NetMessageReader.Read(WrapEvent(body), state);
        GameEventMessage fired = result.Messages[0].ShouldBeOfType<GameEventMessage>();

        fired.Values["s"].ShouldBe("hi");
        fired.Values["f"].ShouldBe(1.5f);
        fired.Values["l"].ShouldBe(-7);
        fired.Values["sh"].ShouldBe((short)-2);
        fired.Values["b"].ShouldBe((byte)200);
        fired.Values["bo"].ShouldBe(false);
    }

    [Fact]
    public void LocalField_OccupiesNoBits_SoTheFieldBehindItStillDecodes()
    {
        // RISKS B14. Type 7 was read as a 64-bit integer here, on the assumption that the wire
        // numbering matched CS:GO's protobuf ordering. It cannot: the type field is 3 bits, and
        // that ordering needs 8 for wstring. Type 7 is `local` - a field the server declares but
        // deliberately does not broadcast - so it contributes zero bits to an event body.
        //
        // The measurement is the field *behind* it. A local field carries no value of its own,
        // so asserting on it could not distinguish the two readings; a wrong width does not
        // return a wrong answer, it desynchronises everything after it.
        NetDecodeState state = new();
        NetMessageReader.Read(
            EventList(
                (11, "with_local",
                [
                    (GameEventValueType.Short, "before"),
                    (GameEventValueType.Local, "hidden"),
                    (GameEventValueType.Short, "after"),
                ]),

                // Control: no local field, so it must decode identically either way. Without it,
                // "the local field reads nothing" and "value reads are broken everywhere" would
                // produce the same evidence.
                (12, "no_local", [(GameEventValueType.Short, "only")])),
            state);

        // The trailing 64 bits are the point of the fixture, not padding. Without them the
        // broken 64-bit read runs off the end of the body and the event is reported truncated -
        // a failure, but the wrong one, and one that would still occur if the width were wrong
        // in some other way. With them the broken read succeeds and returns a *wrong value*,
        // so the assertion below measures alignment rather than merely detecting a crash.
        BitWriter body = new BitWriter().Write(11, 9).Write(1234, 16).Write(4321, 16);
        for (int i = 0; i < 64; i++)
        {
            body.Write(1, 1);
        }

        GameEventMessage withLocal = NetMessageReader
            .Read(WrapEvent(body), state)
            .Messages[0].ShouldBeOfType<GameEventMessage>();

        withLocal.Values["before"].ShouldBe((short)1234);
        withLocal.Values["after"].ShouldBe((short)4321);

        // The local field is still reported, because the definition declares it. It just has
        // no value - which is the honest thing to say about a field the server withheld.
        withLocal.Values["hidden"].ShouldBeNull();

        GameEventMessage control = NetMessageReader
            .Read(WrapEvent(new BitWriter().Write(12, 9).Write(777, 16)), state)
            .Messages[0].ShouldBeOfType<GameEventMessage>();

        control.Values["only"].ShouldBe((short)777);
    }

    [Fact]
    public void GameEvent_FieldTypeOutsideTheWireRange_Throws()
    {
        // The 3-bit type field covers exactly the eight handled values, so this guard is
        // unreachable from wire data. It can only fire on a definition seeded through the
        // public API - a programming error, which is why it throws rather than reporting.
        NetDecodeState state = new();
        state.AddEventDefinitions(
            [new GameEventDefinition(5, "corrupt", [new GameEventField("f", (GameEventValueType)9)])]);

        BitWriter body = new BitWriter().Write(5, 9);

        System.InvalidOperationException exception =
            Should.Throw<System.InvalidOperationException>(
                () => NetMessageReader.Read(WrapEvent(body), state));

        exception.Message.ShouldBe("Unhandled game event value type 9.");
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
        BitWriter body = new BitWriter().Write(1, 9);
        for (int i = 0; i < extraBits; i++)
        {
            body.Write(1, 1);
        }

        byte[] bodyBytes = body.Build();
        BitWriter writer = new();
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

    private static byte[] AppendNetTick(byte[] prefix, uint tick)
    {
        BitWriter writer = new();
        foreach (byte b in prefix)
        {
            writer.Write(b, 8);
        }

        return writer.NetTick(tick, 0, 0).Build();
    }
}
