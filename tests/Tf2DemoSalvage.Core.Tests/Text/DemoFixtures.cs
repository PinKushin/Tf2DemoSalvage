using System;
using System.Collections.Generic;
using System.Globalization;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A minimal but genuinely decodable demo: a real <c>dem_datatables</c> payload and a
/// <c>svc_PacketEntities</c> snapshot built against it.
/// </summary>
/// <remarks>
/// Shared because both output writers need it and the two are meant to describe the same demo —
/// a fixture copied into each would let them drift apart silently, which is exactly the
/// difference these tests exist to catch.
///
/// Everything here is written as the parser reads it, not as a struct definition suggests. That
/// distinction has already cost this project once: RISKS B16 came from implementing a message
/// from its C types rather than its read function.
/// </remarks>
internal static class DemoFixtures
{
    /// <summary>Entity index of the entering entity.</summary>
    public const int EnteringEntity = 0;

    /// <summary>Class id the entering entity carries, and the name it should resolve to.</summary>
    public const int EnteringClassId = 1;

    /// <summary>The name <see cref="EnteringClassId"/> resolves to through the schema.</summary>
    public const string EnteringClassName = "COther";

    /// <summary>Serial number written for the entering entity.</summary>
    public const int EnteringSerial = 517;

    /// <summary>Class ids are sized from the class count, and two classes need two bits.</summary>
    private const int ClassBits = 2;

    /// <summary>Width of an entity's serial number.</summary>
    private const int SerialBits = 10;

    /// <summary>A packet carrying one <c>TextMsg</c> user message with a decodable body.</summary>
    /// <param name="tick">Tick to stamp the command with.</param>
    /// <param name="text">The localisation key or literal text.</param>
    /// <param name="param">One substitution, or empty for none.</param>
    /// <returns>A single packet command.</returns>
    public static IReadOnlyList<DemoCommand> TextMessage(
        int tick = 7, string text = "#Game_connected", string param = "Sassy")
    {
        List<byte> body = [3];                               // destination
        foreach (string value in param.Length == 0 ? new[] { text } : new[] { text, param })
        {
            body.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            body.Add(0);
        }

        BitWriter packet = new();
        packet.Message(NetMessageType.UserMessage)
              .Write(TextMsgType, 8)
              .Write((uint)(body.Count * 8), 11);
        foreach (byte value in body)
        {
            packet.Write(value, 8);
        }

        return [new(DemoCommandType.Packet, tick, packet.Build())];
    }

    /// <summary><c>TextMsg</c>'s registered id.</summary>
    private const uint TextMsgType = 5;

    /// <summary>A one-table, two-class schema as <c>dem_datatables</c> puts it on the wire.</summary>
    public static byte[] SchemaPayload()
    {
        BitWriter writer = new();
        writer.Write(1, 1)                       // a table follows
              .Write(0, 1)                       // needsDecoder
              .String("DT_Test")
              .Write(1, 10);                     // one property
        writer.Write((uint)SendPropType.Int, 5)
              .String("m_iHealth")
              .Write(1, 16)                      // flags: unsigned
              .Write(0, 32)                      // low
              .Write(0, 32)                      // high
              .Write(10, 7);                     // bit count
        writer.Write(0, 1);                      // no more tables

        writer.Write(2, 16);                     // two classes
        writer.Write(0, 16).String("CTest").String("DT_Test");
        writer.Write(EnteringClassId, 16).String(EnteringClassName).String("DT_Test");

        return writer.Build();
    }

    /// <summary>
    /// A snapshot in which one entity enters, another leaves and a third is deleted.
    /// </summary>
    /// <remarks>
    /// The entering entity carries no properties — a bare continuation bit ends the list — which
    /// keeps the fixture about lifecycle rather than about value encoding. Leave and Delete carry
    /// nothing beyond their two-bit update type.
    /// </remarks>
    public static IReadOnlyList<DemoCommand> EntityLifecycle(int tick = 42)
    {
        BitWriter body = new();

        body.UBitVar(0)                                      // entity 0
            .Write((uint)EntityUpdateType.Enter, 2)
            .Write(EnteringClassId, ClassBits)
            .Write(EnteringSerial, SerialBits)
            .Write(0, 1);                                    // no properties follow

        body.UBitVar(0).Write((uint)EntityUpdateType.Leave, 2);    // entity 1
        body.UBitVar(0).Write((uint)EntityUpdateType.Delete, 2);   // entity 2

        int bodyBits = body.BitCount;

        BitWriter packet = new();
        packet.Message(NetMessageType.PacketEntities)
              .Write(2048, 11)                   // max entries
              .Write(1, 1)                       // is delta
              .Write(100, 32)                    // delta from tick
              .Write(0, 1)                       // baseline index
              .Write(3, 11)                      // updated entries
              .Write((uint)bodyBits, 20)
              .Write(0, 1);                      // update baseline
        packet.Append(body);

        return
        [
            new(DemoCommandType.DataTables, 0, SchemaPayload()),
            new(DemoCommandType.Packet, tick, packet.Build()),
        ];
    }

    public static IReadOnlyList<DemoCommand> EventNamingAPlayer(
        int userId = 7, string fieldName = "userid", string playerName = "Sassy")
    {
        BitWriter signon = new();
        // Entity index 0, not 1: the entry is written as "index follows the previous one",
        // which for the first entry means index 0, and a userinfo entry's text is that same
        // index in decimal. The fixture previously said 1 while sitting at 0 - a disagreement
        // that never occurs in a real demo, and one the roster builder now rejects rather than
        // guessing which of the two to believe (RISKS B22).
        WriteUserInfoTable(signon, name: playerName, userId: 7, entityIndex: 0);

        BitWriter definitions = new();
        BitWriter body = new();
        body.Write(1, 9).String("player_hurt");
        body.Write((uint)GameEventValueType.Short, 3).String(fieldName);
        body.Write((uint)GameEventValueType.None, 3);
        definitions.Message(NetMessageType.GameEventList).Write(1, 9).Write((uint)body.BitCount, 20);
        definitions.Append(body);

        BitWriter events = new();
        BitWriter eventBody = new();
        eventBody.Write(1, 9).Write((uint)userId, 16);
        events.Message(NetMessageType.GameEvent).Write((uint)eventBody.BitCount, 11);
        events.Append(eventBody);

        return
        [
            new(DemoCommandType.Signon, 0, signon.Build()),
            new(DemoCommandType.Signon, 0, definitions.Build()),
            new(DemoCommandType.Packet, 99, events.Build()),
        ];
    }

    /// <summary>Writes a userinfo string table holding one 132-byte player record.</summary>
    private static void WriteUserInfoTable(
        BitWriter writer, string name, int userId, int entityIndex)
    {
        // UTF-8, not ASCII. Player names are arbitrary bytes the client chose and TF2 players use
        // that freely; a fixture that can only express ASCII cannot test the case that breaks.
        byte[] record = new byte[PlayerInfo.RecordBytes];
        System.Text.Encoding.UTF8.GetBytes(name).CopyTo(record, 0);
        BitConverter.GetBytes((uint)userId).CopyTo(record, 32);
        System.Text.Encoding.UTF8.GetBytes("[U:1:1]").CopyTo(record, 36);

        // Entry layout, taken from StringTableCodec rather than guessed: a bit saying the index
        // follows the previous one, a bit saying text is present, a bit saying it is not a
        // substring back-reference, the text, then a bit saying user data follows and its
        // length in bytes. An earlier version of this fixture invented a different shape and
        // produced a table that parsed to nothing.
        BitWriter table = new();
        table.Write(1, 1);                                  // index is previous + 1
        table.Write(1, 1);                                  // has text
        table.Write(0, 1);                                  // not a substring reference
        table.String(entityIndex.ToString(CultureInfo.InvariantCulture));
        table.Write(1, 1);                                  // has user data
        table.Write((uint)record.Length, 14);               // length in bytes
        foreach (byte b in record)
        {
            table.Write(b, 8);
        }

        writer.Message(NetMessageType.CreateStringTable)
            .String("userinfo")
            .Write(64, 16)                                  // max entries
            .Write(1, 7);                                   // entry count, log2(64)+1 bits

        // This fixture sends no svc_ServerInfo, so decode state reports protocol 0 and every
        // protocol-conditional field takes its oldest form. Two of them are here:
        //
        //   * the length is a fixed 20-bit field, not a varint (varint arrives at 24)
        //   * there is no compression flag at all (it arrives at 15)
        //
        // Both have caught this fixture out. Writing the varint produced a table that silently
        // parsed to nothing; writing the compression bit shifted the table by one when the
        // pre-15 rule landed. Emulating protocol 0 means emulating *all* of it, and the list
        // grows every time a new boundary is implemented — if this breaks again, the fix is to
        // give the fixture a real svc_ServerInfo rather than to keep chasing the default.
        writer.Write((uint)table.BitCount, 20);
        writer.Write(0, 1);                                 // not fixed user data size
        writer.Append(table);
    }

    /// <summary>Writes a two-entry <c>svc_GameEventList</c>, each event holding one short.</summary>
    public static void WriteEventList(BitWriter writer)
    {
        BitWriter body = new();
        foreach ((int id, string name) in new[] { (1, "player_hurt"), (2, "player_death") })
        {
            body.Write((uint)id, 9).String(name);
            body.Write((uint)GameEventValueType.Short, 3).String("userid");
            body.Write((uint)GameEventValueType.None, 3);
        }

        writer.Message(NetMessageType.GameEventList).Write(2, 9).Write((uint)body.BitCount, 20);
        writer.Append(body);
    }

    public static void WriteEvent(BitWriter writer, int id, short userId)
    {
        BitWriter body = new();
        body.Write((uint)id, 9).Write((ushort)userId, 16);

        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        writer.Append(body);
    }

}
