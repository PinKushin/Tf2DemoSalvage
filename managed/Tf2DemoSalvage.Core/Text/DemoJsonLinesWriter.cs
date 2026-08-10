using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Writes a demo as JSON Lines — one self-contained JSON object per line.
/// </summary>
/// <remarks>
/// The machine-readable counterpart to <see cref="DemoTextDumper"/>, and the pair is deliberate:
/// the text dump is for a person deciding whether a demo is intact, this is for anything that
/// wants to compute over it.
///
/// **One object per line, never pretty-printed.** That single rule is what makes the format
/// worth choosing: a consumer can <c>grep</c> for a player, pipe to <c>jq</c>, or stream a
/// 120,000-event demo without holding any of it in memory. A record split across lines breaks
/// all three at once, so the writer is configured to make that impossible rather than merely
/// avoided.
///
/// Numbers are invariant, for the reason the text dump's are: a file written on a machine with a
/// comma decimal separator has to parse everywhere else.
/// </remarks>
public static class DemoJsonLinesWriter
{
    /// <summary>Never indents, so a record can never span lines.</summary>
    private static readonly JsonWriterOptions LineOptions = new() { Indented = false };

    /// <summary>Writes the demo as JSON Lines.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="fileName">Name to report for the demo.</param>
    /// <param name="header">The demo's parsed header.</param>
    /// <param name="commands">The demo's commands, in stream order.</param>
    /// <param name="progress">Optional listener for scan progress.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/>, <paramref name="header"/>, or <paramref name="commands"/> is
    /// <c>null</c>.
    /// </exception>
    public static void Write(
        TextWriter writer,
        string fileName,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands,
        IProgress<DumpProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        writer.NewLine = "\n";

        // The header goes first so a streaming consumer knows the map and tick count before it
        // interprets anything that follows.
        WriteLine(writer, json =>
        {
            json.WriteString("type", "header");
            json.WriteString("file", fileName);
            json.WriteNumber("demoProtocol", header.DemoProtocol);
            json.WriteNumber("networkProtocol", header.NetworkProtocol);
            json.WriteString("server", header.ServerName);
            json.WriteString("client", header.ClientName);
            json.WriteString("map", header.MapName);
            json.WriteString("gameDirectory", header.GameDirectory);
            json.WriteNumber("playbackTimeSeconds", header.PlaybackTimeSeconds);
            json.WriteNumber("playbackTicks", header.PlaybackTicks);
            json.WriteNumber("playbackFrames", header.PlaybackFrames);
            json.WriteNumber("signonLengthBytes", header.SignonLengthBytes);
        });

        // Entity events are on here and off for the text dump. This is the machine format, and a
        // consumer asking "when did entity 42 exist" has nowhere else to get the answer; the text
        // dump has --trace for the same information in stream order.
        DemoScan.Result scan = DemoScan.Run(
            commands, int.MaxValue, progress, (ushort)header.NetworkProtocol,
            includeEntityEvents: true);

        foreach (PlayerInfo player in scan.Players.Values)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "player");
                json.WriteNumber("entity", player.EntityIndex);
                json.WriteNumber("userId", player.UserId);
                json.WriteString("name", player.Name);
                json.WriteString("steamId", player.SteamId);
                json.WriteBoolean("bot", player.IsBot);
                json.WriteBoolean("sourceTv", player.IsSourceTv);
            });
        }

        foreach ((int tick, ChatMessage chat) in scan.Chat)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "chat");
                json.WriteNumber("tick", tick);
                json.WriteNumber("entity", chat.ClientEntityIndex);
                json.WriteString("kind", chat.Kind);
                json.WriteString("from", chat.From);
                json.WriteString("text", chat.Text);
            });
        }

        // The camera track, straight off the commands rather than out of the scan - democmdinfo_t
        // sits in the container, not in the message stream. This is the recording client's own
        // viewpoint over time, which is what a 2D or 3D viewer follows and which nothing else in
        // the file records.
        foreach (DemoCommand command in commands)
        {
            if (command.View is not { } view)
            {
                continue;
            }

            WriteLine(writer, json =>
            {
                json.WriteString("type", "camera");
                json.WriteNumber("tick", command.Tick);
                json.WriteNumber("x", view.OriginX);
                json.WriteNumber("y", view.OriginY);
                json.WriteNumber("z", view.OriginZ);
                json.WriteNumber("pitch", view.Pitch);
                json.WriteNumber("yaw", view.Yaw);
                json.WriteNumber("roll", view.Roll);
            });
        }

        foreach ((int tick, DecodedSound sound) in scan.Sounds)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "sound");
                json.WriteNumber("tick", tick);
                json.WriteNumber("sound", sound.SoundNumber);
                json.WriteNumber("entity", sound.EntityIndex);
                json.WriteNumber("x", sound.OriginX);
                json.WriteNumber("y", sound.OriginY);
                json.WriteNumber("z", sound.OriginZ);
                json.WriteNumber("volume", sound.Volume);
                json.WriteNumber("pitch", sound.Pitch);
                json.WriteNumber("channel", sound.Channel);
                json.WriteNumber("flags", sound.Flags);
            });
        }

        foreach ((int tick, string className, DecodedTempEntity effect) in scan.Effects)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "effect");
                json.WriteNumber("tick", tick);
                json.WriteString("class", className);
                json.WriteNumber("classId", effect.ClassId);
                json.WriteNumber("delay", effect.DelaySeconds);
                json.WriteStartObject("fields");
                foreach (DecodedProperty property in effect.Properties)
                {
                    json.WriteString(
                        property.Definition.Property.Name, property.Value.ToString());
                }

                json.WriteEndObject();
            });
        }

        foreach ((int tick, UserMessage user) in scan.UserMessages)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "usermessage");
                json.WriteNumber("tick", tick);
                json.WriteString("name", user.Name);
                json.WriteNumber("messageType", user.UserMessageType);
                json.WriteStartObject("fields");
                foreach (KeyValuePair<string, object?> field in user.Fields!)
                {
                    WriteField(json, field);
                }

                json.WriteEndObject();
            });
        }

        foreach (DemoScan.EntityEvent entity in scan.EntityEvents)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "entity");
                json.WriteString("event", EntityEventName(entity.Update));
                json.WriteNumber("tick", entity.Tick);
                json.WriteNumber("entity", entity.EntityIndex);
                json.WriteNumber("classId", entity.ClassId);
                json.WriteString("class", entity.ClassName);

                // Only meaningful when the entity entered: it is what distinguishes a reused
                // index from the entity that held it before.
                if (entity.Update == EntityUpdateType.Enter)
                {
                    json.WriteNumber("serial", entity.SerialNumber);
                }
            });
        }

        foreach ((int tick, string name, IReadOnlyList<KeyValuePair<string, object?>> fields)
            in scan.EventSample)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "event");
                json.WriteNumber("tick", tick);
                json.WriteString("name", name);
                json.WriteStartObject("fields");
                foreach (KeyValuePair<string, object?> field in fields)
                {
                    WriteField(json, field);
                }

                json.WriteEndObject();
            });
        }
    }

    /// <summary>Lower-case wire name for a lifecycle transition.</summary>
    /// <remarks>
    /// Spelled out rather than emitting the enum's number, so a line stays readable without the
    /// reader holding this project's enum ordering. <c>Delta</c> never reaches here — it is an
    /// update to an existing entity, not a lifecycle change.
    /// </remarks>
    private static string EntityEventName(EntityUpdateType update) => update switch
    {
        EntityUpdateType.Enter => "enter",
        EntityUpdateType.Leave => "leave",
        EntityUpdateType.Delete => "delete",
        _ => "update",
    };

    /// <summary>
    /// Writes a field with its own type rather than stringifying everything.
    /// </summary>
    /// <remarks>
    /// A consumer comparing <c>damageamount</c> against a threshold should not have to parse a
    /// string first, and a boolean rendered as <c>"False"</c> is a trap in every language whose
    /// truthiness rules differ from C#'s.
    /// </remarks>
    private static void WriteField(Utf8JsonWriter json, KeyValuePair<string, object?> field)
    {
        switch (field.Value)
        {
            case bool flag:
                json.WriteBoolean(field.Key, flag);
                break;

            case string text:
                json.WriteString(field.Key, text);
                break;

            case float number:
                json.WriteNumber(field.Key, number);
                break;

            case IConvertible convertible:
                json.WriteNumber(field.Key, convertible.ToInt64(CultureInfo.InvariantCulture));
                break;

            default:
                json.WriteString(field.Key, Convert.ToString(field.Value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Writes one object and the newline that terminates it.</summary>
    private static void WriteLine(TextWriter writer, Action<Utf8JsonWriter> body)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer, LineOptions))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        // Stryker disable once String: ASCII and UTF-8 agree here by construction. Utf8JsonWriter
        // escapes every non-ASCII character to \uXXXX by default - a real dump renders miałker as
        // "miałker" - so this buffer never holds a byte above 0x7F to decode differently.
        // Equivalent, and the round-trip test above still pins that the data survives.
        writer.Write(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        writer.Write('\n');
    }
}
