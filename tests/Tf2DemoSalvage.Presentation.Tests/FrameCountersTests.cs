using System;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary><see cref="FrameCounters"/>: two readings difference into what ran between them. Frequency 1,000 makes a tick a millisecond.</summary>
[TestFixture]
public sealed class FrameCountersTests
{
    [Test]
    public void Since_TwoReadings_NamesEachDeltaAndSplitsTheJitByThread()
    {
        FrameCounters start = new(10, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(8), TimeSpan.Zero, new(1, 2, 3, 4, 5), 2, 7, new(3, 10, 5, 20), default, new(2, 0, 0), 100);
        FrameCounters end = new(
            14, TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(60), TimeSpan.Zero, new(2, 12, 3, 34, 105), 3, 9, new(4, 52, 8, 31), default,
            new(5, 0, 1, "materials/hud/d_images_v2.vtf"), 350);

        end.Since(start, 1000).ShouldBe(
            "main jit 4 methods 40.0 ms (other threads 12.0 ms); camera view 1.0 setcamera 10.0 wait 0.0 pose 30.0 particles 100.0" +
            "; first uses: sound decodes 1, models packed +1 42.0 ms, baked built +3 11.0 ms, particle systems +2" +
            "; log 0 writes, wait 0.0 ms, flush 0.0 ms, 0 over 5 ms" +
            "; archive reads 3 reads 0.0 ms, 1 over 5 ms (last: materials/hud/d_images_v2.vtf); page faults 250");
    }
}
