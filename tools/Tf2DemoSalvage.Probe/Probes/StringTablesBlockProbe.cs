using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a <c>dem_stringtables</c> block carries that the messages before it never did (B452).
/// </summary>
/// <remarks>
/// Per table: the block's entry count, and the entries whose index-and-text no earlier create or
/// update stated — the ones only the block can deliver. Walks with <c>NetMessageReader</c> and
/// <c>DemoStringTables.Read</c>, the production readers. The control is the count of entries the
/// earlier messages DID state: a table reading "0 only in block" beside a non-zero prior count is a
/// real agreement, not a blind reader.
/// </remarks>
public sealed class StringTablesBlockProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "stringtables-block";

    /// <inheritdoc/>
    public string Summary =>
        "entries a dem_stringtables block carries that no earlier message stated: stringtables-block <demo> [examples]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("stringtables-block <demo> [examples]");
            return;
        }

        int examples = arguments.Count > 1 ? int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture) : 3;
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        NetDecodeState state = new() { NetworkProtocol = protocol };
        Dictionary<string, HashSet<(int, string?)>> stated = new(StringComparer.Ordinal);

        void Note(string? table, IReadOnlyList<StringTableEntry> entries)
        {
            if (table is null)
            {
                return;
            }

            if (!stated.TryGetValue(table, out HashSet<(int, string?)>? set))
            {
                stated[table] = set = [];
            }

            foreach (StringTableEntry entry in entries)
            {
                set.Add((entry.Index, entry.Text));
            }
        }

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.StringTables)
            {
                output.WriteLine($"{Path.GetFileName(path)}: block at tick {command.Tick}");

                foreach (DemoStringTable table in DemoStringTables.Read(command.Payload.Span))
                {
                    HashSet<(int, string?)> prior = stated.GetValueOrDefault(table.Name) ?? [];
                    List<StringTableEntry> only = [.. table.Entries.Where(e => !prior.Contains((e.Index, e.Text)))];

                    output.WriteLine(
                        $"  {table.Name,-24} block {table.Entries.Count,5}  stated before {prior.Count,5}  only in block {only.Count,5}"
                        + string.Concat(only.Take(examples).Select(e => $"  [{e.Index}] {e.Text}")));
                }

                return;
            }

            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is CreateStringTableMessage create)
                {
                    Note(create.Name, create.Entries);
                }
                else if (message is UpdateStringTableMessage update)
                {
                    Note(state.StringTableName(update.TableId), update.Entries);
                }
            }
        }

        output.WriteLine($"{Path.GetFileName(path)}: no dem_stringtables block");
    }
}
