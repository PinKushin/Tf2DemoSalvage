using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The memory line `--measure` prints at its end: the GC's own account beside the process's (finding 80).</summary>
public sealed class GcMemoryLineTests
{
    /// <remarks>
    /// Distinct values in every slot, so no column can stand in for another. Committed minus live minus fragmented is the
    /// GC's slack, the number finding 80 inferred from a region walk and this line exists to read.
    /// </remarks>
    [Test]
    public void Format_DistinctValues_ReportsEachAndTheSlack()
    {
        const long Mb = 1L << 20;

        string line = GcMemoryLine.Format(
            privateBytes: 8000 * Mb,
            workingSet: 6000 * Mb,
            heapSize: 3000 * Mb,
            committed: 6200 * Mb,
            fragmented: 700 * Mb,
            generations: [10 * Mb, 20 * Mb, 2500 * Mb, 400 * Mb, 70 * Mb]);

        line.ShouldBe(
            "memory: private 8000 MB, working set 6000 MB; GC heap 3000 MB, committed 6200 MB, fragmented 700 MB, " +
            "slack 2500 MB; generations 10, 20, 2500, LOH 400, POH 70 MB");
    }
}
