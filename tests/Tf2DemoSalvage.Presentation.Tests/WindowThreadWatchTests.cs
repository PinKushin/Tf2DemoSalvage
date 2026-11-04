using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The watch tells a thread that stopped pumping from one that never went idle, on a clock the test holds (B707).</summary>
public sealed class WindowThreadWatchTests
{
    private sealed class Lines : ILogger
    {
        public List<string> Said { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Said.Add(formatter(state, exception));
    }

    private long _now;

    /// <summary>A watch made at 1000 ms on its clock, 200 ms after the process started.</summary>
    private WindowThreadWatch Watch(ILogger log)
    {
        _now = 1000;

        return new WindowThreadWatch(log, () => _now, frequency: 1000, TimeSpan.FromMilliseconds(200));
    }

    [Test]
    public void Message_TheFirstOneEver_MeasuresFromTheProcessStart()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        _now = 30_800;
        watch.Message(objectRequest: false);

        watch.LargestGapMilliseconds.ShouldBe(30_000d);
        log.Said.ShouldBe(["window thread dispatched nothing for 30000 ms, until now"]);
    }

    [Test]
    public void IdleBegan_TheFirstOneEver_MeasuresFromTheProcessStart()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        _now = 29_800;
        watch.IdleBegan();

        watch.LargestIdleGapMilliseconds.ShouldBe(29_000d);
        log.Said.ShouldBe(["window thread reached no idle pass for 29000 ms, until now"]);
    }

    [Test]
    public void Message_AfterAThreadStoppedPumping_ReportsTheGapAndKeepsTheLargest()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Message(objectRequest: false);
        log.Said.Clear();
        _now += 100;
        watch.Message(objectRequest: false);
        _now += 6500;
        watch.Message(objectRequest: false);
        _now += 50;
        watch.Message(objectRequest: false);

        watch.LargestGapMilliseconds.ShouldBe(6500d, "a later, shorter gap does not replace it");
        log.Said.ShouldBe(["window thread dispatched nothing for 6500 ms, until now"]);
    }

    [Test]
    public void Message_BelowTheSayingLength_SaysNothing()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        _now += 599;
        watch.Message(objectRequest: false);

        log.Said.ShouldBeEmpty();
    }

    [Test]
    public void IdleBegan_WhileMessagesKeepComing_ReportsAnIdleGapWithNoMessageGap()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Message(objectRequest: false);
        watch.IdleBegan();
        watch.IdleEnded();
        log.Said.Clear();

        for (int step = 0; step < 300; step++)
        {
            _now += 100;
            watch.Message(objectRequest: false);
        }

        watch.IdleBegan();
        watch.IdleEnded();
        _now += 5;
        watch.IdleBegan();

        watch.LargestGapMilliseconds.ShouldBe(200d, "from the process's start to the first message");
        watch.LargestIdleGapMilliseconds.ShouldBe(30_000d, "a later, shorter gap does not replace it");
        log.Said.ShouldBe(["window thread reached no idle pass for 30000 ms, until now"]);
    }

    [Test]
    public void IdleBegan_AfterALongIdlePass_DoesNotCountThePassItself()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.IdleBegan();
        _now += 9000;
        watch.IdleEnded();
        _now += 10;
        watch.IdleBegan();

        watch.LargestIdleGapMilliseconds.ShouldBe(200d, "from the process's start to the first pass; the pass's 9000 ms is not a gap");
    }

    [Test]
    public void Message_AnObjectRequest_SaysWhenItWasServedOnce()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Message(objectRequest: false);
        log.Said.Clear();
        _now += 900;
        watch.Message(objectRequest: true);
        _now += 10;
        watch.Message(objectRequest: true);

        watch.FirstObjectRequestMilliseconds.ShouldBe(1100d);
        log.Said.ShouldBe(["first WM_GETOBJECT served, 1100 ms after the process started"]);
    }

    [Test]
    public void Stop_WithTheLastStretchStillOpen_CountsItAndReportsOnce()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.IdleBegan();
        watch.IdleEnded();
        watch.Message(objectRequest: false);
        _now += 250;
        watch.IdleBegan();
        watch.IdleEnded();
        _now += 500;
        watch.Stop();
        watch.Stop();

        watch.Watching.ShouldBeFalse();
        log.Said.Single().ShouldBe(
            WindowThreadWatch.ReportLine + " largest gap between messages 750 ms, between idle passes 250 ms; no WM_GETOBJECT");
    }

    [Test]
    public void Message_AfterStop_NeitherMeasuresNorStopsHearingARequest()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Stop();

        double before = watch.LargestGapMilliseconds;

        log.Said.Clear();
        _now += 9000;
        watch.Message(objectRequest: false);
        watch.LargestGapMilliseconds.ShouldBe(before);
        log.Said.ShouldBeEmpty();

        watch.Message(objectRequest: true);
        watch.FirstObjectRequestMilliseconds.ShouldBe(9200d);
    }
}
