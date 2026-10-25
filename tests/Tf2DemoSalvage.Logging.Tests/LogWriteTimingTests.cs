using System.Diagnostics;

using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Logging.Tests;

/// <summary>The writer's own account of what its lines cost (B595).</summary>
public sealed class LogWriteTimingTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    [Test]
    public void One_OverFiveMilliseconds_CountsOneSlowWrite()
    {
        Assert.That(LogWriteTiming.One(2 * Ms, 4 * Ms), Is.EqualTo(new LogWriteTiming(1, 2 * Ms, 4 * Ms, 1)));
    }

    [Test]
    public void One_AtExactlyFiveMilliseconds_IsNotSlow()
    {
        long five = (long)(LogWriteTiming.SlowMilliseconds * Stopwatch.Frequency / 1000d);
        Assert.That(LogWriteTiming.One(0, five).SlowWrites, Is.Zero);
    }

    [Test]
    public void Subtract_TwoReadings_IsTheWindow()
    {
        LogWriteTiming late = new(5, 50, 70, 2);
        LogWriteTiming early = new(2, 10, 30, 1);
        Assert.That(late - early, Is.EqualTo(new LogWriteTiming(3, 40, 40, 1)));
    }

    // The writer's caller-side and drain-side split (D230): FileLogWriterQueueTests.Write_ThreeLines_TimesTheCallerApartFromTheWriter.
}
