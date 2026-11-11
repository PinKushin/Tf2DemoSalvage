using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What a seek does to sound: the engine's demo skip, read from engine.dll (B550).
/// </summary>
/// <remarks>
/// - `CDemoPlayer::SkipToTick` (FUN_180074e10): a target behind RELOADS the demo through `StartPlayback` (FUN_180074ed0) and
///   skips from its start; a target ahead only sets the skip tick. Opening a demo is a `StartPlayback` as well.
/// - `StartPlayback` opens with `SCR_BeginLoadingPlaque` (FUN_1800e9ba0), which calls `S_StopAllSounds( true )`
///   (FUN_18002ffb0). Nothing on the way ahead stops anything.
/// - `CL_DispatchSound` (FUN_18008caf0) drops a sound while skipping unless it is `CHAN_STATIC` or carries `SND_STOP`.
/// </remarks>
public sealed class SoundPresenterSkipConformanceTests
{
    private const string Hum = "ambient/machine_hum.wav";
    private const string Bell = "ambient/bell.wav";
    private const string Door = "doors/door_metal_rusty_move1.wav";
    private const string Shot = "weapons/shot.wav";

    private const int Static = SoundScript.StaticChannel;
    private const int Body = 4;
    private const int Weapon = 1;

    [Test]
    public void Update_ASkipAhead_LeavesWhatPlaysPlaying()
    {
        Recording sink = Run([Sound(50, Door, 30, Body)], 40, 50, 1000);

        sink.Played.ShouldBe([Door]);
        sink.SilencedAll.ShouldBe(1, "only the open's StartPlayback stops everything; a skip ahead stops nothing");
    }

    [Test]
    public void Update_ASkipAhead_StartsTheStaticSoundsAndStopsAndDropsTheDynamicOnes()
    {
        Recording sink = Run(
            [
                Sound(100, Hum, 20, Static),
                Sound(150, Hum, 24, Static),
                Sound(200, Bell, 21, Static),
                Sound(300, Shot, 22, Weapon),
                Sound(300, Door, 23, Body),
                Sound(400, Hum, 24, Static) with { IsStop = true },
            ],
            40,
            41,
            1000);

        sink.Played.ShouldBe([Hum, Hum, Bell], "a static one-shot plays at the landing as a static loop does");
        sink.Stopped.ShouldBe([(24, Static, Hum)]);
    }

    [Test]
    public void Update_ASkipAheadThenOneBack_StopsOnlyForTheReloadAndStartsTheStaticSoundsFromTheStart()
    {
        Recording sink = Run([Sound(50, Shot, 22, Weapon), Sound(100, Hum, 20, Static)], 40, 60, 1000, 500);

        sink.SilencedAll.ShouldBe(2, "the open and the reload each stop everything");
        sink.Played.ShouldBe([Shot, Hum, Hum], "the reload's skip starts the hum again and drops the shot");
    }

    [Test]
    public void Update_OpeningAtATick_StartsOnlyTheStaticSoundsBeforeIt()
    {
        Recording sink = Run([Sound(4, Hum, 20, Static), Sound(4, Door, 23, Body)], 1000);

        sink.Played.ShouldBe([Hum]);
    }

    /// <summary>Half-open as playback is: the tick already played does not dispatch again, the tick skipped to does.</summary>
    [Test]
    public void Update_ASkipAhead_DispatchesTheTargetsTickAndNotThePlayedOne()
    {
        Recording sink = Run([Sound(41, Hum, 20, Static), Sound(1000, Bell, 21, Static)], 40, 41, 1000);

        sink.Played.ShouldBe([Hum, Bell], "the hum at 41 played as playback reached it, and the skip does not start it again");
    }

    /// <remarks>
    /// **The frames dispatch, and the landing goes on from where they read** (`CL_DispatchSound` runs as each batch is read). The walk's
    /// reload stops everything at its first frame; the landing stops nothing more.
    /// </remarks>
    [Test]
    public void PlaySkipFrames_AWalkBack_StopsAtItsFirstFrameAndTheLandingGoesOnFromItsFrames()
    {
        (SoundPresenter presenter, Recording sink) = Presenter(
            [Sound(100, Hum, 20, Static), Sound(200, Bell, 21, Static), Sound(300, Hum, 25, Static)]);

        Update(presenter, sink, 40);
        Update(presenter, sink, 41);
        Update(presenter, sink, 1000);
        sink.Played.ShouldBe([Hum, Bell, Hum], "the control: the skip ahead dispatched all three");

        presenter.SkipFrameRead(139, reload: true);
        presenter.PlaySkipFrames(sink, (0f, 0f, 0f), (0f, -1f, 0f));
        sink.SilencedAll.ShouldBe(2, "the walk back's first frame is the reload");

        presenter.SkipFrameRead(238, reload: false);
        presenter.PlaySkipFrames(sink, (0f, 0f, 0f), (0f, -1f, 0f));
        Update(presenter, sink, 500);

        sink.Played.ShouldBe([Hum, Bell, Hum, Hum, Bell, Hum], "each frame its batch, the landing the rest");
        sink.SilencedAll.ShouldBe(2, "the landing does not stop again what the frames started");
        presenter.Reloaded.ShouldBeTrue();
    }

    private static void Update(SoundPresenter presenter, Recording sink, int tick) =>
        presenter.Update(sink, tick, (0f, 0f, 0f), (0f, -1f, 0f), tick * 0.015d);

    private static Recording Run(SceneSound[] sounds, params int[] ticks)
    {
        (SoundPresenter presenter, Recording sink) = Presenter(sounds);

        foreach (int tick in ticks)
        {
            Update(presenter, sink, tick);
        }

        return sink;
    }

    private static (SoundPresenter Presenter, Recording Sink) Presenter(SceneSound[] sounds)
    {
        Dictionary<string, SoundSample> samples = new(StringComparer.Ordinal)
        {
            [Hum] = new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }, Loops: true) { Key = Hum },
            [Bell] = new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }) { Key = Bell },
            [Door] = new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }, Loops: true) { Key = Door },
            [Shot] = new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }) { Key = Shot },
        };

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => samples.TryGetValue(name, out SoundSample sample) ? sample : null,
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule(sounds),
            Held = name => samples.TryGetValue(name, out SoundSample sample) ? sample : null,
        };

        return (presenter, new Recording());
    }

    private static SceneSound Sound(int tick, string name, int entity, int channel) =>
        new(tick, name, 1, entity, channel, 1f, 75, 100, 0f, 0f, 0f, 0f);

    private sealed class Recording : IAudioSink
    {
        private readonly Dictionary<(int, int), List<string>> _playing = [];

        public float MasterGain { get; set; } = 1f;

        public List<string> Played { get; } = [];

        public List<(int Entity, int Channel, string Sound)> Stopped { get; } = [];

        public int SilencedAll { get; private set; }

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            string name = sample.Key ?? string.Empty;

            Played.Add(name);

            if (!_playing.TryGetValue((entity, channel), out List<string>? on))
            {
                _playing[(entity, channel)] = on = [];
            }

            on.Add(name);

            return 0;
        }

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetGain(int entity, int channel, float gain) => _playing.ContainsKey((entity, channel));

        public bool SetPitch(int entity, int channel, float pitch) => _playing.ContainsKey((entity, channel));

        public void SilenceAll()
        {
            SilencedAll++;
            _playing.Clear();
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) => _playing.Remove((entity, channel));

        public int Silence(int entity, int channel, SoundSample sample)
        {
            if (_playing.TryGetValue((entity, channel), out List<string>? on) && sample.Key is { } name && on.Remove(name))
            {
                Stopped.Add((entity, channel, name));
            }

            return 0;
        }
    }
}
