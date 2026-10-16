using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio;

/// <summary>One voice packet decoded to PCM, with who said it and when it arrived.</summary>
/// <param name="Tick">The tick it arrived.</param>
/// <param name="Entity">The speaker, <c>client + 1</c>.</param>
/// <param name="Proximity">Whether it is spatialised from the speaker.</param>
/// <param name="Pcm">Its samples, mono.</param>
/// <param name="SampleRate">Their rate.</param>
public sealed record DecodedVoice(int Tick, int Entity, bool Proximity, ReadOnlyMemory<short> Pcm, int SampleRate);

/// <summary>Where one packet's samples begin inside its spurt.</summary>
/// <param name="Tick">The tick the packet arrived.</param>
/// <param name="Offset">Its first sample in <see cref="VoiceSpurt.Pcm"/>.</param>
public readonly record struct VoicePacketSpan(int Tick, int Offset);

/// <summary>
/// One run of a voice channel, from <c>Voice_AssignChannel</c> to the starve that ends it — what one start of the
/// channel's sound plays.
/// </summary>
/// <param name="Entity">The speaker.</param>
/// <param name="Proximity">Whether it is spatialised from the speaker.</param>
/// <param name="Channel">The voice channel index, 0 to 4.</param>
/// <param name="StartTick">The tick its pad drains and its sound starts.</param>
/// <param name="SampleRate">The PCM's rate.</param>
/// <param name="Pcm">Every packet's samples, before gain.</param>
/// <param name="Packets">Where each packet begins, in arrival order.</param>
public sealed record VoiceSpurt(
    int Entity, bool Proximity, int Channel, int StartTick, int SampleRate, ReadOnlyMemory<short> Pcm, IReadOnlyList<VoicePacketSpan> Packets);

/// <summary>What <see cref="VoicePlan.From"/> made of a demo's voice.</summary>
/// <param name="Spurts">The spurts, in start order.</param>
/// <param name="Refusal">Why some or all voice could not be decoded, or null.</param>
public sealed record VoicePlanResult(IReadOnlyList<VoiceSpurt> Spurts, string? Refusal);

/// <summary>The engine's client voice path, laid out ahead of playback: which channel plays what, from when.</summary>
/// <remarks>
/// **Read from TF2's x64 engine.dll** — citations per behaviour in <c>VoiceConformanceTests</c>. In short:
/// <c>ProcessVoiceData</c> gets or assigns the speaker one of five channels; <c>Voice_AssignChannel</c> resets the
/// channel's buffer, its <see cref="VoiceAutoGain"/>, and a <c>voice_buffer_ms</c> pad; <c>Voice_Idle</c> counts the pad
/// down and starts the channel's sound on <see cref="EngineChannel"/>, and ends the channel when its buffer runs dry.
///
/// **What is ours, not the engine's:** each speaker keeps one decoder for the whole demo, where the engine resets the
/// channel's codec on assign; the samples stay at the codec's rate for the device to resample, where the engine
/// upsamples linearly into its buffer; and the gain is applied at <see cref="Render"/>, at the moment the sound starts,
/// which is when the engine's <c>Reset</c> reads <c>voice_scale</c> too.
/// </remarks>
public static class VoicePlan
{
    /// <summary><c>CHAN_VOICE_BASE</c>: <c>VoiceSE_StartChannel</c> (FUN_18005a670) starts channel <c>index + 8</c>.</summary>
    public const int ChannelBase = 8;

    /// <summary>The engine's voice channels: <c>Voice_AddIncomingData</c> refuses an index above 4.</summary>
    public const int Channels = 5;

    /// <summary>The <c>entchannel</c> a voice channel's sound plays on.</summary>
    /// <param name="channel">The voice channel index.</param>
    /// <returns><c>index + 8</c>.</returns>
    public static int EngineChannel(int channel) => ChannelBase + channel;

    /// <summary>The jitter pad, <c>clamp(voice_buffer_ms, 1, 5000) × 0.001</c> seconds (FUN_180057350).</summary>
    public static float PadSeconds => Math.Clamp(EngineConVars.ByName("voice_buffer_ms").Number, 1f, 5000f) * 0.001f;

    /// <summary>Ticks from a channel's assignment to its sound: <c>Voice_Idle</c> starts it on the first frame the pad reaches zero.</summary>
    /// <param name="interval">Seconds per tick.</param>
    /// <returns>The pad in whole ticks, rounded up.</returns>
    public static int PadTicks(float interval) => (int)MathF.Ceiling(PadSeconds / interval);

    /// <summary>Decodes a demo's voice and lays it out.</summary>
    /// <param name="inits">The demo's <c>svc_VoiceInit</c>s.</param>
    /// <param name="voice">Its <c>svc_VoiceData</c>s.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <returns>The spurts, and why any voice could not be decoded.</returns>
    public static VoicePlanResult From(IReadOnlyList<SceneVoiceInit> inits, IReadOnlyList<SceneVoice> voice, float interval)
    {
        ArgumentNullException.ThrowIfNull(inits);
        ArgumentNullException.ThrowIfNull(voice);

        if (voice.Count == 0)
        {
            return new VoicePlanResult([], null);
        }

        string? codec = inits.Count > 0 ? inits[^1].Codec : null;
        IReadOnlyList<DecodedVoice> decoded = VoiceDecoding.Decode(codec, voice, out string? refusal);

        return new VoicePlanResult(Build(decoded, interval), refusal);
    }

    /// <summary>Lays decoded packets out on channels as the engine would play them.</summary>
    /// <param name="packets">Decoded packets, in arrival order.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <returns>The spurts, in start order.</returns>
    public static IReadOnlyList<VoiceSpurt> Build(IReadOnlyList<DecodedVoice> packets, float interval)
    {
        ArgumentNullException.ThrowIfNull(packets);

        float pad = PadSeconds;
        int padTicks = PadTicks(interval);
        Open?[] slots = new Open?[Channels];
        List<Open> done = [];

        foreach (DecodedVoice packet in packets)
        {
            float now = packet.Tick * interval;

            // `Voice_Idle` ends a channel once its pad has drained and the mixer has read everything buffered.
            for (int i = 0; i < Channels; i++)
            {
                if (slots[i] is { } open && open.PlayFrom <= now && open.PlayFrom + open.Seconds <= now)
                {
                    done.Add(open);
                    slots[i] = null;
                }
            }

            int slot = Array.FindIndex(slots, open => open?.Entity == packet.Entity);

            if (slot < 0)
            {
                slot = Array.IndexOf(slots, null);

                if (slot < 0)
                {
                    // Voice_AssignChannel failed: five speakers already hold a channel, and the engine drops this.
                    continue;
                }

                slots[slot] = new Open(packet, slot, packet.Tick + padTicks, now + pad);
            }

            Open target = slots[slot]!;
            target.Packets.Add(new VoicePacketSpan(packet.Tick, target.Pcm.Count));
            target.Pcm.AddRange(packet.Pcm.Span);
            target.Seconds += (float)packet.Pcm.Length / packet.SampleRate;
        }

        done.AddRange(slots.OfType<Open>());

        return [.. done
            .OrderBy(open => open.StartTick)
            .ThenBy(open => open.Channel)
            .Select(open => new VoiceSpurt(
                open.Entity, open.Proximity, open.Channel, open.StartTick, open.SampleRate, open.Pcm.ToArray(), open.Packets))];
    }

    /// <summary>A spurt's sound from one of its samples on, gained and faded as the engine mixes it.</summary>
    /// <param name="spurt">The spurt.</param>
    /// <param name="fromOffset">The first sample to play — 0, or a packet's offset when playback joins part-way.</param>
    /// <param name="scale"><c>voice_scale</c>.</param>
    /// <returns>Mono samples at the spurt's rate.</returns>
    /// <remarks>
    /// **A fresh <see cref="VoiceAutoGain"/> per start**, because each start is a fresh <c>Voice_AssignChannel</c>.
    /// **The fade** is <c>Voice_GetOutputData</c>'s (FUN_180057dc0): the last <c>N = max(2, rate × voice_fadeouttime)</c>
    /// samples of what is buffered are multiplied by <c>1 - k / (N - 1)</c>, so the final sample is silent.
    /// </remarks>
    public static SoundSample Render(VoiceSpurt spurt, int fromOffset, float scale)
    {
        ArgumentNullException.ThrowIfNull(spurt);

        short[] pcm = spurt.Pcm.Span[fromOffset..].ToArray();
        new VoiceAutoGain(scale).Process(pcm);

        int fade = Math.Max(2, (int)(spurt.SampleRate * EngineConVars.ByName("voice_fadeouttime").Number));
        float step = 1f / (fade - 1);
        float[] samples = new float[pcm.Length];

        for (int i = 0; i < pcm.Length; i++)
        {
            int k = i - (pcm.Length - fade);
            int value = k < 0 ? pcm[i] : (int)((1f - (k * step)) * pcm[i]);
            samples[i] = value / 32768f;
        }

        return new SoundSample(spurt.SampleRate, 1, samples);
    }

    /// <summary>A channel while it is being filled.</summary>
    private sealed class Open(DecodedVoice first, int channel, int startTick, float playFrom)
    {
        public int Entity { get; } = first.Entity;

        public bool Proximity { get; } = first.Proximity;

        public int SampleRate { get; } = first.SampleRate;

        public int Channel { get; } = channel;

        public int StartTick { get; } = startTick;

        public float PlayFrom { get; } = playFrom;

        public float Seconds { get; set; }

        public List<short> Pcm { get; } = [];

        public List<VoicePacketSpan> Packets { get; } = [];
    }
}
