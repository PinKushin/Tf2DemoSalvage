using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Logging.Tests;

/// <summary>The file log's background writer (D230): the caller enqueues, one thread writes and flushes.</summary>
public sealed class FileLogWriterQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Test]
    public void Dispose_TenThousandLines_AllOnDiskInOrder()
    {
        string folder = NewFolder();

        try
        {
            string path;
            using (FileLogWriter writer = new(folder, "order"))
            {
                path = writer.Path;
                for (int i = 0; i < 10_000; i++)
                {
                    writer.Write(i.ToString(CultureInfo.InvariantCulture));
                }
            }

            string[] lines = File.ReadAllLines(path);
            lines.ShouldBe(Enumerable.Range(0, 10_000).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void Write_FromFourThreads_KeepsEachThreadsOrderAndEveryLine()
    {
        StringWriter sink = new();
        using (FileLogWriter writer = new(sink, capacity: 1 << 16, background: true))
        {
            Parallel.For(0, 4, t =>
            {
                for (int i = 0; i < 2_000; i++)
                {
                    writer.Write(string.Create(CultureInfo.InvariantCulture, $"{t} {i}"));
                }
            });
        }

        string[] lines = sink.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(8_000);

        for (int t = 0; t < 4; t++)
        {
            string prefix = string.Create(CultureInfo.InvariantCulture, $"{t} ");
            lines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal))
                .Select(l => int.Parse(l.AsSpan(prefix.Length), CultureInfo.InvariantCulture))
                .ShouldBe(Enumerable.Range(0, 2_000));
        }
    }

    [Test]
    public void Flush_WithNoBackgroundThread_PutsEveryQueuedLineInTheSink()
    {
        // No writer thread, so Flush is the only thing that can drain: the crash handlers' path, isolated.
        StringWriter sink = new();
        using FileLogWriter writer = new(sink, capacity: 64, background: false);
        writer.Write("a");
        writer.Write("b");
        sink.ToString().ShouldBeEmpty();

        writer.Flush(Patience).ShouldBeTrue();

        sink.ToString().ShouldBe("a" + Environment.NewLine + "b" + Environment.NewLine);
    }

    [Test]
    public void Write_WhileTheSinkIsStalled_ReturnsWithoutWaiting()
    {
        using StalledSink sink = new();
        using FileLogWriter writer = new(sink, capacity: 1 << 16, background: true);
        writer.Write("first");
        sink.Entered.Wait(Patience).ShouldBeTrue();

        try
        {
            Task writes = Task.Run(() =>
            {
                for (int i = 0; i < 1_000; i++)
                {
                    writer.Write("more");
                }
            });

            writes.Wait(Patience).ShouldBeTrue("a write waited on the stalled sink");
            sink.Release.IsSet.ShouldBeFalse();
        }
        finally
        {
            // Released either way, so a failure here is a red test rather than a hung host.
            sink.Release.Set();
        }
    }

    [Test]
    public void Write_PastCapacityWhileStalled_DropsTheOldestAndCountsThem()
    {
        using StalledSink sink = new();
        FileLogWriter writer = new(sink, capacity: 4, background: true);
        writer.Write("0");
        sink.Entered.Wait(Patience).ShouldBeTrue();

        for (int i = 1; i <= 10; i++)
        {
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
        }

        writer.Dropped.ShouldBe(6);
        writer.HighWater.ShouldBe(4);

        sink.Release.Set();
        writer.Dispose();

        sink.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(["0", "(6 log lines dropped here: the queue was full)", "7", "8", "9", "10"]);
    }

    [Test]
    public void Log_DrainedLater_CarriesTheTimeItWasLogged()
    {
        StringWriter sink = new();
        using FileLogWriter writer = new(sink, capacity: 64, background: false);
        using FileLoggerProvider provider = new(writer);

        provider.CreateLogger("t").LogInformation("{Message}", "x");
        DateTime logged = DateTime.Now;

        Stopwatch wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 50)
        {
            Thread.SpinWait(1_000);
        }

        writer.Flush(Patience).ShouldBeTrue();

        DateTime stamped = DateTime.ParseExact(sink.ToString()[..12], "HH:mm:ss.fff", CultureInfo.InvariantCulture);
        stamped.TimeOfDay.ShouldBeLessThanOrEqualTo(logged.TimeOfDay);
    }

    [Test]
    public void Write_ThreeLines_TimesTheCallerApartFromTheWriter()
    {
        StringWriter sink = new();
        LogWriteTiming before = FileLogWriter.ThreadTiming;
        using FileLogWriter writer = new(sink, capacity: 64, background: false);
        writer.Write("a");
        writer.Write("b");
        writer.Write("c");
        writer.Flush(Patience).ShouldBeTrue();

        LogWriteTiming caller = FileLogWriter.ThreadTiming - before;
        Assert.Multiple(() =>
        {
            Assert.That(caller.Writes, Is.EqualTo(3));
            Assert.That(caller.FlushTicks, Is.Zero, "the caller never touches the disk");
            Assert.That(writer.Timing.Writes, Is.EqualTo(3));
            Assert.That(writer.Timing.WaitTicks, Is.Zero, "the writer enqueues nothing");
        });
    }

    [Test]
    public void Write_AfterDispose_IsANoOpThatNeitherThrowsNorBlocks()
    {
        // A component that logs during its own late dispose, after the log closed, must cost nothing.
        StringWriter sink = new();
        FileLogWriter writer = new(sink, capacity: 64, background: true);
        writer.Write("before");
        writer.Dispose();

        Task late = Task.Run(() =>
        {
            writer.Write("after");
            writer.Flush(Patience);
            writer.Dispose();
        });

        late.Wait(Patience).ShouldBeTrue();
        sink.ToString().ShouldBe("before" + Environment.NewLine);
    }

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), $"tf2ds-logqueue-{Guid.NewGuid():N}");

    /// <summary>A sink whose first write blocks until released: a disk that stopped answering.</summary>
    private sealed class StalledSink : TextWriter
    {
        private readonly StringBuilder _text = new();
        private bool _stalled;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            if (!_stalled)
            {
                _stalled = true;
                Entered.Set();
                Release.Wait();
            }

            lock (_text)
            {
                _text.Append(value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Entered.Dispose();
                Release.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
