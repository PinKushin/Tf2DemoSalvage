using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

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
    /// <param name="EntityEvents">
    /// Entity lifecycle in stream order — entering, leaving and being deleted. Empty unless the
    /// caller asked for it.
    /// </param>
    /// <param name="UserMessages">
    /// User messages whose body decoded, in stream order. Types with no known layout are not
    /// collected — a type name and a bit count is not something a consumer can compute over.
    /// </param>
    internal sealed record Result(
        SortedDictionary<int, PlayerInfo> Players,
        Dictionary<string, int> EventCounts,
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> EventSample,
        int EventTotal,
        List<(int Tick, ChatMessage Chat)> Chat,
        List<EntityEvent> EntityEvents,
        List<(int Tick, UserMessage Message)> UserMessages);

    /// <summary>One entity entering, leaving or being deleted.</summary>
    /// <param name="Tick">The command tick it happened on.</param>
    /// <param name="EntityIndex">The entity.</param>
    /// <param name="Update">Which of the three it was.</param>
    /// <param name="ClassId">The entity's networked class.</param>
    /// <param name="ClassName">That class's name, or empty if the entity was never seen entering.</param>
    /// <param name="SerialNumber">Distinguishes reuses of an index; meaningful only on entry.</param>
    /// <remarks>
    /// Lifecycle only, deliberately. A long demo holds thousands of these and millions of
    /// property changes, and "when did this entity exist" is a different question from "what were
    /// its values" — answering the first should not require reading the second.
    /// </remarks>
    internal sealed record EntityEvent(
        int Tick,
        int EntityIndex,
        EntityUpdateType Update,
        int ClassId,
        string ClassName,
        int SerialNumber);

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
        ushort networkProtocol,
        bool includeEntityEvents = false)
    {
        // From the demo header, not from svc_ServerInfo: the protocol sizes the message type
        // field, so ServerInfo cannot be read without it. See NetDecodeState.NetworkProtocol.
        NetDecodeState state = new() { NetworkProtocol = networkProtocol };
        SortedDictionary<int, PlayerInfo> players = [];
        Dictionary<string, int> counts = [];
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> sample = [];
        List<(int Tick, ChatMessage Chat)> chat = [];
        List<EntityEvent> entityEvents = [];
        List<(int Tick, UserMessage Message)> userMessages = [];
        int total = 0;
        int scanned = 0;

        // Built from dem_datatables when it arrives, which is before any packet carrying
        // entities. Null until then, and null for the whole scan when entity events were not
        // asked for - decoding every snapshot is the expensive half of a walk.
        EntityDecoder? decoder = null;
        Dictionary<int, string> classNames = [];

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

            if (includeEntityEvents && command.Type == DemoCommandType.DataTables)
            {
                DemoSchema schema = SendTableParser.Parse(command.Payload.Span, networkProtocol);
                decoder = new EntityDecoder(
                    schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
                classNames = schema.ServerClasses.ToDictionary(c => c.Id, c => c.ClassName);
                continue;
            }

            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                // Roster first, and shared with the trace writer so the two outputs cannot
                // disagree about who a user id belongs to. It handles both the create message and
                // the update that carries mid-match joiners (RISKS B22).
                Roster.Observe(message, state, players);

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

                    case ChatMessage line:
                        chat.Add((command.Tick, line));
                        break;

                    // Only the ones whose body decoded. A record naming a type and a bit count
                    // carries nothing a consumer can compute over, and CheapBreakModel alone is
                    // 259 of the corpus's 756 user messages - listing every one would bury the
                    // handful that say something. The trace remains the complete view.
                    case UserMessage user when user.Fields is { Count: > 0 }:
                        userMessages.Add((command.Tick, user));
                        break;

                    case PacketEntitiesMessage snapshot when decoder is not null:
                        RecordLifecycle(
                            decoder, snapshot, command.Tick, classNames, entityEvents);
                        break;

                    default:
                        break;
                }
            }
        }

        return new Result(
            players, counts, sample, total, chat, entityEvents, userMessages);
    }

    /// <summary>
    /// Decodes one snapshot and records the entities entering, leaving or being deleted.
    /// </summary>
    /// <remarks>
    /// A snapshot that fails to decode is skipped rather than aborting the scan. The rest of a
    /// dump — players, chat, game events — is independent of the entity stream and is worth more
    /// than nothing, and a demo that desynchronises partway is exactly the case this project
    /// exists to salvage.
    /// </remarks>
    private static void RecordLifecycle(
        EntityDecoder decoder,
        PacketEntitiesMessage snapshot,
        int tick,
        Dictionary<int, string> classNames,
        List<EntityEvent> into)
    {
        IReadOnlyList<DecodedEntity> entities;
        try
        {
            entities = decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            return;
        }

        foreach (DecodedEntity entity in entities)
        {
            if (entity.UpdateType == EntityUpdateType.Delta)
            {
                continue;
            }

            into.Add(new EntityEvent(
                tick,
                entity.EntityIndex,
                entity.UpdateType,
                entity.ClassId,
                classNames.TryGetValue(entity.ClassId, out string? name) ? name : string.Empty,
                entity.SerialNumber));
        }
    }

}
