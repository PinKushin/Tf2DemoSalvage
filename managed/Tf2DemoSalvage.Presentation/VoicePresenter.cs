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
public sealed class VoicePresenter : IDisposable
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
            ClearRenders();
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

        now = MixerTime(now);
        _fadeSeconds = Math.Max(1e-3, EngineConVars.ByName("voice_fadeouttime").Number);

        if (!Enabled)
        {
            // `Voice_Idle` ends every channel; nothing queued survives (`Voice_Deinit`).
            SilencePlaying(output);
            _pending.Clear();
            _starved.Clear();
            Prune();
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
            // Stalled: nothing arrives, but the mixer reads on; a start whose pad has run out and whose decode has just
            // finished still starts below.
            Starve(output, tick, now);
        }
        else
        {
            Arrive(last, tick);
            Starve(output, tick, now);
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
                // Recorded for the caller's log rather than thrown on the frame; forgotten so a resume decodes it again.
                Failure = due.Sound.Exception?.GetBaseException().Message;
                Prune();
                continue;
            }

            int skip = (int)Math.Round((tick - due.AtTick) * (double)Interval * due.Spurt.SampleRate);
            Start(output, due.Spurt, due.Packet, due.Sound.Result, skip, now, listener, right, viewEntity, speakerAt);
        }
    }

    /// <summary>The last decode that failed, for the caller to log; null when none has. Written from the decode's own thread too.</summary>
    public string? Failure
    {
        get => System.Threading.Volatile.Read(ref _failure);
        private set => System.Threading.Volatile.Write(ref _failure, value);
    }

    private string? _failure;

    /// <summary>Runs a start's decode; off the frame by default, inline for a test.</summary>
    public Func<Func<SoundSample>, Task<SoundSample>> Renderer { get; init; } = Task.Run;

    /// <summary>
    /// Queues a start and begins decoding it now, from the packet that assigns the channel, with a fresh decoder and gain —
    /// as <c>Voice_AssignChannel</c> resets both and reads <c>voice_scale</c> — off the frame, so the pad's lead hides it.
    /// </summary>
    /// <remarks>
    /// **A decode not finished when the pad drains starts late, part-way in.** The engine has no such case: it decodes
    /// each packet as it arrives (<c>Voice_AddIncomingData</c>), so by the pad's end the buffer holds what arrived, and the
    /// mixer reads from its start. Skipping what the mixer would have read since is the nearest match.
    /// A queued decode that nothing will play — superseded by a jump — is cancelled.
    /// </remarks>
    private void Queue(VoiceSpurt spurt, int packet, int atTick)
    {
        // Keyed with the scale too: each start reads `voice_scale` afresh, as `Voice_AssignChannel` does.
        float scale = Scale;

        if (!_renders.TryGetValue((spurt, packet, scale), out Rendering? rendering))
        {
            Func<VoiceKind, IVoiceStreamDecoder> decoders = Decoders;
            // CA2000 is a false positive: ownership moves into `_renders`, and `Prune` cancels and disposes it.
#pragma warning disable CA2000
            System.Threading.CancellationTokenSource cancel = new();
#pragma warning restore CA2000
            System.Threading.CancellationToken token = cancel.Token;
            Task<SoundSample> sound;

            try
            {
                sound = Renderer(() => VoicePlan.Render(spurt, packet, 0, scale, decoders, token));
            }
            catch
            {
                // A renderer that throws before handing back a task leaves the source with nobody to dispose it.
                cancel.Dispose();
                throw;
            }

            // Observed even when nothing reaches its start, so a fault is reported rather than unobserved.
            _ = sound.ContinueWith(
                faulted => Failure = faulted.Exception?.GetBaseException().Message,
                System.Threading.CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            rendering = new Rendering(sound, cancel);
            _renders[(spurt, packet, scale)] = rendering;
        }

        _pending.Add(new Pending(spurt, packet, atTick, rendering.Sound, scale));
    }

    /// <summary>Cancels and drops the decodes nothing pending still needs; a started sound holds its own samples.</summary>
    private void Prune()
    {
        foreach ((VoiceSpurt, int, float) key in new List<(VoiceSpurt, int, float)>(_renders.Keys))
        {
            // A failed decode is kept, so a jump every frame does not decode a known-bad run again every frame; it holds no
            // samples, and goes with the plan.
            if (!_renders[key].Sound.IsFaulted && !_pending.Exists(pending =>
                ReferenceEquals(pending.Spurt, key.Item1) && pending.Packet == key.Item2 &&
                BitConverter.SingleToInt32Bits(pending.Scale) == BitConverter.SingleToInt32Bits(key.Item3)))
            {
                _renders[key].Cancel.Cancel();
                _renders[key].Cancel.Dispose();
                _renders.Remove(key);
            }
        }
    }

    private readonly Dictionary<(VoiceSpurt, int, float), Rendering> _renders = [];

    private sealed record Rendering(Task<SoundSample> Sound, System.Threading.CancellationTokenSource Cancel);

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

    /// <summary>
    /// The mixer reads in real time and packets arrive in demo time: a voice that has played everything that has arrived is
    /// starved and ended (<c>Voice_Idle</c>), stalled or not — so slow playback stutters, as the engine's does.
    /// </summary>
    private void Starve(IAudioSink output, int tick, double now)
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

            // In samples the buffer kept: what it overwrote never reaches the mixer.
            double arrived = (double)(VoicePlan.KeptOffset(playing.Spurt, playing.Spurt.Packets[next].Offset) -
                VoicePlan.KeptOffset(playing.Spurt, playing.FirstSample)) / playing.Spurt.SampleRate;

            double left = arrived - (now - playing.StartedAt);

            // The mixer fades the last `voice_fadeouttime` of what is buffered (FUN_180057dc0): followed per voice as the
            // read closes on the end of what arrived, and lifted again if more arrives first.
            if (playing.Voice != 0 && left > 0d)
            {
                output.SetVoiceGain(playing.Voice, playing.Gain * (float)Math.Min(1d, left / FadeSeconds));
            }

            if (left <= 0d)
            {
                output.Silence(playing.Entity, playing.Channel, playing.Sample);
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
            // The one voice this started, matched by its own sample, so nothing else on the channel stops (B545).
            output.Silence(playing.Entity, playing.Channel, playing.Sample);
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

        // Rendered from the packet the channel was assigned at; what the mixer had already read is sliced off.
        int from = Math.Min(skip, rendered.Samples.Length);
        SoundSample sample = rendered with { Samples = rendered.Samples[from..] };

        if (sample.Samples.IsEmpty)
        {
            // A channel run of no samples — Steam Voice's silence records — held its channel through the pad, as the
            // engine's does, and the mixer's first read starves it: there is nothing to hand the device.
            return;
        }

        float gain = SoundGain.AtDistance(spurt.Proximity ? 80 : 60, distance);
        int voice = output.Play(sample, left, rightGain, gain, 1f, entity, channel);

        // A channel holds one spurt at a time, so whatever last played on it has ended.
        _playing.RemoveAll(playing => playing.Channel == channel);
        _playing.Add(new Playing(
            spurt, entity, channel, sample, now - ((double)skip / spurt.SampleRate), spurt.Packets[packet].Offset, voice, gain));
        Prune();
    }

    private readonly record struct Pending(VoiceSpurt Spurt, int Packet, int AtTick, Task<SoundSample> Sound, float Scale);

    private readonly record struct Playing(
        VoiceSpurt Spurt, int Entity, int Channel, SoundSample Sample, double StartedAt, int FirstSample, int Voice, float Gain);

    /// <summary><c>voice_fadeouttime</c>, the mixer's fade over the end of what is buffered, read once per pass.</summary>
    private double FadeSeconds => _fadeSeconds;

    private double _fadeSeconds = 0.1;

    /// <summary>The most mixer time one pass may advance: <c>snd_mixahead</c>'s default, 0.1 s.</summary>
    private const double MostMixedPerPass = 0.1;

    private double? _wallBefore;
    private double _mixed;

    /// <summary>
    /// The mixer's clock: wall time, but advancing at most <see cref="MostMixedPerPass"/> a pass, so a long hitch — a map
    /// load — does not drain every voice buffer at once.
    /// </summary>
    /// <remarks>
    /// **Interpolated, not read** (the binary was not available for this pass): Source's mixer paints at most
    /// <c>snd_mixahead</c> ahead of the device and, after a stall, resumes from where the device is rather than painting the
    /// gap — Quake's <c>S_Update_</c> moves <c>paintedtime</c> up to <c>soundtime</c> — so a voice channel's buffer is read
    /// only by what is painted. Listed in findings/31 as ours until <c>S_Update_</c> is read in TF2's engine.dll.
    /// </remarks>
    private double MixerTime(double wall)
    {
        if (_wallBefore is { } before)
        {
            _mixed += Math.Clamp(wall - before, 0d, MostMixedPerPass);
        }

        _wallBefore = wall;
        return _mixed;
    }

    /// <inheritdoc/>
    public void Dispose() => ClearRenders();

    /// <summary>Cancels and drops every decode, failed ones included.</summary>
    private void ClearRenders()
    {
        foreach (Rendering rendering in _renders.Values)
        {
            rendering.Cancel.Cancel();
            rendering.Cancel.Dispose();
        }

        _renders.Clear();
    }
}
