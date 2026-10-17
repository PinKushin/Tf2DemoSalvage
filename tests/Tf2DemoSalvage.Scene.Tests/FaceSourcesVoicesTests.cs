using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The mouth's sources, kept between the events that change them rather than replayed every frame.</summary>
/// <remarks>
/// **Profiled on f12 at 30000**: `FaceSources.Voices` replayed every sound an actor ever made, from the first, on every
/// frame for every face drawn — 39% of `FlexFor`. The mixer holds its channels and changes them only when a sound starts
/// or runs out. A differential, not a re-derivation: one instance asked across a sweep of times, forwards, backwards and
/// repeated, must answer exactly what a fresh instance answers at each — so the kept answer can never be a stale one.
/// </remarks>
public sealed class FaceSourcesVoicesTests
{
    private const double Interval = 0.015;

    [Test]
    public void Voices_SweptForwardsBackwardsAndRepeated_AnswersAsAFreshWalkDoesAtEveryTime()
    {
        List<double> times = [.. Enumerable.Range(0, 81).Select(at => at * 0.05)];
        List<double> sweep = [.. times, .. Enumerable.Reverse(times), .. times, 0.45, 0.45, 1.0, 1.5, 1.5];

        FaceSources kept = Sources();
        int changed = 0;
        IReadOnlyList<FaceVoice>? previous = null;

        foreach (double at in sweep)
        {
            IReadOnlyList<FaceVoice> expected = Sources().Voices(3, at);
            IReadOnlyList<FaceVoice> actual = kept.Voices(3, at);

            actual.ShouldBe(expected, $"at {at:0.00} s");

            if (previous is not null && !previous.SequenceEqual(actual))
            {
                changed++;
            }

            previous = actual;
        }

        changed.ShouldBeGreaterThan(4, "the control: the sweep crosses starts, ends and the stop, so the answer moves");
    }

    /// <remarks>
    /// **An early free is an event at the time it happens** — risk from review: b.wav (scheduled to end at 2.45 s) is stopped
    /// at 1.5 s, and the span kept after it must begin at 1.5, not at the end b would have had, or nothing after the stop
    /// is ever kept. Between the stop and c.wav at 1.8 s, three asks walk once.
    /// </remarks>
    [Test]
    public void Voices_AfterAStopThatFreesEarly_KeepsTheAnswerUntilTheNextEvent()
    {
        FaceSources sources = Sources();

        _ = sources.Voices(3, 1.6);
        int walks = sources.Walks;
        _ = sources.Voices(3, 1.65);
        _ = sources.Voices(3, 1.75);

        walks.ShouldBe(1, "the control: the first ask walks");
        sources.Walks.ShouldBe(1);
    }

    private static FaceSources Sources()
    {
        Sentence line = new([], [], SampleCount: 44100, SampleRate: 44100);

        return new FaceSources(
            [],
            _ => null,
            [
                Voice(tick: 0, "vo/a.wav"),
                Voice(tick: 30, "vo/b.wav"),
                Voice(tick: 120, "vo/c.wav"),
                Voice(tick: 100, "vo/b.wav") with { IsStop = true },
            ],
            Interval,
            new Dictionary<string, Sentence>(StringComparer.OrdinalIgnoreCase)
            {
                [SoundCacheFile.Normalise("sound/vo/a.wav")] = line,
                [SoundCacheFile.Normalise("sound/vo/b.wav")] = line,
                [SoundCacheFile.Normalise("sound/vo/c.wav")] = line,
            },
            _ => null,
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                [SoundCacheFile.Normalise("sound/vo/a.wav")] = 1f,
                [SoundCacheFile.Normalise("sound/vo/b.wav")] = 2f,
                [SoundCacheFile.Normalise("sound/vo/c.wav")] = 0.5f,
            });
    }

    /// <summary>A voice line on entity 3's own channel 2 (`CHAN_VOICE`) and on channel AUTO alternately, at full pitch.</summary>
    private static SceneSound Voice(int tick, string name) =>
        new(tick, name, 0, EntityIndex: 3, Channel: tick % 60 == 0 ? 2 : 0, 1f, 75, Pitch: 100, 0f, 0f, 0f, 0f);
}
