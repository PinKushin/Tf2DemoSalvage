using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// Walks the command stream that follows a demo's header.
/// </summary>
/// <remarks>
/// The stream is a flat sequence of <c>[command header][payload]</c> with no index and no
/// back-pointers, so it is strictly forward-parsed — there is no way to seek to tick N without
/// walking. CONFIRMED against three corpus demos; see <c>docs/SPEC.md</c>.
/// </remarks>
public static class DemoCommandReader
{
    /// <summary>Command header size at demo protocol 3: one type byte plus an int32 tick.</summary>
    private const int CommandHeaderBytes = 5;

    /// <summary>
    /// <c>democmdinfo_t</c> at demo protocol 3: one <c>Split_t</c> of an int32 flags field plus
    /// six <c>Vector</c>s of three floats each.
    /// </summary>
    private const int CommandInfoBytes = 76;

    /// <summary>Two int32 sequence numbers follow <c>democmdinfo_t</c>.</summary>
    private const int SequenceNumberBytes = 8;

    private const int Int32Bytes = 4;

    /// <summary>
    /// Enumerates commands until <see cref="DemoCommandType.Stop"/> or the end of the buffer.
    /// </summary>
    /// <param name="data">The demo's bytes from the end of the header onward.</param>
    /// <returns>A lazy sequence of commands.</returns>
    /// <exception cref="InvalidDataException">
    /// An unrecognised command byte, or a negative payload length.
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// The buffer ends part-way through a command header or payload. <em>Except</em> for
    /// <see cref="DemoCommandType.Stop"/>, which is allowed to be short — see below.
    /// </exception>
    public static IEnumerable<DemoCommand> Read(ReadOnlyMemory<byte> data) =>
        Read(data, null);

    /// <summary>Reads the command stream, reporting a truncated tail rather than throwing.</summary>
    /// <param name="data">The demo after its header.</param>
    /// <param name="onTruncated">
    /// Called with an explanation if the file stops in the middle of a command. Enumeration then
    /// ends normally.
    /// </param>
    /// <returns>Every command that is completely present.</returns>
    /// <remarks>
    /// **A truncated demo is the normal case, not a corrupt one, and this is measured.** Of 370
    /// real competitive demos from an ESEA archive, 159 - forty-three percent - end in the middle
    /// of a command. Every one of them stops within four kilobytes of the end of the file, and
    /// none fails anywhere else: the median demo is 99.995% complete, so throwing meant discarding
    /// a twenty megabyte recording over its final two hundred bytes.
    ///
    /// That is what a match ending does. The server stops writing mid-packet when the map changes
    /// or the process goes away, and nothing goes back to tidy up the tail.
    ///
    /// So the tail ends the walk and says so, rather than failing the read. A caller that wants to
    /// know passes <paramref name="onTruncated"/>; a caller that does not gets every command that
    /// was actually there. Salvaging a file the game itself refuses to play is the entire point of
    /// this project, and a file the game wrote badly is the easiest case of it.
    ///
    /// **A negative length is still fatal**, because that is not a short file - it is a value no
    /// writer produces, and continuing past it would rewind the cursor.
    /// </remarks>
    public static IEnumerable<DemoCommand> Read(
        ReadOnlyMemory<byte> data, Action<string>? onTruncated) =>
        ReadCore(data, onTruncated is null ? null : (_, reason) => onTruncated(reason));

    /// <summary>Reads the command stream off a stream, one command at a time (B449).</summary>
    /// <param name="stream">Positioned at the end of the demo's header.</param>
    /// <param name="onTruncated">
    /// Called if the file stops inside a command, with the bytes from that command's start to the
    /// end of the stream — the same tail <see cref="ReadWhole"/> returns — and the same explanation.
    /// </param>
    /// <returns>A lazy sequence of commands, each in arrays of its own.</returns>
    /// <remarks>
    /// **The engine's shape.** <c>CDemoFile::ReadCmdHeader</c> and <c>ReadRawData</c> read one command
    /// off a file handle, so the engine holds a command, never the file. The array overloads hold the
    /// whole demo, which a 2 GB idle-server recording outgrew under a 6 GiB heap. Here the stream is
    /// read to the last byte of the command being yielded and no further, and nothing from an
    /// earlier command is kept. Same commands, tail, reports and exceptions as the array walk.
    /// </remarks>
    public static IEnumerable<DemoCommand> Read(
        Stream stream, Action<ReadOnlyMemory<byte>, string>? onTruncated = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadStream(stream, onTruncated);
    }

    /// <summary>Reads every whole command, and keeps the bytes after the last one (B448).</summary>
    /// <param name="data">The demo after its header.</param>
    /// <param name="onTruncated">Called with the explanation if the file stops inside a command.</param>
    /// <returns>
    /// The commands, and the bytes from the start of the command the file stops inside to its end
    /// — empty for a demo that ends on a whole command.
    /// </returns>
    /// <remarks>
    /// **Preserved, never interpreted.** The engine stops at a short read too
    /// (<c>CDemoFile::ReadRawData</c> fails), so nothing here decodes the tail; it exists so the
    /// assembly can write a cut file back to every one of its bytes (D200). The offset is the one the
    /// walk was at when it stopped, carried out rather than recomputed from the commands.
    /// </remarks>
    public static (IReadOnlyList<DemoCommand> Commands, ReadOnlyMemory<byte> Tail) ReadWhole(
        ReadOnlyMemory<byte> data, Action<string>? onTruncated = null)
    {
        int tailStart = data.Length;
        List<DemoCommand> commands = [.. ReadCore(data, (offset, reason) =>
        {
            tailStart = offset;
            onTruncated?.Invoke(reason);
        })];

        return (commands, data[tailStart..]);
    }

    private static IEnumerable<DemoCommand> ReadStream(
        Stream stream, Action<ReadOnlyMemory<byte>, string>? onTruncated)
    {
        // The command header, the largest prologue and the length: everything before a payload.
        byte[] head = new byte[CommandHeaderBytes + CommandInfoBytes + SequenceNumberBytes + Int32Bytes];
        long position = 0;

        while (true)
        {
            int first = stream.ReadByte();
            if (first < 0)
            {
                yield break;
            }

            DemoCommandType type = (DemoCommandType)first;
            if (!Enum.IsDefined(type))
            {
                throw new InvalidDataException(Unrecognised(first, position));
            }

            head[0] = (byte)first;
            int tickBytes = stream.ReadAtLeast(head.AsSpan(1, Int32Bytes), Int32Bytes, throwOnEndOfStream: false);

            if (type == DemoCommandType.Stop)
            {
                // The short-header accommodation the array walk makes, for the same reason.
                yield return new DemoCommand(
                    DemoCommandType.Stop, ReadPartialTick(head.AsSpan(1, tickBytes), 0), ReadOnlyMemory<byte>.Empty);
                yield break;
            }

            int held = 1 + tickBytes;
            if (tickBytes < Int32Bytes)
            {
                onTruncated?.Invoke(head.AsSpan(0, held).ToArray(), HeaderShort(type, position, held));
                yield break;
            }

            int tick = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(1));
            position += CommandHeaderBytes;

            if (type == DemoCommandType.SyncTick)
            {
                yield return new DemoCommand(type, tick, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
                continue;
            }

            int prologueLength = PrologueBytes(type);
            string? shortBy = Fill(stream, head, ref held, prologueLength, type, ref position)
                ?? Fill(stream, head, ref held, Int32Bytes, type, ref position);

            if (shortBy is not null)
            {
                onTruncated?.Invoke(head.AsSpan(0, held).ToArray(), shortBy);
                yield break;
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(held - Int32Bytes));
            if (length < 0)
            {
                throw new InvalidDataException(NegativeLength(type, position - Int32Bytes, length));
            }

            byte[] payload = ReadUpTo(stream, length);
            if (payload.Length < length)
            {
                byte[] tail = [.. head.AsSpan(0, held), .. payload];
                onTruncated?.Invoke(tail, PayloadShort(type, length, position, payload.Length));
                yield break;
            }

            position += length;
            byte[] prologue = head.AsSpan(CommandHeaderBytes, prologueLength).ToArray();
            ViewInfo? view = type is DemoCommandType.Signon or DemoCommandType.Packet ? ViewInfo.Read(prologue) : null;

            yield return new DemoCommand(type, tick, payload, prologue, view);
        }
    }

    /// <summary>Reads <paramref name="count"/> more bytes into <paramref name="head"/>, or explains the short read.</summary>
    private static string? Fill(
        Stream stream, byte[] head, ref int held, int count, DemoCommandType type, ref long position)
    {
        int read = stream.ReadAtLeast(head.AsSpan(held, count), count, throwOnEndOfStream: false);
        string? shortBy = read < count ? NeedsMore(type, count, position, read) : null;
        held += read;
        position += read;
        return shortBy;
    }

    /// <summary>
    /// Up to <paramref name="length"/> bytes, never allocating more than the stream can supply — a
    /// corrupt length near <see cref="int.MaxValue"/> is a short read, not a 2 GB array.
    /// </summary>
    private static byte[] ReadUpTo(Stream stream, int length)
    {
        if (stream.CanSeek)
        {
            byte[] exact = new byte[(int)Math.Min(length, Math.Max(0, stream.Length - stream.Position))];
            stream.ReadExactly(exact);
            return exact;
        }

        // ponytail: doubling buffer for an unseekable stream; no caller passes one today.
        byte[] buffer = new byte[Math.Min(length, 1 << 16)];
        int filled = 0;

        while (filled < length)
        {
            if (filled == buffer.Length)
            {
                Array.Resize(ref buffer, (int)Math.Min(length, 2L * buffer.Length));
            }

            int read = stream.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        Array.Resize(ref buffer, filled);
        return buffer;
    }

    /// <summary>The bytes between a command's header and its length or payload.</summary>
    private static int PrologueBytes(DemoCommandType type) => type switch
    {
        DemoCommandType.Signon or DemoCommandType.Packet => CommandInfoBytes + SequenceNumberBytes,

        // A point-of-view demo only field: the outgoing command sequence number.
        DemoCommandType.UserCmd => Int32Bytes,
        _ => 0,
    };

    // Stryker disable all : the String mutator wraps an interpolated literal in a ternary that cannot
    // bind to string.Create's interpolated-string handler (CS1620), and Safe Mode then drops every
    // mutation in the method — B410. These are the reports both walks share, word for word.
    private static string Unrecognised(int value, long position) => string.Create(
        CultureInfo.InvariantCulture, $"Unrecognised demo command {value} at offset {position}.");

    private static string HeaderShort(DemoCommandType type, long position, long remain) => string.Create(
        CultureInfo.InvariantCulture,
        $"The demo ends inside a {type} command header at offset {position}: " +
        $"{CommandHeaderBytes} bytes are needed and {remain} remain.");

    private static string NeedsMore(DemoCommandType type, int count, long position, long remain) => string.Create(
        CultureInfo.InvariantCulture,
        $"A {type} command needs {count} more bytes at offset {position}, but only {remain} remain.");

    private static string NegativeLength(DemoCommandType type, long offset, int length) => string.Create(
        CultureInfo.InvariantCulture,
        $"A {type} command at offset {offset} declares a negative payload length of {length}.");

    private static string PayloadShort(DemoCommandType type, int length, long position, long remain) => string.Create(
        CultureInfo.InvariantCulture,
        $"A {type} command declares {length} payload bytes at offset {position}, but only {remain} remain.");

    // Stryker restore all

    /// <summary>The walk, reporting where the unfinished command began along with why it stopped.</summary>
    private static IEnumerable<DemoCommand> ReadCore(
        ReadOnlyMemory<byte> data, Action<int, string>? onTruncated)
    {
        int position = 0;
        DecodeProgress progress = new("the demo command stream", -1);

        while (position < data.Length)
        {
            progress.Advanced(position);
            int commandStart = position;

            DemoCommandType type = (DemoCommandType)data.Span[position];
            if (!Enum.IsDefined(type))
            {
                throw new InvalidDataException(Unrecognised(data.Span[position], position));
            }

            // dem_stop is where every TF2 demo runs out of bytes. The writer emits the command
            // and its tick, and the file ends one byte early - confirmed across three demos
            // from unrelated servers, in both point-of-view and SourceTV flavours. So the
            // terminator gets a short-header accommodation that no other command gets: demand
            // the full five bytes here and every valid TF2 demo is rejected.
            if (type == DemoCommandType.Stop)
            {
                yield return new DemoCommand(
                    DemoCommandType.Stop,
                    ReadPartialTick(data.Span, position + 1),
                    ReadOnlyMemory<byte>.Empty);
                yield break;
            }

            if (data.Length - position < CommandHeaderBytes)
            {
                onTruncated?.Invoke(commandStart, HeaderShort(type, position, data.Length - position));
                yield break;
            }

            int tick = BinaryPrimitives.ReadInt32LittleEndian(data.Span[(position + 1)..]);
            position += CommandHeaderBytes;

            // Read before the payload, because ReadPayload steps over it. This block was skipped
            // for the whole life of the project - it is the recording client's camera, and the
            // viewers cannot be built without it.
            ViewInfo? view = type is DemoCommandType.Signon or DemoCommandType.Packet
                ? ViewInfo.Read(data.Span[position..])
                : null;

            int prologueStart = position;
            ReadOnlyMemory<byte> payload;
            int prologueLength;

            // Stryker disable all : a mutant that empties the catch removes the 'yield break' that
            // C#'s definite-assignment analysis relies on, leaving 'payload' and 'prologueLength'
            // unassigned at their use below (CS0165), and Safe Mode then drops every mutation in
            // this method — B410.
            try
            {
                payload = ReadPayload(data, type, ref position, out prologueLength);
            }
            catch (EndOfStreamException truncated)
            {
                // The file stops inside this command's payload. Everything before it decoded, and
                // that is what the caller gets.
                onTruncated?.Invoke(commandStart, truncated.Message);
                yield break;
            }

            // Stryker restore all

            yield return new DemoCommand(
                type, tick, payload, data.Slice(prologueStart, prologueLength), view);
        }
    }

    /// <summary>
    /// Reads however many of the tick's four bytes are actually present, zero-extending the
    /// rest. The absent byte is always the most significant one, and always zero in practice
    /// because tick counts never approach 2^24.
    /// </summary>
    private static int ReadPartialTick(ReadOnlySpan<byte> data, int offset)
    {
        int tick = 0;
        int available = Math.Min(Int32Bytes, data.Length - offset);

        for (int i = 0; i < available; i++)
        {
            // Stryker disable once Assignment: each iteration writes a disjoint byte lane of a
            // zero-initialised accumulator, so |= and ^= are indistinguishable. Equivalent
            // mutant, same class as the one in BitReader.
            tick |= data[offset + i] << (i * 8);
        }

        return tick;
    }

    /// <summary>
    /// Reads a command's payload, reporting how many bytes preceded it.
    /// </summary>
    /// <remarks>
    /// The prologue length is an output rather than a discard so the caller can keep those bytes.
    /// They are not decoded here — two of them are sequence numbers and most of democmdinfo_t is
    /// a second split-screen view TF2 does not fill — but a demo cannot be written back
    /// byte-exactly from fields nobody kept.
    /// </remarks>
    private static ReadOnlyMemory<byte> ReadPayload(
        ReadOnlyMemory<byte> data,
        DemoCommandType type,
        ref int position,
        out int prologueLength)
    {
        prologueLength = PrologueBytes(type);

        if (type == DemoCommandType.SyncTick)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        Skip(data, ref position, prologueLength, type);
        return ReadLengthPrefixed(data, ref position, type);
    }

    private static void Skip(
        ReadOnlyMemory<byte> data,
        ref int position,
        int count,
        DemoCommandType type)
    {
        if (data.Length - position < count)
        {
            throw new EndOfStreamException(NeedsMore(type, count, position, data.Length - position));
        }

        position += count;
    }

    private static ReadOnlyMemory<byte> ReadLengthPrefixed(
        ReadOnlyMemory<byte> data,
        ref int position,
        DemoCommandType type)
    {
        Skip(data, ref position, Int32Bytes, type);
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.Span[(position - Int32Bytes)..]);

        if (length < 0)
        {
            // Left unchecked this would rewind the cursor and loop forever on a corrupt file.
            throw new InvalidDataException(NegativeLength(type, position - Int32Bytes, length));
        }

        if (data.Length - position < length)
        {
            throw new EndOfStreamException(PayloadShort(type, length, position, data.Length - position));
        }

        ReadOnlyMemory<byte> payload = data.Slice(position, length);
        position += length;
        return payload;
    }
}
