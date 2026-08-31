using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a demo's own entity schema declares, filtered by name.
/// </summary>
/// <remarks>
/// **The premise of this project, asked directly.** A demo carries its own `SendTables`, so the
/// answer to "does this recording contain X" is in the file rather than in anyone's memory of which
/// build added it.
///
/// <code>
///   schema tf2-2026-pub-pov-clean Rules
///   schema tf2-2026-pub-pov-clean m_iRoundState
/// </code>
///
/// Server classes and tables are reported separately from properties, because they answer different
/// questions: a class says an entity of that kind can exist, and a property says what it would tell
/// us.
/// </remarks>
public sealed class SchemaProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "schema";

    /// <inheritdoc/>
    public string Summary => "what a demo's schema declares: schema <demo> [substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("schema <demo> [substring]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        DemoSchema? schema = null;

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                schema = SendTableParser.Parse(command.Payload.Span, protocol);
                break;
            }
        }

        if (schema is null)
        {
            output.WriteLine($"{Path.GetFileName(path)}: no dem_datatables");
            return;
        }

        output.WriteLine(
            $"{Path.GetFileName(path)} protocol {protocol.ToString(CultureInfo.InvariantCulture)}, "
            + $"{schema.Tables.Count.ToString(CultureInfo.InvariantCulture)} tables, "
            + $"{schema.ServerClasses.Count.ToString(CultureInfo.InvariantCulture)} classes, "
            + $"filter '{filter}'");

        foreach (ServerClass entry in schema.ServerClasses
            .Where(entry => Matches(entry.ClassName, filter) || Matches(entry.TableName, filter))
            .OrderBy(entry => entry.ClassName, StringComparer.Ordinal))
        {
            output.WriteLine($"CLASS  {entry.ClassName}  ->  {entry.TableName}");
        }

        foreach (SendTable table in schema.Tables.Where(table => Matches(table.Name, filter)))
        {
            output.WriteLine(
                $"TABLE  {table.Name}  "
                + $"[{string.Join(", ", table.Properties.Select(property => property.Name))}]");
        }

        foreach (string owner in schema.Tables
            .Where(table => !Matches(table.Name, filter))
            .SelectMany(table => table.Properties
                .Where(property => Matches(property.Name, filter))
                .Select(property => $"PROP   {table.Name}.{property.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            output.WriteLine(owner);
        }
    }

    private static bool Matches(string name, string filter) =>
        filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
