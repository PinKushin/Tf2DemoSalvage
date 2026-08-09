using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

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

        DemoScan.Result scan = DemoScan.Run(commands, int.MaxValue, progress);

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

        foreach ((int tick, string name, IReadOnlyList<KeyValuePair<string, object>> fields)
            in scan.EventSample)
        {
            WriteLine(writer, json =>
            {
                json.WriteString("type", "event");
                json.WriteNumber("tick", tick);
                json.WriteString("name", name);
                json.WriteStartObject("fields");
                foreach (KeyValuePair<string, object> field in fields)
                {
                    WriteField(json, field);
                }

                json.WriteEndObject();
            });
        }
    }

    /// <summary>
    /// Writes a field with its own type rather than stringifying everything.
    /// </summary>
    /// <remarks>
    /// A consumer comparing <c>damageamount</c> against a threshold should not have to parse a
    /// string first, and a boolean rendered as <c>"False"</c> is a trap in every language whose
    /// truthiness rules differ from C#'s.
    /// </remarks>
    private static void WriteField(Utf8JsonWriter json, KeyValuePair<string, object> field)
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

        writer.Write(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        writer.Write('\n');
    }
}
