using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

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
/// Packet payloads are expanded into one line per message, and a message with no text form yet
/// appears as <c>raw</c> with its bit length and its bits. That is the shape the format grows in:
/// each type promoted out of <c>raw</c> keeps the round trip green or it does not get promoted.
/// Every other command's payload is still whole-payload hex.
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

        // The same state the reader keeps, for the same reason: the message type field is five
        // bits at or below protocol 15 and six above, so a payload cannot be split into messages
        // without knowing which.
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        // Built when dem_datatables goes past, and carried from there on: an entity snapshot is
        // meaningless without the schema, and the schema arrives once as its own command.
        EntityDecoder? entities = null;

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

            bool expandable = command.Type is DemoCommandType.Signon or DemoCommandType.Packet;

            if (!expandable && !command.Payload.IsEmpty)
            {
                line.Append(' ').Append(DataKeyword).Append(' ')
                    .Append(Convert.ToHexString(command.Payload.Span));
            }

            writer.WriteLine(line.ToString());

            if (expandable)
            {
                WriteMessages(writer, command.Payload.Span, state, entities);
                writer.WriteLine(EndKeyword);
            }
            else if (command.Type == DemoCommandType.DataTables)
            {
                entities = BuildDecoder(command, (ushort)header.NetworkProtocol);
            }
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
        NetDecodeState state = new();
        EntityDecoder? entities = null;
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

                // Set as soon as it is read, because the packets that follow cannot be assembled
                // without it: it sizes the message type field.
                if (line[..space] == "networkprotocol")
                {
                    state.NetworkProtocol = ushort.Parse(
                        fields["networkprotocol"], CultureInfo.InvariantCulture);
                }

                continue;
            }

            DemoCommand command = ParseCommand(line);

            if (command.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            {
                command = command with { Payload = ReadMessages(reader, state, entities) };
            }
            else if (command.Type == DemoCommandType.DataTables)
            {
                // The same schema the writer had, from the same bytes. Rebuilt here rather than
                // carried in the text, because the text is not where a schema belongs.
                entities = BuildDecoder(command, state.NetworkProtocol);
            }

            commands.Add(command);
        }

        if (!headerSeen)
        {
            throw new InvalidDataException("The assembly has no 'demo' header block.");
        }

        return (BuildHeader(fields), commands);
    }

    /// <summary>Expands a packet payload into one line per message.</summary>
    /// <remarks>
    /// **Every structured message is assembled back and compared before it is written.** That is
    /// what makes promoting a type safe: a text form that loses something falls back to <c>raw</c>
    /// instead of producing a file that will not compile to the same bytes. The cost is decoding
    /// each candidate twice; the benefit is that the round trip cannot be broken by an experiment.
    ///
    /// A message with no text form is written as its own bits rather than folded into a
    /// neighbour, so promoting a type later changes one line and nothing around it. The bits after
    /// the last message go out the same way - a payload is a whole number of bytes and the
    /// messages inside it are not, so there is nearly always a remainder.
    /// </remarks>
    /// <summary>Builds a decoder from a dem_datatables payload, or nothing if it will not parse.</summary>
    /// <remarks>
    /// One corpus demo has no readable schema at all - a protocol-11 SourceTV recording whose
    /// writer truncated the table at 64 KiB (RISKS B24). Its entity snapshots stay as bits, which
    /// is the correct outcome rather than a failure to report.
    /// </remarks>
    private static EntityDecoder? BuildDecoder(DemoCommand command, ushort protocol)
    {
        try
        {
            DemoSchema schema = SendTableParser.Parse(command.Payload.Span, protocol);
            return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    private static void WriteMessages(
        TextWriter writer, ReadOnlySpan<byte> payload, NetDecodeState state,
        EntityDecoder? entities)
    {
        NetMessageReadResult result = NetMessageReader.Read(payload, state);

        // A separate state for the verification pass, advanced in step with the writer's. Sharing
        // the reader's would let a message be checked against a state it had not reached yet.
        NetDecodeState check = new() { NetworkProtocol = state.NetworkProtocol };

        for (int i = 0; i < result.Messages.Count; i++)
        {
            INetMessage message = result.Messages[i];
            int start = result.MessageStartBits[i];
            int end = i + 1 < result.Messages.Count
                ? result.MessageStartBits[i + 1]
                : result.BitsConsumed;

            byte[] original = Slice(payload, start, end - start);
            IReadOnlyList<string>? lines = TryStructured(
                message, original, end - start, check, state.NetworkProtocol, entities);

            if (lines is null)
            {
                writer.WriteLine("  " + MessageAssembly.WriteRaw(original, end - start));
            }
            else
            {
                foreach (string line in lines)
                {
                    writer.WriteLine("  " + line);
                }
            }

            if (message is ServerInfoMessage info)
            {
                check.ServerInfo = info;
            }
        }

        int trailing = (payload.Length * 8) - result.BitsConsumed;
        if (trailing > 0)
        {
            writer.WriteLine(
                "  " + MessageAssembly.WriteRaw(
                    Slice(payload, result.BitsConsumed, trailing), trailing));
        }
    }

    /// <summary>
    /// Renders a message as text, or <c>null</c> when the text does not assemble back to the same
    /// bits.
    /// </summary>
    private static IReadOnlyList<string>? TryStructured(
        INetMessage message,
        byte[] original,
        int bitCount,
        NetDecodeState state,
        ushort protocol,
        EntityDecoder? entities)
    {
        if (!MessageAssembly.CanWrite(message))
        {
            return null;
        }

        IReadOnlyList<string>? lines;
        BitWriter check = new();
        try
        {
            lines = MessageAssembly.Write(message, protocol, entities);
            if (lines is null)
            {
                return null;
            }

            int index = 0;
            IReadOnlyList<string> written = lines;
            MessageAssembly.Assemble(
                written[0],
                () => ++index < written.Count ? written[index] : null,
                check,
                Copy(state),
                entities);
        }
        catch (Exception failure) when (
            failure is InvalidDataException or EndOfStreamException or FormatException or
                OverflowException or NotSupportedException)
        {
            // A text form that cannot read its own output is a bug worth finding, but not one
            // worth failing a decompile over: raw carries the same bits either way.
            return null;
        }

        return check.BitCount == bitCount && Same(original, check.Build(), bitCount)
            ? lines
            : null;
    }

    /// <summary>A copy, so the verification pass cannot advance the writer's own state.</summary>
    private static NetDecodeState Copy(NetDecodeState state) =>
        new() { NetworkProtocol = state.NetworkProtocol, ServerInfo = state.ServerInfo };

    /// <summary>Whether two buffers agree over the first <paramref name="bits"/> bits.</summary>
    private static bool Same(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int index = bit / 8;
            int shift = bit % 8;
            if (((left[index] >> shift) & 1) != ((right[index] >> shift) & 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Copies a bit range into its own buffer, starting at bit zero.</summary>
    private static byte[] Slice(ReadOnlySpan<byte> source, int startBit, int bits)
    {
        BitWriter writer = new();
        for (int i = 0; i < bits; i++)
        {
            int bit = startBit + i;
            writer.Write((uint)((source[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
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

    /// <summary>Assembles a packet's message lines back into a payload.</summary>
    /// <remarks>
    /// The block ends at <c>end</c>, and the payload it produces is a whole number of bytes
    /// because the trailing bits were written out as their own <c>raw</c> line. Padding to a
    /// boundary here instead would be inventing bits.
    /// </remarks>
    private static byte[] ReadMessages(
        TextReader reader, NetDecodeState state, EntityDecoder? entities)
    {
        BitWriter writer = new();

        while (reader.ReadLine() is { } raw)
        {
            string line = Strip(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (line == EndKeyword)
            {
                return writer.Build();
            }

            // A message may consume further lines of its own - a sounds block, a class list - so
            // it is handed a way to pull them rather than being given one line at a time.
            MessageAssembly.Assemble(
                line,
                () =>
                {
                    string? next = reader.ReadLine();
                    return next is null ? null : Strip(next);
                },
                writer,
                state,
                entities);
        }

        throw new InvalidDataException("A packet block was not closed with 'end'.");
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
