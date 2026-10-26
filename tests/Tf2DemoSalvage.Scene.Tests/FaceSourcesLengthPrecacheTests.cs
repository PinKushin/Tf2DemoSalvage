using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A sound's length is read before play, as the engine creates each precached sound's source at level load
/// (`S_PrecacheSound` → `S_LoadSound`, engine.dll FUN_18002cab0, finding 90) — not at the face's first frame (B595 part 6).
/// </summary>
public sealed class FaceSourcesLengthPrecacheTests
{
    private const double Interval = 0.015;

    [Test]
    public void PrecacheLengths_ThenVoices_ReadsNoFileInPlay()
    {
        List<string> reads = [];
        FaceSources sources = Sources(path =>
        {
            reads.Add(path);
            return 1f;
        });

        sources.PrecacheLengths();
        reads.Count.ShouldBe(2, "the control: a.mp3 and b.mp3, each once; the stop starts nothing");
        reads.Clear();

        _ = sources.Voices(3, 0.2);
        _ = sources.Voices(3, 1.2);

        reads.ShouldBeEmpty();
    }

    [Test]
    public void PrecacheLengths_ThenVoices_AnswersAsTheLazyRead()
    {
        FaceSources precached = Sources(path => path.Contains("a.mp3", StringComparison.Ordinal) ? 0.5f : 2f);
        FaceSources lazy = Sources(path => path.Contains("a.mp3", StringComparison.Ordinal) ? 0.5f : 2f);

        precached.PrecacheLengths();

        foreach (double at in new[] { 0.1, 0.6, 1.0, 1.4, 2.0 })
        {
            precached.Voices(3, at).ShouldBe(lazy.Voices(3, at), $"at {at:0.0} s");
        }
    }

    private static FaceSources Sources(Func<string, float?> readLength)
    {
        Sentence line = new([], [], SampleCount: 44100, SampleRate: 44100);

        return new FaceSources(
            [],
            _ => null,
            [Voice(tick: 0, "vo/a.mp3"), Voice(tick: 60, "vo/b.mp3"), Voice(tick: 80, "vo/b.mp3") with { IsStop = true }],
            Interval,
            new Dictionary<string, Sentence>(StringComparer.OrdinalIgnoreCase)
            {
                [SoundCacheFile.Normalise("sound/vo/a.mp3")] = line,
                [SoundCacheFile.Normalise("sound/vo/b.mp3")] = line,
            },
            _ => null,
            null,
            readLength);
    }

    private static SceneSound Voice(int tick, string name) =>
        new(tick, name, 0, EntityIndex: 3, Channel: 2, 1f, 75, Pitch: 100, 0f, 0f, 0f, 0f);
}
