using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

// Usage:
//   dump <demo> flat ALL|<ClassName>   - flattened property lists, for the B12 differential
//   dump <demo> snapshots <limit>      - per-snapshot entity updates, for the B13 differential
string demo = args[0];
string mode = args.Length > 1 ? args[1] : "flat";
string arg = args.Length > 2 ? args[2] : "ALL";

byte[] bytes = File.ReadAllBytes(demo);
DemoSchema? schema = null;
foreach (DemoCommand c in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
{
    if (c.Type == DemoCommandType.DataTables)
    {
        schema = SendTableParser.Parse(c.Payload.Span);
        break;
    }
}

if (mode == "text")
{
    DemoHeader h = DemoHeader.Parse(bytes);
    List<DemoCommand> cmds = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];
    using StreamWriter sw = new(arg);
    Tf2DemoSalvage.Core.Text.DemoTextDumper.Write(
        sw, Path.GetFileName(demo), h, cmds,
        new Tf2DemoSalvage.Core.Text.DemoDumpOptions { IncludeCommandListing = false },
        new ConsoleBar());
    Console.WriteLine();
    return;
}

if (mode == "verify")
{
    DemoHeader hdr = DemoHeader.Parse(bytes);
    NetDecodeState vs = new();
    EntityDecoder dec = new(schema!, EntityDecoder.ClassIdBits(schema!.ServerClasses.Count));
    int snaps = 0, ents = 0, props = 0, stops = 0;
    bool started = false;
    string? err = null;
    Dictionary<string,int> stopKinds = new();

    foreach (DemoCommand cmd in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
    {
        if (cmd.Type is not (DemoCommandType.Signon or DemoCommandType.Packet)) { continue; }
        NetMessageReadResult r = NetMessageReader.Read(cmd.Payload.Span, vs);
        if (r.StoppedAt is NetMessageType st)
        {
            stops++;
            string k = st.ToString();
            stopKinds[k] = stopKinds.TryGetValue(k, out int v) ? v + 1 : 1;
        }

        foreach (PacketEntitiesMessage m in r.Messages.OfType<PacketEntitiesMessage>())
        {
            if (m.Body.IsEmpty) { continue; }
            started |= m.IsFullSnapshot;
            if (!started) { continue; }
            try
            {
                IReadOnlyList<DecodedEntity> es = dec.Decode(m.Body.Span, m, m.LengthBits);
                snaps++; ents += es.Count; props += es.Sum(e => e.Properties.Count);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                err = ex.Message; goto done;
            }
        }
    }

done:
    string kinds = stopKinds.Count == 0 ? "-" : string.Join(" ", stopKinds.Select(k => k.Key + "x" + k.Value));
    Console.WriteLine($"{hdr.MapName,-24} frames={hdr.PlaybackFrames,-7} snaps={snaps,-7} ents={ents,-9} props={props,-10} stops={stops,-4} {kinds}");
    if (err is not null) { Console.WriteLine($"  STOPPED: {err}"); }
    return;
}

if (mode == "stops")
{
    NetDecodeState st = new();
    Dictionary<string,int> stops = new();
    int packets = 0, lostAfter = 0;
    foreach (DemoCommand cmd in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
    {
        if (cmd.Type is not (DemoCommandType.Signon or DemoCommandType.Packet)) { continue; }
        packets++;
        NetMessageReadResult r = NetMessageReader.Read(cmd.Payload.Span, st);
        if (r.StoppedAt is NetMessageType t)
        {
            string k = t.ToString();
            stops[k] = stops.TryGetValue(k, out int v) ? v + 1 : 1;
            lostAfter++;
            Console.WriteLine($"  stop at packet {packets}: {k}");
        }
        if (packets >= int.Parse(arg)) { break; }
    }

    Console.WriteLine($"packets={packets} stopped={lostAfter}");
    foreach (var kv in stops.OrderByDescending(k => k.Value)) { Console.WriteLine($"  {kv.Key}	{kv.Value}"); }
    return;
}

if (mode == "props")
{
    ServerClass target = schema!.ServerClasses.First(c => c.ClassName == "CTFPlayer");
    IReadOnlyList<FlatProperty> flat2 = SchemaFlattener.Flatten(schema, target);
    foreach (int i in arg.Split(',').Select(int.Parse))
    {
        SendProperty pr = flat2[i].Property;
        Console.WriteLine($"{i}	{flat2[i].OwnerTable}.{pr.Name}	type={pr.Type}	flags=0x{pr.Flags:X4}	bits={pr.BitCount}	low={pr.LowValue}	high={pr.HighValue}	elems={pr.ElementCount}");
    }

    return;
}

if (mode == "flat")
{
    foreach (ServerClass sc in schema!.ServerClasses)
    {
        if (arg != "ALL" && sc.ClassName != arg)
        {
            continue;
        }

        IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, sc);
        for (int i = 0; i < flat.Count; i++)
        {
            Console.WriteLine($"{sc.Id}\t{sc.ClassName}\t{i}\t{flat[i].OwnerTable}.{flat[i].Property.Name}");
        }
    }

    return;
}

int limit = int.Parse(arg);
EntityDecoder decoder = new(schema!, EntityDecoder.ClassIdBits(schema!.ServerClasses.Count));
NetDecodeState state = new();
int snapshot = 0;

foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
{
    if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
    {
        continue;
    }

    foreach (PacketEntitiesMessage message in NetMessageReader.Read(command.Payload.Span, state)
        .Messages.OfType<PacketEntitiesMessage>())
    {
        if (snapshot >= limit)
        {
            continue;
        }

        if (message.Body.IsEmpty)
        {
            Console.WriteLine($"{snapshot}	EMPTYBODY	delta={message.IsDelta}	updated={message.UpdatedEntries}	lenBits={message.LengthBits}");
            snapshot++;
            continue;
        }

        IReadOnlyList<DecodedEntity> entities;
        try
        {
            entities = decoder.Decode(message.Body.Span, message, message.LengthBits);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            Console.WriteLine($"{snapshot}\tSTOPPED\t{error.Message}");
            return;
        }

        foreach (DecodedEntity entity in entities)
        {
            Console.WriteLine(
                $"{snapshot}\t{entity.EntityIndex}\t{entity.UpdateType}\t{entity.ClassId}\t" +
                string.Join(",", entity.Properties.Select(p => $"{p.Index}={Show(p.Value)}")));
        }

        snapshot++;
    }
}

// Round-trippable formatting, so a textual diff against the oracle reports only real
// differences. PropertyValue.ToString rounds floats for readability, which is right for a text
// dump and useless for a differential.
static string Show(PropertyValue value) => value.Kind switch
{
    PropertyValueKind.Float => value.AsFloat.ToString("R", CultureInfo.InvariantCulture),
    PropertyValueKind.Vector => FormattableString.Invariant(
        $"({value.AsVector.X:R}, {value.AsVector.Y:R}, {value.AsVector.Z:R})"),
    PropertyValueKind.VectorXY => FormattableString.Invariant(
        $"({value.AsVectorXY.X:R}, {value.AsVectorXY.Y:R})"),
    PropertyValueKind.Array => "[" + string.Concat(value.AsArray.Select(Show)) + "]",
    _ => value.ToString(),
};


/// <summary>Draws the progress bar on one rewritten console line.</summary>
internal sealed class ConsoleBar : IProgress<Tf2DemoSalvage.Core.Text.DumpProgress>
{
    public void Report(Tf2DemoSalvage.Core.Text.DumpProgress value)
    {
        Console.Write((char)13);
        Console.Write(value.ToBar());
    }
}
