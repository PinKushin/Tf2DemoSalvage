using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>Which decoder a voice packet needs.</summary>
public enum VoiceKind
{
    /// <summary>Not a codec this viewer decodes.</summary>
    None,

    /// <summary><c>steam</c>: Opus chunks inside Steam Voice.</summary>
    Opus,

    /// <summary><c>vaudio_speex</c> carrying Steam Voice: SILK frames (B441).</summary>
    Silk,

    /// <summary><c>vaudio_speex</c>: raw 28-byte Speex frames.</summary>
    Speex,

    /// <summary><c>vaudio_celt</c>: bare 64-byte CELT frames.</summary>
    Celt,
}

/// <summary>How a voice packet is framed: its decoder, its rate, and how many samples it holds — read without decoding.</summary>
/// <param name="Kind">The decoder.</param>
/// <param name="SampleRate">The rate it decodes at.</param>
/// <param name="Samples">How many samples it decodes to.</param>
public readonly record struct VoiceFraming(VoiceKind Kind, int SampleRate, int Samples);

/// <summary>Voice framing and decoding, per codec.</summary>
/// <remarks>
/// **The framing is the probe's and the corpus tests'** (findings/02): <c>steam</c> is Opus chunks inside
/// <see cref="SteamVoicePayload"/>; <c>vaudio_speex</c> is Steam Voice with SILK inside when the body's tail is its own
/// CRC32 (B441), and raw 28-byte Speex frames otherwise; <c>vaudio_celt</c> is bare 64-byte CELT frames, 512 samples
/// each at 22050 Hz.
///
/// **Sample counts are read from the framing, so laying voice out needs no native library.** Speex and CELT are fixed
/// frames; an Opus chunk's count is its TOC byte (RFC 6716 §3.1); a Steam SILK frame is one 20 ms frame at 16 kHz,
/// measured on every SILK frame of the corpus (findings/02), a zero-length one being a concealed frame of the same size.
/// </remarks>
public static class VoiceDecoding
{
    private const int SpeexFrameBytes = 28;
    private const int SpeexFrameSamples = 160;
    private const int SilkFrameSamples = SilkVoiceDecoder.SampleRate / 50;

    private static readonly Dictionary<VoiceKind, string?> Availability = [];
    private static readonly object AvailabilityLock = new();

    /// <summary>Frames one packet without decoding it.</summary>
    /// <param name="codec">The codec in force when it arrived.</param>
    /// <param name="body">The <c>svc_VoiceData</c> body.</param>
    /// <returns>Its framing; <see cref="VoiceKind.None"/> for a codec or a body this cannot frame.</returns>
    public static VoiceFraming Frame(string? codec, ReadOnlySpan<byte> body)
    {
        switch (codec)
        {
            case "steam":
                return SteamVoicePayload.TryDecode(body, out VoicePacket? opus)
                    ? new VoiceFraming(VoiceKind.Opus, OpusVoiceDecoder.SampleRate, OpusSamples(opus))
                    : default;

            case "vaudio_speex" when SteamVoicePayload.TryDecode(body, out VoicePacket? silk):
                return new VoiceFraming(VoiceKind.Silk, SilkVoiceDecoder.SampleRate, silk.Chunks.Count * SilkFrameSamples);

            case "vaudio_speex":
                return new VoiceFraming(VoiceKind.Speex, SpeexVoiceDecoder.SampleRate, body.Length / SpeexFrameBytes * SpeexFrameSamples);

            case "vaudio_celt":
                return new VoiceFraming(VoiceKind.Celt, CeltVoiceDecoder.SampleRate, body.Length / CeltVoiceDecoder.CompressedFrameBytes * NativeCelt.FrameSize);

            default:
                return default;
        }
    }

    /// <summary>Samples in one Opus packet at 24 kHz, from its TOC byte (RFC 6716 §3.1).</summary>
    /// <param name="packet">The Opus packet.</param>
    /// <returns>Its sample count; 0 for an empty packet.</returns>
    public static int OpusPacketSamples(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return 0;
        }

        int config = packet[0] >> 3;

        // Frame length in tenths of a millisecond: SILK-only 10/20/40/60, hybrid 10/20, CELT-only 2.5/5/10/20.
        int tenths = config switch
        {
            < 12 => new[] { 100, 200, 400, 600 }[config & 3],
            < 16 => new[] { 100, 200 }[config & 1],
            _ => new[] { 25, 50, 100, 200 }[config & 3],
        };

        int frames = (packet[0] & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1 ? packet[1] & 0x3F : 0,
        };

        return frames * tenths * OpusVoiceDecoder.SampleRate / 10_000;
    }

    /// <summary>Why a decoder cannot be made on this machine, asked once per process; null when it can.</summary>
    /// <param name="kind">The decoder.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Unavailable(VoiceKind kind)
    {
        lock (AvailabilityLock)
        {
            if (!Availability.TryGetValue(kind, out string? why))
            {
                why = Probe(kind);
                Availability[kind] = why;
            }

            return why;
        }
    }

    private static string? Probe(VoiceKind kind)
    {
        try
        {
            using VoiceStreamDecoder probe = new(kind);
            probe.Open();
            return null;
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException or InvalidOperationException or EntryPointNotFoundException)
        {
            return kind + ": " + error.GetType().Name + ": " + error.Message;
        }
    }

    private static int OpusSamples(VoicePacket packet)
    {
        int samples = 0;

        foreach (VoiceChunk chunk in packet.Chunks)
        {
            samples += OpusPacketSamples(chunk.Data.Span);
        }

        return samples;
    }
}

/// <summary>A channel's decoder, as <c>VoicePlan.Render</c> uses it.</summary>
public interface IVoiceStreamDecoder : IDisposable
{
    /// <summary>Decodes one packet into exactly its framed sample count.</summary>
    /// <param name="body">The packet.</param>
    /// <param name="into">The packet's samples, overwritten.</param>
    /// <returns>How many frames were rejected and left silent.</returns>
    public int Decode(ReadOnlySpan<byte> body, Span<short> into);
}

/// <summary>One channel's decoder: made fresh for each start, as <c>Voice_AssignChannel</c> resets the channel's codec.</summary>
/// <param name="kind">The decoder to make.</param>
public sealed class VoiceStreamDecoder(VoiceKind kind) : IVoiceStreamDecoder
{
    private IDisposable? _decoder;

    /// <summary>Makes the native decoder now, so a missing library fails here rather than mid-stream.</summary>
    public void Open() => _decoder ??= kind switch
    {
        VoiceKind.Opus => new OpusVoiceDecoder(),
        VoiceKind.Silk => new SilkVoiceDecoder(),
        VoiceKind.Speex => new SpeexVoiceDecoder(),
        VoiceKind.Celt => new CeltVoiceDecoder(),
        _ => throw new InvalidOperationException("no decoder for " + kind),
    };

    /// <summary>Decodes one packet into a buffer, padding or trimming to its framed length so offsets stay where the plan put them.</summary>
    /// <param name="body">The packet.</param>
    /// <param name="into">Exactly the packet's framed sample count.</param>
    /// <returns>How many frames the decoder rejected; their samples are left silent.</returns>
    public int Decode(ReadOnlySpan<byte> body, Span<short> into)
    {
        Open();
        List<short> pcm = [];
        int rejected = 0;

        switch (_decoder)
        {
            case OpusVoiceDecoder opus:
                foreach (VoiceChunk chunk in SteamVoicePayload.Decode(body).Chunks)
                {
                    rejected += Try(() => pcm.AddRange(opus.Decode(chunk.Data.Span)));
                }

                break;

            case SilkVoiceDecoder silk:
                foreach (VoiceChunk chunk in SteamVoicePayload.Decode(body).Chunks)
                {
                    rejected += Try(() => pcm.AddRange(silk.Decode(chunk.Data.Span)));
                }

                break;

            case SpeexVoiceDecoder speex:
                rejected += Frames(body.ToArray(), 28, frame => pcm.AddRange(speex.Decode(frame)));
                break;

            case CeltVoiceDecoder celt:
                rejected += Frames(body.ToArray(), CeltVoiceDecoder.CompressedFrameBytes, frame => pcm.AddRange(celt.Decode(frame)));
                break;
        }

        into.Clear();
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pcm)[..Math.Min(pcm.Count, into.Length)].CopyTo(into);

        return rejected;
    }

    /// <inheritdoc />
    public void Dispose() => _decoder?.Dispose();

    private delegate void FrameAction(ReadOnlySpan<byte> frame);

    private static int Frames(byte[] body, int width, FrameAction decode)
    {
        int rejected = 0;

        // A trailing partial frame is not a frame: `VoiceCodec_Frame::Decompress` is a bare loop over whole ones.
        for (int at = 0; at + width <= body.Length; at += width)
        {
            int from = at;
            rejected += Try(() => decode(body.AsSpan(from, width)));
        }

        return rejected;
    }

    private static int Try(Action decode)
    {
        try
        {
            decode();
            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // The engine's decompress yields no samples for a frame it cannot read: left silent and counted.
            return 1;
        }
    }
}
