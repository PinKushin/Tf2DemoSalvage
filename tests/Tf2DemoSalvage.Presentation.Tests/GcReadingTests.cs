using System;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class GcReadingTests
{
    [Test]
    public void Since_TwoReadingsOverFourFrames_GivesPerFramePauseExclusiveCountsAndAllocation()
    {
        GcReading start = new(TimeSpan.FromMilliseconds(100), Gen0: 10, Gen1: 4, Gen2: 1, Allocated: 0);

        // 7 more gen0 counts, 3 more gen1 and 1 more gen2: one gen2, two gen1-only, four gen0-only.
        GcReading end = new(TimeSpan.FromMilliseconds(140), Gen0: 17, Gen1: 7, Gen2: 2, Allocated: 8L * 1048576);

        end.Since(start, 4).ShouldBe("gc: paused 10.00 ms a frame; collections gen0 4, gen1 2, gen2 1; allocated 2.00 MB a frame");
    }

    [Test]
    public void Since_NoFrames_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => default(GcReading).Since(default, 0));
    }
}
