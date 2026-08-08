using System;
using System.Collections.Generic;
using System.Globalization;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Walks the network message stream inside a <c>dem_packet</c> payload.
/// </summary>
/// <remarks>
/// A packet payload is <c>[6-bit type][body]</c> repeated, with no length prefix on any
/// message. That single fact shapes everything here: **there is no skip**. The next message
/// begins wherever the previous body ended, so reaching a type this reader cannot decode means
/// the remainder of the packet is unreachable, not empty.
///
/// So the reader stops and reports, rather than guessing a width or silently returning what it
/// managed. Support is added one message type at a time, and each addition unlocks whatever
/// followed it.
/// </remarks>
public static class NetMessageReader
{
    /// <summary>Width of <c>net_Tick</c>'s body: a 32-bit tick and two 16-bit values.</summary>
    private const int NetTickBodyBits = 64;

    /// <summary>Reads as much of <paramref name="payload"/> as is currently supported.</summary>
    /// <param name="payload">One <c>dem_packet</c> payload.</param>
    /// <returns>The messages decoded and where the walk ended.</returns>
    public static NetMessageReadResult Read(ReadOnlySpan<byte> payload) =>
        Read(payload, new NetDecodeState());

    /// <summary>
    /// Reads a packet, carrying decode state forward from earlier packets.
    /// </summary>
    /// <param name="payload">One <c>dem_packet</c> payload.</param>
    /// <param name="state">
    /// State accumulated from earlier packets. Game events cannot be decoded without the
    /// definitions from a prior <c>svc_GameEventList</c>, so a packet read in isolation will
    /// report its events as undecoded.
    /// </param>
    /// <returns>The messages decoded and where the walk ended.</returns>
    public static NetMessageReadResult Read(ReadOnlySpan<byte> payload, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        BitReader reader = new(payload);
        List<INetMessage> messages = new();
        int lastGoodBit = 0;

        // Fewer bits left than a type field means trailing padding: packets are padded to a
        // byte boundary, so this is the normal way a healthy packet ends.
        while (reader.BitsRemaining >= NetMessage.TypeBits)
        {
            int typeStartBit = reader.BitsRead;
            uint rawType = reader.ReadUInt32(NetMessage.TypeBits);

            if (!Enum.IsDefined((NetMessageType)rawType))
            {
                return Stopped(messages, lastGoodBit, null, string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unrecognised message id {rawType} at bit {typeStartBit}. Ids 1, 9, 16, " +
                    $"20 and 22 are unused at network protocol 24."));
            }

            NetMessageType type = (NetMessageType)rawType;

            switch (type)
            {
                case NetMessageType.Empty:
                    // net_NOP is padding: the type field and nothing else. Still reported,
                    // so the message count reflects what the stream actually holds.
                    messages.Add(NetEmptyMessage.Instance);
                    break;

                case NetMessageType.NetTick:
                    if (reader.BitsRemaining < NetTickBodyBits)
                    {
                        return Stopped(messages, lastGoodBit, null, string.Create(
                            CultureInfo.InvariantCulture,
                            $"Packet is truncated: {type} at bit {typeStartBit} needs " +
                            $"{NetTickBodyBits} body bits but only {reader.BitsRemaining} remain."));
                    }

                    messages.Add(new NetTickMessage(
                        (int)reader.ReadUInt32(32),
                        (ushort)reader.ReadUInt32(16),
                        (ushort)reader.ReadUInt32(16)));
                    break;

                case NetMessageType.Print:
                    messages.Add(new PrintMessage(NetBitReading.ReadString(ref reader)));
                    break;

                case NetMessageType.StringCmd:
                    messages.Add(new StringCmdMessage(NetBitReading.ReadString(ref reader)));
                    break;

                case NetMessageType.SetConVar:
                {
                    int count = (int)reader.ReadUInt32(8);
                    List<KeyValuePair<string, string>> variables = new(count);
                    for (int i = 0; i < count; i++)
                    {
                        string key = NetBitReading.ReadString(ref reader);
                        variables.Add(new KeyValuePair<string, string>(
                            key, NetBitReading.ReadString(ref reader)));
                    }

                    messages.Add(new SetConVarMessage(variables));
                    break;
                }

                case NetMessageType.ServerInfo:
                {
                    ServerInfoMessage info = ReadServerInfo(ref reader);
                    state.ServerInfo = info;
                    messages.Add(info);
                    break;
                }

                case NetMessageType.PacketEntities:
                {
                    int maxEntries = (int)reader.ReadUInt32(11);
                    bool isDelta = reader.ReadBit();
                    int? deltaFrom = isDelta ? (int)reader.ReadUInt32(32) : null;
                    bool baseline = reader.ReadBit();
                    int updatedEntries = (int)reader.ReadUInt32(11);
                    int entityBits = (int)reader.ReadUInt32(20);
                    bool updateBaseline = reader.ReadBit();

                    // Copied out rather than decoded in place. EntityDecoder needs the schema,
                    // which arrives in dem_datatables - a different demo command - so the body
                    // is carried until a caller has both.
                    byte[] body = NetBitReading.CopyBits(ref reader, entityBits);

                    messages.Add(new PacketEntitiesMessage(
                        maxEntries, isDelta, deltaFrom, baseline, updatedEntries, entityBits,
                        updateBaseline, body));
                    break;
                }

                case NetMessageType.ClassInfo:
                {
                    ClassInfoMessage info = ReadClassInfo(ref reader);
                    state.ClassInfo = info;
                    messages.Add(info);
                    break;
                }

                case NetMessageType.CreateStringTable:
                {
                    CreateStringTableMessage table = StringTableCodec.ReadCreate(ref reader, state);
                    state.AddStringTable(table.MaxEntries);
                    messages.Add(table);
                    break;
                }

                case NetMessageType.UpdateStringTable:
                    messages.Add(StringTableCodec.ReadUpdate(ref reader, state));
                    break;

                case NetMessageType.GameEventList:
                {
                    GameEventListMessage list = GameEventCodec.ReadList(ref reader);
                    state.AddEventDefinitions(list.Definitions);
                    messages.Add(list);
                    break;
                }

                case NetMessageType.GameEvent:
                    messages.Add(GameEventCodec.ReadEvent(ref reader, state));
                    break;

                default:
                    return Stopped(messages, lastGoodBit, type, string.Create(
                        CultureInfo.InvariantCulture,
                        $"{type} at bit {typeStartBit} is not decoded yet. Messages carry no " +
                        $"length prefix, so the rest of this packet cannot be reached until " +
                        $"it is implemented."));
            }

            lastGoodBit = reader.BitsRead;
        }

        return new NetMessageReadResult { Messages = messages, BitsConsumed = lastGoodBit };
    }

    /// <summary>
    /// Reads <c>svc_ServerInfo</c>. No length prefix, so every width below must be exact or
    /// the entire signon stream behind it becomes unreadable.
    /// </summary>
    private static ServerInfoMessage ReadServerInfo(ref BitReader reader)
    {
        ushort protocol = (ushort)reader.ReadUInt32(16);
        uint serverCount = reader.ReadUInt32(32);
        bool sourceTv = reader.ReadBit();
        bool dedicated = reader.ReadBit();
        uint mapCrc = reader.ReadUInt32(32);
        ushort maxClasses = (ushort)reader.ReadUInt32(16);

        // Protocol 18 replaced the 4-byte map CRC with a 16-byte hash. Our corpus is all
        // protocol 24; the older branch is written from the reference implementation and has
        // no specimen to verify it against, so it is flagged rather than trusted.
        byte[] mapHash = new byte[protocol > 17 ? 16 : 4];
        for (int i = 0; i < mapHash.Length; i++)
        {
            mapHash[i] = reader.ReadByte();
        }

        byte playerSlot = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        float intervalPerTick = BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32));
        char platform = (char)reader.ReadByte();

        string game = NetBitReading.ReadString(ref reader);
        string map = NetBitReading.ReadString(ref reader);
        string skybox = NetBitReading.ReadString(ref reader);
        string serverName = NetBitReading.ReadString(ref reader);

        bool replay = protocol > 15 && reader.ReadBit();

        return new ServerInfoMessage(
            protocol,
            serverCount,
            sourceTv,
            dedicated,
            mapCrc,
            maxClasses,
            mapHash,
            playerSlot,
            maxPlayers,
            intervalPerTick,
            platform,
            game,
            map,
            skybox,
            serverName,
            replay);
    }

    /// <summary>
    /// Reads <c>svc_ClassInfo</c>. Class ids are written at a width derived from the count,
    /// so the count has to be read before the entries can be.
    /// </summary>
    private static ClassInfoMessage ReadClassInfo(ref BitReader reader)
    {
        int count = (int)reader.ReadUInt32(16);
        bool createOnClient = reader.ReadBit();

        if (createOnClient)
        {
            // Nothing follows. Reading entries anyway would consume bits belonging to the
            // next message.
            return new ClassInfoMessage(count, true, []);
        }

        int idBits = 0;
        while (1 << idBits < count)
        {
            idBits++;
        }

        List<ServerClass> classes = new(count);
        for (int i = 0; i < count; i++)
        {
            classes.Add(new ServerClass(
                (int)reader.ReadUInt32(idBits + 1),
                NetBitReading.ReadString(ref reader),
                NetBitReading.ReadString(ref reader)));
        }

        return new ClassInfoMessage(count, false, classes);
    }

    private static NetMessageReadResult Stopped(
        List<INetMessage> messages,
        int bitsConsumed,
        NetMessageType? stoppedAt,
        string reason) =>
        new()
        {
            Messages = messages,
            BitsConsumed = bitsConsumed,
            StoppedAt = stoppedAt,
            StopReason = reason,
        };
}
