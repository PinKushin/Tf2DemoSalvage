using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Builds <c>svc_GameEventList</c> and <c>svc_GameEvent</c> bytes for tests.
/// </summary>
/// <remarks>
/// Shared rather than duplicated, because both messages embed a bit length and then a body that
/// has to be re-packed bit by bit to land at the reader's unaligned offset. That re-packing loop
/// is the part every hand-built fixture in this project has got wrong at least once, and a second
/// copy of it is a second chance to write a fixture that parses to nothing instead of failing.
/// </remarks>
internal static class GameEventFixtures
{
    /// <summary>Appends a body to a writer bit by bit, preserving the writer's bit offset.</summary>
    private static void AppendBits(BitWriter writer, BitWriter body)
    {
        byte[] bytes = body.Build();
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bytes[bit / 8] >> (bit % 8)) & 1), 1);
        }
    }

    /// <summary>
    /// Writes a <c>svc_GameEventList</c>: 9-bit count, 20-bit body length, then definitions of
    /// [9-bit id][name][(3-bit type)(name)]* terminated by a zero type.
    /// </summary>
    internal static byte[] EventList(
        params (int Id, string Name, (GameEventValueType Type, string Name)[] Fields)[] events)
    {
        BitWriter writer = new();
        AppendList(writer, events);
        return writer.Build();
    }

    /// <summary>Appends a <c>svc_GameEventList</c> to an existing writer.</summary>
    internal static void AppendList(
        BitWriter writer,
        params (int Id, string Name, (GameEventValueType Type, string Name)[] Fields)[] events)
    {
        BitWriter body = new();
        foreach ((int id, string name, (GameEventValueType Type, string Name)[] fields) in events)
        {
            body.Write((uint)id, 9).String(name);
            foreach ((GameEventValueType type, string fieldName) in fields)
            {
                body.Write((uint)type, 3).String(fieldName);
            }

            body.Write((uint)GameEventValueType.None, 3);
        }

        writer.Message(NetMessageType.GameEventList)
            .Write((uint)events.Length, 9)
            .Write((uint)body.BitCount, 20);

        AppendBits(writer, body);
    }

    /// <summary>Wraps an event body in a <c>svc_GameEvent</c> with its 11-bit length.</summary>
    internal static byte[] WrapEvent(BitWriter body)
    {
        BitWriter writer = new();
        AppendEvent(writer, body);
        return writer.Build();
    }

    /// <summary>Appends a <c>svc_GameEvent</c> to an existing writer.</summary>
    internal static void AppendEvent(BitWriter writer, BitWriter body)
    {
        writer.Message(NetMessageType.GameEvent).Write((uint)body.BitCount, 11);
        AppendBits(writer, body);
    }
}
