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
    public static NetMessageReadResult Read(ReadOnlySpan<byte> payload)
    {
        var reader = new BitReader(payload);
        var messages = new List<INetMessage>();
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

            var type = (NetMessageType)rawType;

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
