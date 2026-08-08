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
        if (message.Body.IsEmpty || snapshot >= limit)
        {
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
    _ => value.ToString(),
};
