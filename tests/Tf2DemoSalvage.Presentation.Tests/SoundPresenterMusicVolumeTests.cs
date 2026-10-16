using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `snd_musicvolume` at the sink: the gain a `#` one-shot at `SNDLVL_NONE` is started with, and that it follows a
/// change while it plays, as the engine's per-mix channel gain does (FUN_1800292f0, B545;
/// <c>SoundMusicVolumeConformanceTests</c> holds the citation).
/// </summary>
/// <remarks>
/// The sink here keeps voices as the engine keeps channels: each start is its own voice, a named channel's next start
/// ends the one before (`SND_PickDynamicChannel`, FUN_18002a330, hands back the busy channel), CHAN_AUTO never does,
/// and a stop ends the first voice on its key playing that sound (`S_AlterChannel`, FUN_18002aa20).
/// </remarks>
public sealed class SoundPresenterMusicVolumeTests
{
    private const int Entity = 3;
    private const int AutoChannel = 0;
    private const int WeaponChannel = 1;

    [Test]
    public void Update_AMusicOneShot_PlaysAtVolumeTimesMusicVolumeAndFollowsAChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        sink.Started.ShouldBe([0.2f], "volume 0.8 × snd_musicvolume 0.25");

        musicVolume = 0.5f;
        Run(presenter, sink, 61);

        sink.Gained.ShouldBe([0.4f], "re-gained on the next pass while it plays");
    }

    [Test]
    public void Update_AMusicOneShotAtMusicVolumeZero_StillStartsSoRaisingItIsHeard()
    {
        float musicVolume = 0f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        musicVolume = 1f;
        Run(presenter, sink, 61);

        sink.Started.ShouldBe([0f]);
        sink.Gained[^1].ShouldBe(0.8f);
    }

    [Test]
    public void Update_AnOrdinaryOneShot_IgnoresMusicVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(static () => 0.25f, Sound(50, "music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);

        sink.Started.ShouldBe([0.8f]);
    }

    /// <remarks>CHAN_AUTO gives each start its own channel, so the music keeps following and the beep keeps its own gain.</remarks>
    [Test]
    public void Update_AnOrdinarySoundOverlappingMusicOnAuto_LeavesBothTheirOwnGains()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "ui/beep.wav", 0.6f));

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f, 0.6f]);
        sink.GainedBy.ShouldBe([(1, 0.2f)], "only the music voice is re-gained, and to its own level");
    }

    [Test]
    public void Update_AnOrdinarySoundReplacingMusicOnANamedChannel_EndsTheFollowing()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f, WeaponChannel),
            Sound(55, "ui/beep.wav", 0.6f, WeaponChannel));

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f, 0.6f]);
        sink.Gained.ShouldBeEmpty("the beep took the music's channel, and nothing re-gains it to music × 0.25");
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

        Run(presenter, sink, 40, 52, 53);
        sink.Gained.ShouldBe([0.2f], "followed before the stop");

        // Pass 60 re-gains before the stop it carries, so 2; pass 61 adds one only for a stop that missed.
        Run(presenter, sink, 60, 61);

        sink.Gained.Count.ShouldBe(forgets ? 2 : 3);
    }

    [Test]
    public void Update_AStopNamingAnotherSound_KeepsFollowingTheMusic()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/other.wav", 0.8f) with { IsStop = true });

        Run(presenter, sink, 40, 60, 61);

        sink.Gained.ShouldBe([0.2f], "S_AlterChannel stops only the sound a stop names");
    }

    [Test]
    public void Update_AVolumeChangeOnAMusicOneShot_AltersItAndKeepsTheNewVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f], "altered in place, not restarted");
        sink.Gained.ShouldBe([0.1f, 0.1f], "the change, then the next pass keeping it");
    }

    [Test]
    public void Update_AVolumeChangeAfterTheMusicEnded_StartsItAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 52);

        // The voice survives this pass's re-gain and has ended by the change.
        sink.GainsLeft = 1;
        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.2f, 0.1f], "S_AlterChannel matched nothing, so S_StartSound starts it");
    }

    [Test]
    public void Update_APitchChangeAloneOnMusic_KeepsItsVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.1f) with { ChangesPitch = true, Pitch = 120 });

        Run(presenter, sink, 40, 60, 61);

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

        Run(presenter, sink, 40, 60);

        sink.Started.ShouldBe([0.2f, 0.1f]);
    }

    [Test]
    public void Update_WhilePaused_AppliesAMusicVolumeChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        musicVolume = 0.5f;
        Run(presenter, sink, 60);

        sink.Gained.ShouldBe([0.4f], "a paused pass is the same tick again, and still re-gains");
    }

    [Test]
    public void Update_AMusicOneShotAndALoopOnOneKey_KeepTheirOwnGains()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(55, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60, 61);

        sink.GainedBy.ShouldBe([(2, 0.2f), (1, 0.6f)], "each voice at its own gain, music then the loop");
    }

    [Test]
    public void Update_AStopNamingTheMusicOnALoopsKey_LeavesTheLoopFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.8f) with { IsStop = true });

        Run(presenter, sink, 40, 60);
        sink.GainedBy.Clear();
        Run(presenter, sink, 61);

        sink.GainedBy.ShouldBe([(1, 0.6f)], "only the loop is re-gained");
    }

    /// <remarks>`S_AlterChannel` acts on the first match in channel order, so after a voice ends and another starts, a change
    /// still reaches the oldest one still playing — not the newest, which a hash map reusing the freed slot would list first.</remarks>
    [Test]
    public void Update_AVolumeChangeAfterAVoiceEndedAndAnotherStarted_AltersTheOldestStillPlaying()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "#music/song.wav", 0.5f),
            Sound(51, "#music/song.wav", 0.6f),
            Sound(52, "#music/song.wav", 0.7f),
            Sound(56, "#music/song.wav", 0.8f),
            Sound(58, "#music/song.wav", 0.1f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 53);
        sink.Finish(1);
        Run(presenter, sink, 54, 57);
        sink.GainedBy.Clear();
        Run(presenter, sink, 59);

        // The pass re-gains every music voice first (2, 3, 4 at their volumes), then the change alters one.
        sink.GainedBy.ShouldBe([(2, 0.6f), (3, 0.7f), (4, 0.8f), (2, 0.1f)], "the change reaches voice 2, the first still playing");
    }

    [Test]
    public void Update_AVolumeChangeOnALoopItsChannelReplaced_StartsTheLoopAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f, WeaponChannel),
            Sound(52, "ui/beep.wav", 0.5f, WeaponChannel),
            Sound(55, "ambient/hum_loop.wav", 0.3f, WeaponChannel) with { ChangesVolume = true });

        // Pass 53 starts the beep after its re-gain, so pass 54 is the first to find the loop's voice gone.
        Run(presenter, sink, 40, 51, 53, 54);
        sink.Loops.Count.ShouldBe(0, "the pass that found the replaced voice gone stopped following it");

        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.6f, 0.5f, 0.3f], "the loop's voice was replaced, so the change matched nothing and started it");
    }

    [Test]
    public void Update_AVolumeChangeOnALoopThatEndedThisPass_StartsTheLoopAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f, WeaponChannel),
            Sound(55, "ambient/hum_loop.wav", 0.3f, WeaponChannel) with { ChangesVolume = true });

        Run(presenter, sink, 40, 51);

        // The voice survives this pass's re-gain and has gone by the change.
        sink.GainsLeft = 1;
        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.6f, 0.3f], "S_AlterChannel matched nothing, so the change starts the loop");
    }

    [Test]
    public void Update_TwoAutoLoopsOnOneEntity_AreBothFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "ambient/fan_loop.wav", 0.4f));

        Run(presenter, sink, 40, 60, 61);

        sink.GainedBy.ShouldBe([(1, 0.6f), (2, 0.4f)], "each CHAN_AUTO loop is its own channel");
    }

    private static void Run(SoundPresenter presenter, Sink sink, params int[] ticks)
    {
        foreach (int tick in ticks)
        {
            presenter.Update(sink, tick, (0f, 0f, 0f), (0f, -1f, 0f), now: tick / 100d);
        }
    }

    private static SceneSound Sound(int tick, string name, float volume, int channel = AutoChannel) =>
        new(tick, name, 1, Entity, channel, volume, 0, 100, 0f, 0f, 0f, 0f);

    private static (SoundPresenter Presenter, Sink Sink) Presenter(Func<float> musicVolume, params SceneSound[] sounds)
    {
        ActiveLoops loops = new();

        // One decode per name, as SoundCache keeps it, so a stop's sample names the sound it stops.
        Dictionary<string, SoundSample> samples = new(StringComparer.Ordinal);

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            loops,
            name =>
            {
                if (!samples.TryGetValue(name, out SoundSample sample))
                {
                    sample = new(
                        SampleRate: 22050,
                        Channels: 1,
                        Samples: new float[] { 0.5f, -0.5f },
                        Loops: name.EndsWith("_loop.wav", StringComparison.Ordinal));
                    samples[name] = sample;
                }

                return sample;
            },
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule(sounds),
            MusicVolume = musicVolume,
        };

        return (presenter, new Sink { Loops = loops });
    }

    private sealed class Sink : IAudioSink
    {
        private readonly List<(int Id, int Entity, int Channel, SoundSample Sample, bool Alive)> _voices = [];

        public float MasterGain { get; set; } = 1f;

        /// <summary>The presenter's loops, so a test can see what is still followed.</summary>
        public ActiveLoops Loops { get; init; } = new();

        public List<float> Started { get; } = [];

        public List<(int Voice, float Gain)> GainedBy { get; } = [];

        public List<float> Gained => [.. GainedBy.Select(static gain => gain.Gain)];

        public List<float> Pitched { get; } = [];

        /// <summary>How many more voice gains the voices live through before they have ended.</summary>
        public int GainsLeft { get; set; } = int.MaxValue;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            if (channel != AutoChannel)
            {
                End(index => _voices[index].Entity == entity && _voices[index].Channel == channel, all: true);
            }

            Started.Add(gain);
            _voices.Add((_voices.Count + 1, entity, channel, sample, true));

            return _voices.Count;
        }

        public bool SetVoiceGain(int voice, float gain)
        {
            if (!Alive(voice))
            {
                return false;
            }

            if (GainsLeft-- <= 0)
            {
                End(index => _voices[index].Id == voice, all: true);
                return false;
            }

            GainedBy.Add((voice, gain));
            return true;
        }

        public bool SetVoicePitch(int voice, float pitch)
        {
            if (!Alive(voice))
            {
                return false;
            }

            Pitched.Add(pitch);
            return true;
        }

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll() => End(static _ => true, all: true);

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) =>
            End(index => _voices[index].Entity == entity && _voices[index].Channel == channel, all: true);

        public int Silence(int entity, int channel, SoundSample sample) =>
            End(
                index => _voices[index].Entity == entity && _voices[index].Channel == channel &&
                         _voices[index].Sample.Samples.Equals(sample.Samples),
                all: false);

        /// <summary>One voice finishes on its own.</summary>
        public void Finish(int voice) => End(index => _voices[index].Id == voice, all: true);

        private bool Alive(int voice) => voice > 0 && voice <= _voices.Count && _voices[voice - 1].Alive;

        private int End(Func<int, bool> matches, bool all)
        {
            int first = 0;

            for (int index = 0; index < _voices.Count; index++)
            {
                if (_voices[index].Alive && matches(index))
                {
                    _voices[index] = _voices[index] with { Alive = false };
                    first = first == 0 ? _voices[index].Id : first;

                    if (!all)
                    {
                        break;
                    }
                }
            }

            return first;
        }
    }
}
