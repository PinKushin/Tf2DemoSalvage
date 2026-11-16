using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which of a demo's classes the client gives no IK — its NextBots, by their table's base chain (B739).</summary>
/// <remarks>
/// **Through the production rule**, <c>SchemaClasses.UsesNoIk</c>, on the demo's own send tables. **The control** is the
/// count of classes whose base chain reaches <c>DT_BaseAnimating</c>: a walk that follows the wrong property finds none,
/// and "no NextBot" would then mean nothing.
/// <code>
///   no-ik &lt;demo&gt;
/// </code>
/// </remarks>
public sealed class NoIkClassesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "no-ik";

    /// <inheritdoc/>
    public string Summary => "a demo's classes the client gives no IK (NextBots), with a base-chain control: no-ik <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Give a demo: no-ik <demo>");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        DemoCommand tables = DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .FirstOrDefault(command => command.Type == DemoCommandType.DataTables);

        if (tables.Type != DemoCommandType.DataTables)
        {
            output.WriteLine("The demo carries no dem_datatables.");
            return;
        }

        DemoSchema schema = SendTableParser.Parse(tables.Payload.Span, protocol);
        int animating = schema.ServerClasses.Count(serverClass =>
            SchemaClasses.BaseChainReaches(schema, serverClass.TableName, "DT_BaseAnimating"));

        output.WriteLine($"{Path.GetFileName(path)}: {schema.ServerClasses.Count} classes, {animating} animating (control)");

        foreach (ServerClass serverClass in schema.ServerClasses.Where(serverClass =>
            SchemaClasses.UsesNoIk(schema, serverClass.TableName)))
        {
            output.WriteLine($"  no IK: {serverClass.ClassName} ({serverClass.TableName})");
        }
    }
}
