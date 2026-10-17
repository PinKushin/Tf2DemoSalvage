using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Core.Tests;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The viewer's window cache over checkpoints at the decided spacing (D217 step 4).</summary>
public sealed class WindowCacheTests
{
    /// <remarks>
    /// **Seconds to ticks TRUNCATES** (the owner's 2 s): 133.33 ticks at TF2's 15 ms is 133, and an interval where the
    /// fraction passes a half (2 / 0.01497 = 133.6) still gives 133 where rounding gives 134 — never further apart than
    /// asked; a tick longer than the spacing still gives one.
    /// </remarks>
    [TestCase(0.015, 133)]
    [TestCase(0.01497, 133)]
    [TestCase(1d / 66, 132)]
    [TestCase(5d, 1)]
    public void SpacingTicks_AnInterval_TruncatesTheTwoSeconds(double secondsPerTick, int expected) =>
        WindowCache.SpacingTicks(secondsPerTick).ShouldBe(expected);

    /// <summary>A player moving every other tick for 600 ticks — five checkpoints at 133.</summary>
    private static byte[] Walk() =>
        SyntheticPlayer.DemoOverTicks(0.015f, [.. Enumerable.Range(0, 300).Select(step => (10 + (step * 2), step * 3f, (step % 5) * 1.5f))]);

    /// <remarks>
    /// **A seek anywhere answers as the whole timeline does**, every question at every tick, through the window the cache
    /// serves there; and serving one sets the next building, so it is held before it is asked for.
    /// </remarks>
    [Test]
    public async Task At_EveryTick_AnswersAsTheWholeTimelineAndHoldsTheNext()
    {
        byte[] demo = Walk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        WindowSettings settings = new();
        WindowCache cache = WindowCache.Walk(demo, settings).ShouldNotBeNull();
        cache.Count.ShouldBeGreaterThan(3);
        List<string> differing = [];
        long values = 0;

        for (int tick = whole.FirstTick; tick <= whole.LastTick; tick += 7)
        {
            DemoTimeline window = await cache.At(tick + 0.5).ConfigureAwait(false);
            (IReadOnlyList<string> questions, long compared) = WindowEquivalence.Compare(whole, window, tick + 0.5, settings);
            differing.AddRange(questions.Select(question => $"{question} at {tick}"));
            values += compared;
        }

        differing.ShouldBeEmpty();
        values.ShouldBeGreaterThan(1000);

        // The prefetch: serving the first window holds the second, and nothing past it.
        await cache.At(whole.FirstTick).ConfigureAwait(false);
        cache.Held.ShouldBe(2);
    }

    /// <remarks>
    /// **Two threads, one checkpoint**: eight windows of one span built at once all answer as the whole timeline does, and
    /// the checkpoint they resumed is left unchanged — the case the prefetch makes, and where the frozen bags' read cache
    /// is shared.
    /// </remarks>
    [Test]
    public async Task Window_EightAtOnceFromOneCheckpoint_AllAnswerAsTheWholeAndLeaveItUnchanged()
    {
        byte[] demo = Walk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        WindowSettings settings = new();
        TimelineWindows walk = TimelineWindows.Walk(demo, WindowCache.SpacingTicks(0.015), settings).ShouldNotBeNull();
        int start = walk.Ticks[2];
        int through = walk.Ticks[3] - 1;

        DemoTimeline[] windows = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => walk.Window(start, through)))).ConfigureAwait(false);

        windows.Select(window => WindowEquivalence.Compare(whole, window, start + 40.5, settings).Differing.Count).ShouldAllBe(count => count == 0);
        walk.WindowsLeaveCheckpointUnchanged(2).ShouldBeTrue();
    }
}
