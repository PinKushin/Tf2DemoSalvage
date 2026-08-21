using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Does a demo say WHICH item a weapon entity is?</summary>
public sealed class ItemDefinitionProbe
{
    [Test]
    [Explicit("diagnostic")]
    public void ItemDefinition_InTheSchema_IsReported()
    {
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);

            List<string> found =
            [
                .. schema.Tables
                    .SelectMany(table => table.Properties.Select(p => $"{table.Name}.{p.Name}"))
                    .Where(name => name.Contains("ItemDefinition", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("m_iItemID", StringComparison.OrdinalIgnoreCase))
                    .Distinct(),
            ];

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: {string.Join(", ", found.Take(6))}");
        }

        Corpus.FilesWithSchema().Count.ShouldBeGreaterThan(0);
    }
}
