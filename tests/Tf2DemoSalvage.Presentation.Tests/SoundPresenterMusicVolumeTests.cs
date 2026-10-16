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

    [Test]
    public void Update_AnOrdinarySoundAfterMusicOnTheSameChannel_KeepsItsOwnGain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "ui/beep.wav", 0.6f));

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Started.ShouldBe([0.2f, 0.6f]);
        sink.Gained.ShouldBeEmpty("the channel no longer plays music, so nothing re-gains it to music × 0.25");
    }

    /// <remarks>
    /// `S_AlterChannel` matches by <c>CSfxTable</c>, which `S_FindName` keys on the whole name lower-cased with its
    /// separators fixed (FUN_18002b290, `utlsymbol.cpp:312`): case and slash direction do not matter, the `#` does.
    /// </remarks>
    [TestCase("#music/song.wav", true)]
    [TestCase("#MUSIC/Song.WAV", true)]
    [TestCase("#music\\song.wav", true)]
    [TestCase("#music//song.wav", true)]
    [TestCase("music/song.wav", false)]
    public void Update_AStopOnAMusicChannel_ForgetsTheMusicWhenItIsTheSameSfx(string stop, bool forgets)
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, stop, 0.8f) with { IsStop = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 52, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        presenter.Update(sink, 53, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.15d);
        sink.Gained.ShouldBe([0.2f], "followed before the stop");

        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);
        sink.Replay();
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.3d);

        // Pass 60 re-gains before the stop it carries, so 2; pass 61 adds one only for a stop that missed.
        sink.Gained.Count.ShouldBe(forgets ? 2 : 3);
    }

    [Test]
    public void Update_AVolumeChangeAfterTheMusicEnded_StartsItAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 52, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);

        // The voice survives this pass's re-gain and has ended by the change.
        sink.GainsLeft = 1;
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Started.ShouldBe([0.2f, 0.1f], "S_AlterChannel matched nothing, so S_StartSound starts it");
    }

    [Test]
    public void Update_APitchChangeAloneOnMusic_KeepsItsVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.1f) with { ChangesPitch = true, Pitch = 120 });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Started.ShouldBe([0.2f]);
        sink.Pitched.ShouldBe([1.2f]);
        sink.Gained[^1].ShouldBe(0.2f, "a pitch-only change carries no volume to take");
    }

    [Test]
    public void Update_AChangeNamingAnotherSound_StartsIt()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/other.wav", 0.4f) with { ChangesVolume = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);

        sink.Started.ShouldBe([0.2f, 0.1f]);
    }

    [Test]
    public void Update_AStopNamingTheMusicOnALoopsKey_LeavesTheLoopFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.8f) with { IsStop = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        sink.Replay();
        sink.Gained.Clear();
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Gained.ShouldBe([0.6f], "only the loop is re-gained");
    }

    [Test]
    public void Update_AStopNamingAnotherSound_KeepsFollowingTheMusic()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/other.wav", 0.8f) with { IsStop = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        sink.Replay();
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Gained.ShouldBe([0.2f], "S_AlterChannel stops only the sound a stop names");
    }

    [Test]
    public void Update_AVolumeChangeOnAMusicOneShot_AltersItAndKeepsTheNewVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Started.ShouldBe([0.2f], "altered in place, not restarted");
        sink.Gained.ShouldBe([0.1f, 0.1f], "the change, then the next pass keeping it");
    }

    [Test]
    public void Update_WhilePaused_AppliesAMusicVolumeChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter("#music/song.wav", () => musicVolume);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        musicVolume = 0.5f;
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Gained.ShouldBe([0.4f], "a paused pass is the same tick again, and still re-gains");
    }

    [Test]
    public void Update_AMusicOneShotOnALoopsChannel_LeavesTheLoopsGain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(55, "#music/song.wav", 0.8f));

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.1d);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.2d);

        sink.Gained[^1].ShouldBe(0.6f, "the loop is gained last, so it is what the key is left at");
    }

    private static SceneSound Sound(int tick, string name, float volume) =>
        new(tick, name, 1, Entity, Channel, volume, 0, 100, 0f, 0f, 0f, 0f);

    private static (SoundPresenter Presenter, Sink Sink) Presenter(string name, System.Func<float> musicVolume) =>
        Presenter(musicVolume, Sound(50, name, 0.8f));

    private static (SoundPresenter Presenter, Sink Sink) Presenter(System.Func<float> musicVolume, params SceneSound[] sounds)
    {
        SoundSample sample = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f }, Loops: false);
        SoundSample loop = sample with { Loops = true };

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => name.EndsWith("_loop.wav", System.StringComparison.Ordinal) ? loop : sample,
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule(sounds),
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

        public List<float> Pitched { get; } = [];

        /// <summary>How many more gains the voice lives through before it has ended.</summary>
        public int GainsLeft { get; set; } = int.MaxValue;

        public bool SetGain(int entity, int channel, float gain)
        {
            if (_playing && GainsLeft-- <= 0)
            {
                _playing = false;
            }

            if (_playing && entity == Entity && channel == Channel)
            {
                Gained.Add(gain);
            }

            return _playing;
        }

        public bool SetPitch(int entity, int channel, float pitch)
        {
            if (_playing)
            {
                Pitched.Add(pitch);
            }

            return _playing;
        }

        /// <summary>Something else starts on the stopped channel, so a stale entry would find a voice to re-gain.</summary>
        public void Replay() => _playing = true;

        public void SilenceAll() => _playing = false;

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) => _playing = false;

        public void Silence(int entity, int channel, SoundSample sample) => _playing = false;
    }
}
