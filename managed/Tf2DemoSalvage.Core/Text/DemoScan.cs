using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Walks a demo's packet stream once, collecting everything the output writers need.
/// </summary>
/// <remarks>
/// Shared rather than duplicated per writer. Decoding is the expensive part of reading a demo —
/// a hundred thousand packets, each a bit-level walk — and the text dump and the JSON Lines
/// writer want different slices of the same messages. Scanning once per writer would make the
/// cost scale with the number of output formats, which is the wrong thing to scale with.
/// </remarks>
internal static class DemoScan
{
    /// <summary>The string table naming connected players.</summary>
    private const string UserInfoTable = "userinfo";

    /// <summary>Label reported alongside scan progress.</summary>
    private const string ScanStage = "Scanning packets";

    /// <summary>
    /// Commands between progress reports. Reporting every command would cost more in callbacks
    /// than the scan itself on a 120,000-frame demo.
    /// </summary>
    private const int ProgressInterval = 512;

    /// <summary>What one walk of the packet stream produced.</summary>
    /// <param name="Players">Players named by the <c>userinfo</c> table, keyed by entity index.</param>
    /// <param name="EventCounts">How many of each game event fired.</param>
    /// <param name="EventSample">The first events in order, capped by the caller.</param>
    /// <param name="EventTotal">Total events decoded, including those past the sample cap.</param>
    /// <param name="Chat">Chat lines, with the tick each was sent on.</param>
    internal sealed record Result(
        SortedDictionary<int, PlayerInfo> Players,
        Dictionary<string, int> EventCounts,
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> EventSample,
        int EventTotal,
        List<(int Tick, ChatMessage Chat)> Chat);

    /// <summary>
    /// Walks the packet stream once, collecting everything the report sections need.
    /// </summary>
    /// <remarks>
    /// One pass rather than one per section. Decoding is the expensive part of a dump — a
    /// hundred thousand packets, each a bit-level walk — and the sections all want different
    /// slices of the same messages. Scanning per section doubled the cost the moment a second
    /// section existed.
    /// </remarks>
    internal static Result Run(
        IReadOnlyList<DemoCommand> commands,
        int sampleSize,
        IProgress<DumpProgress>? progress,
        ushort networkProtocol)
    {
        // From the demo header, not from svc_ServerInfo: the protocol sizes the message type
        // field, so ServerInfo cannot be read without it. See NetDecodeState.NetworkProtocol.
        NetDecodeState state = new() { NetworkProtocol = networkProtocol };
        SortedDictionary<int, PlayerInfo> players = [];
        Dictionary<string, int> counts = [];
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> sample = [];
        List<(int Tick, ChatMessage Chat)> chat = [];
        int total = 0;
        int scanned = 0;

        foreach (DemoCommand command in commands)
        {
            // Reported per command rather than per packet, so the fraction reaches 1 even on a
            // demo that is mostly console commands. Every iteration counts, including skipped
            // ones, or the bar would stall on a stretch this scan ignores.
            scanned++;
            if (progress is not null &&
                (scanned % ProgressInterval == 0 || scanned == commands.Count))
            {
                progress.Report(new DumpProgress(ScanStage, scanned, commands.Count));
            }

            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case GameEventMessage gameEvent:
                    {
                        string name = gameEvent.Name ?? string.Create(
                            CultureInfo.InvariantCulture, $"#{gameEvent.EventId}");
                        counts[name] = counts.TryGetValue(name, out int seen) ? seen + 1 : 1;
                        total++;

                        if (sample.Count < sampleSize)
                        {
                            // Fields are kept raw and resolved when the section is written. A
                            // player can be named by a userinfo update *after* an event
                            // referencing them, so resolving here would miss them.
                            sample.Add((command.Tick, name, [.. gameEvent.Values]));
                        }

                        break;
                    }

                    case CreateStringTableMessage table when table.Name == UserInfoTable:
                        RosterBuilder.Apply(table.Entries, players);
                        break;

                    // Mid-game joins arrive here, not in the create message (RISKS B22). An
                    // update names its table only by creation-order id, which is why
                    // NetDecodeState now remembers table names.
                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == UserInfoTable:
                        RosterBuilder.Apply(update.Entries, players);
                        break;

                    case ChatMessage line:
                        chat.Add((command.Tick, line));
                        break;

                    default:
                        break;
                }
            }
        }

        return new Result(players, counts, sample, total, chat);
    }

}
