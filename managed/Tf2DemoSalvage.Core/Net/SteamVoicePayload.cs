using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One encoded audio frame from a voice packet.</summary>
/// <param name="Sequence">Order within the speaker's stream.</param>
/// <param name="Data">The codec frame, untouched.</param>
public readonly record struct VoiceChunk(int Sequence, ReadOnlyMemory<byte> Data);

/// <summary>
/// A decoded <c>steam</c>-codec voice packet: who spoke, at what rate, and their audio frames.
/// </summary>
/// <param name="SteamId">The speaker's Steam account.</param>
/// <param name="SampleRate">Sample rate the frames decode at.</param>
/// <param name="Chunks">Audio frames, in the order sent.</param>
/// <param name="IsTerminated">Whether the audio block ended with the <c>0xFFFF</c> sentinel.</param>
/// <param name="Tail">
/// CRC32 of the steamID and every sub-packet — everything preceding these four bytes.
/// </param>
public sealed record VoicePacket(
    ulong SteamId,
    int SampleRate,
    IReadOnlyList<VoiceChunk> Chunks,
    bool IsTerminated,
    uint Tail);

/// <summary>
/// Reads the framing inside a <c>svc_VoiceData</c> body when the session codec is <c>steam</c>.
/// </summary>
/// <remarks>
/// **Valve publishes nothing about this layout.** It was established by exact consumption over the
/// corpus — parse the whole payload, require the parser to land precisely on the end, and count.
/// A model that is nearly right scores zero rather than "most", which is what makes that number
/// worth trusting. The route to it, including two models that scored zero, is in
/// `docs/findings/02-net-messages.md`.
///
/// <code>
/// u64  steamID64
/// repeat until four bytes remain:
///   u8 type
///     0x0B  u16 sample rate
///     0x00  u16
///     0x06  u16 length, then a block of chunks
/// u32  tail
/// </code>
///
/// and inside a type <c>0x06</c> block:
///
/// <code>
/// repeat: u16 length, u16 sequence, &lt;length&gt; bytes
///         a length of 0xFFFF terminates the block instead
/// </code>
///
/// **The steamID is the part that could not be had any other way.** <c>svc_VoiceData</c> gives a
/// client slot, which is only meaningful against the roster at that instant and is reused when a
/// player leaves. The account is stable for the whole file and across files.
/// </remarks>
public static class SteamVoicePayload
{
    /// <summary>Sub-packet declaring the stream's sample rate.</summary>
    private const byte SampleRateType = 0x0B;

    /// <summary>Sub-packet carrying encoded audio.</summary>
    private const byte AudioType = 0x06;

    /// <summary>
    /// Sub-packet carrying a single 16-bit value and no audio, seen only in the 18-byte packets
    /// that bracket a talk burst. Its meaning is not established; its width is.
    /// </summary>
    private const byte SilenceType = 0x00;

    /// <summary>A chunk length of this value ends the block rather than declaring a chunk.</summary>
    private const int BlockTerminator = 0xFFFF;

    private const int SteamIdBytes = 8;
    /// <summary>
    /// Width of the trailing CRC32.
    /// </summary>
    /// <remarks>
    /// Derived positionally first: three packets' declared type-0x06 lengths came up four bytes
    /// short of the payload every time, so four bytes had to be a tail. What they *are* was
    /// settled later, and by agreement rather than assertion — <c>demostf/steam-audio-codec</c>,
    /// an unrelated implementation, reads the same structure and calls them a CRC32 over
    /// everything before them. Checked against this project's own corpus rather than adopted:
    /// 1452 of 1452 payloads match (<c>CorpusVoiceChecksumTests</c>).
    ///
    /// Carried rather than validated here. A demo is a recording of what arrived, and rejecting a
    /// packet whose CRC disagrees would discard evidence this project exists to preserve — the
    /// value is checked by the test, so a systematic mismatch would surface there.
    /// </remarks>
    private const int TailBytes = 4;
    private const int ChunkHeaderBytes = 4;
    private const int FieldBytes = 2;

    /// <summary>Reads a voice packet body.</summary>
    /// <param name="body">The message body, as <c>svc_VoiceData</c> carried it.</param>
    /// <returns>The decoded packet.</returns>
    /// <exception cref="InvalidDataException">
    /// The body does not consume exactly, or a declared length runs past the end.
    /// </exception>
    public static VoicePacket Decode(ReadOnlySpan<byte> body)
    {
        if (body.Length < SteamIdBytes + TailBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A voice payload of {body.Length} bytes is too short to hold a steamID and a " +
                $"tail, which every packet has."));
        }

        ulong steamId = BinaryPrimitives.ReadUInt64LittleEndian(body);
        int end = body.Length - TailBytes;
        int at = SteamIdBytes;

        int sampleRate = 0;
        bool terminated = false;
        List<VoiceChunk> chunks = [];

        DecodeProgress progress = new("a Steam voice payload", at - 1);

        while (at < end)
        {
            progress.Advanced(at);

            byte type = body[at++];

            switch (type)
            {
                case SampleRateType:
                    sampleRate = ReadUInt16(body, ref at, end, "a sample rate");
                    break;

                case SilenceType:
                    _ = ReadUInt16(body, ref at, end, "a silence value");
                    break;

                case AudioType:
                {
                    int length = ReadUInt16(body, ref at, end, "an audio block length");

                    if (at + length > end)
                    {
                        throw new InvalidDataException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"An audio block declares {length} bytes at offset {at}, but only " +
                            $"{end - at} remain before the tail."));
                    }

                    terminated |= ReadChunks(body.Slice(at, length), chunks);
                    at += length;
                    break;
                }

                default:
                    throw new InvalidDataException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"A voice payload carries sub-packet type 0x{type:X2} at offset " +
                        $"{at - 1}, which is not one this layout knows. Guessing its width " +
                        $"would desynchronise everything after it."));
            }
        }

        if (at != end)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A voice payload's sub-packets ended at offset {at} rather than {end}, so one " +
                $"of them was read at the wrong width."));
        }

        return new VoicePacket(
            steamId,
            sampleRate,
            chunks,
            terminated,
            BinaryPrimitives.ReadUInt32LittleEndian(body[end..]));
    }

    /// <summary>Splits an audio block into chunks. Returns whether it ended with the sentinel.</summary>
    private static bool ReadChunks(ReadOnlySpan<byte> block, List<VoiceChunk> chunks)
    {
        int at = 0;

        while (at + FieldBytes <= block.Length)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(block[at..]);

            // The sentinel occupies only the length field: there is no sequence number behind it
            // and no data. Read as a chunk length it asks for 65535 bytes that are not there.
            if (length == BlockTerminator)
            {
                at += FieldBytes;

                if (at != block.Length)
                {
                    throw new InvalidDataException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"An audio block's terminator is at offset {at - FieldBytes} with " +
                        $"{block.Length - at} bytes behind it, so it is not a terminator."));
                }

                return true;
            }

            if (at + ChunkHeaderBytes + length > block.Length)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"An audio chunk at offset {at} declares {length} bytes, which runs past " +
                    $"the {block.Length}-byte block holding it."));
            }

            int sequence = BinaryPrimitives.ReadUInt16LittleEndian(block[(at + FieldBytes)..]);
            chunks.Add(new VoiceChunk(
                sequence, block.Slice(at + ChunkHeaderBytes, length).ToArray()));

            at += ChunkHeaderBytes + length;
        }

        if (at != block.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"An audio block has {block.Length - at} bytes after its last chunk, which is " +
                $"neither a chunk nor the two-byte terminator."));
        }

        return false;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> body, ref int at, int end, string what)
    {
        if (at + FieldBytes > end)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A voice payload ends before {what} that its sub-packet type declares."));
        }

        int value = BinaryPrimitives.ReadUInt16LittleEndian(body[at..]);
        at += FieldBytes;
        return value;
    }
}
