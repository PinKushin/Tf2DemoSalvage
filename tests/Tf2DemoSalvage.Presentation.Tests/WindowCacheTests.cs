using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Presentation;

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
    internal static byte[] Walk() =>
        SyntheticPlayer.DemoOverTicks(0.015f, [.. Enumerable.Range(0, 300).Select(step => (10 + (step * 2), step * 3f, (step % 5) * 1.5f))]);

    private static readonly Lazy<DemoTimeline> Small = new(() => DemoTimeline.Build(Walk()));

    /// <remarks>
    /// **A seek anywhere answers as the whole timeline does**, every question at every seventh tick, through the window the
    /// cache serves there; and serving the first window holds it and the next, nothing more.
    /// </remarks>
    [Test]
    public async Task At_EverySeventhTick_AnswersAsTheWholeTimelineAndHoldsTheNext()
    {
        byte[] demo = Walk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        WindowSettings settings = new();
        using WindowCache cache = WindowCache.Walk(demo, 0.015, settings, out _).ShouldNotBeNull();
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

        // The prefetch: once the served window is built, the next is set building, and nothing past it.
        await cache.At(whole.FirstTick).ConfigureAwait(false);
        SpinWait.SpinUntil(() => cache.Held.Count == 2, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        cache.Held.Order().ShouldBe([0, 1]);
    }

    /// <summary>A builder over four checkpoints that waits for its token and records what it was asked and what it saw.</summary>
    private sealed class Builds
    {
        public ConcurrentBag<(int Start, int Through)> Asked { get; } = [];

        public ConcurrentBag<int> Cancelled { get; } = [];

        public DemoTimeline Build(int start, int through, CancellationToken cancel)
        {
            Asked.Add((start, through));

            // Held until cancelled, as a long decode is, so an eviction's cancel is what ends it.
            if (cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Cancelled.Add(start);
                cancel.ThrowIfCancellationRequested();
            }

            return Small.Value;
        }
    }

    /// <remarks>
    /// **Latest wins, nothing queues** (the owner, 2026-10-17): four seeks in quick succession — to 50, 150, 250, 350, each
    /// overtaking a build still running — leave one build in flight, the last target's; the other three are cancelled
    /// mid-build and none completes; and no prefetch starts while the last is still building.
    /// </remarks>
    [Test]
    public async Task At_FourSeeksInQuickSuccession_BuildOnlyTheLastTarget()
    {
        Builds builds = new();
        using WindowCache cache = new([0, 100, 200, 300, 400], builds.Build);
        List<Task<DemoTimeline>> overtaken = [];

        foreach (int tick in new[] { 50, 150, 250 })
        {
            overtaken.Add(cache.At(tick));

            // Each build running before the next seek, so each is cancelled mid-build rather than before it starts.
            int asked = overtaken.Count;
            SpinWait.SpinUntil(() => builds.Asked.Count == asked, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        }

        Task<DemoTimeline> last = cache.At(350);

        foreach (Task<DemoTimeline> lost in overtaken)
        {
            await Should.ThrowAsync<OperationCanceledException>(lost).ConfigureAwait(false);
        }

        SpinWait.SpinUntil(() => builds.Cancelled.Count == 3, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        SpinWait.SpinUntil(() => builds.Asked.Count == 4, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        builds.Cancelled.Order().ShouldBe([0, 100, 200]);
        cache.Held.ShouldBe([3]);
        (last.IsCompleted, builds.Asked.Count).ShouldBe((false, 4));
    }

    /// <remarks>**The last window reaches the end of the demo and has no next**: one window held, asked through the end.</remarks>
    [Test]
    public void At_TheLastCheckpoint_HoldsOnlyItThroughTheEnd()
    {
        Builds builds = new();
        using WindowCache cache = new([0, 100, 200, 300], builds.Build);

        cache.At(350);

        cache.Held.ShouldBe([3]);
        SpinWait.SpinUntil(() => !builds.Asked.IsEmpty, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        builds.Asked.ShouldBe([(300, int.MaxValue - 1)]);
    }

    /// <remarks>
    /// **Before the first checkpoint, the first window answers**: a tick of −10 asks for 0 through 99, and nothing else
    /// while that build runs.
    /// </remarks>
    [Test]
    public void At_ATickBeforeTheFirstCheckpoint_ServesTheFirstWindow()
    {
        Builds builds = new();
        using WindowCache cache = new([0, 100, 200, 300], builds.Build);

        cache.At(-10);

        SpinWait.SpinUntil(() => !builds.Asked.IsEmpty, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        cache.Held.ShouldBe([0]);
        builds.Asked.ShouldBe([(0, 99)]);
    }

    /// <remarks>**A faulted window is never served again**: the first build throws; asked again, the cache builds afresh.</remarks>
    [Test]
    public async Task At_AfterAFaultedWindow_BuildsItAgain()
    {
        int calls = 0;
        using WindowCache cache = new(
            [0],
            (_, _, _) => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("broken") : Small.Value);

        await Should.ThrowAsync<InvalidOperationException>(cache.At(5)).ConfigureAwait(false);
        DemoTimeline second = await cache.At(5).ConfigureAwait(false);

        (second, calls).ShouldBe((Small.Value, 2));
    }
}
