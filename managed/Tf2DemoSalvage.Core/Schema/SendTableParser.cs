using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Parses a <c>dem_datatables</c> payload into the demo's entity schema.
/// </summary>
/// <remarks>
/// The tables are one continuous bit stream with no per-table length, so there is nothing to
/// resynchronise against: a single wrong field width turns every table after it into noise.
///
/// The width most likely to be got wrong is the flags field. It is transmitted with
/// <c>SPROP_NUMFLAGBITS_NETWORKED</c> (16) bits — not <c>SPROP_NUMFLAGBITS</c> (17), which
/// counts a 17th flag the SDK marks server-side only. 17 is the more prominently named
/// constant, which is exactly the trap.
/// </remarks>
public static class SendTableParser
{
    /// <summary>Flags width on the wire: <c>SPROP_NUMFLAGBITS_NETWORKED</c>.</summary>
    private const int FlagBits = 16;

    private const int TypeBits = 5;
    private const int PropCountBits = 10;
    private const int BitCountBits = 7;
    private const int ElementCountBits = 10;
    private const int ClassCountBits = 16;
    private const int ClassIdBits = 16;

    /// <summary>Parses the payload of a <c>dem_datatables</c> command.</summary>
    /// <param name="payload">The command's raw payload.</param>
    /// <returns>The demo's entity schema.</returns>
    public static DemoSchema Parse(ReadOnlySpan<byte> payload)
    {
        BitReader reader = new(payload);
        List<SendTable> tables = [];

        // A one-bit flag precedes each table and is clear when the list ends.
        while (reader.ReadBit())
        {
            bool needsDecoder = reader.ReadBit();
            string name = NetBitReading.ReadString(ref reader);
            int propertyCount = (int)reader.ReadUInt32(PropCountBits);

            List<SendProperty> properties = new(propertyCount);
            for (int i = 0; i < propertyCount; i++)
            {
                properties.Add(ReadProperty(ref reader));
            }

            tables.Add(new SendTable(name, needsDecoder, properties));
        }

        int classCount = (int)reader.ReadUInt32(ClassCountBits);
        List<ServerClass> classes = new(classCount);
        for (int i = 0; i < classCount; i++)
        {
            classes.Add(new ServerClass(
                (int)reader.ReadUInt32(ClassIdBits),
                NetBitReading.ReadString(ref reader),
                NetBitReading.ReadString(ref reader)));
        }

        return new DemoSchema(tables, classes);
    }

    private static SendProperty ReadProperty(ref BitReader reader)
    {
        SendPropType type = (SendPropType)reader.ReadUInt32(TypeBits);
        string name = NetBitReading.ReadString(ref reader);
        int flags = (int)reader.ReadUInt32(FlagBits);

        // Three mutually exclusive shapes follow, chosen by the type *and* the exclude flag —
        // an excluded property names a table whatever its declared type says.
        if (type == SendPropType.DataTable || (flags & SendProperty.ExcludeFlag) != 0)
        {
            return new SendProperty(
                type, name, flags, NetBitReading.ReadString(ref reader), 0f, 0f, 0, 0);
        }

        if (type == SendPropType.Array)
        {
            return new SendProperty(
                type, name, flags, string.Empty, 0f, 0f, 0,
                (int)reader.ReadUInt32(ElementCountBits));
        }

        float low = BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32));
        float high = BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32));
        int bitCount = (int)reader.ReadUInt32(BitCountBits);

        return new SendProperty(type, name, flags, string.Empty, low, high, bitCount, 0);
    }
}
