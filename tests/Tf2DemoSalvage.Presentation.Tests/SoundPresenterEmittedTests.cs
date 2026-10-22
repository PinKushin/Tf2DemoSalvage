using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
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

        public void Silence(int entity, int channel)
        {
            // Nothing is tracked per channel here, and no stop is scheduled in these tests.
        }

        public int Silence(int entity, int channel, SoundSample sample) => 0;
    }
}
