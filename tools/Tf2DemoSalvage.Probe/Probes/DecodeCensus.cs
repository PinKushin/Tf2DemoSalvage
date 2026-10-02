using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

using static System.FormattableString;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One demo through every stage of the production decode, filling one <see cref="CensusRow"/>.
/// </summary>
/// <remarks>
/// **Every stage calls what its corpus test calls, over the WHOLE demo** — the tests cap commands (400, 900,
/// 1,500) to stay inside a suite run, and a cap on stream position removes the late half of a match, which is
/// where anything that depends on the match having started lives (`CorpusAssemblyRoundTripTests`' own note).
///
/// | stage | test it answers for | production calls |
/// |---|---|---|
/// | container | every corpus test's first step | <see cref="DemoCommandReader.ReadWhole"/> |
/// | schema | `Corpus.Schema` | <see cref="SendTableParser.Parse"/> on every `dem_datatables` |
/// | messages | `EveryWritableMessage_ReproducesItsOwnBitsExactly`, `PayloadRoundTrip_TheCorpus_IsReported` | <see cref="NetMessageReader"/>, <see cref="NetMessageWriter"/> |
/// | entities | `EntityRoundTrip_TheCorpus_IsReported` | <see cref="EntityDecoder.Decode"/>, <see cref="EntityDecoder.EncodeEntities(IReadOnlyList{DecodedEntity}, IReadOnlyList{int}, bool, int, out int)"/> |
/// | trace | `EveryDemo_TracesWithoutAnUnreadableBlock`, `Trace_EveryMessage_IsNamed`, the CLI's `-t -e` | <see cref="DemoTraceWriter.Write"/> with entities |
/// | assembly | `EveryDemo_CompilesBackToItsOwnBytes`, the CLI's `-a` then `-c` | <see cref="DemoAssembly"/>, <see cref="DemoWriter"/>, through a file as the CLI does |
/// | timeline | "decodes to a playable timeline" | <see cref="DemoTimeline.Build"/> |
/// | voice | `EverySpeexFrame_DecodesToPcm`, `EveryCeltFrame_DecodesToPcm`, `EveryChunk_DecodesToNonSilentPcm` | the three decoders, framed as those tests frame them |
///
/// **Where this is stricter than its test, on purpose, and each is a blind spot of the test:** a snapshot that
/// throws in <see cref="EntityDecoder.Decode"/> is a failure here and a silent `continue` there; a demo with no
/// snapshot at all fails here and is skipped there (both B440 demos); and the round trip must give back every
/// byte, where the test accepts a prefix — which a demo ending in a command cut off mid-write always is.
///
/// **Every pass carries a count that shows the stage did work** — commands read, classes parsed, messages
/// re-encoded, snapshots matched, `dem_packet` blocks traced, bytes rebuilt, frames built, voice frames decoded.
/// A pass with a zero there is a fail, because it is a stage that measured nothing.
/// </remarks>
internal sealed class DemoCensus
{
    /// <summary>The assembly failure shape of a demo whose only loss is a final command cut off mid-write.</summary>
    public const string CutTailShape = "tail not carried: a final command cut off mid-write";

    private const long Megabyte = 1 << 20;

    /// <summary>What a demo costs just to hold: its bytes, its command list and one packet's messages.</summary>
    private const int BaseMultiple = 2;

    /// <summary>
    /// The round trip holds the demo, its compiled commands, <see cref="DemoWriter"/>'s growing
    /// <see cref="MemoryStream"/> (twice, as it doubles) and the rebuilt array.
    /// </summary>
    private const int AssemblyMultiple = 5;

    /// <summary>A built timeline holds up to 84 times its demo (B439, `docs/verification`), plus the demo.</summary>
    private const int TimelineMultiple = 85;

    private const int SpeexFrameBytes = 28;
    private const int CeltFrameBytes = 64;

    /// <summary>Fewer voice frames than this say nothing about silence either way.</summary>
    private const int SilenceSample = 100;

    /// <summary>What <c>new StreamWriter(path)</c> writes, which is how the CLI's <c>-o</c> writes the assembly.</summary>
    private static readonly UTF8Encoding CliEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly CensusRow _row;
    private readonly byte[] _bytes;
    private readonly DemoHeader _header;
    private readonly CensusOptions _options;
    private readonly TextWriter _log;
    private readonly string _name;
    private IReadOnlyList<DemoCommand> _commands = [];
    private ReadOnlyMemory<byte> _tail;
    private string? _truncated;
    private bool _read;
    private bool _schema;

    /// <summary>Which command a loop is on, so an exception out of production code can say where it came from.</summary>
    private int _at = -1;

    public DemoCensus(CensusRow row, byte[] bytes, DemoHeader header, CensusOptions options, TextWriter log)
    {
        _row = row;
        _bytes = bytes;
        _header = header;
        _options = options;
        _log = log;
        _name = Path.GetFileName(row["path"]);
    }

    private ushort Protocol => (ushort)_header.NetworkProtocol;

    /// <summary>Whether a demo of this size can be held at all under the budget.</summary>
    public static bool Fits(long bytes, long budget) => BaseMultiple * bytes <= budget;

    /// <summary>Runs every stage in order.</summary>
    public void Run()
    {
        Stage("container", Container);
        Stage("schema", Schema);
        Stage("messages", Messages);
        Stage("entities", Entities);
        Stage("trace", Trace);
        Stage("assembly", Assembly);
        Stage("timeline", Timeline);
        Stage("voice", Voice);
    }

    /// <summary>Runs one stage, timing it and turning anything production code throws into that stage's failure.</summary>
    /// <remarks>
    /// **The catch is a backstop, never a design path** (`docs/memory/decode-must-be-total.md`): a throw out of the
    /// production path on a real demo is a defect, and the census's job is to name it and move to the next demo
    /// rather than stop at the first, which is the masking half of B440. An out-of-memory under the heap limit is a
    /// budget skip, not a decode failure. The stage is logged BEFORE it runs (`logs-are-the-debugger.md`), so a
    /// process that dies here names the demo and the stage.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A census reports every exception the production path throws as a finding; see remarks.")]
    [SuppressMessage("Major Code Smell", "S2221:\"Exception\" should not be caught",
        Justification = "As CA1031: the exception is the finding.")]
    private void Stage(string stage, Action body)
    {
        // The container and the schema always run: every other stage reads their output, and both take under a
        // second, so leaving them out of --stages would only block what was asked for.
        if (!_options.Stages.Contains(stage) && stage is not ("container" or "schema"))
        {
            _row.Skip(stage, "not asked", "not in --stages");
            return;
        }

        _log.WriteLine(Invariant($"{DateTime.UtcNow:O} STAGE {stage}"));
        long started = Stopwatch.GetTimestamp();

        try
        {
            body();
        }
        catch (OutOfMemoryException)
        {
            _row.Skip(stage, "budget", "ran out of memory under the GC heap limit");
        }
        catch (Exception error)
        {
            string where = _at >= 0 && _at < _commands.Count
                ? Invariant($"command {_at}, tick {_commands[_at].Tick}")
                : string.Empty;

            _row.Fail(stage, "throws " + error.GetType().Name + ": " + error.Message, error.GetType().Name + ": " + error.Message, where);
        }
        finally
        {
            _at = -1;
        }

        _row.Set(stage + "_seconds", Stopwatch.GetElapsedTime(started).TotalSeconds);
    }

    /// <summary>Skips a stage whose prerequisite failed, and says so.</summary>
    private bool Requires(string stage, bool met, string reason)
    {
        if (!met)
        {
            _row.Skip(stage, "blocked", reason);
        }

        return met;
    }

    private void Container()
    {
        (_commands, _tail) = DemoCommandReader.ReadWhole(_bytes.AsMemory(DemoHeader.SizeBytes), message => _truncated = message);

        _row.Set("commands", _commands.Count);
        _row.Set("packets", _commands.Count(command => command.Type == DemoCommandType.Packet));
        _row.Set("signons", _commands.Count(command => command.Type == DemoCommandType.Signon));
        _row.Set("usercmds", _commands.Count(command => command.Type == DemoCommandType.UserCmd));
        _row.Set("datatables", _commands.Count(command => command.Type == DemoCommandType.DataTables));
        _row.Set("stop", _commands.Count(command => command.Type == DemoCommandType.Stop));
        _row["truncated"] = _truncated ?? string.Empty;

        if (_commands.Count == 0)
        {
            _row.Fail("container", "no command after the header", "the command stream is empty", "byte 1072");
            return;
        }

        _read = true;
        _row.Pass("container", Invariant($"{_commands.Count} commands") + (_truncated is null ? string.Empty : "; ends in a cut-off command"));
    }

    private void Schema()
    {
        if (!Requires("schema", _read, "the container did not read"))
        {
            return;
        }

        List<DemoCommand> tables = [.. _commands.Where(command => command.Type == DemoCommandType.DataTables)];

        if (tables.Count == 0)
        {
            _row.Fail("schema", "no dem_datatables command", "the demo carries no dem_datatables command", string.Empty);
            return;
        }

        // Every dem_datatables is parsed, though the readers build from the first: a second that throws is a
        // demo this project cannot fully read, whichever table the viewer happens to use.
        DemoSchema? first = null;

        for (_at = 0; _at < _commands.Count; _at++)
        {
            if (_commands[_at].Type == DemoCommandType.DataTables)
            {
                DemoSchema parsed = SendTableParser.Parse(_commands[_at].Payload.Span, Protocol);
                first ??= parsed;
            }
        }

        _row.Set("tables", first!.Tables.Count);
        _row.Set("classes", first.ServerClasses.Count);

        if (first.Tables.Count == 0 || first.ServerClasses.Count == 0)
        {
            _row.Fail("schema", "the schema parsed empty", "no tables or no server classes", string.Empty);
            return;
        }

        _schema = true;
        _row.Pass("schema", Invariant($"{first.Tables.Count} tables, {first.ServerClasses.Count} classes"));
    }

    private void Messages()
    {
        if (!Requires("messages", _read, "the container did not read"))
        {
            return;
        }

        NetDecodeState read = new() { NetworkProtocol = Protocol };
        NetDecodeState write = new() { NetworkProtocol = Protocol };
        MessageTally tally = new();

        for (_at = 0; _at < _commands.Count; _at++)
        {
            if (_commands[_at].Type is DemoCommandType.Signon or DemoCommandType.Packet)
            {
                CheckPacket(_commands[_at], read, write, tally);
            }
        }

        tally.Report(_row);
    }

    /// <summary>Reads one packet and re-encodes each message against the bits it came from.</summary>
    /// <remarks>
    /// Each message is written the moment it is read, against a write state that has seen exactly what the read
    /// state had seen BEFORE this message — the snapshot `CorpusMessageRoundTripTests.Packets` takes per message,
    /// without the copy. `svc_ServerInfo` sizes later fields and a game event list orders every later event, so
    /// both reach the write state after their own message is written, at the point they arrived.
    /// </remarks>
    private void CheckPacket(DemoCommand command, NetDecodeState read, NetDecodeState write, MessageTally tally)
    {
        ReadOnlySpan<byte> payload = command.Payload.Span;
        NetMessageReadResult result = NetMessageReader.Read(payload, read);

        // The width the read settled, carried: at protocol 15 the first packet decides it (B440) and the write
        // state reads no packet, so untold it wrote five bits where the later builds wrote six (B451).
        write.MessageTypeBits = read.MessageTypeBits;

        for (int index = 0; index < result.Messages.Count; index++)
        {
            INetMessage message = result.Messages[index];
            int start = result.MessageStartBits[index];
            int length = (index + 1 < result.Messages.Count ? result.MessageStartBits[index + 1] : result.BitsConsumed) - start;

            tally.Messages++;
            tally.Bits += length;
            tally.Events += message is GameEventMessage ? 1 : 0;
            Rewrite(message, write, payload, start, length, tally, command);

            if (message is ServerInfoMessage info)
            {
                write.ServerInfo = info;
            }
            else if (message is GameEventListMessage list)
            {
                write.AddEventDefinitions(list.Definitions);
            }
        }

        if (result.StopReason is { } reason)
        {
            tally.Stops++;
            tally.Note("reader stops: " + reason, Invariant($"stopped after {result.BitsConsumed} bits: {reason}"), Where(command, result.BitsConsumed));
        }
    }

    private void Rewrite(
        INetMessage message, NetDecodeState write, ReadOnlySpan<byte> payload, int start, int length, MessageTally tally, DemoCommand command)
    {
        string where = Where(command, start);

        if (!NetMessageWriter.CanWrite(message))
        {
            tally.Unwritable[message.Type.ToString()] = tally.Unwritable.GetValueOrDefault(message.Type.ToString()) + length;
            tally.Note(
                Invariant($"{message.Type} cannot be re-encoded"),
                Invariant($"{message.Type}, {length} bits: NetMessageWriter.CanWrite is false"),
                where);
            return;
        }

        BitWriter writer = new();

        if (!NetMessageWriter.TryWrite(writer, message, write))
        {
            tally.Mismatches++;
            tally.Note(Invariant($"{message.Type}: the writer declined it"), Invariant($"{message.Type} at bit {start}: TryWrite returned false"), where);
            return;
        }

        if (writer.BitCount != length)
        {
            tally.Mismatches++;
            tally.Note(
                Invariant($"{message.Type} re-encodes to a different length"),
                Invariant($"{message.Type}: {length} bits on the wire re-encode to {writer.BitCount}"),
                where);
            return;
        }

        int differs = FirstDifferentBit(payload, start, writer.Build(), length);

        if (differs >= 0)
        {
            tally.Mismatches++;
            tally.Note(
                Invariant($"{message.Type} re-encodes to different bits"),
                Invariant($"{message.Type}: bit {differs} of {length} differs"),
                where);
            return;
        }

        tally.Exact++;
    }

    private void Entities()
    {
        if (!Requires("entities", _schema, "the schema did not parse"))
        {
            return;
        }

        // The decoder from the demo's first dem_datatables, before any packet is read, as the trace writer and
        // the timeline build theirs; every packet read with one state from the first, so signon arrives in it.
        DemoCommand tables = _commands.First(command => command.Type == DemoCommandType.DataTables);
        DemoSchema schema = SendTableParser.Parse(tables.Payload.Span, Protocol);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        NetDecodeState state = new() { NetworkProtocol = Protocol };
        EntityTally tally = new();

        for (_at = 0; _at < _commands.Count; _at++)
        {
            DemoCommand command = _commands[_at];

            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (PacketEntitiesMessage snapshot in
                NetMessageReader.Read(command.Payload.Span, state).Messages.OfType<PacketEntitiesMessage>())
            {
                CheckSnapshot(decoder, snapshot, command, tally);
            }
        }

        tally.Report(_row);
    }

    /// <summary>Decodes one snapshot and re-encodes it, compared over its content as the corpus test compares it.</summary>
    private static void CheckSnapshot(EntityDecoder decoder, PacketEntitiesMessage snapshot, DemoCommand command, EntityTally tally)
    {
        IReadOnlyList<DecodedEntity> entities;

        try
        {
            entities = decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            // The corpus test `continue`s past this without counting it. A snapshot that does not decode is a
            // snapshot this project cannot read, so here it is a failure.
            tally.Throws++;
            tally.Note("a snapshot does not decode: " + error.Message, error.GetType().Name + ": " + error.Message, Invariant($"tick {command.Tick}"));
            return;
        }

        tally.Snapshots++;
        tally.Entities += entities.Count;

        byte[] rewritten = decoder.EncodeEntities(
            entities, decoder.RemovedEntities, snapshot.IsDelta, snapshot.LengthBits, out int encodedBits);

        // Past the content is the sender's slack, carried by the assembly on a `slack` line: a fact about the
        // format, reported, never failed (the corpus test's own reading).
        if (snapshot.LengthBits > encodedBits)
        {
            tally.SlackBits += snapshot.LengthBits - encodedBits;
        }

        int comparable = Math.Min(encodedBits, snapshot.LengthBits);
        int difference = encodedBits > snapshot.LengthBits
            ? comparable
            : FirstDifferentBit(snapshot.Body.Span, 0, rewritten, comparable);

        if (difference < 0)
        {
            tally.Exact++;
            return;
        }

        tally.Mismatches++;

        if (tally.HasFirst)
        {
            return;
        }

        (string shape, string detail) = encodedBits > snapshot.LengthBits
            ? ("a snapshot re-encodes longer than it was sent", Invariant($"{encodedBits} bits re-encoded from {snapshot.LengthBits}"))
            : DescribeSnapshot(decoder, snapshot, entities, difference);

        tally.Note(shape, detail, Invariant($"tick {command.Tick}, snapshot bit {difference} of {snapshot.LengthBits}"));
    }

    /// <summary>Names the entity a mismatch starts in, or the removal list after them all.</summary>
    /// <remarks>
    /// The method `CorpusEntityRoundTripTests.Describe` uses: encode longer and longer prefixes of the entity
    /// list, non-delta and unpadded so they carry no removal section, and the first prefix that stops matching is
    /// the culprit. When every prefix matches, the difference is in the removal list (B443's shape).
    /// </remarks>
    private static (string Shape, string Detail) DescribeSnapshot(
        EntityDecoder decoder, PacketEntitiesMessage snapshot, IReadOnlyList<DecodedEntity> entities, int difference)
    {
        for (int count = 1; count <= entities.Count; count++)
        {
            byte[] prefix = decoder.EncodeEntities([.. entities.Take(count)], [], isDelta: false, lengthBits: 0, out int prefixBits);

            if (FirstDifferentBit(snapshot.Body.Span, 0, prefix, prefixBits) < 0)
            {
                continue;
            }

            DecodedEntity culprit = entities[count - 1];
            string properties = string.Join(", ", culprit.Properties.Take(8).Select(property => property.Definition.Property.Name));

            return (
                "an entity of class " + decoder.ClassName(culprit.ClassId) + " re-encodes differently",
                Invariant($"entity {culprit.EntityIndex} class {decoder.ClassName(culprit.ClassId)} ({culprit.ClassId}) {culprit.UpdateType}, ") +
                Invariant($"{culprit.Properties.Count} properties: {properties}; first difference at bit {difference}"));
        }

        return ("the removal list re-encodes differently", Invariant($"bit {difference}, after every entity matched"));
    }

    private void Trace()
    {
        if (!Requires("trace", _read, "the container did not read"))
        {
            return;
        }

        using TraceScanner scanner = new();
        DemoTraceWriter.Write(scanner, _name, _header, _commands, null, new DemoTraceOptions { IncludeEntities = true });
        scanner.Flush();

        _row.Set("trace_lines", scanner.Lines);
        _row.Set("trace_chars", scanner.Characters);
        _row.Set("trace_packets", scanner.PacketBlocks);
        _row.Set("trace_stopped", scanner.Stopped);
        _row.Set("trace_undecoded", scanner.Undecoded);
        _row.Set("trace_anonymous", scanner.Anonymous);

        if (scanner.PacketBlocks == 0)
        {
            _row.Fail("trace", "no dem_packet block in the trace", "the trace wrote no `block dem_packet`", string.Empty);
        }
        else if (scanner.First is { } problem)
        {
            _row.Fail("trace", problem.Shape, problem.Line, problem.Block);
        }
        else
        {
            _row.Pass("trace", Invariant($"{scanner.PacketBlocks} dem_packet blocks, {scanner.Lines} lines"));
        }
    }

    /// <summary>Decompiles to assembly and compiles it back, through a file, as `-a -o` then `-c` do.</summary>
    /// <remarks>
    /// **Through a file, and that is both the CLI's route and the memory bound.** The corpus test holds the text
    /// as one string, which runs to gigabytes on a large demo; the CLI streams it to disk and back. The encoding is
    /// the CLI's too — `new StreamWriter(path)`'s UTF-8, which throws on an unencodable character rather than
    /// quietly writing a replacement.
    /// </remarks>
    private void Assembly()
    {
        if (!Requires("assembly", _read, "the container did not read"))
        {
            return;
        }

        if (AssemblyMultiple * _bytes.LongLength > _options.BudgetBytes)
        {
            _row.Skip("assembly", "budget", Invariant(
                $"the round trip holds about {AssemblyMultiple}x the demo, {AssemblyMultiple * _bytes.LongLength / Megabyte:N0} MB, over the {_options.BudgetBytes / Megabyte:N0} MB budget"));
            return;
        }

        string text = Path.Combine(_options.TempDirectory, Invariant($"decode-census-{Environment.ProcessId}-{_row["sha256"][..12]}.dasm"));

        try
        {
            using (StreamWriter writer = new(text, append: false, CliEncoding))
            {
                DemoAssembly.Write(writer, _header, _commands, _tail);
            }

            _row.Set("assembly_bytes", new FileInfo(text).Length);

            DemoHeader compiledHeader;
            IReadOnlyList<DemoCommand> compiled;
            ReadOnlyMemory<byte> compiledTail;

            using (StreamReader reader = new(text))
            {
                (compiledHeader, compiled, compiledTail) = DemoAssembly.Parse(reader);
            }

            if (compiled.Count != _commands.Count)
            {
                _row.Fail("assembly", "the text compiles to a different number of commands",
                    Invariant($"{compiled.Count} commands compiled from {_commands.Count}"), string.Empty);
                return;
            }

            Compare(DemoWriter.Write(compiledHeader, compiled, compiledTail));
        }
        finally
        {
            File.Delete(text);
        }
    }

    /// <summary>Compares the rebuilt demo with the file, byte for byte, and names what a difference is.</summary>
    private void Compare(byte[] rebuilt)
    {
        _row.Set("rebuilt_bytes", rebuilt.LongLength);

        int common = _bytes.AsSpan().CommonPrefixLength(rebuilt);
        _row.Set("first_difference", common == _bytes.Length && common == rebuilt.Length ? -1 : common);

        if (common == _bytes.Length && common == rebuilt.Length)
        {
            _row.Pass("assembly", Invariant($"{rebuilt.LongLength} bytes rebuilt byte for byte"));
        }
        else if (common < Math.Min(_bytes.Length, rebuilt.Length))
        {
            _row.Fail("assembly", "the first differing byte is in " + Locate(common, withOffset: false),
                Invariant($"rebuilt {rebuilt.LongLength} bytes from {_bytes.LongLength}; first difference at byte {common}"),
                Locate(common, withOffset: true));
        }
        else if (rebuilt.Length > _bytes.Length)
        {
            _row.Fail("assembly", "rebuilt longer than the demo",
                Invariant($"rebuilt {rebuilt.LongLength} bytes from {_bytes.LongLength}, every one of the demo's matching"),
                Invariant($"byte {_bytes.Length}"));
        }
        else
        {
            // Every rebuilt byte matches and the file is longer: the tail is whatever the reader did not return as a
            // command — a final command cut off mid-write when the reader said so, else what follows dem_stop.
            long tail = _bytes.LongLength - rebuilt.LongLength;
            string shape = "tail not carried: bytes after the last command the reader returned";

            if (_truncated is not null)
            {
                shape = CutTailShape;
            }
            else if (_commands[^1].Type == DemoCommandType.Stop)
            {
                shape = "tail not carried: bytes after dem_stop";
            }

            _row.Fail("assembly", shape,
                Invariant($"every byte rebuilt matches, and the last {tail} of {_bytes.LongLength} are not rebuilt") +
                (_truncated is null ? string.Empty : "; the reader: " + _truncated),
                Invariant($"byte {rebuilt.LongLength}"));
        }
    }

    /// <summary>Which header field or which command a file offset falls in.</summary>
    private string Locate(long offset, bool withOffset)
    {
        if (offset < DemoHeader.SizeBytes)
        {
            string field = offset switch
            {
                < 8 => "the header stamp",
                < 16 => "the header protocols",
                < 276 => "the header server name",
                < 536 => "the header client name",
                < 796 => "the header map name",
                < 1056 => "the header game directory",
                _ => "the header playback fields",
            };

            return withOffset ? Invariant($"{field}, byte {offset}") : field;
        }

        int found = -1;

        for (int index = 0; index < _commands.Count; index++)
        {
            if (TryStart(_commands[index], out long start) && start <= offset)
            {
                found = index;
            }
        }

        if (found < 0)
        {
            return withOffset ? Invariant($"byte {offset}") : "a command the reader returned no bytes for";
        }

        DemoCommand command = _commands[found];
        string kind = "a " + command.Type + " command";

        return withOffset ? Invariant($"{kind} at tick {command.Tick}, command {found}, byte {offset}") : kind;
    }

    /// <summary>Where a command starts in the file: five bytes (type and tick) before its prologue.</summary>
    /// <remarks>
    /// The reader slices every command's prologue out of the file — empty where the command has none — so its offset
    /// is exact; only dem_stop, which the reader builds without one, names no offset of its own.
    /// </remarks>
    private bool TryStart(DemoCommand command, out long start)
    {
        if (MemoryMarshal.TryGetArray(command.Prologue, out ArraySegment<byte> segment) && ReferenceEquals(segment.Array, _bytes))
        {
            start = segment.Offset - 5;
            return true;
        }

        start = 0;
        return false;
    }

    /// <summary>Builds the timeline the viewer builds, and measures what it holds.</summary>
    /// <remarks>
    /// **Bounded before it starts, because a timeline is 40–84 times its demo** (B439): the census skips, as a budget
    /// skip, any demo whose 84x would pass the run's budget, and measures the rest as `timeline-heap` does — the live
    /// heap after a full compacting collection, before and after — so the ratio is re-measured across the pool.
    /// </remarks>
    private void Timeline()
    {
        if (!Requires("timeline", _read, "the container did not read"))
        {
            return;
        }

        long predicted = TimelineMultiple * _bytes.LongLength;

        if (predicted > _options.BudgetBytes)
        {
            _row.Skip("timeline", "budget", Invariant(
                $"a timeline holds up to 84x its demo (B439): {predicted / Megabyte:N0} MB predicted, over the {_options.BudgetBytes / Megabyte:N0} MB budget"));
            return;
        }

        long before = LiveHeap();
        DemoTimeline timeline = DemoTimeline.Build(_bytes);
        long held = LiveHeap() - before;

        _row.Set("timeline_frames", timeline.Frames.Count);
        _row.Set("timeline_props", timeline.Props.Count);
        _row.Set("timeline_players", timeline.PlayerTracks.Count);
        _row.Set("timeline_events", timeline.GameEvents.Count);
        _row.Set("timeline_mb", (double)held / Megabyte);
        _row.Set("timeline_ratio", (double)held / _bytes.LongLength);

        if (timeline.Frames.Count == 0)
        {
            _row.Fail("timeline", "the timeline has no frames", "DemoTimeline.Build returned no frames", string.Empty);
        }
        else
        {
            _row.Pass("timeline", Invariant($"{timeline.Frames.Count} frames, {timeline.PlayerTracks.Count} player tracks"));
        }

        GC.KeepAlive(timeline);
    }

    /// <summary>The live managed heap: everything unreachable collected and compacted first, as `timeline-heap` reads it.</summary>
    public static long LiveHeap()
    {
        // A measurement of what is LIVE needs a full blocking collection; this is a probe, not a hot path.
#pragma warning disable S1215
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
        return GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes;
    }

    private void Voice()
    {
        if (!Requires("voice", _read, "the container did not read"))
        {
            return;
        }

        (string? codec, List<byte[]> packets) = WalkVoice();
        _row["voice_codec"] = codec ?? string.Empty;
        _row.Set("voice_packets", packets.Count);

        if (packets.Count == 0)
        {
            _row.Skip("voice", "none", "no svc_VoiceData with a body" + (codec is null ? string.Empty : ", codec " + codec));
            return;
        }

        VoiceTally tally = new();

        switch (codec)
        {
            case "vaudio_speex":
                Speex(packets, tally);
                break;

            case "vaudio_celt":
                Celt(packets, tally);
                break;

            case "steam":
                Steam(packets, tally);
                break;

            default:
                tally.Note(
                    "no decoder for voice codec '" + (codec ?? "none declared") + "'",
                    Invariant($"{packets.Count} voice packets, codec '{codec ?? "none declared"}'"),
                    string.Empty);
                break;
        }

        tally.Report(_row, codec);
    }

    /// <summary>The session's codec and every voice body, walked as `Corpus.WalkVoice` walks them.</summary>
    private (string? Codec, List<byte[]> Packets) WalkVoice()
    {
        NetDecodeState state = new() { NetworkProtocol = Protocol };
        string? codec = null;
        List<byte[]> packets = [];

        for (_at = 0; _at < _commands.Count; _at++)
        {
            if (_commands[_at].Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(_commands[_at].Payload.Span, state).Messages)
            {
                if (message is VoiceInitMessage init)
                {
                    codec = init.Codec;
                }
                else if (message is VoiceDataMessage { BodyBits: > 0 } voice)
                {
                    packets.Add(voice.Body.ToArray());
                }
            }
        }

        return (codec, packets);
    }

    /// <summary>
    /// Speex: one decoder per demo, fixed 28-byte frames, as `EverySpeexFrame_DecodesToPcm` does — and, as it does,
    /// a packet whose tail is its CRC32 is Steam Voice (B441) and goes to SILK, one decoder per steamID.
    /// </summary>
    private static void Speex(List<byte[]> packets, VoiceTally tally)
    {
        if (!SpeexVoiceDecoder.IsAvailable)
        {
            tally.Unavailable = "speex: " + WhyUnavailable(() => new SpeexVoiceDecoder());
            return;
        }

        using SpeexVoiceDecoder decoder = new();
        Dictionary<ulong, SilkVoiceDecoder> silk = [];

        try
        {
            foreach (byte[] body in packets)
            {
                if (!SteamVoicePayload.TryDecode(body, out VoicePacket? steam))
                {
                    SpeexPacket(decoder, body, tally);
                    continue;
                }

                if (!SilkVoiceDecoder.IsAvailable)
                {
                    tally.Unavailable = "silk: " + WhyUnavailable(() => new SilkVoiceDecoder());
                    return;
                }

                tally.Framed++;

                if (!silk.TryGetValue(steam.SteamId, out SilkVoiceDecoder? speaker))
                {
                    speaker = new SilkVoiceDecoder();
                    silk[steam.SteamId] = speaker;
                }

                foreach (VoiceChunk chunk in steam.Chunks)
                {
                    tally.Decode(() => speaker.Decode(chunk.Data.Span), "SILK");
                }
            }

            tally.Speakers = silk.Count;
        }
        finally
        {
            foreach (SilkVoiceDecoder speaker in silk.Values)
            {
                speaker.Dispose();
            }
        }
    }

    /// <summary>One raw Speex packet: whole 28-byte frames through the demo's decoder.</summary>
    private static void SpeexPacket(SpeexVoiceDecoder decoder, byte[] body, VoiceTally tally)
    {
        if (body.Length % SpeexFrameBytes != 0)
        {
            tally.Bad++;
            tally.Note(
                Invariant($"a Speex payload is not a whole number of {SpeexFrameBytes}-byte frames"),
                Invariant($"a {body.Length}-byte Speex payload is not a whole number of {SpeexFrameBytes}-byte frames"),
                string.Empty);
            return;
        }

        for (int at = 0; at < body.Length; at += SpeexFrameBytes)
        {
            tally.Decode(() => decoder.Decode(body.AsSpan(at, SpeexFrameBytes)), "Speex");
        }
    }

    /// <summary>CELT: a fresh decoder per packet, fixed 64-byte frames, as `EveryCeltFrame_DecodesToPcm` does.</summary>
    private static void Celt(List<byte[]> packets, VoiceTally tally)
    {
        if (!CeltVoiceDecoder.IsAvailable)
        {
            tally.Unavailable = "celt: " + WhyUnavailable(() => new CeltVoiceDecoder());
            return;
        }

        foreach (byte[] body in packets)
        {
            if (body.Length % CeltFrameBytes != 0)
            {
                tally.Bad++;
                tally.Note(
                    Invariant($"a CELT payload is not a whole number of {CeltFrameBytes}-byte frames"),
                    Invariant($"a {body.Length}-byte CELT payload is not a whole number of {CeltFrameBytes}-byte frames"),
                    string.Empty);
                continue;
            }

            using CeltVoiceDecoder decoder = new();

            for (int at = 0; at < body.Length; at += CeltFrameBytes)
            {
                tally.Decode(() => decoder.Decode(body.AsSpan(at, CeltFrameBytes)), "CELT");
            }
        }
    }

    /// <summary>Steam's Opus: one decoder per speaker's steamID, as `EveryChunk_DecodesToNonSilentPcm` does.</summary>
    private static void Steam(List<byte[]> packets, VoiceTally tally)
    {
        if (WhyUnavailable(() => new OpusVoiceDecoder()) is { } why)
        {
            tally.Unavailable = "opus: " + why;
            return;
        }

        Dictionary<ulong, OpusVoiceDecoder> decoders = [];

        try
        {
            foreach (byte[] body in packets)
            {
                VoicePacket packet;

                try
                {
                    packet = SteamVoicePayload.Decode(body);
                }
                catch (InvalidDataException error)
                {
                    tally.Bad++;
                    tally.Note("a steam voice payload does not frame: " + error.Message, error.Message, string.Empty);
                    continue;
                }

                tally.Framed++;

                if (!decoders.TryGetValue(packet.SteamId, out OpusVoiceDecoder? decoder))
                {
                    decoder = new OpusVoiceDecoder();
                    decoders[packet.SteamId] = decoder;
                }

                foreach (VoiceChunk chunk in packet.Chunks)
                {
                    tally.Decode(() => decoder.Decode(chunk.Data.Span), "Opus");
                }
            }

            tally.Speakers = decoders.Count;
        }
        finally
        {
            foreach (OpusVoiceDecoder decoder in decoders.Values)
            {
                decoder.Dispose();
            }
        }
    }

    /// <summary>
    /// Why a voice decoder cannot be made on this machine, in the wrapper's own words — or null when it can.
    /// </summary>
    /// <remarks>
    /// The Speex and CELT libraries are not committed; the probe's csproj copies them in from `NativeAudioDirectory`.
    /// A skip says which library and why, because "unavailable" alone hides a missing file and a failed mode alike.
    /// </remarks>
    private static string? WhyUnavailable(Func<IDisposable> create)
    {
        try
        {
            using IDisposable probe = create();
            return null;
        }
        catch (Exception error) when (error is DllNotFoundException or InvalidOperationException or TypeInitializationException)
        {
            return error.GetType().Name + ": " + error.Message +
                (error.InnerException is { } inner ? " (" + inner.GetType().Name + ": " + inner.Message + ")" : string.Empty);
        }
    }

    /// <summary>A bit's position in the file, for a failure report.</summary>
    private string Where(DemoCommand command, int bit)
    {
        string offset = MemoryMarshal.TryGetArray(command.Payload, out ArraySegment<byte> segment) && ReferenceEquals(segment.Array, _bytes)
            ? Invariant($", payload at byte {segment.Offset}")
            : string.Empty;

        return Invariant($"tick {command.Tick}, command {_at}{offset}, bit {bit}");
    }

    /// <summary>The first of <paramref name="bits"/> bits that differ, or -1 when every one matches.</summary>
    private static int FirstDifferentBit(ReadOnlySpan<byte> wire, int wireStart, byte[] written, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int at = wireStart + bit;

            if (bit >> 3 >= written.Length || ((wire[at >> 3] >> (at & 7)) & 1) != ((written[bit >> 3] >> (bit & 7)) & 1))
            {
                return bit;
            }
        }

        return -1;
    }

    /// <summary>Counts for the message stage, and its first failure in stream order.</summary>
    private sealed class MessageTally : FirstFailure
    {
        public long Messages { get; set; }

        public long Exact { get; set; }

        public long Bits { get; set; }

        public long Stops { get; set; }

        public long Mismatches { get; set; }

        public long Events { get; set; }

        public Dictionary<string, long> Unwritable { get; } = new(StringComparer.Ordinal);

        public void Report(CensusRow row)
        {
            row.Set("messages", Messages);
            row.Set("messages_exact", Exact);
            row.Set("message_bits", Bits);
            row.Set("unwritable_bits", Unwritable.Values.Sum());
            row.Set("reader_stops", Stops);
            row.Set("mismatches", Mismatches);
            row.Set("game_events", Events);

            string counts = Invariant($"{Messages} messages, {Exact} re-encoded bit for bit, {Events} game events");

            if (First is { } first)
            {
                string unwritable = string.Join(", ", Unwritable.OrderByDescending(entry => entry.Value)
                    .Select(entry => Invariant($"{entry.Key} {entry.Value} bits")));

                row.Fail("messages", first.Shape,
                    first.Detail + Invariant($" ({counts}; {Mismatches} mismatches, {Stops} reader stops") +
                    (unwritable.Length == 0 ? ")" : "; not writable: " + unwritable + ")"),
                    first.Where);
            }
            else if (Messages == 0 || Exact == 0)
            {
                row.Fail("messages", "no message was read or re-encoded", counts, string.Empty);
            }
            else
            {
                row.Pass("messages", counts);
            }
        }
    }

    /// <summary>Counts for the entity round trip.</summary>
    private sealed class EntityTally : FirstFailure
    {
        public long Snapshots { get; set; }

        public long Exact { get; set; }

        public long Entities { get; set; }

        public long Throws { get; set; }

        public long Mismatches { get; set; }

        public long SlackBits { get; set; }

        public void Report(CensusRow row)
        {
            row.Set("snapshots", Snapshots);
            row.Set("snapshots_exact", Exact);
            row.Set("entities", Entities);
            row.Set("snapshot_throws", Throws);
            row.Set("slack_bits", SlackBits);

            string counts = Invariant($"{Exact} of {Snapshots} snapshots re-encode exactly, {Entities} entity updates");

            if (First is { } first)
            {
                row.Fail("entities", first.Shape,
                    first.Detail + Invariant($" ({counts}; {Mismatches} mismatches, {Throws} throw)"), first.Where);
            }
            else if (Snapshots == 0)
            {
                // Both B440 demos had no snapshot, and the corpus test skips such a demo; a TF2 demo always has one.
                row.Fail("entities", "no entity snapshot decoded", counts, string.Empty);
            }
            else
            {
                row.Pass("entities", counts);
            }
        }
    }

    /// <summary>Counts for the voice stage.</summary>
    private sealed class VoiceTally : FirstFailure
    {
        public long Frames { get; private set; }

        public long Silent { get; private set; }

        public long Bad { get; set; }

        /// <summary>Steam packets whose framing read exactly — the stage's work even when none carries audio.</summary>
        public long Framed { get; set; }

        public long Errors { get; private set; }

        public int Speakers { get; set; }

        public string? Unavailable { get; set; }

        /// <summary>Decodes one frame, counting it and whether it is silent; a decoder's rejection is counted, not thrown.</summary>
        public void Decode(Func<short[]> decode, string codec)
        {
            short[] pcm;

            try
            {
                pcm = decode();
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                Errors++;
                Note(codec + " rejects a frame: " + error.Message, error.Message, string.Empty);
                return;
            }

            Frames++;
            Silent += pcm.AsSpan().ContainsAnyExcept((short)0) ? 0 : 1;
        }

        public void Report(CensusRow row, string? codec)
        {
            row.Set("voice_frames", Frames);
            row.Set("voice_silent", Silent);
            row.Set("voice_speakers", Speakers);
            row.Set("voice_bad", Bad + Errors);

            string counts = Invariant($"{Frames} {codec} frames decoded, {Silent} silent");

            if (Unavailable is not null)
            {
                row.Skip("voice", "unavailable", Unavailable);
            }
            else if (First is { } first)
            {
                row.Fail("voice", first.Shape, first.Detail + Invariant($" ({counts}; {Bad} packets unframed, {Errors} frames rejected)"), first.Where);
            }
            else if (Frames == 0 && Framed > 0)
            {
                // A steam packet can frame exactly and carry no audio chunk — the 18-byte packets that bracket a talk
                // burst (`SteamVoicePayload`'s silence sub-packet). Every one read to its last byte is the stage's work.
                row.Pass("voice", Invariant($"{Framed} steam packets framed exactly, none carrying audio"));
            }
            else if (Frames == 0)
            {
                row.Fail("voice", "no voice frame reached the decoder", counts, string.Empty);
            }
            else if (Frames >= SilenceSample && Silent * 2 >= Frames)
            {
                // The corpus tests' own reading: most frames silent means frames fed in the wrong shape or order.
                row.Fail("voice", "most frames decode to silence", counts, string.Empty);
            }
            else
            {
                row.Pass("voice", counts);
            }
        }
    }

    /// <summary>A stage's first failure in stream order: what it is, the detail, and where.</summary>
    private abstract class FirstFailure
    {
        public (string Shape, string Detail, string Where)? First { get; private set; }

        public bool HasFirst => First is not null;

        public void Note(string shape, string detail, string where) => First ??= (shape, detail, where);
    }
}

/// <summary>
/// Reads a trace as <see cref="DemoTraceWriter"/> writes it, a line at a time, keeping counts and not the text.
/// </summary>
/// <remarks>
/// **The whole `-t -e` trace of a demo runs to gigabytes**, so it is never held: each line is looked at and
/// dropped. What it looks for is what the corpus tests look for — `stopped after` (the reader could not finish a
/// packet, `EveryDemo_TracesWithoutAnUnreadableBlock`), an anonymous `svc_x bits N` (a message with no decoder,
/// `Trace_EveryMessage_IsNamed`) — plus `undecoded`, which the trace writes in place of a sound, temp entity or
/// entity snapshot that threw, and which the timeline swallows without a count.
///
/// A marker counts only at the START of a line, never inside one, because player text is quoted inside lines and a
/// chat message saying "stopped after" is not a stopped reader.
/// </remarks>
internal sealed class TraceScanner : TextWriter
{
    private char[] _line = new char[512];
    private int _length;
    private string _block = string.Empty;
    private string _message = string.Empty;

    /// <inheritdoc/>
    public override Encoding Encoding => Encoding.UTF8;

    public long Lines { get; private set; }

    public long Characters { get; private set; }

    public long PacketBlocks { get; private set; }

    public long Stopped { get; private set; }

    public long Undecoded { get; private set; }

    public long Anonymous { get; private set; }

    /// <summary>The first line that is a failure: its shape, the line, and the block it was in.</summary>
    public (string Shape, string Line, string Block)? First { get; private set; }

    /// <inheritdoc/>
    public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));

    /// <inheritdoc/>
    public override void Write(string? value) => Write(value.AsSpan());

    /// <inheritdoc/>
    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<char> buffer)
    {
        Characters += buffer.Length;

        while (!buffer.IsEmpty)
        {
            int newline = buffer.IndexOf('\n');
            Append(newline < 0 ? buffer : buffer[..newline]);

            if (newline < 0)
            {
                return;
            }

            EndLine();
            buffer = buffer[(newline + 1)..];
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        if (_length > 0)
        {
            EndLine();
        }

        base.Flush();
    }

    private void Append(ReadOnlySpan<char> text)
    {
        if (_length + text.Length > _line.Length)
        {
            Array.Resize(ref _line, Math.Max(_line.Length * 2, _length + text.Length));
        }

        text.CopyTo(_line.AsSpan(_length));
        _length += text.Length;
    }

    private void EndLine()
    {
        Scan(_line.AsSpan(0, _length));
        _length = 0;
        Lines++;
    }

    private void Scan(ReadOnlySpan<char> line)
    {
        if (line.StartsWith("block ", StringComparison.Ordinal))
        {
            PacketBlocks += line.StartsWith("block dem_packet ", StringComparison.Ordinal) ? 1 : 0;

            // Kept only while nothing has failed: the block a failure sits in is all it is needed for.
            _block = First is null ? line.ToString() : _block;
            return;
        }

        ReadOnlySpan<char> text = line.TrimStart(' ');

        if (line.StartsWith("    svc_", StringComparison.Ordinal) && First is null)
        {
            int space = text.IndexOf(' ');
            _message = (space < 0 ? text : text[..space]).ToString();
        }

        if (text.StartsWith("stopped after ", StringComparison.Ordinal))
        {
            Stopped++;
            Note("stopped: " + ReasonOf(text), line);
        }
        else if (text.StartsWith("undecoded ", StringComparison.Ordinal))
        {
            Undecoded++;
            Note("undecoded in " + _message + ": " + text["undecoded ".Length..].ToString(), line);
        }
        else if (IsAnonymous(text))
        {
            Anonymous++;
            Note("anonymous " + text.ToString(), line);
        }
    }

    private void Note(string shape, ReadOnlySpan<char> line) =>
        First ??= (shape, line.Trim().ToString(), _block.TrimEnd(' ', '{'));

    /// <summary>The reason after "stopped after N bits:", which is what two stops share.</summary>
    private static string ReasonOf(ReadOnlySpan<char> text)
    {
        int colon = text.IndexOf(':');
        return (colon < 0 ? text : text[(colon + 1)..]).Trim().ToString();
    }

    /// <summary>A bare `svc_x bits N`, which is how a message with no decoder renders (the corpus test's reading).</summary>
    private static bool IsAnonymous(ReadOnlySpan<char> text)
    {
        ReadOnlySpan<char> line = text.TrimEnd().TrimEnd(';');

        if (!line.StartsWith("svc_", StringComparison.Ordinal) && !line.StartsWith("net_", StringComparison.Ordinal))
        {
            return false;
        }

        int first = line.IndexOf(' ');

        if (first < 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = line[(first + 1)..];
        int second = rest.IndexOf(' ');

        return second >= 0 &&
            rest[..second].SequenceEqual("bits") &&
            rest[(second + 1)..].IndexOf(' ') < 0 &&
            int.TryParse(rest[(second + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
