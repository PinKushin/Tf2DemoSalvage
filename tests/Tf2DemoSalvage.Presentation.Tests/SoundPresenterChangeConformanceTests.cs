using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `SND_CHANGE_VOL` and `SND_CHANGE_PITCH` (`soundflags.h:118`): `S_StartSound` hands a sound carrying either to
/// `S_AlterChannel`, which finds the channel playing that sound on that entity and channel and changes it in place. Only
/// when nothing matches does it fall through and start the sound.
/// </summary>
public sealed class SoundPresenterChangeConformanceTests
{
    private const string Loop = "physics/metal/metal_box_scrape_rough_loop1.wav";
    private const string Other = "doors/door_metal_rusty_move1.wav";
    private const int Entity = 12;
    private const int Channel = 4;

    [Test]
    public void Update_AChangeForTheLoopPlaying_AltersItRatherThanStartingIt()
    {
        Recording sink = Run(
            Sound(50, Loop, volume: 1f),
            Sound(55, Loop, volume: 0.5f, pitch: 120) with { ChangesVolume = true, ChangesPitch = true });

        sink.Played.ShouldBe([Loop]);
        sink.Pitched.ShouldBe([(Entity, Channel, 1.2f)]);
        sink.Gained[^1].Gain.ShouldBe(0.5f, 1e-6f, "the listener stands on it, so the gain is the new volume");
    }

    [Test]
    public void Update_AChangeWithNothingPlaying_StartsTheSound()
    {
        Recording sink = Run(Sound(55, Loop, volume: 0.5f) with { ChangesVolume = true });

        sink.Played.ShouldBe([Loop]);
    }

    [Test]
    public void Update_AChangeNamingAnotherSound_StartsIt()
    {
        // `S_AlterChannel` matches the sound as well as the entity and channel.
        Recording sink = Run(Sound(50, Loop, volume: 1f), Sound(55, Other, volume: 0.5f) with { ChangesVolume = true });

        sink.Played.ShouldBe([Loop, Other]);
    }

    /// <remarks>
    /// `SND_STOP` goes through `S_AlterChannel` too (B416, engine.dll `FUN_18002aa20`): it stops the first channel playing
    /// THAT sound on the entity and channel, and nothing else there.
    /// </remarks>
    [Test]
    public void Update_AStop_SilencesOnlyTheSoundItNames()
    {
        Recording sink = Run(Sound(50, Loop, volume: 1f), Sound(55, Loop, volume: 1f) with { IsStop = true });

        sink.Stopped.ShouldBe([(Entity, Channel, Loop)]);
        sink.Silenced.ShouldBeEmpty();
    }

    [Test]
    public void Update_AStopNamingASoundThatCannotLoad_StopsNothing()
    {
        Recording sink = Run(Sound(50, Loop, volume: 1f), Sound(55, "missing.wav", volume: 1f) with { IsStop = true });

        sink.Stopped.ShouldBeEmpty();
        sink.Silenced.ShouldBeEmpty();
    }

    private static Recording Run(params SceneSound[] sounds)
    {
        Dictionary<string, SoundSample> samples = new(StringComparer.Ordinal)
        {
            [Loop] = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f }, Loops: true),
            [Other] = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.25f, -0.25f }, Loops: true),
        };

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => samples.TryGetValue(name, out SoundSample sample) ? sample : null,
            NullLogger.Instance) { Schedule = new SoundSchedule(sounds) };

        Recording sink = new(sample => sample.Samples.Span[0] > 0.3f ? Loop : Other);

        // The first call only positions the schedule; the second crosses every sound.
        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.3d);

        return sink;
    }

    private static SceneSound Sound(int tick, string name, float volume, int pitch = 100) =>
        new(tick, name, 1, Entity, Channel, volume, 75, pitch, 0f, 0f, 0f, 0f);

    private sealed class Recording(Func<SoundSample, string> nameOf) : IAudioSink
    {
        public List<string> Played { get; } = [];

        public List<(int Entity, int Channel, float Gain)> Gained { get; } = [];

        public List<(int Entity, int Channel, float Pitch)> Pitched { get; } = [];

        private readonly Dictionary<(int, int), string> _playing = [];

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Played.Add(nameOf(sample));
            _playing[(entity, channel)] = nameOf(sample);
        }

        public bool SetGain(int entity, int channel, float gain)
        {
            Gained.Add((entity, channel, gain));
            return _playing.ContainsKey((entity, channel));
        }

        public bool SetPitch(int entity, int channel, float pitch)
        {
            Pitched.Add((entity, channel, pitch));
            return _playing.ContainsKey((entity, channel));
        }

        public void SilenceAll() => _playing.Clear();

        public int Reclaim() => 0;

        public List<(int Entity, int Channel)> Silenced { get; } = [];

        public List<(int Entity, int Channel, string Sound)> Stopped { get; } = [];

        public void Silence(int entity, int channel)
        {
            Silenced.Add((entity, channel));
            _playing.Remove((entity, channel));
        }

        public void Silence(int entity, int channel, SoundSample sample) => Stopped.Add((entity, channel, nameOf(sample)));
    }
}
