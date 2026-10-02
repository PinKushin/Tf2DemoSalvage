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
    /// Introduces the bytes a cut file holds after its last whole command — the last line, verbatim (B448).
    /// </summary>
    private const string TailKeyword = "tail";

    /// <summary>The header field stating how wide every message's type field is (B440).</summary>
    private const string TypeBitsKeyword = "messagetypebits";

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

    /// <summary>Writes a demo file's assembly text to another file.</summary>
    /// <param name="demoPath">The demo to read, streamed a command at a time (B449).</param>
    /// <param name="outputPath">The text file to write.</param>
    /// <returns>The number of commands written.</returns>
    /// <remarks>
    /// The one path behind the CLI's <c>--asm -o</c> file case and the viewer's Export button, so a
    /// 2 GB demo exports in what its state costs rather than what its file costs.
    /// </remarks>
    public static int Export(string demoPath, string outputPath)
    {
        DemoCommandCollection commands = DemoCommandCollection.Open(demoPath);

        using (StreamWriter writer = new(outputPath))
        {
            Write(writer, commands.Header, commands, commands.Tail);
        }

        return commands.Count;
    }

    /// <summary>Compiles an assembly text file back into a demo file.</summary>
    /// <param name="assemblyPath">The assembly text.</param>
    /// <param name="outputPath">The demo to write.</param>
    /// <returns>The number of commands compiled and of bytes written.</returns>
    /// <exception cref="InvalidDataException">The text is not valid assembly.</exception>
    public static (int Commands, int Bytes) Compile(string assemblyPath, string outputPath)
    {
        // Written beside the target and moved over it only once the whole text compiled: a parse that
        // fails at the end must not leave a partial demo where the user asked for one. Same folder, so
        // the move is a rename.
        string temp = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
            Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            (int commands, long bytes) result;

            using (StreamReader reader = new(assemblyPath))
            using (FileStream output = new(temp, FileMode.CreateNew, FileAccess.Write))
            {
                result = Compile(reader, output);
            }

            File.Move(temp, outputPath, overwrite: true);
            return (result.commands, checked((int)result.bytes));
        }
        finally
        {
            // A no-op after the move; the cleanup of a failed compile otherwise.
            File.Delete(temp);
        }
    }

    /// <summary>Compiles assembly text into a demo on a stream, a command at a time (B449).</summary>
    /// <param name="reader">The assembly text.</param>
    /// <param name="output">
    /// Where the demo goes, from its current position. It must seek: the header is written last, over a
    /// placeholder, because the text may state a header field anywhere in its <c>demo</c> block — as
    /// <see cref="Parse"/> reads it — and a command cannot wait for the whole text.
    /// </param>
    /// <returns>The commands compiled — every one parsed, as <see cref="Parse"/> counts them — and the bytes written.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The text is not valid assembly.</exception>
    /// <remarks>
    /// Memory is one command and the parse state, never the demo: <see cref="Parse"/> then
    /// <see cref="DemoWriter.Write"/> held the command list and the file, which a 2 GB demo outgrows. The
    /// bytes are <see cref="DemoWriter"/>'s own, command by command, so the two routes cannot differ.
    /// </remarks>
    public static (int Commands, long Bytes) Compile(TextReader reader, Stream output)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(output);

        long start = output.Position;
        output.Write(new byte[DemoHeader.SizeBytes]);

        int commands = 0;
        bool open = true;

        (DemoHeader header, byte[]? tail) = ParseEach(reader, command =>
        {
            commands++;
            open = open && DemoWriter.WriteCommand(output, command);
        });

        output.Write(tail.AsSpan());
        long end = output.Position;

        output.Position = start;
        output.Write(DemoWriter.WriteHeader(header));
        output.Position = end;

        return (commands, end - start);
    }

    /// <summary>Writes the demo as assembly text.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="header">The demo's header.</param>
    /// <param name="commands">The demo's commands, in stream order.</param>
    /// <param name="tail">
    /// The bytes after the last whole command of a cut file, from
    /// <see cref="DemoCommandReader.ReadWhole"/> — carried as one <c>tail</c> line, never decoded (B448).
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static void Write(
        TextWriter writer,
        DemoHeader header,
        IReadOnlyCollection<DemoCommand> commands,
        ReadOnlyMemory<byte> tail = default)
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

        // **Stated, because the compiler has no bytes to ask** (B440). The type field is five bits
        // below protocol 15, six above it, and either at 15 — two builds wrote it — where reading
        // decides it from the first packet. Compiling writes that packet, so the width has to be in
        // the text before any packet is: the protocol alone would compile a six-bit demo at five.
        int typeBits = MessageTypeBitsOf(header, commands);
        WriteField(writer, TypeBitsKeyword, typeBits);
        writer.WriteLine(EndKeyword);

        // The same state the reader keeps, for the same reason: a payload cannot be split into
        // messages without knowing the width of their type fields.
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)header.NetworkProtocol,
            MessageTypeBits = typeBits,
        };

        // Built when dem_datatables goes past, and carried from there on: an entity snapshot is
        // meaningless without the schema, and the schema arrives once as its own command.
        EntityDecoder? entities = null;

        // **Carried across packets, which it was not.** This is the state a candidate line is
        // verified against, and it has to be the state a compiler would have at that point in the
        // file - which means everything learned since the beginning of the demo, not since the
        // beginning of this packet. Rebuilding it per packet silently declined every message whose
        // width or meaning depends on an earlier one: svc_ServerInfo sizes a prefetch, a game
        // event list types every event, a create string table sizes an update's indices. All three
        // arrive in signon and were forgotten immediately.
        NetDecodeState check = new()
        {
            NetworkProtocol = (ushort)header.NetworkProtocol,
            MessageTypeBits = typeBits,
        };

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
                WriteMessages(writer, command.Payload.Span, state, check, entities);
                writer.WriteLine(EndKeyword);
            }
            else if (command.Type == DemoCommandType.DataTables)
            {
                entities = BuildDecoder(command, (ushort)header.NetworkProtocol);
            }
        }

        if (!tail.IsEmpty)
        {
            writer.WriteLine($"{TailKeyword} {Convert.ToHexString(tail.Span)}");
        }
    }

    /// <summary>Compiles assembly text back into a header and commands.</summary>
    /// <param name="reader">The assembly text.</param>
    /// <returns>The header, commands and tail, ready for <see cref="DemoWriter"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The text is not valid assembly.</exception>
    public static AssembledDemo Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        List<DemoCommand> commands = [];
        (DemoHeader header, byte[]? tail) = ParseEach(reader, commands.Add);
        return new AssembledDemo(header, commands, tail);
    }

    /// <summary>The one parse: each command handed on as it is compiled, the header and tail returned at the end.</summary>
    private static (DemoHeader Header, byte[]? Tail) ParseEach(TextReader reader, Action<DemoCommand> emit)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        byte[]? tail = null;
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

                // The same, and it outranks the protocol: text written before this field existed
                // lacks it, and there the protocol's own width is right.
                if (line[..space] == TypeBitsKeyword)
                {
                    state.MessageTypeBits = TypeBits(fields[TypeBitsKeyword]);
                }

                continue;
            }

            // The file ended inside the command these bytes began, so nothing can follow them.
            if (tail is not null)
            {
                throw new InvalidDataException($"'{line}' follows the '{TailKeyword}' line, which ends the demo.");
            }

            if (line.StartsWith(TailKeyword + " ", StringComparison.Ordinal))
            {
                tail = AssemblyText.Hex(line[(TailKeyword.Length + 1)..], $"'{TailKeyword}' line", "The tail");
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

            emit(command);
        }

        if (!headerSeen)
        {
            throw new InvalidDataException("The assembly has no 'demo' header block.");
        }

        return (BuildHeader(fields), tail);
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
        TextWriter writer,
        ReadOnlySpan<byte> payload,
        NetDecodeState state,
        NetDecodeState check,
        EntityDecoder? entities)
    {
        NetMessageReadResult result = NetMessageReader.Read(payload, state);

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
                writer.WriteLine(
                    "  " + MessageAssembly.WriteRaw(
                        original, end - start, Label(message)));

                // A declined message still happened. Its effect on the state has to be applied
                // here, because the verification pass never assembled it - and a compiler reading
                // the file back will not learn it either, which is a limit of a raw line worth
                // knowing rather than a bug to hide.
                Advance(check, message);
            }
            else
            {
                foreach (string line in lines)
                {
                    writer.WriteLine("  " + line);
                }
            }

        }

        int trailing = (payload.Length * 8) - result.BitsConsumed;
        if (trailing > 0)
        {
            // Padding rather than a message: a packet is a whole number of bytes and the
            // messages inside it are not. The bits are usually zero and demonstrably not always,
            // so they are carried rather than assumed.
            writer.WriteLine(
                "  " + MessageAssembly.WriteRaw(
                    Slice(payload, result.BitsConsumed, trailing), trailing, "padding"));
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
                state,
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

    /// <summary>Applies a message's effect on decode state, as the reader would.</summary>
    private static void Advance(NetDecodeState state, INetMessage message)
    {
        switch (message)
        {
            case ServerInfoMessage info:
                state.ServerInfo = info;
                break;

            case GameEventListMessage list:
                state.AddEventDefinitions(list.Definitions);
                break;

            case CreateStringTableMessage table:
                state.AddStringTable(table.Name, table.MaxEntries);
                break;

            default:
                break;
        }
    }

    /// <summary>What a raw line stands for, for the comment on it.</summary>
    /// <remarks>
    /// Two different situations share the <c>raw</c> keyword and it is worth telling them apart in
    /// the file: a type with no text form at all, and one that has a text form which did not
    /// reproduce these particular bits. The second is a finding; the first is a queue.
    /// </remarks>
    private static string Label(INetMessage message) =>
        MessageAssembly.CanWrite(message)
            ? message.Type + " declined"
            : message.Type.ToString();

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
            byte[] bytes = AssemblyText.Hex(
                parts[i + 1], $"'{parts[i]}' section", $"Command line '{line}'");
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
            //
            // **Wrapped so a failure names the line it failed on.** Without this the message was
            // "Unknown message ''" against a file of three million lines, which is a report that a
            // problem exists and nothing more. Diagnosing one meant bisecting the input, and the
            // bisect misled: a truncated prefix fails for its own reasons, so it converged on the
            // last line rather than the bad one.
            try
            {
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
            catch (InvalidDataException failure)
            {
                throw new InvalidDataException(
                    $"{failure.Message} (assembling: {line})", failure);
            }
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
        PlaybackTimeSeconds = AssemblyText.Real(
            Text(fields, "playbacktime"), "'playbacktime' field", HeaderSubject),
        PlaybackTicks = Integer(fields, "playbackticks"),
        PlaybackFrames = Integer(fields, "playbackframes"),
        SignonLengthBytes = Integer(fields, "signonlength"),
    };

    /// <summary>What a refusal about the header calls the thing it was reading.</summary>
    private const string HeaderSubject = "The header";

    /// <summary>The width a demo's message type fields were written at.</summary>
    /// <remarks>
    /// Asked of the reader rather than worked out here, so the text states the width every packet
    /// below it is then read at: a state reads its first packet and keeps what that decided.
    /// </remarks>
    private static int MessageTypeBitsOf(DemoHeader header, IReadOnlyCollection<DemoCommand> commands)
    {
        NetDecodeState first = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        foreach (DemoCommand command in commands)
        {
            if (command.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            {
                _ = NetMessageReader.Read(command.Payload.Span, first);
                break;
            }
        }

        return first.MessageTypeBits;
    }

    /// <summary>The stated type width, refusing one no build ever wrote.</summary>
    /// <remarks>
    /// A trust boundary: the text is edited by hand, and any other width compiles every message
    /// after it into bits no client can read, without anything failing on the way.
    /// </remarks>
    private static int TypeBits(string text)
    {
        int bits = AssemblyText.Number(text, $"'{TypeBitsKeyword}' field", HeaderSubject);

        if (bits is NetMessage.OldTypeBits or NetMessage.TypeBits)
        {
            return bits;
        }

        // Stryker disable all : the String mutator wraps the interpolated literal in a ternary that
        // cannot bind to string.Create's interpolated-string handler (CS1620), and Safe Mode then
        // drops every mutation in this method — B410.
        throw new InvalidDataException(string.Create(
            CultureInfo.InvariantCulture,
            $"The header's '{TypeBitsKeyword}' is {bits}; a message type field is " +
            $"{NetMessage.OldTypeBits} or {NetMessage.TypeBits} bits."));

        // Stryker restore all
    }

    private static int Integer(Dictionary<string, string> fields, string name) =>
        AssemblyText.Number(Text(fields, name), $"'{name}' field", HeaderSubject);

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
    /// <remarks>
    /// **Shares <c>MessageAssembly.Quote</c> rather than having its own, because it had its own and
    /// they disagreed.** This escaped the quote character and nothing else — not the backslash that
    /// makes escaping work, not the newline, not the carriage return that ends a line for
    /// <c>ReadLine</c>. A server name containing any of those could not survive the round trip,
    /// while the same string inside a message could.
    ///
    /// Two implementations of one rule is the shape that drifts: the message side gained backslash
    /// and newline escaping at some point and this did not, and nothing failed, because no test put
    /// an awkward character in a header. One function, so the next escape added is added once.
    /// </remarks>
    private static void WriteField(TextWriter writer, string name, string value) =>
        writer.WriteLine($"  {name} {MessageAssembly.Quote(value)}");

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

    /// <summary>The inverse of <see cref="MessageAssembly.Quote"/>.</summary>
    /// <remarks>
    /// **Delegates for the same reason the writer above does.** This unescaped only
    /// <c>\"</c>, so a value written with a backslash, a newline or a carriage return came back
    /// wrong — and a value ending in a backslash came back with its closing quote absorbed. The
    /// escape rule now has one writer and one reader rather than two of each.
    /// </remarks>
    private static string Unquote(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return trimmed;
        }

        return MessageAssembly.Unquote(trimmed);
    }
}
