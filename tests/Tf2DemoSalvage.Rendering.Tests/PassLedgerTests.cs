using System.Diagnostics;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The `--measure` pass ledger (B597): CPU means over frames drawn, GPU means over frames read, absent passes omitted.</summary>
public sealed class PassLedgerTests
{
    private static readonly long OneMillisecond = Stopwatch.Frequency / 1000;

    [Test]
    public void Report_TwoFramesDrawnOneRead_MeansEachSideOverItsOwnFrames()
    {
        PassLedger ledger = new();

        for (int frame = 0; frame < 2; frame++)
        {
            ledger.AddCpu(RenderPass.World, 3 * OneMillisecond, draws: 10, uploads: 4);
            ledger.AddCpu(RenderPass.Hud, OneMillisecond, draws: 2, uploads: 0);
            ledger.CpuFrame();
        }

        ledger.AddGpu(RenderPass.World, 0.5, primitives: 1000, pixelInvocations: 900);
        ledger.GpuFrame();
        ledger.Skipped();
        ledger.Disjoint();

        string report = ledger.Report(1280, 720);

        report.ShouldContain("at 1280x720: 2 frames drawn, 1 read on the GPU, 1 skipped, 1 disjoint");
        report.ShouldContain("World: cpu 3.000 ms, gpu 0.500 ms, draws 10.0, maps 4.0, primitives 1000, pixels 900");
        report.ShouldContain("Hud: cpu 1.000 ms, gpu 0.000 ms, draws 2.0, maps 0.0");
        report.ShouldContain("all: cpu 4.000 ms, gpu 0.500 ms, draws 12.0, maps 4.0");
        report.ShouldNotContain("Particles", Case.Sensitive, "a pass that never ran is not reported");
    }

    [Test]
    public void Report_NoFrames_IsZeroNotNaN()
    {
        string report = new PassLedger().Report(640, 360);

        report.ShouldContain("all: cpu 0.000 ms, gpu 0.000 ms");
        report.ShouldNotContain("NaN");
    }
}
