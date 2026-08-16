using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Decodes <c>svc_GameEventList</c> and <c>svc_GameEvent</c>.
/// </summary>
/// <remarks>
/// Both carry an explicit bit length, which makes them the first messages in this layer that
/// can be stepped over even when their contents cannot be understood. Implementing them
/// therefore unlocks whatever follows them in a packet, regardless of decode success.
/// </remarks>
internal static class GameEventCodec
{
    /// <summary>Descriptors a list may carry, bounded by the id that indexes them.</summary>
    /// <remarks>
    /// **Internal so the widths can be checked against the engine rather than restated.** Two of
    /// these are <c>MAX_EVENT_BITS</c> from <c>public/igameevents.h</c>; the other three are wire
    /// widths from <c>netmessages.h</c>, which the SDK does not ship — see
    /// <c>GameEventConformanceTests</c> for which is which and what pins the rest.
    /// </remarks>
    internal const int CountBits = 9;

    /// <summary>Bit length of a whole <c>svc_GameEventList</c> body.</summary>
    internal const int ListLengthBits = 20;

    /// <summary>Bit length of one <c>svc_GameEvent</c> body.</summary>
    internal const int EventLengthBits = 11;

    /// <summary><c>MAX_EVENT_BITS</c>: the width of an event's index.</summary>
    internal const int EventIdBits = 9;

    /// <summary>Width of a field's type tag. Eight possible values, and seven are used.</summary>
    internal const int ValueTypeBits = 3;

    /// <summary>Reads a <c>svc_GameEventList</c> body.</summary>
    internal static GameEventListMessage ReadList(ref BitReader reader)
    {
        int count = (int)reader.ReadUInt32(CountBits);
        int lengthBits = (int)reader.ReadUInt32(ListLengthBits);

        // The body is copied out so it can be read as its own stream. That also means a
        // malformed definition cannot run past the list and corrupt the rest of the packet -
        // the outer reader has already been advanced by exactly the declared length.
        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);
        BitReader bodyReader = new(body);

        // Checked against the BODY, which is the stream the definitions actually come from -
        // the outer reader has already skipped past the whole list. A definition is an id, a
        // name (at minimum its terminator) and the None marker that ends its field list.
        Primitives.WireBounds.EnsureCountFits(
            "svc_gameeventlist", count, EventIdBits + 8 + ValueTypeBits, bodyReader.BitsRemaining);

        List<GameEventDefinition> definitions = new(count);
        for (int i = 0; i < count; i++)
        {
            int id = (int)bodyReader.ReadUInt32(EventIdBits);
            string name = NetBitReading.ReadString(ref bodyReader);

            List<GameEventField> fields = new();
            while (true)
            {
                GameEventValueType type = (GameEventValueType)bodyReader.ReadUInt32(ValueTypeBits);
                if (type == GameEventValueType.None)
                {
                    break;
                }

                fields.Add(new GameEventField(NetBitReading.ReadString(ref bodyReader), type));
            }

            definitions.Add(new GameEventDefinition(id, name, fields));
        }

        return new GameEventListMessage(definitions, lengthBits);
    }

    /// <summary>Reads a <c>svc_GameEvent</c> body against the definitions seen so far.</summary>
    internal static GameEventMessage ReadEvent(ref BitReader reader, NetDecodeState state)
    {
        int lengthBits = (int)reader.ReadUInt32(EventLengthBits);
        byte[] body = NetBitReading.CopyBits(ref reader, lengthBits);
        BitReader bodyReader = new(body);

        int eventId = (int)bodyReader.ReadUInt32(EventIdBits);

        if (!state.EventDefinitions.TryGetValue(eventId, out GameEventDefinition? definition))
        {
            // An event before its definition. The length prefix already moved the outer
            // reader past it, so this costs one event rather than the rest of the packet.
            return new GameEventMessage(
                eventId, null, new Dictionary<string, object?>(), lengthBits);
        }

        Dictionary<string, object?> values = new(definition.Fields.Count, StringComparer.Ordinal);
        foreach (GameEventField field in definition.Fields)
        {
            values[field.Name] = ReadValue(ref bodyReader, field.Type);
        }

        return new GameEventMessage(eventId, definition.Name, values, lengthBits);
    }

    /// <summary>Writes a <c>svc_GameEventList</c> body, length prefix included.</summary>
    /// <remarks>
    /// The body is built before it is written, because its own length comes first. Nothing here
    /// is optional, so unlike a sound there is no encoding shape to recover — the definitions
    /// determine the bits exactly, apart from the trailing padding the length implies.
    /// </remarks>
    internal static void WriteList(BitWriter writer, GameEventListMessage message)
    {
        BitWriter body = new();
        foreach (GameEventDefinition definition in message.Definitions)
        {
            body.Write((uint)definition.Id, EventIdBits).WriteString(definition.Name);
            foreach (GameEventField field in definition.Fields)
            {
                body.Write((uint)field.Type, ValueTypeBits).WriteString(field.Name);
            }

            body.Write((uint)GameEventValueType.None, ValueTypeBits);
        }

        Pad(body, message.BodyBits);
        writer.Write((uint)message.Definitions.Count, CountBits)
            .Write((uint)body.BitCount, ListLengthBits)
            .AppendBits(body.Build(), body.BitCount);
    }

    /// <summary>Writes a <c>svc_GameEvent</c> body, length prefix included.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="message">The event.</param>
    /// <param name="definition">
    /// The event's definition, which supplies field ORDER. The decoded values are a dictionary
    /// and a dictionary does not remember the wire's order, so writing them in enumeration order
    /// would produce a body that decodes to the same values and different bits.
    /// </param>
    internal static void WriteEvent(
        BitWriter writer, GameEventMessage message, GameEventDefinition definition)
    {
        BitWriter body = new();
        body.Write((uint)message.EventId, EventIdBits);

        foreach (GameEventField field in definition.Fields)
        {
            WriteValue(body, field.Type, message.Values[field.Name]);
        }

        Pad(body, message.BodyBits);
        writer.Write((uint)body.BitCount, EventLengthBits)
            .AppendBits(body.Build(), body.BitCount);
    }

    /// <summary>Zero-fills to the body length the message declared.</summary>
    /// <remarks>
    /// Real demos need this. A sender measures its buffer in bytes and states the length in bits,
    /// so a body routinely runs a few bits past its last field. Those bits are on the wire, and
    /// omitting them would shorten the message and shift everything after it in the packet.
    /// </remarks>
    private static void Pad(BitWriter body, int bodyBits)
    {
        for (int bit = body.BitCount; bit < bodyBits; bit++)
        {
            body.WriteBit(false);
        }
    }

    private static void WriteValue(BitWriter writer, GameEventValueType type, object? value)
    {
        switch (type)
        {
            case GameEventValueType.String:
                writer.WriteString((string)value!);
                break;

            case GameEventValueType.Float:
                writer.Write((uint)BitConverter.SingleToInt32Bits((float)value!), 32);
                break;

            case GameEventValueType.Long:
                writer.Write((uint)(int)value!, 32);
                break;

            case GameEventValueType.Short:
                writer.Write((uint)(ushort)(short)value!, 16);
                break;

            case GameEventValueType.Byte:
                writer.Write((byte)value!, 8);
                break;

            case GameEventValueType.Bool:
                writer.WriteBit((bool)value!);
                break;

            case GameEventValueType.Local:
                // Declared but never broadcast, so it occupies no bits in either direction.
                break;

            default:
                throw new InvalidOperationException($"Unhandled game event value type {type}.");
        }
    }

    private static object? ReadValue(ref BitReader reader, GameEventValueType type) => type switch
    {
        GameEventValueType.String => NetBitReading.ReadString(ref reader),
        GameEventValueType.Float => BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32)),
        GameEventValueType.Long => (int)reader.ReadUInt32(32),
        GameEventValueType.Short => (short)reader.ReadUInt32(16),
        GameEventValueType.Byte => (byte)reader.ReadUInt32(8),
        GameEventValueType.Bool => reader.ReadBit(),
        // Declared by the server, deliberately not broadcast. Reported as a field with no
        // value rather than omitted, because the definition says it exists and a trace that
        // silently drops it would misdescribe the event. Reads nothing - see the remarks on
        // GameEventValueType.Local for why it is not the 64-bit integer this once assumed.
        GameEventValueType.Local => null,
        _ => throw new InvalidOperationException($"Unhandled game event value type {type}."),
    };

}
