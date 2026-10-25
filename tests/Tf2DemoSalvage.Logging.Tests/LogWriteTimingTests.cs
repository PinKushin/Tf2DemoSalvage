using System;
using System.Diagnostics;
using System.IO;

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

    [Test]
    public void Write_ThreeLines_CountsThreeOnTheWriterAndTheCallingThread()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"tf2ds-logtiming-{Guid.NewGuid():N}");
        LogWriteTiming threadBefore = FileLogWriter.ThreadTiming;

        try
        {
            using FileLogWriter writer = new(folder, "timing");
            writer.Write("a");
            writer.Write("b");
            writer.Write("c");

            LogWriteTiming thread = FileLogWriter.ThreadTiming - threadBefore;
            Assert.Multiple(() =>
            {
                Assert.That(writer.Timing.Writes, Is.EqualTo(3));
                Assert.That(writer.Timing.FlushTicks, Is.Positive);
                Assert.That(thread.Writes, Is.EqualTo(3));
                Assert.That(thread.FlushTicks, Is.EqualTo(writer.Timing.FlushTicks));
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
