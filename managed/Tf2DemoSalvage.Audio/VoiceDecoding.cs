using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio;

/// <summary>Turns a demo's <c>svc_VoiceData</c> bodies into PCM with the decoder its codec names.</summary>
/// <remarks>
/// **The framing is the probe's and the corpus tests'** (findings/02): <c>steam</c> is Opus chunks inside
/// <see cref="SteamVoicePayload"/>; <c>vaudio_speex</c> is Steam Voice with SILK inside when the body's tail is its own
/// CRC32 (B441) and raw 28-byte Speex frames otherwise; <c>vaudio_celt</c> is bare 64-byte CELT frames. One decoder per
/// speaker for the whole demo, because each is stateful and speakers interleave.
/// </remarks>
public static class VoiceDecoding
{
    private const int SpeexFrameBytes = 28;

    /// <summary>Decodes every packet it can.</summary>
    /// <param name="codec">The session's codec, from <c>svc_VoiceInit</c>, or null when none was sent.</param>
    /// <param name="voice">The packets, in arrival order.</param>
    /// <param name="refusal">Why some voice was not decoded, or null when all of it was.</param>
    /// <returns>One entry per packet that decoded.</returns>
    public static IReadOnlyList<DecodedVoice> Decode(string? codec, IReadOnlyList<SceneVoice> voice, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(voice);

        refusal = null;
        List<DecodedVoice> decoded = [];
        Dictionary<int, Speaker> speakers = [];
        int frameErrors = 0;

        try
        {
            foreach (SceneVoice packet in voice)
            {
                if (!speakers.TryGetValue(packet.Entity, out Speaker? speaker))
                {
                    speaker = new Speaker();
                    speakers[packet.Entity] = speaker;
                }

                (short[]? pcm, int rate, string? why) = speaker.Read(codec, packet.Body, ref frameErrors);

                if (pcm is null)
                {
                    refusal ??= why;
                    continue;
                }

                decoded.Add(new DecodedVoice(packet.Tick, packet.Entity, packet.Proximity, pcm, rate));
            }
        }
        finally
        {
            foreach (Speaker speaker in speakers.Values)
            {
                speaker.Dispose();
            }
        }

        if (frameErrors > 0)
        {
            refusal ??= string.Create(CultureInfo.InvariantCulture, $"{frameErrors} {codec} frames were rejected by the decoder");
        }

        return decoded;
    }

    /// <summary>One speaker's decoders, made as the stream first needs them.</summary>
    private sealed class Speaker : IDisposable
    {
        private OpusVoiceDecoder? _opus;
        private SilkVoiceDecoder? _silk;
        private SpeexVoiceDecoder? _speex;
        private CeltVoiceDecoder? _celt;

        public (short[]? Pcm, int Rate, string? Refusal) Read(string? codec, ReadOnlyMemory<byte> body, ref int frameErrors)
        {
            try
            {
                switch (codec)
                {
                    case "steam":
                        _opus ??= new OpusVoiceDecoder();
                        return (Chunks(SteamVoicePayload.Decode(body.Span), _opus.Decode, ref frameErrors), OpusVoiceDecoder.SampleRate, null);

                    case "vaudio_speex" when SteamVoicePayload.TryDecode(body.Span, out VoicePacket? steam):
                        _silk ??= new SilkVoiceDecoder();
                        return (Chunks(steam, _silk.Decode, ref frameErrors), SilkVoiceDecoder.SampleRate, null);

                    case "vaudio_speex":
                        _speex ??= new SpeexVoiceDecoder();
                        return (Frames(body, SpeexFrameBytes, _speex.Decode, ref frameErrors), SpeexVoiceDecoder.SampleRate, null);

                    case "vaudio_celt":
                        _celt ??= new CeltVoiceDecoder();
                        return (Frames(body, CeltVoiceDecoder.CompressedFrameBytes, _celt.Decode, ref frameErrors), CeltVoiceDecoder.SampleRate, null);

                    default:
                        return (null, 0, "no decoder for voice codec '" + (codec ?? "none declared") + "'");
                }
            }
            catch (Exception error) when (error is DllNotFoundException or TypeInitializationException or InvalidOperationException or System.IO.InvalidDataException)
            {
                // A missing native library or a body that does not frame: this packet is lost, and the reason is kept.
                return (null, 0, codec + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        public void Dispose()
        {
            _opus?.Dispose();
            _silk?.Dispose();
            _speex?.Dispose();
            _celt?.Dispose();
        }

        private delegate short[] FrameDecoder(ReadOnlySpan<byte> frame);

        private static short[] Chunks(VoicePacket packet, FrameDecoder decode, ref int frameErrors)
        {
            List<short> pcm = [];

            foreach (VoiceChunk chunk in packet.Chunks)
            {
                Append(pcm, decode, chunk.Data.Span, ref frameErrors);
            }

            return [.. pcm];
        }

        private static short[] Frames(ReadOnlyMemory<byte> body, int width, FrameDecoder decode, ref int frameErrors)
        {
            List<short> pcm = [];

            // A trailing partial frame is not a frame: `VoiceCodec_Frame::Decompress` is a bare loop over whole ones.
            for (int at = 0; at + width <= body.Length; at += width)
            {
                Append(pcm, decode, body.Span.Slice(at, width), ref frameErrors);
            }

            return [.. pcm];
        }

        private static void Append(List<short> pcm, FrameDecoder decode, ReadOnlySpan<byte> frame, ref int frameErrors)
        {
            try
            {
                pcm.AddRange(decode(frame));
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                // The engine's decompress returns no samples for a frame it cannot read; counted and reported once.
                frameErrors++;
            }
        }
    }
}
