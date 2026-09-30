using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every entity snapshot that does not decode, or does not re-encode to its own bits, and what surrounds it (B443).
/// </summary>
/// <remarks>
/// **Written for B443, and what it found was the instrument.** Two demos' snapshots re-encoded LONGER than their
/// stated length with every entity prefix matching, so the report blamed "the removal list". This walk printed what
/// the report could not — and the first thing it printed was dozens of snapshots that did not decode at all, which
/// the report had been catching and skipping. Both came from one walk that read no packet until it had a schema:
/// the signon packets before <c>dem_datatables</c> create the string tables, so the state lacked them and every
/// later <c>svc_UpdateStringTable</c> misaligned its packet. Read the way production reads — every packet, through
/// <see cref="DemoCorpus.EntitySnapshots"/> — both demos re-encode 894 of 894.
///
/// **The second decode is the engine's reading.** <c>SVC_PacketEntities::ReadFromBuffer</c> (engine.dll
/// <c>0x1801dec90</c>) copies the whole packet's <c>bf_read</c> into <c>m_DataIn</c> and then seeks past
/// <c>m_nLength</c>, so the client's entity read is not bounded at the stated length: it runs on into the next
/// message's bits and overflows only at the packet's end (<c>CL_ParsePacketEntities:  buffer read overflow</c>,
/// <c>0x18006a120</c>). For a snapshot that does not re-encode, this decodes the body again over the rest of the
/// packet and says whether the two readings agree.
///
/// <code>
///   entity-overrun demostf-cp_sunshine-2026-08-08-2233
///   entity-overrun demostf-koth_cascade_rc1a-1491232 900
/// </code>
/// </remarks>
public sealed class EntityOverrunProbe : IProbe
{
    /// <summary>The round-trip test's own command limit, so a default run looks at the same snapshots.</summary>
    private const int DefaultCommandLimit = 900;

    /// <summary>How many bits either side of an edge are printed.</summary>
    private const int Window = 24;

    /// <inheritdoc/>
    public string Name => "entity-overrun";

    /// <inheritdoc/>
    public string Summary =>
        "snapshots that do not decode or re-encode, with the bits around their stated end (B443): "
        + "entity-overrun <demo> [commands]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("entity-overrun <demo> [commands]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int limit = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultCommandLimit;

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        // **The first dem_datatables in the file, as DemoTimeline.Build takes it** — found before the walk, so a
        // snapshot is never met without the schema it needs.
        DemoCommand tables = DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .FirstOrDefault(command => command.Type == DemoCommandType.DataTables);

        if (tables.Type != DemoCommandType.DataTables)
        {
            output.WriteLine("The demo carries no dem_datatables, so it has no entities to decode.");
            return;
        }

        DemoSchema schema = SendTableParser.Parse(tables.Payload.Span, protocol);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        int snapshots = 0;
        int exact = 0;
        int undecodable = 0;
        int differing = 0;

        foreach ((DemoCommand command, PacketEntitiesMessage snapshot, int bodyStart) in
            DemoCorpus.EntitySnapshots(bytes, limit))
        {
            snapshots++;

            IReadOnlyList<DecodedEntity> entities;
            try
            {
                entities = decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            {
                undecodable++;
                output.WriteLine($"tick {command.Tick}: does not decode: {error.Message}");
                continue;
            }

            int sectionBits = decoder.EntitySectionBits;
            List<int> removed = [.. decoder.RemovedEntities];
            byte[] rewritten = decoder.EncodeEntities(
                entities, removed, snapshot.IsDelta, snapshot.LengthBits, out int encodedBits);

            int comparable = Math.Min(encodedBits, snapshot.LengthBits);
            int difference = encodedBits > snapshot.LengthBits
                ? comparable
                : FirstDifferingBit(snapshot.Body.Span, rewritten, comparable);

            if (difference < 0)
            {
                exact++;
                continue;
            }

            differing++;
            byte[] extended = FromBit(command.Payload.Span, bodyStart);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {command.Tick}: {(snapshot.IsDelta ? "delta" : "full")} snapshot, {snapshot.UpdatedEntries} " +
                $"entities, stated {snapshot.LengthBits} bits, decoder's entity section ended at {sectionBits}, " +
                $"re-encoded {encodedBits}, first difference at {difference}"));
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    removals read: [{string.Join(", ", removed)}]; body at packet bit {bodyStart}, stated end at " +
                $"packet bit {bodyStart + snapshot.LengthBits} of {command.Payload.Length * 8}"));

            int from = Math.Max(0, Math.Min(sectionBits, snapshot.LengthBits) - Window);
            output.WriteLine($"    isolated body from bit {from}: {Bits(snapshot.Body.Span, from, (snapshot.LengthBits - from) + Window, snapshot.LengthBits)}");
            output.WriteLine($"    packet    from bit {from}: {Bits(extended, from, (snapshot.LengthBits - from) + Window, snapshot.LengthBits)}");
            output.WriteLine($"    re-encode from bit {from}: {Bits(rewritten, from, (encodedBits - from) + Window, snapshot.LengthBits)}");

            ReportEngineReading(output, schema, snapshot, extended, entities, removed);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"SNAPSHOTS {snapshots} read, {exact} re-encode exactly, {differing} do not, {undecodable} do not decode"));
    }

    /// <summary>Decodes the body as the engine does — reading on past the stated end — and compares.</summary>
    /// <remarks>
    /// A fresh decoder, because <see cref="EntityDecoder"/> carries entity classes and baselines across snapshots
    /// and this second pass must not disturb the first one's state. It knows no entity that entered earlier, so a
    /// delta for one throws here; that is reported, and only an ENTER-only snapshot compares cleanly.
    /// </remarks>
    private static void ReportEngineReading(
        TextWriter output,
        DemoSchema schema,
        PacketEntitiesMessage snapshot,
        byte[] extended,
        IReadOnlyList<DecodedEntity> isolated,
        IReadOnlyList<int> isolatedRemoved)
    {
        EntityDecoder engine = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<DecodedEntity> read;
        try
        {
            read = engine.Decode(extended, snapshot, extended.Length * 8);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            output.WriteLine($"    engine reading: does not decode on a fresh decoder: {error.Message}");
            return;
        }

        bool sameEntities = read.Count == isolated.Count && read.Zip(isolated).All(pair =>
            pair.First.EntityIndex == pair.Second.EntityIndex &&
            pair.First.Properties.Count == pair.Second.Properties.Count &&
            pair.First.Properties.Zip(pair.Second.Properties).All(
                property => Equals(property.First.Value, property.Second.Value)));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    engine reading: entity section ends at {engine.EntitySectionBits}, removals " +
            $"[{string.Join(", ", engine.RemovedEntities)}] (isolated: [{string.Join(", ", isolatedRemoved)}]); " +
            $"entities {(sameEntities ? "identical" : "DIFFER")} to the isolated reading"));
    }

    /// <summary>The payload from one bit onward, realigned to bit zero.</summary>
    private static byte[] FromBit(ReadOnlySpan<byte> payload, int startBit)
    {
        BitReader reader = new(payload);

        for (int skipped = 0; skipped < startBit;)
        {
            int step = Math.Min(32, startBit - skipped);
            reader.ReadUInt32(step);
            skipped += step;
        }

        return NetBitReading.CopyBits(ref reader, reader.BitsRemaining);
    }

    /// <summary>A run of bits, with a bar at the stated end so the edge can be seen.</summary>
    private static string Bits(ReadOnlySpan<byte> source, int startBit, int count, int statedEnd)
    {
        StringBuilder text = new();

        for (int i = 0; i < count; i++)
        {
            int bit = startBit + i;

            if (bit == statedEnd)
            {
                text.Append('|');
            }

            int index = bit / 8;
            text.Append(index >= source.Length ? '.' : (char)('0' + ((source[index] >> (bit % 8)) & 1)));
        }

        return text.ToString();
    }

    /// <summary>Index of the first bit that differs, or -1 when they match.</summary>
    private static int FirstDifferingBit(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int index = bit / 8;

            if (index >= right.Length || index >= left.Length)
            {
                return bit;
            }

            int shift = bit % 8;

            if (((left[index] >> shift) & 1) != ((right[index] >> shift) & 1))
            {
                return bit;
            }
        }

        return -1;
    }
}
