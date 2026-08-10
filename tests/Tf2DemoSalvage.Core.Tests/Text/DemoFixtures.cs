using System.Collections.Generic;
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
}
