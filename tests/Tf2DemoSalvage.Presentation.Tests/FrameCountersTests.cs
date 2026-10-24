using System;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary><see cref="FrameCounters"/>: two readings difference into what ran between them. Frequency 1,000 makes a tick a millisecond.</summary>
[TestFixture]
public sealed class FrameCountersTests
{
    [Test]
    public void Since_TwoReadings_NamesEachDeltaAndSplitsTheJitByThread()
    {
        FrameCounters start = new(10, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(8), TimeSpan.Zero, new(1, 2, 3, 4, 5), 2, 100, 7);
        FrameCounters end = new(
            14, TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(60), TimeSpan.Zero, new(2, 12, 3, 34, 105), 3, 160, 9);

        end.Since(start, 1000).ShouldBe(
            "main jit 4 methods 40.0 ms (other threads 12.0 ms); camera view 1.0 setcamera 10.0 wait 0.0 pose 30.0 particles 100.0" +
            "; first uses: sound decodes 1, model vertices +60, particle systems +2");
    }
}
