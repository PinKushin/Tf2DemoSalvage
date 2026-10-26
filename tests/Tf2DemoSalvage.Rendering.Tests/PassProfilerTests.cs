using System;

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
}
