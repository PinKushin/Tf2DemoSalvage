using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>Width of a property's bit-count field before it was widened to seven.</summary>
    private const int OldBitCountBits = 6;

    /// <summary>Last protocol whose property bit-count field is six bits wide.</summary>
    /// <remarks>
    /// **Measured, and absent from <c>proto_version.h</c>** — the same blind spot as the message
    /// type width (B17) and the <c>SendPropType</c> renumbering (B18). Six bits holds 0–63, which
    /// is enough for any property Source actually sends; the seventh bit arrived with room to
    /// spare rather than out of need, which is presumably why nobody wrote the change down.
    ///
    /// One bit, and it costs the entire file. The schema is a single continuous bit stream with
    /// no per-table length, so reading seven bits where six were written desynchronises after the
    /// first numeric property and every table after it is noise.
    /// </remarks>
    private const ushort SixBitBitCountProtocol = 14;

    private const int ElementCountBits = 10;
    private const int ClassCountBits = 16;
    private const int ClassIdBits = 16;

    /// <summary>Parses the payload of a <c>dem_datatables</c> command.</summary>
    /// <param name="payload">The command's raw payload.</param>
    /// <param name="networkProtocol">
    /// The demo's network protocol, from its header. Property types are numbered
    /// differently before and after <c>DPT_VectorXY</c> was added — see
    /// <see cref="MapPropertyType"/>. Defaults to the current protocol.
    /// </param>
    /// <returns>The demo's entity schema.</returns>
    public static DemoSchema Parse(ReadOnlySpan<byte> payload, ushort networkProtocol = CurrentProtocol)
    {
        try
        {
            return ReadSchema(payload, networkProtocol);
        }
        catch (EndOfStreamException exhausted)
        {
            // Running off the end is not the same failure as reading a wrong width, and the
            // difference matters to a caller. A SourceTV recording on TF2's launch build truncates
            // this payload at exactly 65,536 bytes — the POV of the same session carries 85,063 —
            // so the demo is intact and its schema is simply cut off. Entities cannot be decoded
            // from it, and nothing else about the demo is affected.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The dem_datatables payload ends mid-table after {payload.Length} bytes. " +
                $"A schema truncated on the wire cannot be completed by guessing, so no entity " +
                $"decoding is possible for this demo; the rest of it is unaffected."),
                exhausted);
        }
    }

    private static DemoSchema ReadSchema(ReadOnlySpan<byte> payload, ushort networkProtocol)
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
                properties.Add(ReadProperty(ref reader, networkProtocol));
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

    /// <summary>The protocol current builds record at.</summary>
    private const ushort CurrentProtocol = 24;

    /// <summary>Last protocol whose property types were numbered without <c>VectorXY</c>.</summary>
    /// <remarks>
    /// **Bounded, not exact — the same open boundary as the message type width.** Protocol 15
    /// uses the 2009 numbering and 24 uses the current one, both measured; the change is
    /// somewhere in 16–23. See <c>RISKS.md</c> B18.
    /// </remarks>
    private const ushort VectorXyProtocol = 15;

    /// <summary>Translates a wire type code into the canonical enum for its era.</summary>
    /// <param name="wireType">The raw value read from the schema.</param>
    /// <param name="networkProtocol">The demo's network protocol.</param>
    /// <returns>The property type.</returns>
    /// <remarks>
    /// Valve's <c>dt_common.h</c>, between the <c>orangebox</c> branch and the <c>tf2</c> branch:
    ///
    /// <code>
    /// 2009     Int=0 Float=1 Vector=2 String=3 Array=4 DataTable=5
    /// current  Int=0 Float=1 Vector=2 VectorXY=3 String=4 Array=5 DataTable=6
    /// </code>
    ///
    /// <c>DPT_VectorXY</c> was inserted at 3, pushing the three above it up by one. Reading a
    /// 2009 schema with the current numbering turns every nested table into an array, and the
    /// schema is where entity decoding starts — so the whole file becomes unreadable a few
    /// hundred bits in.
    /// </remarks>
    public static SendPropType MapPropertyType(uint wireType, ushort networkProtocol)
    {
        if (networkProtocol > VectorXyProtocol)
        {
            return (SendPropType)wireType;
        }

        // Below VectorXY's insertion point the two numberings agree, so only the three types
        // above it are remapped.
        return wireType < (uint)SendPropType.VectorXY
            ? (SendPropType)wireType
            : (SendPropType)(wireType + 1);
    }

    private static SendProperty ReadProperty(ref BitReader reader, ushort networkProtocol)
    {
        SendPropType type = MapPropertyType(reader.ReadUInt32(TypeBits), networkProtocol);
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
        int bitCount = (int)reader.ReadUInt32(
            networkProtocol > SixBitBitCountProtocol ? BitCountBits : OldBitCountBits);

        return new SendProperty(type, name, flags, string.Empty, low, high, bitCount, 0);
    }
}
