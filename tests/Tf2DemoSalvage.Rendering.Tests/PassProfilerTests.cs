using System;
using System.Diagnostics;
using System.Threading;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>`--measure`'s per-pass profiler over a real device: what a frame that misbehaves leaves in the ledger.</summary>
public sealed class PassProfilerTests
{
    /// <summary>A pass marked more times than there are passes must not overrun the frame's order.</summary>
    [Test]
    public void Mark_MorePassesThanTheOrderHolds_StopsProfilingTheFrameAndEndsIt()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        using PassProfiler profiler = target.CreatePassProfiler();

        profiler.BeginFrame(target.Context);

        for (int mark = 0; mark < 3 * Enum.GetValues<RenderPass>().Length; mark++)
        {
            profiler.Mark(target.Context, RenderPass.World);
        }

        profiler.EndFrame(target.Context);

        profiler.TakeLedger().Report(4, 4).ShouldStartWith("render passes, mean per frame at 4x4: 1 frames drawn");
    }

    /// <summary>A frame whose draw threw — begun, marked, never ended — is closed by the next and counted, not drawn.</summary>
    [Test]
    public void BeginFrame_AfterAFrameNeverEnded_CountsItAbandonedAndNotDrawn()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        using PassProfiler profiler = target.CreatePassProfiler();

        profiler.BeginFrame(target.Context);
        profiler.Mark(target.Context, RenderPass.Clear);
        profiler.BeginFrame(target.Context);
        profiler.Mark(target.Context, RenderPass.Clear);
        profiler.EndFrame(target.Context);

        profiler.TakeLedger().Report(4, 4).ShouldContain("1 frames drawn, 0 read on the GPU, 0 skipped, 0 disjoint, 1 abandoned");
    }

    /// <summary>An abandoned frame's marks are rolled back: its passes must not feed the drawn frames' mean.</summary>
    [Test]
    public void BeginFrame_AfterAFrameNeverEnded_RollsBackItsPasses()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        using PassProfiler profiler = target.CreatePassProfiler();

        profiler.BeginFrame(target.Context);
        TickPasses();
        profiler.Mark(target.Context, RenderPass.World);
        profiler.BeginFrame(target.Context);
        TickPasses();
        profiler.Mark(target.Context, RenderPass.Clear);
        profiler.EndFrame(target.Context);

        string report = profiler.TakeLedger().Report(4, 4);

        report.ShouldContain("Clear: cpu", customMessage: "the control: the drawn frame's pass is reported");
        report.ShouldNotContain("World:");
    }

    /// <summary>A throw on a measurement's LAST frame is counted abandoned at the throw, before the ledger is taken.</summary>
    [Test]
    public void FrameScope_DrawThrowsOnTheLastFrame_CountsItAbandonedBeforeTheLedgerIsTaken()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        using PassProfiler profiler = target.CreatePassProfiler();

        Should.Throw<InvalidOperationException>(() =>
        {
            using PassFrame frame = PassProfiler.Begin(profiler, target.Context);
            TickPasses();
            profiler.Mark(target.Context, RenderPass.World);
            throw new InvalidOperationException("the draw threw");
        });

        string report = profiler.TakeLedger().Report(4, 4);

        report.ShouldContain("0 frames drawn, 0 read on the GPU, 0 skipped, 0 disjoint, 1 abandoned");
        report.ShouldNotContain("World:");
    }

    /// <summary>The control for the scope: a frame that ends normally is drawn, not abandoned.</summary>
    [Test]
    public void FrameScope_FrameEnded_IsDrawnNotAbandoned()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        using PassProfiler profiler = target.CreatePassProfiler();

        using (PassFrame frame = PassProfiler.Begin(profiler, target.Context))
        {
            profiler.Mark(target.Context, RenderPass.Clear);
            profiler.EndFrame(target.Context);
        }

        profiler.TakeLedger().Report(4, 4).ShouldContain("1 frames drawn, 0 read on the GPU, 0 skipped, 0 disjoint, 0 abandoned");
    }

    /// <summary>Waits until the clock moves, so the pass about to be marked has a nonzero CPU time and is reported.</summary>
    private static void TickPasses()
    {
        long at = Stopwatch.GetTimestamp();

        while (Stopwatch.GetTimestamp() == at)
        {
            Thread.SpinWait(1);
        }
    }
}
