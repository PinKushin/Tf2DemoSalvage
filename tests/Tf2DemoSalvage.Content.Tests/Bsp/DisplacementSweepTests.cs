using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>What a triangle sweep costs, beside what it answers (<see cref="DisplacementSweepConformanceTests"/>).</summary>
public sealed class DisplacementSweepTests
{
    [Test]
    public void Against_ASecondSweep_AllocatesNothing()
    {
        // Finding 89: the light cache's sun trace reaches every terrain triangle it passes, and three
        // arrays a triangle were 0.3 MB a frame. `SweepAABBTriIntersect` works in registers.
        DisplacementTriangle flat = DisplacementTriangle.From((0f, 0f, 0f), (64f, 0f, 0f), (0f, 64f, 0f));
        DisplacementSweep.Against((10f, 10f, 100f), (0f, 0f, -200f), (6f, 6f, 6f), flat, best: 1f);

        long before = GC.GetAllocatedBytesForCurrentThread();
        float fraction = DisplacementSweep.Against((10f, 10f, 100f), (0f, 0f, -200f), (6f, 6f, 6f), flat, best: 1f);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        fraction.ShouldBe(0.46984375f, 0.000001);
    }
}
