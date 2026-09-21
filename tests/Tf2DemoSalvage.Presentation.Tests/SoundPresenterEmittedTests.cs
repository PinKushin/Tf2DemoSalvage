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
                    "Test.Explode", Channel: 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), SoundLevel: 95, [Wave]),
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

    /// <summary>Records what was played and ignores the rest.</summary>
    private sealed class Recording : IAudioSink
    {
        public List<(SoundSample Sample, float Gain, int Entity, int Channel)> Played { get; } = [];

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            Played.Add((sample, gain, entity, channel));

        public bool SetGain(int entity, int channel, float gain) => false;

        public void SilenceAll()
        {
            Played.Clear();
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel)
        {
            // Nothing is tracked per channel here, and no stop is scheduled in these tests.
        }
    }
}
