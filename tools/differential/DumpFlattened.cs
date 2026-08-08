using System;
using System.Collections.Generic;
using System.IO;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

string demo = args[0];
string wanted = args.Length > 1 ? args[1] : "ALL";
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

foreach (ServerClass sc in schema!.ServerClasses)
{
    if (wanted != "ALL" && sc.ClassName != wanted)
    {
        continue;
    }

    IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, sc);
    for (int i = 0; i < flat.Count; i++)
    {
        Console.WriteLine($"{sc.Id}\t{sc.ClassName}\t{i}\t{flat[i].OwnerTable}.{flat[i].Property.Name}");
    }
}
