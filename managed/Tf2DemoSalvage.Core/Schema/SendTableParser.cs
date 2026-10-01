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
    /// <remarks>
    /// Internal so <c>WireEncodingConformanceTests</c> checks the value this parser uses rather than
    /// a copy of it. Sixteen, not the seventeen of <c>SPROP_NUMFLAGBITS</c> — reading one bit too
    /// many here consumes part of the next field and takes the whole schema with it.
    /// </remarks>
    internal const int FlagBits = 16;

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
    /// <remarks>
    /// **At protocol 15 the payload decides the numbering, because two builds wrote it.** Build
    /// 3862 (June 2009) numbered without <c>DPT_VectorXY</c>; the builds after it numbered with it
    /// (<c>public/dt_common.h:108-114</c>) and still announced 15 (B440). So a protocol-15 schema is
    /// read the June 2009 way, and read the other way only when that reading is not WHOLE — when it
    /// throws, or when it reaches a class list and stops short of the payload's last byte. The
    /// schema says which numbering wrote it by which one reads it to the end: every schema in the
    /// corpus that parses ends within seven bits, and a VectorXY-numbered one read the old way comes
    /// back as one table and 26,207 classes, which is the number B440 was filed with.
    ///
    /// Every other protocol has one numbering, measured on both sides of 15, and gets only that.
    /// </remarks>
    public static DemoSchema Parse(ReadOnlySpan<byte> payload, ushort networkProtocol = CurrentProtocol) =>
        ParseWhole(KnownSchema.Complete(payload), networkProtocol);

    /// <summary>
    /// <see cref="Parse"/> on a payload already completed. **Every consumer of a schema comes through
    /// <see cref="Parse"/>, so this is the one place build 3258's SourceTV cut is decoded with the
    /// whole schema** (B24, <see cref="KnownSchema"/>) — deliberately better than the 2007 engine,
    /// which Host_Errors on those demos' first entity. Only the decode sees the whole schema; the
    /// demo's own bytes are what a writer writes back.
    /// </summary>
    private static DemoSchema ParseWhole(ReadOnlySpan<byte> payload, ushort networkProtocol)
    {
        if (networkProtocol != VectorXyProtocol)
        {
            return ReadWhole(payload, networkProtocol, networkProtocol > VectorXyProtocol, out _);
        }

        Reading june2009 = Attempt(payload, vectorXy: false);
        if (june2009 is { Schema: { } asBuild3862, BitsLeft: < BitsPerByte })
        {
            return asBuild3862;
        }

        Reading later = Attempt(payload, vectorXy: true);
        if (later is { Schema: { } asTheBuildsAfter, BitsLeft: < BitsPerByte })
        {
            return asTheBuildsAfter;
        }

        // Stryker disable all : the String mutator wraps the interpolated literal in a ternary that
        // cannot bind to string.Create's interpolated-string handler (CS1620), and Safe Mode then
        // drops every mutation in this method — B410.
        throw new InvalidDataException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Neither numbering protocol 15 was written in reads this dem_datatables to its end " +
                $"(B440). Without DPT_VectorXY, as build 3862 wrote it: {Describe(june2009)} " +
                $"With DPT_VectorXY, as the builds after it wrote it: {Describe(later)}"),
            june2009.Failure ?? later.Failure);

        // Stryker restore all
    }

    /// <summary>One way of reading a protocol-15 schema, and how it went.</summary>
    /// <param name="Schema">The schema, when the reading got to the end of its class list.</param>
    /// <param name="BitsLeft">What that left unread after the class list.</param>
    /// <param name="Failure">Why it did not get there, otherwise.</param>
    private readonly record struct Reading(
        DemoSchema? Schema, int BitsLeft, InvalidDataException? Failure);

    /// <summary>Bits in a byte: a payload is padded to one, so a whole reading leaves fewer.</summary>
    private const int BitsPerByte = 8;

    /// <summary>Reads a protocol-15 schema in one numbering, keeping a failure rather than throwing it.</summary>
    /// <remarks>
    /// A failure here is an answer, not an error: it is the evidence that this numbering is not
    /// the one the payload was written in, and <see cref="Parse"/> reports it if the other numbering
    /// fails as well.
    /// </remarks>
    private static Reading Attempt(ReadOnlySpan<byte> payload, bool vectorXy)
    {
        try
        {
            DemoSchema schema = ReadWhole(payload, VectorXyProtocol, vectorXy, out int bitsLeft);
            return new Reading(schema, bitsLeft, null);
        }
        catch (InvalidDataException failure)
        {
            return new Reading(null, 0, failure);
        }
    }

    /// <summary>What one reading of a protocol-15 schema came to, for the refusal.</summary>
    private static string Describe(Reading reading) =>
        reading.Failure?.Message ?? string.Create(
            CultureInfo.InvariantCulture,
            $"its class list ends {reading.BitsLeft} bits before the payload does.");

    /// <summary>Reads a schema in the numbering given, running off the end as a refusal.</summary>
    private static DemoSchema ReadWhole(
        ReadOnlySpan<byte> payload, ushort networkProtocol, bool vectorXy, out int bitsLeft)
    {
        try
        {
            return ReadSchema(payload, networkProtocol, vectorXy, out bitsLeft);
        }
        catch (EndOfStreamException exhausted)
        {
            // Running off the end is not the same failure as reading a wrong width, and the
            // difference matters to a caller. A SourceTV recording on TF2's launch build truncates
            // this payload at exactly 65,536 bytes — the POV of the same session carries 85,063 —
            // so the demo is intact and its schema is simply cut off. Entities cannot be decoded
            // from it, and nothing else about the demo is affected.
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The dem_datatables payload ends mid-table after {payload.Length} bytes. " +
                $"A schema truncated on the wire cannot be completed by guessing, so no entity " +
                $"decoding is possible for this demo; the rest of it is unaffected."),
                exhausted);

            // Stryker restore all
        }
    }

    private static DemoSchema ReadSchema(
        ReadOnlySpan<byte> payload, ushort networkProtocol, bool vectorXy, out int bitsLeft)
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
                properties.Add(ReadProperty(ref reader, networkProtocol, vectorXy));
            }

            tables.Add(new SendTable(name, needsDecoder, properties));
        }

        int classCount = (int)reader.ReadUInt32(ClassCountBits);

        Primitives.WireBounds.EnsureCountFits(
            "dem_datatables class list", classCount, ClassIdBits + 16, reader.BitsRemaining);

        List<ServerClass> classes = new(classCount);
        for (int i = 0; i < classCount; i++)
        {
            classes.Add(new ServerClass(
                (int)reader.ReadUInt32(ClassIdBits),
                NetBitReading.ReadString(ref reader),
                NetBitReading.ReadString(ref reader)));
        }

        bitsLeft = reader.BitsRemaining;
        return new DemoSchema(tables, classes);
    }

    /// <summary>The protocol current builds record at.</summary>
    private const ushort CurrentProtocol = 24;

    /// <summary>
    /// Last protocol any build numbered property types without <c>VectorXY</c> — and the one
    /// protocol numbered both ways.
    /// </summary>
    /// <remarks>
    /// **Measured on both sides, and the protocol turned out not to be the boundary.** Build 3862
    /// (June 2009) numbers protocol 15 the old way; the June 2011 client's protocol-16 schema parses
    /// under the current numbering to 256 server classes whose properties match the classes they
    /// were read for (B18). That was read as "inserted between 15 and 16". It was not: the hl2sdk
    /// branch TF2 builds against took <c>DPT_VectorXY</c> on 14 August 2009 (<c>c789d33e</c>), and
    /// the two protocol-15 SourceTV demos of B440 — one named for November 2010 — number with it.
    /// So <see cref="Parse"/> reads protocol 15 both ways and keeps the reading that is whole.
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
    ///
    /// **At protocol 15 this answers for build 3862**, which numbered the old way; the builds after
    /// it numbered the current way at the same protocol, and <see cref="Parse"/> reads their schemas
    /// so (B440).
    /// </remarks>
    public static SendPropType MapPropertyType(uint wireType, ushort networkProtocol) =>
        Numbered(wireType, networkProtocol > VectorXyProtocol);

    /// <summary>Translates a wire type code in the numbering given.</summary>
    private static SendPropType Numbered(uint wireType, bool vectorXy)
    {
        if (vectorXy)
        {
            return (SendPropType)wireType;
        }

        // Below VectorXY's insertion point the two numberings agree, so only the three types
        // above it are remapped.
        return wireType < (uint)SendPropType.VectorXY
            ? (SendPropType)wireType
            : (SendPropType)(wireType + 1);
    }

    private static SendProperty ReadProperty(ref BitReader reader, ushort networkProtocol, bool vectorXy)
    {
        SendPropType type = Numbered(reader.ReadUInt32(TypeBits), vectorXy);
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
