using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `snd_musicvolume` at the sink: the gain a `#` one-shot at `SNDLVL_NONE` is started with, and that it follows a
/// change while it plays, as the engine's per-mix channel gain does (FUN_1800292f0, B545;
/// <c>SoundMusicVolumeConformanceTests</c> holds the citation).
/// </summary>
public sealed class SoundPresenterMusicVolumeTests
{
    private const int Entity = 3;
    private const int Channel = 0;

    [Test]
    public void Update_AMusicOneShot_PlaysAtVolumeTimesMusicVolumeAndFollowsAChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter("#music/song.wav", () => musicVolume);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);

        sink.Started.ShouldBe([0.2f], "volume 0.8 × snd_musicvolume 0.25");

        musicVolume = 0.5f;
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Gained.ShouldBe([0.4f], "re-gained on the next pass while it plays");
    }

    [Test]
    public void Update_AMusicOneShotAtMusicVolumeZero_StillStartsSoRaisingItIsHeard()
    {
        float musicVolume = 0f;
        (SoundPresenter presenter, Sink sink) = Presenter("#music/song.wav", () => musicVolume);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        musicVolume = 1f;
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Started.ShouldBe([0f]);
        sink.Gained[^1].ShouldBe(0.8f);
    }

    [Test]
    public void Update_AnOrdinaryOneShot_IgnoresMusicVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter("music/song.wav", static () => 0.25f);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);

        sink.Started.ShouldBe([0.8f]);
    }

    private static (SoundPresenter Presenter, Sink Sink) Presenter(string name, System.Func<float> musicVolume)
    {
        SoundSample sample = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f }, Loops: false);
        SceneSound sound = new(50, name, 1, Entity, Channel, 0.8f, 0, 100, 0f, 0f, 0f, 0f);

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            _ => sample,
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule([sound]),
            MusicVolume = musicVolume,
        };

        return (presenter, new Sink());
    }

    private sealed class Sink : IAudioSink
    {
        private bool _playing;

        public float MasterGain { get; set; } = 1f;

        public List<float> Started { get; } = [];

        public List<float> Gained { get; } = [];

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Started.Add(gain);
            _playing = true;
        }

        public bool SetGain(int entity, int channel, float gain)
        {
            if (_playing && entity == Entity && channel == Channel)
            {
                Gained.Add(gain);
            }

            return _playing;
        }

        public bool SetPitch(int entity, int channel, float pitch) => _playing;

        public void SilenceAll() => _playing = false;

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) => _playing = false;

        public void Silence(int entity, int channel, SoundSample sample) => _playing = false;
    }
}
