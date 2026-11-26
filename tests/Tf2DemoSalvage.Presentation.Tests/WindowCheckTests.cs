using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`--windows`' check: its seek logic and its failures (D217 step 4).</summary>
public sealed class WindowCheckTests
{
    private static readonly WindowSettings Settings = new();

    private static readonly Lazy<(DemoTimeline Whole, byte[] Demo)> Demo = new(() =>
    {
        byte[] demo = WindowCacheTests.Walk();
        return (DemoTimeline.Build(demo), demo);
    });

    private static void ObserveUntil(WindowCheck check, Func<bool> done, FrameSkip? skip)
    {
        SpinWait.SpinUntil(
            () =>
            {
                check.Observe(skip, Demo.Value.Whole);
                return done();
            },
            TimeSpan.FromSeconds(30)).ShouldBeTrue();
    }

    /// <remarks>
    /// **The walk builds with the facts the whole timeline was built with** (B872): the window's builder asks them as the
    /// straight build does. Control: the straight build asks the same instrument too, so a zero is not a demo with no model.
    /// </remarks>
    [Test]
    public async Task Start_WithModelFacts_HandsThemToTheWalksBuilder()
    {
        byte[] demo = Core.Tests.SyntheticAnimatingProp.Demo(
            [string.Empty, "models/buildables/sentry1.mdl"], (0, 1, [0.5f], null, null), (10, 1, [0.6f], null, null));

        Asking control = new();
        _ = DemoTimeline.Build(demo, models: control);
        control.Asked.ShouldBeGreaterThan(0, "the control: the straight build asks for a model's facts");

        Asking asking = new();
        using WindowCheck check = WindowCheck.Start(demo, 0.015, "walk", Settings, new RecordingLogger(), models: asking);
        await check.Walking.ConfigureAwait(false);

        asking.Asked.ShouldBeGreaterThan(0);
    }

    private sealed class Asking : IModelFacts
    {
        private int _asked;

        public int Asked => _asked;

        public ModelFacts? FactsOf(string modelPath)
        {
            Interlocked.Increment(ref _asked);
            return null;
        }
    }

    /// <remarks>
    /// **A faulted walk parks the check, said once**: frames after it neither throw nor say it again.
    /// </remarks>
    [Test]
    public async Task Observe_AfterAFaultedWalk_WarnsOnceAndThrowsNothing()
    {
        RecordingLogger log = new();
        using WindowCheck check = new(_ => throw new InvalidOperationException("no walk"), Settings, log);
        await Should.ThrowAsync<InvalidOperationException>(check.Walking).ConfigureAwait(false);

        check.Observe(skip: null, Demo.Value.Whole);
        check.Observe(new FrameSkip(60, 1, 1), Demo.Value.Whole);

        (log.Count("the walk failed"), log.Lines.Count(line => line.Level == LogLevel.Warning)).ShouldBe((1, 1));
    }

    /// <remarks>**A faulted window parks the check, said once**, and a later seek starts nothing.</remarks>
    [Test]
    public void Observe_AfterAFaultedWindow_WarnsOnceAndStopsChecking()
    {
        RecordingLogger log = new();
        int builds = 0;
        WindowCache broken = new([0], (_, _, _) =>
        {
            Interlocked.Increment(ref builds);
            throw new InvalidOperationException("no window");
        });
        using WindowCheck check = new(_ => (broken, 0L, TimeSpan.Zero), Settings, log);

        ObserveUntil(check, () => log.Count("windows:") > 0, skip: null);
        ObserveUntil(check, () => log.Count("failed") > 0, new FrameSkip(60, 1, 1));
        check.Observe(new FrameSkip(90, 2, 2), Demo.Value.Whole);

        (log.Count("the window for 60 failed"), Volatile.Read(ref builds)).ShouldBe((1, 1));
        broken.Held.ShouldBe([0], "the parked check asks nothing more: the faulted window is the only one, never rebuilt");
    }

    /// <remarks>
    /// **A seek made while the walk runs is checked when it ends**: the walk held, a seek to 60, the walk released, and
    /// the check says so and then answers it IDENTICAL against the whole timeline.
    /// </remarks>
    [Test]
    public void Observe_ASeekDuringTheWalk_IsCheckedWhenTheWalkEnds()
    {
        RecordingLogger log = new();
        using ManualResetEventSlim release = new();
        using WindowCheck check = new(
            closing =>
            {
                release.Wait(TimeSpan.FromSeconds(30), closing).ShouldBeTrue();
                return (WindowCache.Walk(Demo.Value.Demo, 0.015, Settings, out TimelineWindows? _, cancel: closing), 0L, TimeSpan.Zero);
            },
            Settings,
            log);
        FrameSkip seek = new(60, 1, 1);

        check.Observe(seek, Demo.Value.Whole);
        log.Count("windows:").ShouldBe(0, "nothing is said while the walk runs");
        release.Set();

        ObserveUntil(check, () => log.Count("IDENTICAL") > 0, seek);

        (log.Count("checking the seek to 60 made during the walk"), log.Count("seek to 60 ready")).ShouldBe((1, 1));
    }

    /// <remarks>**Dispose stops a running walk**: the walk waits on its token, and Dispose ends it cancelled.</remarks>
    [Test]
    public async Task Dispose_MidWalk_CancelsTheWalk()
    {
        WindowCheck check = new(
            closing =>
            {
                closing.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).ShouldBeTrue();
                closing.ThrowIfCancellationRequested();
                return (WindowCache.Walk(Demo.Value.Demo, 0.015, Settings, out TimelineWindows? _, cancel: closing), 0L, TimeSpan.Zero);
            },
            Settings,
            new RecordingLogger());

        check.Dispose();

        await Should.ThrowAsync<OperationCanceledException>(check.Walking).ConfigureAwait(false);
    }
}
