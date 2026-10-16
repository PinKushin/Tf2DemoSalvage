using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio;

/// <summary>One voice packet, framed but not decoded, with who said it and when it arrived.</summary>
/// <param name="Tick">The tick it arrived.</param>
/// <param name="Entity">The speaker, <c>client + 1</c>.</param>
/// <param name="Proximity">Whether it is spatialised from the speaker.</param>
/// <param name="Framing">Its decoder, rate and sample count.</param>
/// <param name="Body">The codec payload.</param>
/// <param name="Init">Which <c>svc_VoiceInit</c> it arrived under; a change between two packets is a <c>Voice_Init</c>.</param>
/// <param name="OutputRate">The output rate that init set, which sizes the channel buffer.</param>
public sealed record VoiceArrival(
    int Tick, int Entity, bool Proximity, VoiceFraming Framing, ReadOnlyMemory<byte> Body, int Init = 0, int OutputRate = 0);

/// <summary>One packet inside a spurt.</summary>
/// <param name="Tick">The tick it arrived.</param>
/// <param name="Offset">Its first sample in the spurt.</param>
/// <param name="Samples">How many samples it holds.</param>
/// <param name="Body">The codec payload, decoded when the spurt is rendered.</param>
public sealed record VoiceSpurtPacket(int Tick, int Offset, int Samples, ReadOnlyMemory<byte> Body);

/// <summary>A run of samples the channel's buffer overwrote before it was played (<c>FUN_180016fd0</c>).</summary>
/// <param name="Offset">The first lost sample.</param>
/// <param name="Count">How many.</param>
public readonly record struct VoiceCut(int Offset, int Count);

/// <summary>
/// One run of a voice channel, from <c>Voice_AssignChannel</c> to the starve or the <c>Voice_Init</c> that ends it — what
/// one start of the channel's sound plays.
/// </summary>
/// <param name="Entity">The speaker.</param>
/// <param name="Proximity">Whether it is spatialised from the speaker.</param>
/// <param name="Channel">The voice channel index, 0 to 4.</param>
/// <param name="StartTick">The tick its pad drains and its sound starts.</param>
/// <param name="Kind">Its decoder.</param>
/// <param name="SampleRate">Its rate.</param>
/// <param name="Packets">Its packets, in arrival order.</param>
/// <param name="Cuts">Samples the buffer overwrote, in order.</param>
public sealed record VoiceSpurt(
    int Entity,
    bool Proximity,
    int Channel,
    int StartTick,
    VoiceKind Kind,
    int SampleRate,
    IReadOnlyList<VoiceSpurtPacket> Packets,
    IReadOnlyList<VoiceCut> Cuts)
{
    /// <summary>Samples in the whole spurt, cuts included.</summary>
    public int Length => Packets.Count == 0 ? 0 : Packets[^1].Offset + Packets[^1].Samples;

    /// <summary>Ticks from the first packet's arrival to the sound: the pad.</summary>
    public int PadTicks => StartTick - Packets[0].Tick;
}

/// <summary>What <see cref="VoicePlan.From"/> made of a demo's voice.</summary>
/// <param name="Spurts">The spurts, in order of their first packet.</param>
/// <param name="Refusal">Why some or all voice cannot be played, or null.</param>
public sealed record VoicePlanResult(IReadOnlyList<VoiceSpurt> Spurts, string? Refusal);

/// <summary>The engine's client voice path, laid out ahead of playback: which channel plays what, from when.</summary>
/// <remarks>
/// **Read from TF2's x64 engine.dll** — citations per behaviour in <c>VoiceConformanceTests</c>; the choices that are
/// ours are listed in findings/31, "Demo voice". The layout assumes playback at the recording's own speed; the
/// presenter follows the real transport.
/// </remarks>
public static class VoicePlan
{
    /// <summary><c>CHAN_VOICE_BASE</c>: <c>VoiceSE_StartChannel</c> (FUN_18005a670) starts channel <c>index + 8</c>.</summary>
    public const int ChannelBase = 8;

    /// <summary>The engine's voice channels: <c>Voice_AddIncomingData</c> refuses an index above 4.</summary>
    public const int Channels = 5;

    /// <summary>Each channel's buffer: 0xac44 bytes of 16-bit samples at the output rate (FUN_180002bb0).</summary>
    public const int BufferSamples = 0xac44 / 2;

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

    /// <summary>
    /// The output rate <c>Voice_Init</c> (FUN_180058700) sets: 44100 for <c>steam</c> with no rate, otherwise the rate
    /// the init carried. With neither, the codec's own rate — ours, since such an init sends none to read.
    /// </summary>
    /// <param name="init">The init in force.</param>
    /// <param name="codecRate">The codec's decode rate.</param>
    /// <returns>Samples per second.</returns>
    public static int OutputRate(SceneVoiceInit? init, int codecRate) =>
        init switch
        {
            { Codec: "steam", SampleRate: null or 0 } => 44100,
            { SampleRate: > 0 and var rate } => rate,
            _ => codecRate,
        };

    /// <summary>Frames a demo's voice and lays it out. Nothing is decoded.</summary>
    /// <param name="inits">The demo's <c>svc_VoiceInit</c>s.</param>
    /// <param name="voice">Its <c>svc_VoiceData</c>s.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="unavailable">Why a decoder cannot be made, or null when it can; <see cref="VoiceDecoding.Unavailable"/> by default.</param>
    /// <param name="cancel">Abandons a plan for a demo that is no longer open.</param>
    /// <returns>The spurts, and why any voice cannot be played.</returns>
    public static VoicePlanResult From(
        IReadOnlyList<SceneVoiceInit> inits,
        IReadOnlyList<SceneVoice> voice,
        float interval,
        Func<VoiceKind, string?>? unavailable = null,
        System.Threading.CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(inits);
        ArgumentNullException.ThrowIfNull(voice);

        unavailable ??= VoiceDecoding.Unavailable;

        List<VoiceArrival> arrivals = [];
        HashSet<string> refusals = [];

        foreach (SceneVoice packet in voice)
        {
            cancel.ThrowIfCancellationRequested();

            // The init in force when this packet arrived, by stream order, as `DemoTimeline.VoiceCodecOf` reads it.
            SceneVoiceInit? init = packet.Init >= 0 && packet.Init < inits.Count ? inits[packet.Init] : null;
            string? codec = init?.Codec;
            VoiceFraming framing = VoiceDecoding.Frame(codec, packet.Body.Span);

            if (framing.Kind == VoiceKind.None)
            {
                refusals.Add("no decoder for voice codec '" + (codec ?? "none declared") + "'");
                continue;
            }

            if (unavailable(framing.Kind) is { } why)
            {
                // Asked once per decoder for the process, so a missing library fails the plan rather than each packet.
                refusals.Add(why);
                continue;
            }

            arrivals.Add(new VoiceArrival(
                packet.Tick, packet.Entity, packet.Proximity, framing, packet.Body, packet.Init, OutputRate(init, framing.SampleRate)));
        }

        IReadOnlyList<VoiceSpurt> spurts = Build(arrivals, interval);

        return new VoicePlanResult(spurts, refusals.Count == 0 ? null : string.Join("; ", refusals));
    }

    /// <summary>Lays framed packets out on channels as the engine would play them.</summary>
    /// <param name="packets">The packets, in arrival order.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <returns>The spurts, in order of their first packet.</returns>
    public static IReadOnlyList<VoiceSpurt> Build(IReadOnlyList<VoiceArrival> packets, float interval)
    {
        ArgumentNullException.ThrowIfNull(packets);

        float pad = PadSeconds;
        int padTicks = PadTicks(interval);
        Open?[] slots = new Open?[Channels];
        List<Open> done = [];
        int? lastInit = null;

        foreach (VoiceArrival packet in packets)
        {
            float now = packet.Tick * interval;

            // A `Voice_Init` between the last packet and this one ends every channel: it calls `Voice_Deinit`
            // (FUN_180057510) first.
            if (lastInit is { } previous && previous != packet.Init)
            {
                Close(slots, done, _ => true);
            }

            lastInit = packet.Init;

            // `Voice_Idle` ends a channel once its pad has drained and the mixer has read everything buffered. Checked
            // for every channel at every arrival, so a slot freed since the last packet is free for this one.
            Close(slots, done, open => open.PlayFrom <= now && open.PlayFrom + open.Remaining <= now);

            int slot = Array.FindIndex(slots, open => open?.Entity == packet.Entity);

            if (slot < 0)
            {
                slot = Array.IndexOf(slots, null);

                if (slot < 0)
                {
                    // `Voice_AssignChannel` failed: five speakers hold a channel. The packet is never decoded.
                    continue;
                }

                int output = packet.OutputRate > 0 ? packet.OutputRate : packet.Framing.SampleRate;
                int capacity = (int)((long)BufferSamples * packet.Framing.SampleRate / output);
                slots[slot] = new Open(packet, slot, packet.Tick + padTicks, now + pad, capacity);
            }

            slots[slot]!.Add(packet, now);
        }

        done.AddRange(slots.OfType<Open>());

        return [.. done
            .OrderBy(open => open.StartTick)
            .ThenBy(open => open.Channel)
            .Select(open => new VoiceSpurt(
                open.Entity, open.Proximity, open.Channel, open.StartTick, open.Kind, open.SampleRate, open.Packets, open.Cuts))];
    }

    /// <summary>A spurt's sound from one packet on, decoded with a fresh decoder, gained and faded as the engine mixes it.</summary>
    /// <param name="spurt">The spurt.</param>
    /// <param name="fromPacket">The packet the channel was assigned at — 0, or the next one after a jump or a starve.</param>
    /// <param name="skip">Samples the mixer would already have read when playback overshot the start.</param>
    /// <param name="scale"><c>voice_scale</c>.</param>
    /// <returns>Mono samples at the spurt's rate.</returns>
    /// <remarks>
    /// **A fresh decoder and a fresh <see cref="VoiceAutoGain"/> per start**, because each start is a fresh
    /// <c>Voice_AssignChannel</c>, which resets both. **The fade** is <c>Voice_GetOutputData</c>'s (FUN_180057dc0): the
    /// last <c>N = max(2, rate × voice_fadeouttime)</c> buffered samples are multiplied by <c>1 - k / (N - 1)</c>.
    /// </remarks>
    public static SoundSample Render(VoiceSpurt spurt, int fromPacket, int skip, float scale) =>
        Render(spurt, fromPacket, skip, scale, kind => new VoiceStreamDecoder(kind));

    /// <summary><see cref="Render(VoiceSpurt, int, int, float)"/> with the decoder supplied, for a test with no native library.</summary>
    /// <param name="spurt">The spurt.</param>
    /// <param name="fromPacket">The packet the channel was assigned at.</param>
    /// <param name="skip">Samples already read by the mixer.</param>
    /// <param name="scale"><c>voice_scale</c>.</param>
    /// <param name="decoders">Makes a fresh decoder for a kind.</param>
    /// <returns>Mono samples at the spurt's rate.</returns>
    public static SoundSample Render(VoiceSpurt spurt, int fromPacket, int skip, float scale, Func<VoiceKind, IVoiceStreamDecoder> decoders) =>
        Render(spurt, fromPacket, skip, scale, decoders, System.Threading.CancellationToken.None);

    /// <summary>A cancellable <see cref="Render(VoiceSpurt, int, int, float, Func{VoiceKind, IVoiceStreamDecoder})"/>, checked per packet.</summary>
    /// <param name="spurt">The spurt.</param>
    /// <param name="fromPacket">The packet the channel was assigned at.</param>
    /// <param name="skip">Samples already read by the mixer.</param>
    /// <param name="scale"><c>voice_scale</c>.</param>
    /// <param name="decoders">Makes a fresh decoder for a kind.</param>
    /// <param name="cancel">Abandons a render nothing will play.</param>
    /// <returns>Mono samples at the spurt's rate.</returns>
    public static SoundSample Render(
        VoiceSpurt spurt, int fromPacket, int skip, float scale, Func<VoiceKind, IVoiceStreamDecoder> decoders, System.Threading.CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(spurt);
        ArgumentNullException.ThrowIfNull(decoders);

        int first = spurt.Packets[fromPacket].Offset;
        short[] pcm = new short[spurt.Length - first];

        using (IVoiceStreamDecoder decoder = decoders(spurt.Kind))
        {
            for (int i = fromPacket; i < spurt.Packets.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                VoiceSpurtPacket packet = spurt.Packets[i];
                decoder.Decode(packet.Body.Span, pcm.AsSpan(packet.Offset - first, packet.Samples));
            }
        }

        new VoiceAutoGain(scale).Process(pcm);

        // What the buffer overwrote never reaches the mixer, and what the mixer has already read is skipped.
        List<short> kept = new(pcm.Length);
        int cut = 0;

        for (int i = 0; i < pcm.Length; i++)
        {
            int at = first + i;

            while (cut < spurt.Cuts.Count && at >= spurt.Cuts[cut].Offset + spurt.Cuts[cut].Count)
            {
                cut++;
            }

            if (cut == spurt.Cuts.Count || at < spurt.Cuts[cut].Offset)
            {
                kept.Add(pcm[i]);
            }
        }

        int start = Math.Min(Math.Max(skip, 0), kept.Count);
        int fade = Math.Max(2, (int)(spurt.SampleRate * EngineConVars.ByName("voice_fadeouttime").Number));
        float step = 1f / (fade - 1);
        float[] samples = new float[kept.Count - start];

        for (int i = start; i < kept.Count; i++)
        {
            int k = i - (kept.Count - fade);
            int value = k < 0 ? kept[i] : (int)((1f - (k * step)) * kept[i]);
            samples[i - start] = value / 32768f;
        }

        return new SoundSample(spurt.SampleRate, 1, samples);
    }

    /// <summary>Where a spurt sample lands once the runs the buffer overwrote are taken out.</summary>
    /// <param name="spurt">The spurt.</param>
    /// <param name="offset">A sample offset in the spurt.</param>
    /// <returns>How many kept samples precede it.</returns>
    public static int KeptOffset(VoiceSpurt spurt, int offset)
    {
        ArgumentNullException.ThrowIfNull(spurt);

        int kept = offset;

        foreach (VoiceCut cut in spurt.Cuts)
        {
            if (cut.Offset < offset)
            {
                kept -= Math.Min(cut.Count, offset - cut.Offset);
            }
        }

        return kept;
    }

    private static void Close(Open?[] slots, List<Open> done, Func<Open, bool> ends)
    {
        for (int i = 0; i < Channels; i++)
        {
            if (slots[i] is { } open && ends(open))
            {
                done.Add(open);
                slots[i] = null;
            }
        }
    }

    /// <summary>A channel while it is being filled.</summary>
    private sealed class Open(VoiceArrival first, int channel, int startTick, float playFrom, int capacity)
    {
        private int _total;
        private int _cut;

        public int Entity { get; } = first.Entity;

        public bool Proximity { get; } = first.Proximity;

        public VoiceKind Kind { get; } = first.Framing.Kind;

        public int SampleRate { get; } = first.Framing.SampleRate;

        public int Channel { get; } = channel;

        public int StartTick { get; } = startTick;

        public float PlayFrom { get; } = playFrom;

        public List<VoiceSpurtPacket> Packets { get; } = [];

        public List<VoiceCut> Cuts { get; } = [];

        /// <summary>Seconds of what is buffered, played or not, after the cuts: when the mixer would run dry.</summary>
        public float Remaining => (float)(_total - _cut) / SampleRate;

        public void Add(VoiceArrival packet, float now)
        {
            int samples = packet.Framing.Samples;

            // The buffer's write always lands, and on overflow the read point jumps to it, so the OLDEST unread samples are
            // lost (FUN_180016fd0: `param_1[1] = param_1[2]; *param_1 = param_1[3]`).
            int read = _cut + Math.Max(0, (int)((now - PlayFrom) * SampleRate));
            int unread = Math.Max(0, _total - read);
            int over = unread + samples - capacity;

            if (over > 0)
            {
                Cuts.Add(new VoiceCut(read, over));
                _cut += over;
            }

            Packets.Add(new VoiceSpurtPacket(packet.Tick, _total, samples, packet.Body));
            _total += samples;
        }
    }
}
