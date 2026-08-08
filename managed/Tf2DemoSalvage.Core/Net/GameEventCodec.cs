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
    private const int CountBits = 9;
    private const int ListLengthBits = 20;
    private const int EventLengthBits = 11;
    private const int EventIdBits = 9;
    private const int ValueTypeBits = 3;

    /// <summary>Reads a <c>svc_GameEventList</c> body.</summary>
    internal static GameEventListMessage ReadList(ref BitReader reader)
    {
        int count = (int)reader.ReadUInt32(CountBits);
        int lengthBits = (int)reader.ReadUInt32(ListLengthBits);

        // The body is copied out so it can be read as its own stream. That also means a
        // malformed definition cannot run past the list and corrupt the rest of the packet -
        // the outer reader has already been advanced by exactly the declared length.
        byte[] body = CopyBits(ref reader, lengthBits);
        BitReader bodyReader = new(body);

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

        return new GameEventListMessage(definitions);
    }

    /// <summary>Reads a <c>svc_GameEvent</c> body against the definitions seen so far.</summary>
    internal static GameEventMessage ReadEvent(ref BitReader reader, NetDecodeState state)
    {
        int lengthBits = (int)reader.ReadUInt32(EventLengthBits);
        byte[] body = CopyBits(ref reader, lengthBits);
        BitReader bodyReader = new(body);

        int eventId = (int)bodyReader.ReadUInt32(EventIdBits);

        if (!state.EventDefinitions.TryGetValue(eventId, out GameEventDefinition? definition))
        {
            // An event before its definition. The length prefix already moved the outer
            // reader past it, so this costs one event rather than the rest of the packet.
            return new GameEventMessage(eventId, null, new Dictionary<string, object>());
        }

        Dictionary<string, object> values = new(definition.Fields.Count, StringComparer.Ordinal);
        foreach (GameEventField field in definition.Fields)
        {
            values[field.Name] = ReadValue(ref bodyReader, field.Type);
        }

        return new GameEventMessage(eventId, definition.Name, values);
    }

    private static object ReadValue(ref BitReader reader, GameEventValueType type) => type switch
    {
        GameEventValueType.String => NetBitReading.ReadString(ref reader),
        GameEventValueType.Float => BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32)),
        GameEventValueType.Long => (int)reader.ReadUInt32(32),
        GameEventValueType.Short => (short)reader.ReadUInt32(16),
        GameEventValueType.Byte => (byte)reader.ReadUInt32(8),
        GameEventValueType.Bool => reader.ReadBit(),
        GameEventValueType.UInt64 => reader.ReadUInt32(32) | ((ulong)reader.ReadUInt32(32) << 32),
        _ => throw new InvalidOperationException($"Unhandled game event value type {type}."),
    };

    /// <summary>
    /// Copies <paramref name="bitCount"/> bits out of the reader into their own buffer.
    /// </summary>
    /// <remarks>
    /// Needed because a length-prefixed body has to be read as an independent stream, and
    /// <see cref="BitReader"/> cannot be positioned at an arbitrary bit offset. The buffers are
    /// small - a game event body is at most 2047 bits - so this is not on a hot path.
    /// </remarks>
    private static byte[] CopyBits(ref BitReader reader, int bitCount)
    {
        // Stryker disable once Arithmetic: mutating the rounding only over-allocates. The
        // extra bytes are never written or read, so decoding stays correct and no assertion
        // can distinguish it. Wasteful, not wrong - an equivalent mutant.
        byte[] buffer = new byte[(bitCount + 7) / 8];
        int whole = bitCount / 8;

        for (int i = 0; i < whole; i++)
        {
            buffer[i] = reader.ReadByte();
        }

        int remainder = bitCount % 8;
        if (remainder > 0)
        {
            buffer[whole] = (byte)reader.ReadUInt32(remainder);
        }

        return buffer;
    }
}
