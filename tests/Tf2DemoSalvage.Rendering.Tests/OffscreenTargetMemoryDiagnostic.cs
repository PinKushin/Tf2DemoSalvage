using System;
using System.Collections.Generic;
using System.Diagnostics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>What an offscreen target leaves behind once disposed — a measurement, not a test (D38).</summary>
/// <remarks>
/// **Why it exists**: the assembly's working set climbed for the whole run even with one worker and with the managed heap
/// capped, so what accumulated was native. This creates a target, draws a map's textures through it, disposes it, and
/// reports the process's working set and the managed heap after each round, so a climb is visible per target.
/// </remarks>
[Explicit("A measurement: reports numbers and asserts nothing.")]
public sealed class OffscreenTargetMemoryDiagnostic
{
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The subject is what survives a collection: one is forced before each reading.")]
    public void Dispose_TenTargetsDrawingOneMap_ReportsTheWorkingSetAfterEach()
    {
        MapAssets assets = Skip.Unless(MapCache.TryLoad("cp_process_f12", maximumTextureSize: 256), "cp_process_f12 or the game is absent");
        List<WorldVertex> quad =
        [
            new(-1f, -1f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.5f, 1f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
        ];
        float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

        for (int round = 0; round <= 10; round++)
        {
            if (round > 0)
            {
                using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(64, 64), "no Direct3D on this machine");

                target.DrawWorld(quad, [new WorldBatch(0, 0, quad.Count)], identity, assets);
                _ = target.PixelAt(32, 32);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using Process self = Process.GetCurrentProcess();

            TestContext.Out.WriteLine(
                $"MEMORY after {round} targets: working set {self.WorkingSet64 / 1048576} MB, private {self.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB");
        }

        Assert.Pass("reported above");
    }

    /// <remarks>One load with its own log, printed: where a load's time and memory go, phase by phase.</remarks>
    [Test]
    public void Load_OneMapWithItsLog_ReportsEachPhase()
    {
        byte[] map = MapCache.Bytes("cp_process_f12");
        RecordingLoggerFactory log = new();

        _ = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 256, loggers: log);

        foreach ((string area, _, string message) in log.Lines)
        {
            if (message.Contains("took", StringComparison.Ordinal))
            {
                TestContext.Out.WriteLine($"PHASE {area}: {message}");
            }
        }

        Assert.Pass("reported above");
    }

    /// <remarks>The other half: the same map loaded and dropped, again and again, with no target at all.</remarks>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The subject is what survives a collection: one is forced before each reading.")]
    public void Load_TheSameMapSixTimesDropped_ReportsTheWorkingSetAfterEach()
    {
        byte[] map = MapCache.Bytes();
        GameArchives game = SharedGame.Archives;

        for (int round = 0; round <= 6; round++)
        {
            if (round > 0)
            {
                _ = MapAssets.Load(map, game, maximumTextureSize: 256);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using Process self = Process.GetCurrentProcess();

            TestContext.Out.WriteLine(
                $"MEMORY after {round} loads: working set {self.WorkingSet64 / 1048576} MB, private {self.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB");
        }

        Assert.Pass("reported above");
    }
}
