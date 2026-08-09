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

        NetDecodeState state = new();
        EntityDecoder? entities = options.IncludeEntities ? BuildDecoder(commands) : null;
        int snapshots = 0;
        int scanned = 0;

        foreach (DemoCommand command in commands)
        {
            scanned++;
            if (progress is not null && (scanned % ProgressInterval == 0 || scanned == commands.Count))
            {
                progress.Report(new DumpProgress("Tracing", scanned, commands.Count));
            }

            WriteBlock(writer, command, state, options, entities, ref snapshots);
        }
    }

    /// <summary>Commands between progress reports.</summary>
    private const int ProgressInterval = 512;

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
    private static EntityDecoder? BuildDecoder(IReadOnlyList<DemoCommand> commands)
    {
        foreach (DemoCommand command in commands)
        {
            if (command.Type != DemoCommandType.DataTables)
            {
                continue;
            }

            DemoSchema schema = SendTableParser.Parse(command.Payload.Span);
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
        ref int snapshots)
    {
        string kind = WireName(command.Type);

        if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
        {
            // Still a block. A trace that omitted dem_synctick or dem_stop would not describe
            // the file it read.
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"block {kind} tick {command.Tick};"));
            return;
        }

        NetMessageReadResult result = NetMessageReader.Read(command.Payload.Span, state);

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"block {kind} tick {command.Tick} {{"));

        foreach (INetMessage message in result.Messages)
        {
            if (message is PacketEntitiesMessage snapshot && entities is not null &&
                WithinLimit(options, snapshots))
            {
                snapshots++;
                WriteSnapshot(writer, snapshot, entities, options);
                continue;
            }

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {Render(message)};"));
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

            if (!options.IncludeEntityProperties || entity.Properties.Count == 0)
            {
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"        entity {entity.EntityIndex} {kind} class {entity.ClassId} " +
                    $"props {entity.Properties.Count};"));
                continue;
            }

            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"        entity {entity.EntityIndex} {kind} class {entity.ClassId} {{"));

            foreach (DecodedProperty property in entity.Properties)
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
    private static string Render(INetMessage message) => message switch
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

        GameEventMessage gameEvent => RenderEvent(gameEvent),

        ServerInfoMessage info => string.Create(
            CultureInfo.InvariantCulture,
            $"svc_serverinfo protocol {info.NetworkProtocol} map {Quote(info.Map)} " +
            $"max_classes {info.MaxClasses} tickrate {info.IntervalPerTick:F6}"),

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

        SkippedMessage skipped => string.Create(
            CultureInfo.InvariantCulture, $"{WireName(skipped.Type)} bits {skipped.BodyBits}"),

        _ => WireName(message.Type),
    };

    private static string RenderEvent(GameEventMessage gameEvent)
    {
        StringBuilder line = new();
        line.Append("svc_gameevent ").Append(gameEvent.Name ?? string.Create(
            CultureInfo.InvariantCulture, $"#{gameEvent.EventId}"));

        foreach (KeyValuePair<string, object> field in gameEvent.Values)
        {
            line.Append(' ').Append(field.Key).Append(' ');
            line.Append(field.Value is string text
                ? Quote(text)
                : Convert.ToString(field.Value, CultureInfo.InvariantCulture));
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
