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
        ReadOnlyMemory<byte> data, Action<string>? onTruncated)
    {
        int position = 0;
        DecodeProgress progress = new("the demo command stream", -1);

        while (position < data.Length)
        {
            progress.Advanced(position);

            DemoCommandType type = (DemoCommandType)data.Span[position];
            if (!Enum.IsDefined(type))
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unrecognised demo command {data.Span[position]} at offset {position}."));
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
                onTruncated?.Invoke(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The demo ends inside a {type} command header at offset {position}: " +
                    $"{CommandHeaderBytes} bytes are needed and {data.Length - position} remain."));
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

            try
            {
                payload = ReadPayload(data, type, ref position, out prologueLength);
            }
            catch (EndOfStreamException truncated)
            {
                // The file stops inside this command's payload. Everything before it decoded, and
                // that is what the caller gets.
                onTruncated?.Invoke(truncated.Message);
                yield break;
            }

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
        switch (type)
        {
            case DemoCommandType.SyncTick:
                prologueLength = 0;
                return ReadOnlyMemory<byte>.Empty;

            case DemoCommandType.Signon:
            case DemoCommandType.Packet:
                prologueLength = CommandInfoBytes + SequenceNumberBytes;
                Skip(data, ref position, prologueLength, type);
                return ReadLengthPrefixed(data, ref position, type);

            case DemoCommandType.UserCmd:
                // A point-of-view demo only field: the outgoing command sequence number.
                prologueLength = Int32Bytes;
                Skip(data, ref position, prologueLength, type);
                return ReadLengthPrefixed(data, ref position, type);

            default:
                prologueLength = 0;
                return ReadLengthPrefixed(data, ref position, type);
        }
    }

    private static void Skip(
        ReadOnlyMemory<byte> data,
        ref int position,
        int count,
        DemoCommandType type)
    {
        if (data.Length - position < count)
        {
            throw new EndOfStreamException(string.Create(
                CultureInfo.InvariantCulture,
                $"A {type} command needs {count} more bytes at offset {position}, but only " +
                $"{data.Length - position} remain."));
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
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A {type} command at offset {position - Int32Bytes} declares a negative " +
                $"payload length of {length}."));
        }

        if (data.Length - position < length)
        {
            throw new EndOfStreamException(string.Create(
                CultureInfo.InvariantCulture,
                $"A {type} command declares {length} payload bytes at offset {position}, but " +
                $"only {data.Length - position} remain."));
        }

        ReadOnlyMemory<byte> payload = data.Slice(position, length);
        position += length;
        return payload;
    }
}
