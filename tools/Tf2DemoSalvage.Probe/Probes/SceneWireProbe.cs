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
/// Whether a demo carries the <c>"Scenes"</c> table and any scene entity at all (B351).
/// </summary>
/// <remarks>
/// **This decides whether B351 can be finished from the wire.** `SceneImage` can now turn a scene
/// filename into a sequence name for all 730 taunt paths the schema declares, but nothing has yet
/// established that a recording says WHICH scene played. Two things have to be present:
///
/// - the <c>"Scenes"</c> network string table, which is where `m_nSceneStringIndex` points
///   (<c>gameinterface.cpp:1448</c>), and
/// - a <c>CSceneEntity</c> in the schema, carrying `m_nSceneStringIndex` and `m_bIsPlayingBack`.
///
/// **If either is absent the route is dead** and a taunt has to be recovered from
/// `TF_COND_TAUNTING` and the item instead — a different, worse problem. So this probe asks before
/// any of it gets built.
///
/// <code>
///   scene-wire tf2-2026-pub-pov-clean
/// </code>
///
/// **Every table is listed, not just the one wanted.** An absent `"Scenes"` reads identically to a
/// table this probe failed to route, and the list of names it DID see is the control
/// (`docs/memory/an-empty-search-needs-a-control.md`).
/// </remarks>
public sealed class SceneWireProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "scene-wire";

    /// <inheritdoc/>
    public string Summary =>
        "whether a demo carries the Scenes table and a scene entity: scene-wire <demo>";

    /// <summary>The table `m_nSceneStringIndex` indexes.</summary>
    private const string SceneTable = "Scenes";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("scene-wire <demo>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        DemoSchema? schema = commands
            .Where(command => command.Type == DemoCommandType.DataTables)
            .Select(command => SendTableParser.Parse(command.Payload.Span, protocol))
            .FirstOrDefault();

        if (schema is null)
        {
            output.WriteLine("The demo carries no dem_datatables.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} protocol {protocol}, {schema.ServerClasses.Count} classes"));

        // **The schema half: is there a class that can carry a scene at all?** A table name is
        // matched loosely because the class has been renamed across eras and this must not report a
        // confident absence on a spelling.
        foreach (ServerClass entry in schema.ServerClasses.Where(
            entry => entry.ClassName.Contains("Scene", StringComparison.OrdinalIgnoreCase) ||
                     entry.TableName.Contains("Scene", StringComparison.OrdinalIgnoreCase)))
        {
            output.WriteLine(
                $"  class {entry.Id.ToString(CultureInfo.InvariantCulture)} " +
                $"'{entry.ClassName}' table '{entry.TableName}'");
        }

        // **And the properties, because a class existing is not a field arriving.** A decoded field
        // with no consumer is this project's most repeated finding, and a declared field nobody
        // sends is its mirror.
        foreach (ServerClass entry in schema.ServerClasses.Where(
            entry => entry.TableName.Contains("Scene", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (FlatProperty property in SchemaFlattener.Flatten(schema, entry))
            {
                output.WriteLine($"    {property.OwnerTable}.{property.Property.Name}");
            }
        }

        // **The table half.** Every table's name and entry count, with the wanted one called out.
        NetDecodeState state = new() { NetworkProtocol = protocol };

        Dictionary<string, int> tables = new(StringComparer.Ordinal);
        List<string> scenes = [];

        foreach (DemoCommand command in commands.Where(
            command => command.Type is DemoCommandType.Packet or DemoCommandType.Signon))
        {
            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case CreateStringTableMessage create:
                        tables[create.Name] =
                            tables.TryGetValue(create.Name, out int had) ? had + create.Entries.Count
                                                                        : create.Entries.Count;

                        if (create.Name == SceneTable)
                        {
                            scenes.AddRange(create.Entries
                                .Select(entry => entry.Text)
                                .OfType<string>());
                        }

                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) is { } name:
                        tables[name] =
                            tables.TryGetValue(name, out int seen) ? seen + update.Entries.Count
                                                                  : update.Entries.Count;

                        if (name == SceneTable)
                        {
                            scenes.AddRange(update.Entries
                                .Select(entry => entry.Text)
                                .OfType<string>());
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  {tables.Count} string tables:"));

        foreach ((string name, int count) in tables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {(name == SceneTable ? "->" : "  ")} '{name}': {count:N0} entries"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  '{SceneTable}': {(tables.ContainsKey(SceneTable) ? "PRESENT" : "ABSENT")}, " +
            $"{scenes.Count:N0} names"));

        foreach (string scene in scenes.Where(
            name => name.Contains("taunt", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            output.WriteLine($"    {scene}");
        }
    }
}
