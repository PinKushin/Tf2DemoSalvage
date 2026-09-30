using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Writes a <c>dem_datatables</c> payload, so a synthetic demo can carry a schema.
/// </summary>
/// <remarks>
/// **The one missing piece that kept the entity half of this codebase corpus-only.** A demo's
/// entity stream cannot be decoded without a schema, the schema arrives in <c>dem_datatables</c>,
/// and this project reads that command but has never written one — the text assembly carries the
/// payload verbatim as bits, because reproducing it is not needed to round-trip a real demo. So
/// every test touching entities needed a recording, and <c>DemoTimeline</c> sat at 424 of 528
/// lines never executed by <c>Core.Tests</c>.
///
/// **This is a test instrument, not production code, and the distinction is deliberate.** Nothing
/// in the parser is used to build the bytes: the layout below is written out from Valve's own
/// order, so a schema this produces and a schema the parser reads agree only if both are right.
/// Building it by inverting <c>SendTableParser</c> field-by-field would have made the round trip
/// tautological — the classic fixture-authored-from-the-same-belief trap, which has cost three
/// bugs in this repository already.
///
/// That is also why it lives in the test project rather than beside the parser. If a demo ever
/// needs to be *written* with a schema — Phase 4 territory — this becomes production code and
/// gains a conformance test of its own. Until then it exists to make specimens.
///
/// See <c>docs/memory/author-the-specimen-the-corpus-lacks.md</c> and
/// <c>docs/memory/fixtures-are-the-weak-point.md#put-the-real-file-in-the-fixture</c>.
/// </remarks>
internal static class SyntheticSchema
{
    /// <summary>Property type field, <c>SendPropType</c> on the wire.</summary>
    private const int TypeBits = 5;

    /// <summary>
    /// Flags field: <c>SPROP_NUMFLAGBITS_NETWORKED</c>, sixteen.
    /// </summary>
    /// <remarks>
    /// Sixteen and not the seventeen of <c>SPROP_NUMFLAGBITS</c>, which counts a server-only flag
    /// that never reaches the wire. Seventeen is the more prominently named constant and is the
    /// trap; writing one bit too many here desynchronises everything after the first property, and
    /// the schema is one continuous stream with no per-table length to resynchronise on.
    /// </remarks>
    private const int FlagBits = 16;

    private const int PropCountBits = 10;
    private const int ElementCountBits = 10;
    private const int ClassCountBits = 16;
    private const int ClassIdBits = 16;

    /// <summary>Property bit-count field, widened from six to seven after protocol 14.</summary>
    private const int BitCountBits = 7;

    private const int OldBitCountBits = 6;
    private const ushort SixBitBitCountProtocol = 14;

    /// <summary>
    /// Last protocol any build numbered property types without <c>DPT_VectorXY</c> — and not every
    /// build at it did (B440).
    /// </summary>
    private const ushort VectorXyProtocol = 15;

    /// <summary>Encodes tables and classes as a <c>dem_datatables</c> payload.</summary>
    /// <param name="schema">The schema to write.</param>
    /// <param name="networkProtocol">Protocol, which sizes the bit-count field and numbers types.</param>
    /// <param name="vectorXyNumbering">
    /// Whether types are numbered with <c>DPT_VectorXY</c> at 3, or null for the numbering the
    /// protocol's first builds used. Only protocol 15 needs saying: TF2 build 3862 (June 2009)
    /// numbered without it and the builds after it at the same protocol numbered with it, which is
    /// what the two SourceTV demos of B440 are.
    /// </param>
    /// <returns>The payload bytes, as the command would carry them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
    public static byte[] Write(
        DemoSchema schema,
        ushort networkProtocol = SyntheticDemo.DefaultProtocol,
        bool? vectorXyNumbering = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        bool vectorXy = vectorXyNumbering ?? networkProtocol > VectorXyProtocol;
        BitWriter writer = new();

        foreach (SendTable table in schema.Tables)
        {
            // A set bit precedes each table; a clear one ends the list.
            writer.WriteBit(true).WriteBit(table.NeedsDecoder);
            writer.WriteString(table.Name);
            writer.Write((uint)table.Properties.Count, PropCountBits);

            foreach (SendProperty property in table.Properties)
            {
                WriteProperty(writer, property, networkProtocol, vectorXy);
            }
        }

        writer.WriteBit(false);

        writer.Write((uint)schema.ServerClasses.Count, ClassCountBits);
        foreach (ServerClass entry in schema.ServerClasses)
        {
            writer.Write((uint)entry.Id, ClassIdBits);
            writer.WriteString(entry.ClassName);
            writer.WriteString(entry.TableName);
        }

        return writer.Build();
    }

    private static void WriteProperty(
        BitWriter writer, SendProperty property, ushort protocol, bool vectorXy)
    {
        writer.Write(WireType(property.Type, vectorXy), TypeBits);
        writer.WriteString(property.Name);
        writer.Write((uint)property.Flags, FlagBits);

        // Three mutually exclusive shapes, chosen by the type AND the exclude flag — an excluded
        // property names a table whatever its declared type says, which is the case a reader that
        // switches on type alone gets wrong.
        if (property.Type == SendPropType.DataTable || property.IsExcluded)
        {
            writer.WriteString(property.ReferencedTable);
            return;
        }

        if (property.Type == SendPropType.Array)
        {
            writer.Write((uint)property.ElementCount, ElementCountBits);
            return;
        }

        writer.Write((uint)BitConverter.SingleToInt32Bits(property.LowValue), 32)
            .Write((uint)BitConverter.SingleToInt32Bits(property.HighValue), 32)
            .Write(
                (uint)property.BitCount,
                protocol > SixBitBitCountProtocol ? BitCountBits : OldBitCountBits);
    }

    /// <summary>Turns a canonical type back into the code its numbering puts on the wire.</summary>
    /// <remarks>
    /// The inverse of the parser's own mapping, and it exists because <c>DPT_VectorXY</c> was
    /// inserted at 3 rather than appended — so String, Array and DataTable each sit one lower in the
    /// numbering without it. Writing the modern numbering where the old one belongs turns every
    /// nested table into an array, which is what makes a whole schema unreadable a few hundred bits
    /// in.
    /// </remarks>
    private static uint WireType(SendPropType type, bool vectorXy)
    {
        if (vectorXy)
        {
            return (uint)type;
        }

        if (type == SendPropType.VectorXY)
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                "VectorXY has no code in the numbering without it — the one protocols 11 to 14 and " +
                "TF2 build 3862 of June 2009 wrote (B440).");
        }

        return type < SendPropType.VectorXY ? (uint)type : (uint)type - 1;
    }
}
