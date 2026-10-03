using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary><c>DemoStringTables.Read</c> — the <c>dem_stringtables</c> block (B452).</summary>
public sealed class DemoStringTablesTests
{
    [Test]
    public void Read_TwoTablesWithUserDataAndClientEntries_ReturnsEachServerEntry()
    {
        IReadOnlyList<DemoStringTable> tables = DemoStringTables.Read(Block());

        tables.Count.ShouldBe(2);

        tables[0].Name.ShouldBe("downloadables");
        tables[0].Entries.Count.ShouldBe(1);
        tables[0].Entries[0].Text.ShouldBe("a");
        tables[0].Entries[0].UserData.ShouldBeEmpty();

        // The second table follows the first table's client-side entries, so its values prove those
        // were read past at the right widths.
        tables[1].Name.ShouldBe("instancebaseline");
        tables[1].Entries.Count.ShouldBe(2);
        tables[1].Entries[0].Text.ShouldBe("247");
        tables[1].Entries[0].UserData.ShouldBe([0xAB, 0xCD, 0xEF]);
        tables[1].Entries[1].Index.ShouldBe(1);
        tables[1].Entries[1].Text.ShouldBe("48");
        tables[1].Entries[1].UserData.ShouldBe([0x12]);
    }

    /// <summary>A block written at the layout the reader documents.</summary>
    internal static byte[] Block(params (string Name, (string Text, byte[]? Data)[] Entries)[] tables)
    {
        BitWriter writer = new();
        writer.Write((uint)tables.Length, 8);

        foreach ((string name, (string Text, byte[]? Data)[] entries) in tables)
        {
            writer.WriteString(name);
            Entries(writer, entries);
            writer.WriteBit(false);
        }

        return writer.Build();
    }

    private static byte[] Block()
    {
        BitWriter writer = new();
        writer.Write(2, 8);

        writer.WriteString("downloadables");
        Entries(writer, [("a", null)]);
        writer.WriteBit(true);
        Entries(writer, [("client", [0x77, 0x88])]);

        writer.WriteString("instancebaseline");
        Entries(writer, [("247", [0xAB, 0xCD, 0xEF]), ("48", [0x12])]);
        writer.WriteBit(false);

        return writer.Build();
    }

    private static void Entries(BitWriter writer, (string Text, byte[]? Data)[] entries)
    {
        writer.Write((uint)entries.Length, 16);

        foreach ((string text, byte[]? data) in entries)
        {
            writer.WriteString(text);
            writer.WriteBit(data is not null);

            if (data is not null)
            {
                writer.Write((uint)data.Length, 16);
                writer.WriteBytes(data);
            }
        }
    }
}
