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
    /// <param name="Sounds">Sound events, in stream order.</param>
    /// <param name="Effects">
    /// Temp entities — explosions, tracers, impacts — with the class name resolved.
    /// </param>
    /// <param name="UserMessages">
    /// User messages whose body decoded, in stream order. Types with no known layout are not
    /// collected — a type name and a bit count is not something a consumer can compute over.
    /// </param>
    /// <param name="Kills">
    /// Every <c>player_death</c>, in order and NOT subject to the event sample cap. A kill feed is
    /// a sequence, and the first entry of a sequence is not a summary of it — the modern corpus
    /// demo fires 407 of these and the cap showed one.
    /// </param>
    /// <param name="Everyone">
    /// Every player seen, keyed by user id, including those whose entity slot was later taken over
    /// by someone else. <see cref="Players"/> answers "who was here"; this answers "who played",
    /// and a game event can reference either.
    /// </param>
    internal sealed record Result(
        SortedDictionary<int, PlayerInfo> Players,
        Dictionary<string, int> EventCounts,
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> EventSample,
        int EventTotal,
        List<(int Tick, ChatMessage Chat)> Chat,
        List<EntityEvent> EntityEvents,
        List<(int Tick, UserMessage Message)> UserMessages,
        List<(int Tick, DecodedSound Sound)> Sounds,
        List<(int Tick, string ClassName, DecodedTempEntity Effect)> Effects,
        List<(int Tick, IReadOnlyList<KeyValuePair<string, object?>> Fields)> Kills,
        Dictionary<int, PlayerInfo> Everyone);

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

        // Keyed by user id and never overwritten by a later occupant of the same slot. The Players
        // section still reports `players` — who was in the match at the end — while anything naming
        // a player from a game event uses this, because an event can reference someone whose slot
        // was taken over long before the demo finished.
        Dictionary<int, PlayerInfo> everyone = [];
        Dictionary<string, int> counts = [];
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> sample = [];
        List<(int Tick, IReadOnlyList<KeyValuePair<string, object?>> Fields)> kills = [];
        List<(int Tick, ChatMessage Chat)> chat = [];
        List<EntityEvent> entityEvents = [];
        List<(int Tick, UserMessage Message)> userMessages = [];
        List<(int Tick, DecodedSound Sound)> sounds = [];
        List<(int Tick, string ClassName, DecodedTempEntity Effect)> effects = [];

        // The EffectDispatch precache, so a dispatch can be named rather than numbered (B305).
        EffectNames effectNames = new();
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
                Roster.Observe(message, state, players, everyone);

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

                        // **Deaths are kept in full, past the sample cap**, because the kill feed is
                        // a sequence and a sample of one is not a sequence. The modern corpus demo
                        // fires player_death 407 times and the cap printed the first.
                        //
                        // Uncapped deliberately: this is bounded by how much killing happened, which
                        // is the quantity a reader is asking about. A 30-minute match is a few
                        // thousand entries of a handful of fields each.
                        if (string.Equals(name, "player_death", StringComparison.Ordinal))
                        {
                            kills.Add((command.Tick, [.. gameEvent.Values]));
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

                    // Both need something the message alone does not carry - sounds need the
                    // protocol, effects need the schema - which is why they are decoded here
                    // rather than in the reader.
                    case SoundsMessage sound when includeEntityEvents && sound.BodyBits > 0:
                        RecordSounds(sound, command.Tick, networkProtocol, sounds);
                        break;

                    case TempEntitiesMessage temp when decoder is not null && temp.BodyBits > 0:
                        RecordEffects(decoder, temp, command.Tick, classNames, effectNames, effects);
                        break;

                    // **The EffectDispatch table, which is what turns `m_iEffectName 3` into a
                    // name** (B305). `CTEEffectDispatch` is a dispatcher: everything else in its
                    // record is one effect's argument list, and without this the record says where
                    // something happened and not what. Measured in `z1800`: 1,697 dispatches
                    // across seven distinct indices.
                    case CreateStringTableMessage create:
                        effectNames.Add(create);
                        break;

                    case UpdateStringTableMessage tableUpdate:
                        effectNames.Add(tableUpdate, state.StringTableName(tableUpdate.TableId));
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
            players, counts, sample, total, chat, entityEvents, userMessages, sounds, effects,
            kills, everyone);
    }

    /// <summary>Decodes a sounds body, skipping one that will not read.</summary>
    private static void RecordSounds(
        SoundsMessage message,
        int tick,
        ushort networkProtocol,
        List<(int Tick, DecodedSound Sound)> into)
    {
        try
        {
            foreach (DecodedSound sound in SoundDecoder.Decode(
                message.Body.Span, message.Count, message.BodyBits, networkProtocol))
            {
                into.Add((tick, sound));
            }
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            // The rest of the dump is independent of this body, and a demo that fails partway is
            // exactly what this project exists to salvage.
        }
    }

    /// <summary>Decodes a temp entities body, skipping one that will not read.</summary>
    private static void RecordEffects(
        EntityDecoder decoder,
        TempEntitiesMessage message,
        int tick,
        Dictionary<int, string> classNames,
        EffectNames effectNames,
        List<(int Tick, string ClassName, DecodedTempEntity Effect)> into)
    {
        try
        {
            foreach (DecodedTempEntity effect in decoder.DecodeTempEntities(
                message.Body.Span, message.Count, message.BodyBits))
            {
                into.Add((
                    tick,
                    Named(effect, classNames, effectNames),
                    effect));
            }
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            // Skipped for the same reason as a sounds body: everything else in the dump is
            // independent of this one, and salvaging what is readable is the point of the project.
        }
    }

    /// <summary>The class of one temp entity, with the dispatched effect's name when it has one.</summary>
    /// <remarks>
    /// **Only <c>CTEEffectDispatch</c> carries an effect name**, because only it is a dispatcher —
    /// every other temp entity IS its effect and its class name says so. Appending the name is what
    /// makes the difference visible in a dump: the class alone says a dispatch happened, the class
    /// with a name says which.
    ///
    /// **The index stays in the properties either way.** This adds a reading rather than replacing
    /// one, so a record whose table never arrived still shows the number and a reader can tell an
    /// unnamed index from an absent one (B305).
    /// </remarks>
    private static string Named(
        DecodedTempEntity effect,
        Dictionary<int, string> classNames,
        EffectNames effectNames)
    {
        if (!classNames.TryGetValue(effect.ClassId, out string? className))
        {
            return string.Empty;
        }

        for (int at = 0; at < effect.Properties.Count; at++)
        {
            DecodedProperty property = effect.Properties[at];

            if (!string.Equals(
                property.Definition.Property.Name, "m_iEffectName", StringComparison.Ordinal))
            {
                continue;
            }

            return effectNames.Name((int)property.Value.AsInt) is { } named
                ? $"{className}({named})"
                : className;
        }

        return className;
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
            // **A shorter dump is not a smaller demo.** Skipping a snapshot silently means every
            // entity it carried is absent from the output with nothing to say they were ever
            // there, and a dump that stops describing a match halfway through looks like a quiet
            // match rather than a failed decode.
            Diagnostics.DecodeLog.Lost(
                "entities", $"decoding a snapshot at tick {tick}", error);

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
