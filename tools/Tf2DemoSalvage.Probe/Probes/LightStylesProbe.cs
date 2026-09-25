using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a demo's <c>lightstyles</c> table holds, and every change to it, by tick.</summary>
/// <remarks>
/// **The engine animates each light style from this table**: an entry's text or user data is a pattern of letters, each
/// a brightness from <c>'a'</c> (dark) to <c>'z'</c>, stepped ten times a second. A face lit by a switchable or
/// flickering light adds that style's lightmap times the current letter's value. Before building that, this reports what
/// a real recording carries: which entries are set at signon, and whether any change during play.
/// </remarks>
public sealed class LightStylesProbe : IProbe
{
    private const string Table = "lightstyles";

    /// <inheritdoc/>
    public string Name => "lightstyles";

    /// <inheritdoc/>
    public string Summary => "a demo's lightstyles table, at signon and each change: lightstyles <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("usage: lightstyles <demo>");
            return;
        }

        byte[] bytes = File.ReadAllBytes(arguments[0]);
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Packet or DemoCommandType.Signon))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                IReadOnlyList<StringTableEntry>? entries = message switch
                {
                    CreateStringTableMessage { Name: Table } create => create.Entries,
                    UpdateStringTableMessage update when state.StringTableName(update.TableId) == Table => update.Entries,
                    _ => null,
                };

                foreach (StringTableEntry entry in entries ?? [])
                {
                    string data = Encoding.ASCII.GetString([.. entry.UserData]).TrimEnd('\0');

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"tick {command.Tick,7}  style {entry.Index,2}  text '{entry.Text}'  data '{data}'"));
                }
            }
        }
    }
}
