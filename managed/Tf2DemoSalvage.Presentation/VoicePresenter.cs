using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Plays a demo's voice as playback reaches it, the way the engine's voice channels sound.</summary>
/// <remarks>
/// **The sound is <c>VoiceSE_StartChannel</c>'s** (engine.dll x64 <c>FUN_18005a670</c>): channel <c>8 + index</c>, volume 1,
/// pitch 100; a proximity channel at <c>SNDLVL_80dB</c> from the speaker, any other at <c>SNDLVL_60dB</c> with the VIEW
/// entity as its source — at the listener, centred and unattenuated.
///
/// **Timing follows the engine's buffer, which holds only what has arrived.** The pad drains on demo time, because
/// <c>Voice_Idle</c> is passed the host frame time, which demo playback scales; so a pause holds a pending start, and a
/// step that overshoots it starts part-way in, as the mixer would have. While the demo is stalled nothing arrives, so a
/// playing voice is silenced once the audio that HAD arrived has been played. After a jump, a step back, or a starve,
/// speech resumes at the next packet that arrives, behind a fresh pad and a fresh gain and decoder.
/// </remarks>
public sealed class VoicePresenter
{
    private readonly List<Pending> _pending = [];
    private readonly List<Playing> _playing = [];
    private readonly List<(VoiceSpurt Spurt, int NextPacket)> _starved = [];
    private IReadOnlyList<VoiceSpurt> _plan = [];
    private int _next;
    private int? _lastTick;

    /// <summary>The demo's voice, laid out by <see cref="VoicePlan"/>, in order of first packet; setting it starts over.</summary>
    public IReadOnlyList<VoiceSpurt> Plan
    {
        get => _plan;
        set
        {
            _plan = value ?? [];
            _pending.Clear();
            _playing.Clear();
            _starved.Clear();
            _renders.Clear();
            _next = 0;
            _lastTick = null;
        }
    }

    /// <summary>Makes a fresh decoder per start; replaceable so a test needs no native library.</summary>
    public Func<VoiceKind, IVoiceStreamDecoder> Decoders { get; init; } = kind => new VoiceStreamDecoder(kind);

    /// <summary>Seconds per tick, for how far a step overshot a start.</summary>
    public float Interval { get; set; } = 0.015f;

    /// <summary><c>voice_scale</c>, read as each spurt starts, as <c>Voice_AssignChannel</c> reads it.</summary>
    public float Scale { get; set; } = EngineConVars.ByName("voice_scale").Number;

    /// <summary><c>voice_enable</c>: off, <c>ProcessVoiceData</c> drops every packet and <c>Voice_Idle</c> ends every channel.</summary>
    public bool Enabled { get; set; } = EngineConVars.ByName("voice_enable").Number != 0f;

    /// <summary>Brings voice up to date for one pass.</summary>
    /// <param name="output">Where sound goes.</param>
    /// <param name="tick">The tick being played.</param>
    /// <param name="jumped">Whether playback jumped here — everything in flight was already silenced.</param>
    /// <param name="listener">Where the ears are.</param>
    /// <param name="right">The listener's right vector.</param>
    /// <param name="now">Seconds on a wall clock, which is the mixer's.</param>
    /// <param name="viewEntity">The view entity, a non-proximity voice's source.</param>
    /// <param name="speakerAt">Where an entity stands now, or null; asked only for a proximity start.</param>
    public void Update(
        IAudioSink output,
        int tick,
        bool jumped,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        double now,
        int viewEntity,
        Func<int, (float X, float Y, float Z)?> speakerAt)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(speakerAt);

        if (!Enabled)
        {
            SilencePlaying(output);
            _lastTick = null;
            return;
        }

        if (_lastTick is not { } last || jumped || tick < last)
        {
            if (!jumped)
            {
                // A step back the schedule did not call a jump: nothing silenced it yet.
                SilencePlaying(output);
            }

            _playing.Clear();
            Resume(tick);
        }
        else if (tick == last)
        {
            Stall(output, tick, now);
            return;
        }
        else
        {
            Arrive(last, tick);
        }

        _lastTick = tick;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending due = _pending[i];

            if (due.AtTick > tick || !due.Sound.IsCompleted)
            {
                // Not due, or still decoding: a late decode starts part-way in, as the mixer would have read on.
                continue;
            }

            _pending.RemoveAt(i);

            if (!due.Sound.IsCompletedSuccessfully)
            {
                // Recorded for the caller's log rather than thrown on the frame.
                Failure = due.Sound.Exception?.GetBaseException().Message;
                continue;
            }

            int skip = (int)Math.Round((tick - due.AtTick) * (double)Interval * due.Spurt.SampleRate);
            Start(output, due.Spurt, due.Packet, due.Sound.Result, skip, now, listener, right, viewEntity, speakerAt);
        }
    }

    /// <summary>The last start whose decode failed, for the caller to log; null when none has.</summary>
    public string? Failure { get; private set; }

    /// <summary>Runs a start's decode; off the frame by default, inline for a test.</summary>
    public Func<Func<SoundSample>, Task<SoundSample>> Renderer { get; init; } = Task.Run;

    /// <summary>
    /// Queues a start and begins decoding it now, at the packet that assigns the channel — where
    /// <c>Voice_AssignChannel</c> reads <c>voice_scale</c> — so the pad's lead hides the decode.
    /// </summary>
    /// <remarks>
    /// **Each spurt is decoded once, from its first packet, and a resume slices it.** A run whose frames step past
    /// <c>SoundSchedule.CatchUpTicks</c> is a jump every frame, and re-decoding every arriving spurt per jump starved the
    /// frame (the 8x playback test fell to 600 ms frames). The engine decodes a resume with a fresh decoder and gain; a
    /// slice carries the state the first packet built. Ours, listed in findings/31.
    /// </remarks>
    private void Queue(VoiceSpurt spurt, int packet, int atTick)
    {
        if (!_renders.TryGetValue(spurt, out Task<SoundSample>? sound))
        {
            float scale = Scale;
            Func<VoiceKind, IVoiceStreamDecoder> decoders = Decoders;
            sound = Renderer(() => VoicePlan.Render(spurt, 0, 0, scale, decoders));
            _renders[spurt] = sound;
        }

        _pending.Add(new Pending(spurt, packet, atTick, sound));
    }

    /// <summary>Drops the decodes nothing pending, starved or playing still needs.</summary>
    private void Prune()
    {
        if (_renders.Count == 0)
        {
            return;
        }

        HashSet<VoiceSpurt> needed = [];

        foreach (Pending pending in _pending)
        {
            needed.Add(pending.Spurt);
        }

        foreach ((VoiceSpurt spurt, int _) in _starved)
        {
            needed.Add(spurt);
        }

        foreach (Playing playing in _playing)
        {
            needed.Add(playing.Spurt);
        }

        foreach (VoiceSpurt spurt in new List<VoiceSpurt>(_renders.Keys))
        {
            if (!needed.Contains(spurt))
            {
                _renders.Remove(spurt);
            }
        }
    }

    private readonly Dictionary<VoiceSpurt, Task<SoundSample>> _renders = new(ReferenceEqualityComparer.Instance);

    /// <summary>Playback moved forward: packets in <c>(last, tick]</c> arrive.</summary>
    private void Arrive(int last, int tick)
    {
        while (_next < _plan.Count && _plan[_next].Packets[0].Tick <= tick)
        {
            VoiceSpurt spurt = _plan[_next++];

            if (spurt.Packets[0].Tick > last)
            {
                Queue(spurt, 0, spurt.StartTick);
            }
        }

        // A starved channel is assigned afresh by its next packet.
        for (int i = _starved.Count - 1; i >= 0; i--)
        {
            (VoiceSpurt spurt, int next) = _starved[i];
            VoiceSpurtPacket packet = spurt.Packets[next];

            if (packet.Tick <= tick)
            {
                _starved.RemoveAt(i);
                Queue(spurt, next, packet.Tick + spurt.PadTicks);
            }
        }
    }

    /// <summary>The demo is not advancing: nothing arrives, and a voice that has played everything that arrived goes quiet.</summary>
    private void Stall(IAudioSink output, int tick, double now)
    {
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            Playing playing = _playing[i];
            int next = NextPacket(playing.Spurt, tick + 1);

            if (next < 0)
            {
                // Everything arrived; the sound plays out and ends of itself.
                continue;
            }

            double arrived = (double)(playing.Spurt.Packets[next].Offset - playing.FirstSample) / playing.Spurt.SampleRate;

            if (now - playing.StartedAt >= arrived)
            {
                output.Silence(playing.Entity, playing.Channel);
                _playing.RemoveAt(i);
                _starved.Add((playing.Spurt, next));
            }
        }
    }

    /// <summary>Queues every spurt with packets still to arrive at or after the tick, from the next one, behind a fresh pad.</summary>
    private void Resume(int tick)
    {
        _pending.Clear();
        _starved.Clear();
        _next = 0;

        while (_next < _plan.Count && _plan[_next].Packets[0].Tick <= tick)
        {
            VoiceSpurt spurt = _plan[_next++];
            int next = NextPacket(spurt, tick);

            if (next >= 0)
            {
                Queue(spurt, next, spurt.Packets[next].Tick + spurt.PadTicks);
            }
        }

        Prune();
    }

    /// <summary>The first packet arriving at or after a tick, or -1.</summary>
    private static int NextPacket(VoiceSpurt spurt, int tick)
    {
        for (int i = 0; i < spurt.Packets.Count; i++)
        {
            if (spurt.Packets[i].Tick >= tick)
            {
                return i;
            }
        }

        return -1;
    }

    private void SilencePlaying(IAudioSink output)
    {
        foreach (Playing playing in _playing)
        {
            output.Silence(playing.Entity, playing.Channel);
        }

        _playing.Clear();
    }

    private void Start(
        IAudioSink output,
        VoiceSpurt spurt,
        int packet,
        SoundSample rendered,
        int skip,
        double now,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        int viewEntity,
        Func<int, (float X, float Y, float Z)?> speakerAt)
    {
        // Proximity: SNDLVL_80dB from the speaker. Otherwise SNDLVL_60dB with the view entity as the source.
        (float X, float Y, float Z) source = spurt.Proximity && speakerAt(spurt.Entity) is { } at ? at : listener;
        int entity = spurt.Proximity ? spurt.Entity : viewEntity;
        int channel = VoicePlan.EngineChannel(spurt.Channel);

        float distance = MathF.Sqrt(
            ((source.X - listener.X) * (source.X - listener.X)) +
            ((source.Y - listener.Y) * (source.Y - listener.Y)) +
            ((source.Z - listener.Z) * (source.Z - listener.Z)));

        (float left, float rightGain) = SoundGain.Pan(SoundGain.Rightward(listener, right, source));

        // From the packet the channel was assigned at, less what the mixer had already read: a slice, not a re-render.
        int from = Math.Min(VoicePlan.KeptOffset(spurt, spurt.Packets[packet].Offset) + skip, rendered.Samples.Length);
        SoundSample sample = rendered with { Samples = rendered.Samples[from..] };
        output.Play(sample, left, rightGain, SoundGain.AtDistance(spurt.Proximity ? 80 : 60, distance), 1f, entity, channel);

        // A channel holds one spurt at a time, so whatever last played on it has ended.
        _playing.RemoveAll(playing => playing.Channel == channel);
        _playing.Add(new Playing(spurt, entity, channel, now - ((double)skip / spurt.SampleRate), spurt.Packets[packet].Offset));
        Prune();
    }

    private readonly record struct Pending(VoiceSpurt Spurt, int Packet, int AtTick, Task<SoundSample> Sound);

    private readonly record struct Playing(VoiceSpurt Spurt, int Entity, int Channel, double StartedAt, int FirstSample);
}
