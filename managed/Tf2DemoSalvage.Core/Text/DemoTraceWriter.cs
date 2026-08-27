using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Decompiles a demo to text, message by message, in stream order.
/// </summary>
/// <remarks>
/// Modelled on <c>lmpc</c>, the Quake tool that decompiles a <c>.dem</c> to a text source and
/// compiles it back. Its format is block-structured: a block per demo frame, holding that
/// frame's messages, each written as a keyword followed by its fields and a semicolon.
///
/// **A trace, not a summary, and the difference is the point.** A summary says what a demo
/// contains; a trace says what it *is*, in order. Aggregates hide position, and position is
/// exactly what matters when a demo is damaged — "3,412 game events" tells you nothing about
/// where the stream stopped making sense, whereas a block that ends in <c>stopped</c> tells you
/// precisely.
///
/// So anything the reader could not finish is reported **in place** rather than omitted, and
/// commands with no messages still get a block. A trace that silently skipped what it could not
/// read would describe a different, healthier file than the one on disk.
/// </remarks>
public static class DemoTraceWriter
{
    /// <summary>Writes the trace.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="fileName">Name to report for the demo.</param>
    /// <param name="header">The demo's parsed header.</param>
    /// <param name="commands">The demo's commands, in stream order.</param>
    /// <param name="progress">Optional listener for progress.</param>
    /// <param name="options">How much detail to write, or <c>null</c> for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/>, <paramref name="header"/>, or <paramref name="commands"/> is
    /// <c>null</c>.
    /// </exception>
    public static void Write(
        TextWriter writer,
        string fileName,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands,
        IProgress<DumpProgress>? progress = null,
        DemoTraceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        options ??= new DemoTraceOptions();
        writer.NewLine = "\n";

        WriteHeader(writer, fileName, header);

        // Seeded from the header, because the protocol sizes the message type field and
        // svc_ServerInfo cannot be read without it. See NetDecodeState.NetworkProtocol.
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };
        EntityDecoder? entities = options.IncludeEntities
            ? BuildDecoder(commands, (ushort)header.NetworkProtocol)
            : null;
        int snapshots = 0;
        int scanned = 0;

        // Accumulated as the walk proceeds, so a game event names the players the stream had
        // introduced by that point rather than everyone who ever appears.
        Dictionary<int, PlayerInfo> roster = [];

        // Sound indices resolve against the soundprecache table, which arrives in the signon
        // stream. Built as the walk proceeds, for the same reason the roster is: a trace is a
        // stream-order account, and a name that had not arrived yet is not a name.
        SoundNames soundNames = new();

        foreach (DemoCommand command in commands)
        {
            scanned++;
            if (progress is not null && (scanned % ProgressInterval == 0 || scanned == commands.Count))
            {
                progress.Report(new DumpProgress("Tracing", scanned, commands.Count));
            }

            WriteBlock(
                writer, command, state, options, entities, roster, soundNames, ref snapshots);
        }
    }

    /// <summary>Commands between progress reports.</summary>
    private const int ProgressInterval = 512;

    /// <summary>How a game event field the server withheld is rendered.</summary>
    private const string LocalFieldValue = "local";

    private static void WriteHeader(TextWriter writer, string fileName, DemoHeader header)
    {
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"// {fileName}"));
        writer.WriteLine("header {");
        WriteField(writer, "demo_protocol", header.DemoProtocol.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "network_protocol", header.NetworkProtocol.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "server", Quote(header.ServerName));
        WriteField(writer, "client", Quote(header.ClientName));
        WriteField(writer, "map", Quote(header.MapName));
        WriteField(writer, "game", Quote(header.GameDirectory));
        WriteField(writer, "playback_time", string.Create(
            CultureInfo.InvariantCulture, $"{header.PlaybackTimeSeconds:F6}"));
        WriteField(writer, "playback_ticks", header.PlaybackTicks.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "playback_frames", header.PlaybackFrames.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Builds an entity decoder from the demo's own schema, if it carries one.
    /// </summary>
    /// <remarks>
    /// A separate pass, because <c>dem_datatables</c> can appear after packets that reference
    /// it, and an entity snapshot cannot be read without the schema those tables describe. This
    /// is the project's premise in miniature: the file explains its own entity layout, so the
    /// decoder is built from the demo rather than from a compiled-in definition.
    /// </remarks>
    private static EntityDecoder? BuildDecoder(
        IReadOnlyList<DemoCommand> commands,
        ushort networkProtocol)
    {
        foreach (DemoCommand command in commands)
        {
            if (command.Type != DemoCommandType.DataTables)
            {
                continue;
            }

            DemoSchema schema = SendTableParser.Parse(command.Payload.Span, networkProtocol);
            return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        }

        return null;
    }

    private static void WriteBlock(
        TextWriter writer,
        DemoCommand command,
        NetDecodeState state,
        DemoTraceOptions options,
        EntityDecoder? entities,
        Dictionary<int, PlayerInfo> roster,
        SoundNames soundNames,
        ref int snapshots)
    {
        string kind = WireName(command.Type);

        if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
        {
            // The two container commands that carry a decodable payload of their own. Everything
            // else in this branch - dem_synctick, dem_stop, dem_datatables, dem_stringtables - is
            // either empty or handled by its own pass, so it stays a bare one-line block.
            if (command.Type == DemoCommandType.UserCmd && !command.Payload.IsEmpty)
            {
                WriteUserCommand(writer, kind, command);
                return;
            }

            if (command.Type == DemoCommandType.ConsoleCmd && !command.Payload.IsEmpty)
            {
                WriteConsoleCommand(writer, kind, command);
                return;
            }

            // Still a block. A trace that omitted dem_synctick or dem_stop would not describe
            // the file it read.
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"block {kind} tick {command.Tick};"));
            return;
        }

        NetMessageReadResult result = NetMessageReader.Read(command.Payload.Span, state);

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"block {kind} tick {command.Tick} {{"));

        // The camera this command was recorded from. Skipped by the reader until now, and the
        // only record in the file of where the recording client was looking.
        if (command.View is { } view)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    view origin {view.OriginX:F3} {view.OriginY:F3} {view.OriginZ:F3} " +
                $"angles {view.Pitch:F3} {view.Yaw:F3} {view.Roll:F3} flags {view.Flags};"));
        }

        foreach (INetMessage message in result.Messages)
        {
            // Captured before rendering, so a sound in the same packet as the table that names
            // it still resolves. Both message kinds are still printed by the switch below.
            if (message is CreateStringTableMessage createdTable)
            {
                soundNames.Add(createdTable);

                // **An entity ENTER is a delta against its class baseline, not against zero.**
                // Without this the trace decoded every entity from nothing, so any property the
                // map sets once at init and never resends was simply missing from the dump:
                // m_iNumControlPoints, m_vCPPositions, m_bCPIsVisible and the rest, absent from
                // 782 MB of cp_process trace while the same entity's gameplay properties appeared
                // hundreds of times.
                //
                // Applied inside the message loop rather than in a pre-pass, because the table
                // must be recorded before the snapshot that relies on it and both can share a
                // packet. DemoTimeline has always done this; only the trace did not.
                if (entities is not null && createdTable.Name == BaselineBuilder.TableName)
                {
                    BaselineBuilder.Apply(createdTable.Entries, entities);
                }
            }
            else if (message is UpdateStringTableMessage updatedTable)
            {
                soundNames.Add(updatedTable, state.StringTableName(updatedTable.TableId));

                // Updates name their table only by id, so the id is resolved through the decode
                // state exactly as the sound path above does.
                if (entities is not null &&
                    state.StringTableName(updatedTable.TableId) == BaselineBuilder.TableName)
                {
                    BaselineBuilder.Apply(updatedTable.Entries, entities);
                }
            }

            if (message is PacketEntitiesMessage snapshot && entities is not null &&
                WithinLimit(options, snapshots))
            {
                snapshots++;
                WriteSnapshot(writer, snapshot, entities, options);
                continue;
            }

            // Sounds and temp entities expand in place. Unlike an entity snapshot these are a
            // handful of lines each - a packet carries one or two sounds and a few effects, not
            // eight hundred entities - so there is no volume argument for hiding them behind a
            // flag, and they are the two things a reader most often wants position for.
            if (message is SoundsMessage sound && sound.BodyBits > 0)
            {
                WriteSounds(writer, sound, state, soundNames);
                continue;
            }

            if (message is TempEntitiesMessage effects && entities is not null &&
                effects.BodyBits > 0)
            {
                WriteTempEntities(writer, effects, entities, options);
                continue;
            }

            // The roster is built as the walk proceeds rather than pre-scanned, so an event
            // resolves against the players the stream had named BY THAT POINT. That is what a
            // stream-order decompile should say: a name that had not arrived yet is not a name
            // the reader could have had.
            Roster.Observe(message, state, roster);

            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {Render(message, roster, entities)};"));
        }

        if (result.StopReason is not null)
        {
            // In place, at the position it happened. This line is the reason a trace beats a
            // summary for a damaged demo.
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    stopped after {result.BitsConsumed} bits: {Quote(result.StopReason)};"));
        }

        writer.WriteLine("}");
    }

    private static bool WithinLimit(DemoTraceOptions options, int snapshots) =>
        options.EntitySnapshotLimit <= 0 || snapshots < options.EntitySnapshotLimit;

    /// <summary>
    /// Expands one entity snapshot into its entities and their changed properties.
    /// </summary>
    /// <remarks>
    /// Nested inside the message rather than flattened alongside it, so the structure of the
    /// snapshot survives into the text — which entity, which properties, in wire order.
    ///
    /// A snapshot that will not decode is reported in place and the block continues. Entity
    /// decoding depends on state built up from earlier snapshots, so one failure tends to
    /// invalidate those after it; saying where the first one was is the useful part.
    /// </remarks>
    /// <summary>Expands a <c>svc_Sounds</c> body into one line per sound.</summary>
    /// <remarks>
    /// A sound that fails to decode is reported in place rather than dropped, for the reason the
    /// whole trace exists: where a stream stops making sense is the information.
    /// </remarks>
    private static void WriteSounds(
        TextWriter writer, SoundsMessage message, NetDecodeState state, SoundNames soundNames)
    {
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    svc_sounds {(message.IsReliable ? "reliable" : "unreliable")} " +
            $"count {message.Count} bits {message.BodyBits} {{"));

        try
        {
            foreach (DecodedSound sound in SoundDecoder.Decode(
                message.Body.Span, message.Count, message.BodyBits, state.NetworkProtocol))
            {
                // The name where the soundprecache table resolved it, the number always. A
                // sound index means nothing outside its own demo, and the number stays so a
                // reader can still cross-reference the raw stream.
                string? name = soundNames.Resolve(sound.SoundNumber);
                string named = name is null ? string.Empty : $" {Quote(name)}";

                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"        sound {sound.SoundNumber}{named} entity {sound.EntityIndex} " +
                    $"origin {sound.OriginX:F1} {sound.OriginY:F1} {sound.OriginZ:F1} " +
                    $"volume {sound.Volume:F2} pitch {sound.Pitch} " +
                    $"channel {sound.Channel} flags {sound.Flags};"));
            }
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"        undecoded {Quote(failure.Message)};"));
        }

        writer.WriteLine("    }");
    }

    /// <summary>Expands a <c>svc_TempEntities</c> body into one line per effect.</summary>
    /// <remarks>
    /// Property values follow the same switch as an entity snapshot's, for the same reason: they
    /// are the bulk of the bits. The difference is that an effect's properties are the whole of
    /// what it says — an explosion without its origin is a report that something exploded
    /// somewhere — so a viewer wants them where an entity's per-tick deltas are noise.
    /// </remarks>
    private static void WriteTempEntities(
        TextWriter writer,
        TempEntitiesMessage message,
        EntityDecoder entities,
        DemoTraceOptions options)
    {
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    svc_tempentities count {message.Count} bits {message.BodyBits} {{"));

        try
        {
            foreach (DecodedTempEntity effect in entities.DecodeTempEntities(
                message.Body.Span, message.Count, message.BodyBits))
            {
                string reliable = effect.IsReliable ? " reliable" : string.Empty;
                string head = string.Create(
                    CultureInfo.InvariantCulture,
                    $"        effect {Named(entities, effect.ClassId)} " +
                    $"delay {effect.DelaySeconds:F2}{reliable}");

                if (!options.IncludeEntityProperties || effect.Properties.Count == 0)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{head} props {effect.Properties.Count};"));
                    continue;
                }

                writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{head} {{"));
                foreach (DecodedProperty property in effect.Properties)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"            {property.Definition.OwnerTable}.{property.Definition.Property.Name} " +
                        $"{property.Value};"));
                }

                writer.WriteLine("        }");
            }
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"        undecoded {Quote(failure.Message)};"));
        }

        writer.WriteLine("    }");
    }

    /// <summary>Renders a decoded user message body, or nothing when it was not decoded.</summary>
    /// <remarks>
    /// Appended to the existing line rather than opening a block. These bodies are two or three
    /// short values — a destination and a localisation key — and a block per message would make
    /// the trace harder to scan than the anonymous version it replaced.
    /// </remarks>
    private static string UserFields(UserMessage user)
    {
        if (user.Fields is null || user.Fields.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder line = new();
        foreach (KeyValuePair<string, object?> field in user.Fields)
        {
            line.Append(CultureInfo.InvariantCulture, $" {field.Key}=");
            line.Append(field.Value is string text
                ? Quote(text)
                : Convert.ToString(field.Value, CultureInfo.InvariantCulture));
        }

        return line.ToString();
    }

    /// <summary>Renders a class as <c>Name(id)</c>, or just the id when the schema lacks it.</summary>
    /// <remarks>
    /// Both, not either. The name is what makes a trace readable — <c>CTFPlayer</c> rather than
    /// <c>212</c> — but the id is what a reader needs to compare this output against another
    /// parser's or against a raw bit dump, which is how the flattening-order bug was found. An id
    /// with no name in the schema prints bare rather than inventing a placeholder.
    /// </remarks>
    /// <summary>The SDK's name for an entity message's type byte, as a trailing " name" or empty.</summary>
    /// <remarks>
    /// Empty rather than absent when nothing can be named, so the line keeps one shape. The name
    /// needs the class, which is why this takes the decoder rather than the byte alone - see
    /// <see cref="EntityMessageNames"/> for the collision that makes it necessary.
    /// </remarks>
    private static string Suffixed(EntityDecoder? entities, int classId, int messageType)
    {
        string? named = entities is null
            ? null
            : EntityMessageNames.Lookup(entities.ClassName(classId), messageType);
        return named is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" {named}");
    }

    private static string Named(EntityDecoder entities, int classId)
    {
        string name = entities.ClassName(classId);
        return name.Length == 0
            ? classId.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{name}({classId})");
    }

    private static void WriteSnapshot(
        TextWriter writer,
        PacketEntitiesMessage snapshot,
        EntityDecoder entities,
        DemoTraceOptions options)
    {
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    svc_packetentities delta {(snapshot.IsDelta ? 1 : 0)} " +
            $"updated {snapshot.UpdatedEntries} bits {snapshot.LengthBits} {{"));

        IReadOnlyList<DecodedEntity> decoded;
        try
        {
            decoded = entities.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"        undecoded {Quote(failure.Message)};"));
            writer.WriteLine("    }");
            return;
        }

        foreach (DecodedEntity entity in decoded)
        {
            string kind = entity.UpdateType.ToString().ToUpperInvariant();

            // **The state, not the bits.** An entering entity is a delta against its class
            // baseline, so its own property list omits everything the baseline already said. A
            // trace that printed only that list described the packet correctly and the entity
            // wrongly - which is how every map-init property went missing from the dump.
            IReadOnlyList<DecodedProperty> properties = entities.EffectiveProperties(entity);

            if (!options.IncludeEntityProperties || properties.Count == 0)
            {
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"        entity {entity.EntityIndex} {kind} class {Named(entities, entity.ClassId)} " +
                    $"props {properties.Count};"));
                continue;
            }

            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"        entity {entity.EntityIndex} {kind} class {Named(entities, entity.ClassId)} {{"));

            foreach (DecodedProperty property in properties)
            {
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"            {property.Definition.OwnerTable}.{property.Definition.Property.Name} " +
                    $"{property.Value};"));
            }

            writer.WriteLine("        }");
        }

        writer.WriteLine("    }");
    }

    /// <summary>Renders one message as a keyword and its fields.</summary>
    private static string Render(
        INetMessage message, Dictionary<int, PlayerInfo> roster,
        EntityDecoder? schema = null) => message switch
    {
        NetTickMessage tick => string.Create(
            CultureInfo.InvariantCulture,
            $"net_tick tick {tick.Tick} frametime {tick.HostFrameTimeSeconds:F6}"),

        PrintMessage print => string.Create(
            CultureInfo.InvariantCulture, $"svc_print {Quote(print.Text)}"),

        StringCmdMessage command => string.Create(
            CultureInfo.InvariantCulture, $"svc_stufftext {Quote(command.Command)}"),

        ChatMessage chat => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_chat from {Quote(chat.From ?? string.Empty)} text {Quote(chat.Text)}"),

        GameEventMessage gameEvent => RenderEvent(gameEvent, roster),

        ServerInfoMessage info => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_serverinfo protocol {info.NetworkProtocol} map {Quote(info.Map)} " +
            $"max_classes {info.MaxClasses} tickrate {info.IntervalPerTick:F6}"),

        // **What the server CHANGED from default, which is the only place a demo says so** (B220).
        // This fell through to the bare-name default and printed `svc_setconvar;` — the message
        // present and its values gone — while the assembly rendered all of them, because `-a` has
        // to compile back to the demo. Nothing was broken in a way a byte comparison could see.
        //
        // It matters more than its size suggests: a real match server sends forty values here,
        // including `sv_client_max_interp_ratio` and the cmdrate clamps that bound what the
        // recording client's own interpolation could have been (D106, docs/CVAR-COVERAGE.md).
        SetConVarMessage convars => RenderConVars(convars),

        PacketEntitiesMessage entities => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_packetentities delta {(entities.IsDelta ? 1 : 0)} " +
            $"updated {entities.UpdatedEntries} bits {entities.LengthBits}"),

        CreateStringTableMessage table => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_createstringtable {Quote(table.Name)} entries {table.Entries.Count} " +
            $"max {table.MaxEntries}"),

        ClassInfoMessage classes => string.Create(
            CultureInfo.InvariantCulture, $"svc_classinfo count {classes.Classes.Count}"),

        FileMessage file => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_file {(file.IsRequested ? "request" : "offer")} " +
            $"id {file.TransferId} name {Quote(file.FileName)}"),

        GetCvarValueMessage cvar => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_getcvarvalue cookie {cvar.Cookie} name {Quote(cvar.CvarName)}"),

        PrefetchMessage prefetch => string.Create(
            CultureInfo.InvariantCulture, $"svc_prefetch sound {prefetch.SoundIndex}"),

        FixAngleMessage angle => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_fixangle {(angle.IsRelative ? "relative" : "absolute")} " +
            $"pitch {angle.Pitch:F3} yaw {angle.Yaw:F3} roll {angle.Roll:F3}"),

        SetViewMessage view => string.Create(
            CultureInfo.InvariantCulture, $"svc_setview entity {view.EntityIndex}"),

        SignOnStateMessage signon => string.Create(
            CultureInfo.InvariantCulture,
            $"net_signonstate state {signon.State} spawn {signon.SpawnCount}"),

        // The leading byte selects the case inside the receiving class's ReceiveMessage, so the
        // class has to be resolved before the byte can be named: 1 is BASEENTITY_MSG_REMOVE_DECALS
        // to most handlers and PLAY_PLAYER_JINGLE to C_BasePlayer. With no schema in hand the
        // number is still reported bare, which claims nothing.
        EntityMessage { MessageType: int entityMessageType } addressed => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_entitymessage entity {addressed.EntityIndex} " +
            $"class {(schema is null ? addressed.ClassId.ToString(CultureInfo.InvariantCulture) : Named(schema, addressed.ClassId))} " +
            $"bits {addressed.BodyBits} type {entityMessageType}" +
            $"{Suffixed(schema, addressed.ClassId, entityMessageType)}"),

        EntityMessage entityMessage => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_entitymessage entity {entityMessage.EntityIndex} " +
            $"class {entityMessage.ClassId} bits {entityMessage.BodyBits}"),

        // A world decal carries no entity or model, so it renders as a different shape rather
        // than as zeroes that look like real indices.
        BspDecalMessage { OnEntity: true } decal => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_bspdecal entity {decal.EntityIndex} model {decal.ModelIndex}"),

        BspDecalMessage => "svc_bspdecal world",

        VoiceInitMessage voice => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_voiceinit codec {Quote(voice.Codec)} quality {voice.Quality}"),

        TempEntitiesMessage temp => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_tempentities count {temp.Count} bits {temp.BodyBits}"),

        SoundsMessage sounds => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_sounds {(sounds.IsReliable ? "reliable" : "unreliable")} " +
            $"count {sounds.Count} bits {sounds.BodyBits}"),

        // Named where the id is known, numbered where it is not. A user message carries no name
        // on the wire, and an unnamed one is the single most common message in some demos.
        UserMessage user => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_usermessage {user.Name ?? "#" + user.UserMessageType.ToString(CultureInfo.InvariantCulture)} " +
            $"type {user.UserMessageType} bits {user.BodyBits}{UserFields(user)}"),

        VoiceDataMessage voice => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_voicedata client {voice.Client} proximity {voice.Proximity} " +
            $"bits {voice.BodyBits}"),

        SkippedMessage skipped => string.Create(
            CultureInfo.InvariantCulture, $"{WireName(skipped.Type)} bits {skipped.BodyBits}"),

        _ => WireName(message.Type),
    };

    private static string RenderEvent(
        GameEventMessage gameEvent, Dictionary<int, PlayerInfo> roster)
    {
        StringBuilder line = new();
        line.Append("svc_gameevent ").Append(gameEvent.Name ?? string.Create(
            CultureInfo.InvariantCulture, $"#{gameEvent.EventId}"));

        foreach (KeyValuePair<string, object?> field in gameEvent.Values)
        {
            line.Append(' ').Append(field.Key).Append(' ');
            line.Append(field.Value switch
            {
                string text => Quote(text),

                // A `local` field is declared but never transmitted, so it has no value. Saying
                // so is the honest rendering: converting the null would print nothing at all,
                // which reads as an empty string and leaves a trailing space on the line.
                null => LocalFieldValue,

                // Player references become Name(id), by the same allowlist the summary uses -
                // the two outputs must not disagree about who a kill belongs to. Non-player
                // fields fall through unchanged.
                _ => PlayerReferences.Render(field, Roster.ByUserId(roster)),
            });
        }

        return line.ToString();
    }

    private static void WriteField(TextWriter writer, string name, string value) =>
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {name} {value};"));

    /// <summary>
    /// Quotes a string, escaping what would otherwise break the format.
    /// </summary>
    /// <remarks>
    /// Chat and console commands carry arbitrary player text, including quotes and newlines. An
    /// unescaped newline inside a value would split one message across two lines and make the
    /// trace unparseable — which matters because this format is meant to be read back.
    /// </remarks>
    /// <summary>Renders <c>net_SetConVar</c> with every name and value it carries (B220).</summary>
    /// <param name="convars">The message.</param>
    /// <returns>The trace line.</returns>
    /// <remarks>
    /// **A loop and a builder rather than a `Join` over a `Select`.** LINQ is a test-only tool in
    /// this project — the owner: *"linq can be slow if its in a hot path, i dont like link in the
    /// program proper so performance stays high"* — and this runs once per message of a demo, which
    /// is a hot path by any reading.
    ///
    /// The count is printed as well as the pairs so that an EMPTY message still says something. A
    /// server that changed nothing sends no message at all, so a zero here means the message existed
    /// and carried nothing, which is a different fact and worth being able to tell apart.
    /// </remarks>
    private static string RenderConVars(SetConVarMessage convars)
    {
        StringBuilder line = new();

        line.Append(CultureInfo.InvariantCulture, $"svc_setconvar count {convars.Variables.Count}");

        foreach (KeyValuePair<string, string> entry in convars.Variables)
        {
            line.Append(' ').Append(Quote(entry.Key)).Append(' ').Append(Quote(entry.Value));
        }

        return line.ToString();
    }

    private static string Quote(string value)
    {
        StringBuilder quoted = new(value.Length + 2);
        quoted.Append('"');

        foreach (char character in value)
        {
            switch (character)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                default: quoted.Append(character); break;
            }
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>Expands a <c>dem_usercmd</c> into the input it records.</summary>
    /// <remarks>
    /// Groups that sit at their defaults are left out. Most commands carry angles, movement and
    /// buttons and nothing else, so printing every field would triple the size of this part of
    /// the trace to say "zero" several hundred thousand times.
    ///
    /// The padding is printed when it is non-zero because it is genuinely in the file, and
    /// because a reader who does not know it is uninitialised engine stack would otherwise never
    /// find out. See <see cref="UserCommand"/>.
    /// </remarks>
    private static void WriteUserCommand(TextWriter writer, string kind, DemoCommand command)
    {
        UserCommand input = UserCommand.Decode(command.Payload.Span);

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"block {kind} tick {command.Tick} {{"));

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    command {input.CommandNumber} client_tick {input.TickCount};"));

        if (input.Pitch != 0 || input.Yaw != 0 || input.Roll != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    angles {input.Pitch:F3} {input.Yaw:F3} {input.Roll:F3};"));
        }

        if (input.ForwardMove != 0 || input.SideMove != 0 || input.UpMove != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    move {input.ForwardMove:F3} {input.SideMove:F3} {input.UpMove:F3};"));
        }

        if (input.Buttons != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    buttons {UserCommandButtons.Describe(input.Buttons)};"));
        }

        if (input.Impulse != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    impulse {input.Impulse};"));
        }

        if (input.WeaponSelect != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    weapon {input.WeaponSelect} subtype {input.WeaponSubtype};"));
        }

        if (input.MouseDx != 0 || input.MouseDy != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    mouse {input.MouseDx} {input.MouseDy};"));
        }

        if (input.Padding != 0)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    pad 0x{input.Padding:X};"));
        }

        writer.WriteLine("}");
    }

    /// <summary>Expands a <c>dem_consolecmd</c> into the command line it holds.</summary>
    /// <remarks>
    /// A null-terminated string and nothing else, which is why it went unprinted for so long -
    /// there was no decoding to do and so nothing prompted anyone to do it. It is worth having:
    /// this is where `killserver`, `say`, and every bound console command the recording player
    /// typed actually appear.
    /// </remarks>
    private static void WriteConsoleCommand(TextWriter writer, string kind, DemoCommand command)
    {
        ReadOnlySpan<byte> payload = command.Payload.Span;
        int end = payload.IndexOf((byte)0);
        string text = Encoding.UTF8.GetString(end < 0 ? payload : payload[..end]);

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"block {kind} tick {command.Tick} {{ command {Quote(text)}; }}"));
    }

    private static string WireName(DemoCommandType type) => type switch
    {
        DemoCommandType.Signon => "dem_signon",
        DemoCommandType.Packet => "dem_packet",
        DemoCommandType.SyncTick => "dem_synctick",
        DemoCommandType.ConsoleCmd => "dem_consolecmd",
        DemoCommandType.UserCmd => "dem_usercmd",
        DemoCommandType.DataTables => "dem_datatables",
        DemoCommandType.Stop => "dem_stop",
        DemoCommandType.StringTables => "dem_stringtables",
        _ => "dem_unknown",
    };

    /// <summary>Falls back to the enum name for messages with no dedicated rendering.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Lowercase is the format, not a normalisation. Valve's own names are " +
                        "svc_print and svc_gameevent, and a trace that shouted SVC_PRINT would " +
                        "not match the engine, the SDK, or any other tool's output.")]
    private static string WireName(NetMessageType type) =>
        "svc_" + type.ToString().ToLowerInvariant();
}
