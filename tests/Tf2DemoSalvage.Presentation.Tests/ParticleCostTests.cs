using System;
using System.Diagnostics;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class ParticleCostTests
{
    [Test]
    public void Since_TwoReadingsOverTwoFrames_GivesEachCounterPerFrame()
    {
        long ms = Stopwatch.Frequency / 1000;
        ParticleCost start = new(10, 0, 0, 100, 20, 5, 0, 8, 3, 0);
        ParticleCost end = new(12, 4 * ms, 2 * ms, 106, 24, 7, 6 * ms, 11, 5, 2 * ms);

        end.Since(start, 2).ShouldBe(
            "particle cost a frame: search 2.00 ms over 1 searches, bursts 1.00 ms; attachments 3 queries, " +
            "2 skeletons built, 1 rebuilt after this frame's first, 3.00 ms; roots built 1.5, eligible 1 in 1.000 ms");
    }

    [Test]
    public void Since_NoFrames_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => default(ParticleCost).Since(default, 0));
    }
}
