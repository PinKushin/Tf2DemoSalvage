using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Writes a demo as text that can be compiled back into the same bytes, and reads it back.
/// </summary>
/// <remarks>
/// **The decompile/compile pair, in the sense the Quake demo tools meant it.** A parser that only
/// reads is checked by whether its output looks right, which is an opinion. A parser that reads and
/// writes is checked by whether the bytes come back, which is not.
///
/// **This is the assembly form, not the readable one.** <see cref="DemoTraceWriter"/> is for a
/// person deciding what happened in a match; this is for a machine deciding whether anything was
/// lost. Payloads appear as hex, so the file is roughly twice the size of the demo and says nothing
/// a reader could not get better elsewhere — what it has instead is completeness, which is the only
/// property that makes the round trip mean anything.
///
/// The intended path from here is to promote payloads out of hex, message by message, keeping the
/// round trip green throughout. When every one is structured text, the hex is gone and the two
/// formats have converged. Until then the hex is honest about what has not been done yet, in the
/// way the codec coverage report is.
///
/// Nothing is derived on the way back in. The <c>democmdinfo_t</c> block travels as raw bytes even
/// though its camera fields are decoded elsewhere, because a demo has to be reproducible from what
/// was read rather than from what was understood.
/// </remarks>
public static class DemoAssembly
{
    /// <summary>Marks the header block.</summary>
    private const string HeaderKeyword = "demo";

    /// <summary>Ends the header block.</summary>
    private const string EndKeyword = "end";

    /// <summary>Introduces the <c>democmdinfo_t</c> and sequence bytes.</summary>
    private const string ViewKeyword = "view";

    /// <summary>Introduces a command's payload.</summary>
    private const string DataKeyword = "data";

    /// <summary>
    /// The grammar's command keywords, stated rather than derived from the enum's names.
    /// </summary>
    /// <remarks>
    /// A file format that spells its keywords by lower-casing an enum name changes whenever the
    /// enum is renamed, and nothing would catch that until an old file failed to compile. These
    /// are the format; the enum is an implementation detail on both sides of it.
    /// </remarks>
    private static readonly Dictionary<DemoCommandType, string> Keywords = new()
    {
        [DemoCommandType.Signon] = "signon",
        [DemoCommandType.Packet] = "packet",
        [DemoCommandType.SyncTick] = "synctick",
        [DemoCommandType.ConsoleCmd] = "consolecmd",
        [DemoCommandType.UserCmd] = "usercmd",
        [DemoCommandType.DataTables] = "datatables",
        [DemoCommandType.Stop] = "stop",
        [DemoCommandType.StringTables] = "stringtables",
    };

    /// <summary>The same map, read back.</summary>
    private static readonly Dictionary<string, DemoCommandType> Commands =
        Keywords.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);

    /// <summary>Writes the demo as assembly text.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="header">The demo's header.</param>
    /// <param name="commands">The demo's commands, in stream order.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static void Write(
        TextWriter writer, DemoHeader header, IReadOnlyList<DemoCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        // LF regardless of platform, matching every other output here: a file written on Windows
        // has to compile on Linux and the hex is byte-oriented either way.
        writer.NewLine = "\n";

        writer.WriteLine(HeaderKeyword);
        WriteField(writer, "demoprotocol", header.DemoProtocol);
        WriteField(writer, "networkprotocol", header.NetworkProtocol);
        WriteField(writer, "server", header.ServerName);
        WriteField(writer, "client", header.ClientName);
        WriteField(writer, "map", header.MapName);
        WriteField(writer, "gamedir", header.GameDirectory);

        // Round-trip format, so the seconds go out at full precision. "R" is what guarantees the
        // float that comes back is the same float; four decimal places would not.
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  playbacktime {header.PlaybackTimeSeconds.ToString("R", CultureInfo.InvariantCulture)}"));

        WriteField(writer, "playbackticks", header.PlaybackTicks);
        WriteField(writer, "playbackframes", header.PlaybackFrames);
        WriteField(writer, "signonlength", header.SignonLengthBytes);
        writer.WriteLine(EndKeyword);

        foreach (DemoCommand command in commands)
        {
            StringBuilder line = new();
            line.Append(Keyword(command.Type))
                .Append(' ')
                .Append(command.Tick.ToString(CultureInfo.InvariantCulture));

            if (!command.Prologue.IsEmpty)
            {
                line.Append(' ').Append(ViewKeyword).Append(' ')
                    .Append(Convert.ToHexString(command.Prologue.Span));
            }

            if (!command.Payload.IsEmpty)
            {
                line.Append(' ').Append(DataKeyword).Append(' ')
                    .Append(Convert.ToHexString(command.Payload.Span));
            }

            writer.WriteLine(line.ToString());
        }
    }

    /// <summary>Compiles assembly text back into a header and commands.</summary>
    /// <param name="reader">The assembly text.</param>
    /// <returns>The header and commands, ready for <see cref="DemoWriter"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The text is not valid assembly.</exception>
    public static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        List<DemoCommand> commands = [];
        bool inHeader = false;
        bool headerSeen = false;

        while (reader.ReadLine() is { } raw)
        {
            string line = Strip(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (line == HeaderKeyword)
            {
                inHeader = true;
                headerSeen = true;
                continue;
            }

            if (inHeader)
            {
                if (line == EndKeyword)
                {
                    inHeader = false;
                    continue;
                }

                int space = line.IndexOf(' ', StringComparison.Ordinal);
                if (space < 0)
                {
                    throw new InvalidDataException($"Header line '{line}' has no value.");
                }

                fields[line[..space]] = Unquote(line[(space + 1)..]);
                continue;
            }

            commands.Add(ParseCommand(line));
        }

        if (!headerSeen)
        {
            throw new InvalidDataException("The assembly has no 'demo' header block.");
        }

        return (BuildHeader(fields), commands);
    }

    private static DemoCommand ParseCommand(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidDataException($"Command line '{line}' has no tick.");
        }

        if (!Commands.TryGetValue(parts[0], out DemoCommandType type))
        {
            throw new InvalidDataException($"Unknown command '{parts[0]}'.");
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
        {
            throw new InvalidDataException($"Command tick '{parts[1]}' is not a number.");
        }

        byte[] prologue = [];
        byte[] payload = [];

        for (int i = 2; i + 1 < parts.Length; i += 2)
        {
            byte[] bytes = Convert.FromHexString(parts[i + 1]);
            switch (parts[i])
            {
                case ViewKeyword:
                    prologue = bytes;
                    break;

                case DataKeyword:
                    payload = bytes;
                    break;

                default:
                    throw new InvalidDataException(
                        $"Command '{parts[0]}' has an unknown section '{parts[i]}'.");
            }
        }

        return new DemoCommand(type, tick, payload, prologue);
    }

    private static DemoHeader BuildHeader(Dictionary<string, string> fields) => new()
    {
        DemoProtocol = Integer(fields, "demoprotocol"),
        NetworkProtocol = Integer(fields, "networkprotocol"),
        ServerName = Text(fields, "server"),
        ClientName = Text(fields, "client"),
        MapName = Text(fields, "map"),
        GameDirectory = Text(fields, "gamedir"),
        PlaybackTimeSeconds = float.Parse(
            Text(fields, "playbacktime"), CultureInfo.InvariantCulture),
        PlaybackTicks = Integer(fields, "playbackticks"),
        PlaybackFrames = Integer(fields, "playbackframes"),
        SignonLengthBytes = Integer(fields, "signonlength"),
    };

    private static int Integer(Dictionary<string, string> fields, string name) =>
        int.Parse(Text(fields, name), CultureInfo.InvariantCulture);

    private static string Text(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value)
            ? value
            : throw new InvalidDataException($"The header has no '{name}' field.");

    private static string Keyword(DemoCommandType type) =>
        Keywords.TryGetValue(type, out string? keyword)
            ? keyword
            : throw new InvalidDataException($"Command type {type} has no assembly keyword.");

    private static void WriteField(TextWriter writer, string name, int value) =>
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name} {value}"));

    /// <summary>Writes a string field, quoted so a map name with spaces survives.</summary>
    private static void WriteField(TextWriter writer, string name, string value) =>
        writer.WriteLine($"  {name} \"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");

    /// <summary>Removes a trailing comment and surrounding whitespace.</summary>
    /// <remarks>
    /// Comments are stripped only outside a quoted string, so a server name containing a hash
    /// survives. Hex payloads never contain one.
    /// </remarks>
    private static string Strip(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                quoted = !quoted;
                continue;
            }

            if (line[i] == '#' && !quoted)
            {
                return line[..i].Trim();
            }
        }

        return line.Trim();
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return trimmed;
        }

        return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
    }
}
