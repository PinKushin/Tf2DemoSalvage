using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Where large allocations come from during `--measure`, by type (finding 78).</summary>
public sealed class AllocationTallyTests
{
    /// <remarks>
    /// The runtime's AllocationTick reports one sampled allocation per ~100 KB, with the bytes since the last tick and
    /// whether it went to the large-object heap. Only large ones are tallied; distinct sums so no type can stand in for
    /// another, and the order is by bytes.
    /// </remarks>
    [Test]
    public void Report_LargeAndSmallTicks_SumsOnlyTheLargeByTypeLargestFirst()
    {
        AllocationTally tally = new();
        const long Mb = 1L << 20;

        tally.Add("WorldVertex[]", large: true, bytes: 300 * Mb);
        tally.Add("Single[]", large: true, bytes: 50 * Mb);
        tally.Add("WorldVertex[]", large: true, bytes: 100 * Mb);
        tally.Add("String", large: false, bytes: 900 * Mb);

        tally.Report(2).ShouldBe("large allocations: 450 MB; WorldVertex[] 400 MB, Single[] 50 MB");
    }
}
