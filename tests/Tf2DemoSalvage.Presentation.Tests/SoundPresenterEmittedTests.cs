using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A sound the client emits itself, played through the same presenter as the demo's own (B415).</summary>
/// <remarks>
/// **The output, not the list.** <see cref="ExplosionSoundsTests"/> asserts what a blast turns INTO; this asserts that
/// the result reaches a sink when playback crosses the blast — the chain the viewer runs, minus the device.
/// </remarks>
public sealed class SoundPresenterEmittedTests
{
    private const string Wave = ")weapons/explode1.wav";

    [Test]
    public void Update_PastABlast_PlaysItsSoundFromTheWorldOnTheScriptsChannel()
    {
        SoundSample boom = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f });

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => string.Equals(name, Wave, StringComparison.Ordinal) ? boom : null,
            NullLogger.Instance);

        IReadOnlyList<SceneSound> emitted = ExplosionSounds.For(
            [new SceneExplosion(50, 100f, 0f, 0f, (0f, 0f, 1f), 22, SceneExplosion.NoEntity, SceneExplosion.NoCustomParticle)],
            static _ => "Test.Explode",
            new Dictionary<string, SoundScriptEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["Test.Explode"] = new(
                    "Test.Explode", Channel: 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), SoundLevel: new SoundRange(95f, 95f), [Wave]),
            });

        presenter.Schedule = new SoundSchedule(ExplosionSounds.Merged([], emitted));

        Recording sink = new();

        // The first call only positions the schedule; the second crosses tick 50.
        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.3d);

        (SoundSample sample, float gain, int entity, int channel) = sink.Played.ShouldHaveSingleItem();

        sample.ShouldBe(boom);
        entity.ShouldBe(ExplosionSounds.FromWorld);
        channel.ShouldBe(1);
        gain.ShouldBeGreaterThan(0f, "a 95 dB blast 100 units away is audible");
    }

    /// <summary>
    /// **The client's own bullet sounds as it fires them** (B415). `FireBullet` calls `UTIL_ImpactTrace` at the shot
    /// (`tf_player_shared.cpp:10526`), once its trace has met or missed a player, so the sound is emitted then rather than
    /// scheduled at load.
    /// </summary>
    [Test]
    public void Update_AfterEmit_PlaysTheSoundOnce()
    {
        SoundSample hit = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f });
        SoundPresenter presenter = Presenter(hit);
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(Impact(at: 100f));
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);
        presenter.Update(sink, 42, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.03d);

        sink.Played.ShouldHaveSingleItem().Sample.ShouldBe(hit);
    }

    /// <remarks>
    /// **A sound whose sample is still loading plays late, once, when it arrives** (finding 90). The engine keeps the
    /// channel S_StartSound made and leaves it out of each pass's mix until the mixer reports ready (engine.dll
    /// FUN_18003f2c0: mixer slot +0x60 false removes it from that pass's list without freeing it, and no samples are
    /// skipped), so it starts from its first sample at arrival with the parameters it was started with.
    /// </remarks>
    [Test]
    public void Update_AfterEmitWhileItsSampleLoads_PlaysItOnceWhenItArrives()
    {
        SoundSample hit = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f });
        bool arrived = false;
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => arrived && string.Equals(name, Wave, StringComparison.Ordinal) ? hit : null,
            NullLogger.Instance)
        {
            Loading = name => !arrived && string.Equals(name, Wave, StringComparison.Ordinal),
            Schedule = new SoundSchedule([]),
        };
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(Impact(at: 100f));
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);
        presenter.Update(sink, 42, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.03d);
        sink.Played.ShouldBeEmpty("still loading");

        arrived = true;
        presenter.Update(sink, 43, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.045d);
        presenter.Update(sink, 44, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.06d);

        (SoundSample sample, float gain, _, int channel) = sink.Played.ShouldHaveSingleItem();
        sample.ShouldBe(hit);
        channel.ShouldBe(0);
        gain.ShouldBeGreaterThan(0f);
    }

    /// <remarks>
    /// **A stop cancels a sound still waiting for its data** (review of finding 90). The engine's not-ready channel stays
    /// in the active set (FUN_18003f2c0), so `S_AlterChannel` matches it by entity, channel and sfx and frees it
    /// (FUN_18002aa20, B416). Emitted, stopped two ticks later while loading, then the load lands: nothing plays.
    /// The second and third cases are the controls: a stop naming another sound, or another channel, cancels nothing.
    /// </remarks>
    [TestCase(Wave, 0, 0)]
    [TestCase("weapons/other.wav", 0, 1)]
    [TestCase(Wave, 5, 1)]
    public void Update_AStopWhileItsSoundLoads_CancelsOnlyThatSound(string stopName, int stopChannel, int expectedPlays)
    {
        SoundSample hit = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f, -0.5f });
        bool arrived = false;
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => arrived && string.Equals(name, Wave, StringComparison.Ordinal) ? hit : null,
            NullLogger.Instance)
        {
            Loading = name => !arrived && string.Equals(name, Wave, StringComparison.Ordinal),
            Schedule = new SoundSchedule([]),
        };
        Recording sink = new();
        SceneSound started = Impact(at: 100f);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(started);
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);
        presenter.Emit(started with { Tick = 43, Name = stopName, Channel = stopChannel, IsStop = true });
        presenter.Update(sink, 43, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.045d);

        arrived = true;
        presenter.Update(sink, 44, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.06d);
        presenter.Update(sink, 45, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.075d);

        sink.Played.Count.ShouldBe(expectedPlays);
    }

    /// <summary>A stop asks only what is held, so a stop of a sound never loaded starts no load (review of finding 90).</summary>
    [Test]
    public void Update_AStopOfASoundNotHeld_AsksNothingToLoad()
    {
        List<string> loaded = [];
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name =>
            {
                loaded.Add(name);
                return null;
            },
            NullLogger.Instance)
        {
            Held = _ => null,
            Schedule = new SoundSchedule([]),
        };
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(Impact(at: 100f) with { IsStop = true });
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);

        loaded.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A stop matches the sfx, not its wave data** (review of `fd31d8d2`). A channel holds its `CSfxTable` and
    /// `S_AlterChannel` matches `ch->sfx` (FUN_18002aa20, B416, B545), whether or not the data is resident. So a sound
    /// started, then let go by the cache's cap while it plays, is still stopped, by its sfx key, and nothing is read.
    /// </remarks>
    [Test]
    public void Update_AStopOfAPlayingSoundTheCapEvicted_SilencesItBySfxAndReadsNothing()
    {
        List<string> reads = [];
        SoundCache cache = new(NullLogger.Instance, capacityBytes: 2)
        {
            Read = path =>
            {
                reads.Add(path);
                return OneSampleWave;
            },
        };
        cache.Precache([Wave]);

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            cache.Sample,
            NullLogger.Instance)
        {
            Held = cache.HeldSample,
            Loading = cache.Loading,
            Schedule = new SoundSchedule([]),
        };
        Recording sink = new();
        SceneSound started = Impact(at: 100f);

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(started);
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);
        sink.Played.ShouldHaveSingleItem("the control: it started from the precache");

        cache.Precache(["weapons/other.wav"]);
        cache.HeldSample(Wave).ShouldBeNull("the control: the cap let it go");
        reads.Clear();

        presenter.Emit(started with { Tick = 42, IsStop = true });
        presenter.Update(sink, 42, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.03d);

        sink.Stopped.ShouldHaveSingleItem().ShouldBe((started.EntityIndex, started.Channel, SoundName.SfxKey(Wave)));
        reads.ShouldBeEmpty();
    }

    /// <summary>A 16-bit mono RIFF/WAVE of one sample, written from the format: two bytes held.</summary>
    private static byte[] OneSampleWave =>
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F', 38, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E',
        (byte)'f', (byte)'m', (byte)'t', (byte)' ', 16, 0, 0, 0, 1, 0, 1, 0, 0x22, 0x56, 0, 0, 0x44, 0xAC, 0, 0, 2, 0, 16, 0,
        (byte)'d', (byte)'a', (byte)'t', (byte)'a', 2, 0, 0, 0, 1, 0,
    ];

    /// <summary>The control: a sound that is not loading and will not open plays nothing, now or later.</summary>
    [Test]
    public void Update_AfterEmitOfASoundThatWillNotOpen_NeverPlaysIt()
    {
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            _ => null,
            NullLogger.Instance)
        {
            Loading = _ => false,
            Schedule = new SoundSchedule([]),
        };
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(Impact(at: 100f));
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);
        presenter.Update(sink, 42, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.03d);

        sink.Played.ShouldBeEmpty();
    }

    /// <summary>`ImpactCallback`'s camera gate holds for an emitted sound as for a scheduled one.</summary>
    [Test]
    public void Update_AfterEmitBeyondItsGate_PlaysNothing()
    {
        SoundPresenter presenter = Presenter(new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }));
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Emit(Impact(at: ImpactSounds.AudibleWithin));
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d);

        sink.Played.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A skip the sound sat out is not taken later** (D221): a frame whose move is about another tick marks it seen,
    /// so normal play afterwards does not silence everything as though it had just jumped.
    /// </remarks>
    [Test]
    public void Update_AfterASkipItSatOut_PlaysOnWithoutSilencing()
    {
        SoundPresenter presenter = Presenter(new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f }));
        Recording sink = new();

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0d);
        presenter.Update(sink, 41, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.015d, move: new FrameSkip(900, 2, 2) { Skipped = true });
        presenter.Update(sink, 42, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.03d, move: new FrameSkip(42, 3, 2));

        sink.Silenced.ShouldBe(0);
    }

    private static SoundPresenter Presenter(SoundSample sample)
    {
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => string.Equals(name, Wave, StringComparison.Ordinal) ? sample : null,
            NullLogger.Instance);

        presenter.Schedule = new SoundSchedule([]);

        return presenter;
    }

    private static SceneSound Impact(float at) =>
        ImpactSounds.For(
            new BulletLanding(41, (at, 0f, 0f), "Test.Impact", Ricochets: false),
            seed: 0,
            new Dictionary<string, SoundScriptEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["Test.Impact"] = new(
                    "Test.Impact", Channel: 0, new SoundRange(1f, 1f), new SoundRange(100f, 100f), SoundLevel: new SoundRange(95f, 95f), [Wave]),
            }).ShouldHaveSingleItem();

    /// <summary>Records what was played and ignores the rest.</summary>
    private sealed class Recording : IAudioSink
    {
        public float MasterGain { get; set; } = 1f;

        public List<(SoundSample Sample, float Gain, int Entity, int Channel)> Played { get; } = [];

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Played.Add((sample, gain, entity, channel));
            return 0;
        }

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public int Silenced { get; private set; }

        public void SilenceAll()
        {
            Silenced++;
            Played.Clear();
        }

        public int Reclaim() => 0;

        public List<(int Entity, int Channel, string? Key)> Stopped { get; } = [];

        public void Silence(int entity, int channel)
        {
            // Nothing is tracked per channel here; a stop arrives through the sample overload below.
        }

        public int Silence(int entity, int channel, SoundSample sample)
        {
            Stopped.Add((entity, channel, sample.Key));
            return 0;
        }
    }
}
