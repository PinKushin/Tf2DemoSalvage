using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Plays a demo's voice as playback reaches it, the way the engine's voice channels sound.</summary>
/// <remarks>
/// **The sound is <c>VoiceSE_StartChannel</c>'s** (engine.dll x64 <c>FUN_18005a670</c>): channel <c>8 + index</c>, volume 1,
/// pitch 100; a proximity channel at <c>SNDLVL_80dB</c> from the speaker, any other at <c>SNDLVL_60dB</c> from the view
/// entity, which puts it at the listener — centred and unattenuated. Like every other one-shot here, the pan and gain
/// are fixed when the sound starts.
///
/// **Seek and pause follow the engine's buffer** (<c>Voice_Idle</c>, FUN_180058090): it only holds what has arrived.
/// Paused, nothing arrives, so a playing voice is silenced once the pad has drained. After a jump, or on resuming, a
/// spurt whose packets are still arriving restarts from the next packet, behind a fresh pad.
/// </remarks>
public sealed class VoicePresenter
{
    private readonly List<(VoiceSpurt Spurt, int Offset, int AtTick)> _pending = [];
    private readonly List<VoiceSpurt> _playing = [];
    private IReadOnlyList<VoiceSpurt> _plan = [];
    private int? _lastTick;
    private double _lastAdvance;
    private bool _starved;

    /// <summary>The demo's voice, laid out by <see cref="VoicePlan"/>; setting it starts over.</summary>
    public IReadOnlyList<VoiceSpurt> Plan
    {
        get => _plan;
        set
        {
            _plan = value ?? [];
            _pending.Clear();
            _playing.Clear();
            _lastTick = null;
            _starved = false;
        }
    }

    /// <summary><c>voice_scale</c>, read as each spurt starts, as <c>Voice_AssignChannel</c> reads it.</summary>
    public float Scale { get; set; } = EngineConVars.ByName("voice_scale").Number;

    /// <summary>Brings voice up to date for one pass.</summary>
    /// <param name="output">Where sound goes.</param>
    /// <param name="tick">The tick being played.</param>
    /// <param name="jumped">Whether playback jumped here rather than playing to it — everything in flight was silenced.</param>
    /// <param name="listener">Where the ears are.</param>
    /// <param name="right">The listener's right vector.</param>
    /// <param name="now">Seconds on a wall clock, for the pad draining while paused.</param>
    /// <param name="speakerAt">Where an entity stands at this tick, or null when it is not known.</param>
    public void Update(
        IAudioSink output,
        int tick,
        bool jumped,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        double now,
        Func<int, (float X, float Y, float Z)?> speakerAt)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(speakerAt);

        if (_lastTick is not { } last || jumped || tick < last)
        {
            // Nothing that was in flight survives; speech still arriving resumes from its next packet.
            _playing.Clear();
            Resume(tick);
        }
        else if (tick == last)
        {
            if (!_starved && now - _lastAdvance > VoicePlan.PadSeconds)
            {
                // Paused: no packet arrives, the buffer drains, `Voice_Idle` ends the channel.
                foreach (VoiceSpurt spurt in _playing)
                {
                    output.Silence(spurt.Entity, VoicePlan.EngineChannel(spurt.Channel));
                }

                _playing.Clear();
                _pending.Clear();
                _starved = true;
            }

            return;
        }
        else if (_starved)
        {
            Resume(tick);
        }
        else
        {
            // A spurt is queued when its first packet arrives, and sounds when its pad has drained.
            foreach (VoiceSpurt spurt in _plan.Where(spurt => spurt.Packets[0].Tick > last && spurt.Packets[0].Tick <= tick))
            {
                _pending.Add((spurt, 0, spurt.StartTick));
            }
        }

        _lastTick = tick;
        _lastAdvance = now;
        _starved = false;

        foreach ((VoiceSpurt spurt, int offset, int _) in _pending.Where(entry => entry.AtTick <= tick).ToList())
        {
            Start(output, spurt, offset, listener, right, speakerAt);
        }

        _pending.RemoveAll(entry => entry.AtTick <= tick);
    }

    /// <summary>Queues every spurt with packets still to arrive, from the first at or after the tick, behind a fresh pad.</summary>
    private void Resume(int tick)
    {
        _pending.Clear();

        foreach (VoiceSpurt spurt in _plan)
        {
            if (spurt.Packets[0].Tick > tick || spurt.Packets[^1].Tick < tick)
            {
                // Not begun — the ordinary crossing queues it — or finished arriving.
                continue;
            }

            VoicePacketSpan next = spurt.Packets.First(packet => packet.Tick >= tick);
            _pending.Add((spurt, next.Offset, tick + (spurt.StartTick - spurt.Packets[0].Tick)));
        }
    }

    private void Start(
        IAudioSink output,
        VoiceSpurt spurt,
        int offset,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        Func<int, (float X, float Y, float Z)?> speakerAt)
    {
        // Proximity: SNDLVL_80dB from the speaker. Otherwise SNDLVL_60dB from the view entity, which is the listener.
        (float X, float Y, float Z) source = spurt.Proximity && speakerAt(spurt.Entity) is { } at ? at : listener;
        int soundLevel = spurt.Proximity ? 80 : 60;

        float distance = MathF.Sqrt(
            ((source.X - listener.X) * (source.X - listener.X)) +
            ((source.Y - listener.Y) * (source.Y - listener.Y)) +
            ((source.Z - listener.Z) * (source.Z - listener.Z)));

        (float left, float rightGain) = SoundGain.Pan(SoundGain.Rightward(listener, right, source));

        output.Play(
            VoicePlan.Render(spurt, offset, Scale),
            left,
            rightGain,
            SoundGain.AtDistance(soundLevel, distance),
            1f,
            spurt.Entity,
            VoicePlan.EngineChannel(spurt.Channel));

        // A channel holds one spurt at a time, so whatever last played on it has ended.
        _playing.RemoveAll(playing => playing.Channel == spurt.Channel);
        _playing.Add(spurt);
    }
}
