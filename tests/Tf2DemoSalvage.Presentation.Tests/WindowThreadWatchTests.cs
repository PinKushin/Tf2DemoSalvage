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

    private WindowThreadWatch Watch(ILogger log)
    {
        _now = 0;

        return new WindowThreadWatch(log, () => _now, frequency: 1000);
    }

    [Test]
    public void Message_AfterAThreadStoppedPumping_ReportsTheGapAndSaysItWhenItEnds()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Message(objectRequest: false);
        _now = 100;
        watch.Message(objectRequest: false);
        _now = 6600;
        watch.Message(objectRequest: false);
        _now = 6650;
        watch.Message(objectRequest: false);

        watch.LargestGapMilliseconds.ShouldBe(6500d, "a later, shorter gap does not replace it");
        log.Said.ShouldBe(["window thread dispatched nothing for 6500 ms, until now"]);
    }

    [Test]
    public void Message_BelowTheSayingLength_SaysNothing()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Message(objectRequest: false);
        _now = 999;
        watch.Message(objectRequest: false);

        watch.LargestGapMilliseconds.ShouldBe(999d);
        log.Said.ShouldBeEmpty();
    }

    [Test]
    public void Idled_WhileMessagesKeepComing_ReportsAnIdleGapWithNoMessageGap()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Idled();

        for (int step = 0; step < 300; step++)
        {
            _now += 100;
            watch.Message(objectRequest: false);
        }

        watch.Idled();
        _now += 5;
        watch.Idled();

        watch.LargestGapMilliseconds.ShouldBe(100d);
        watch.LargestIdleGapMilliseconds.ShouldBe(30000d, "a later, shorter gap does not replace it");
        log.Said.ShouldBe(["window thread reached no idle pass for 30000 ms, until now"]);
    }

    [Test]
    public void Message_AnObjectRequest_SaysWhenItWasServedOnce()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        _now = 54000;
        watch.Message(objectRequest: true);
        _now = 54010;
        watch.Message(objectRequest: true);

        watch.FirstObjectRequestMilliseconds.ShouldBe(54000d);
        log.Said.ShouldBe(["first WM_GETOBJECT served, 54000 ms after the session opened"]);
    }

    [Test]
    public void Stop_WithTheLastStretchStillOpen_CountsItAndReportsOnce()
    {
        Lines log = new();
        WindowThreadWatch watch = Watch(log);

        watch.Idled();
        watch.Message(objectRequest: false);
        _now = 250;
        watch.Idled();
        _now = 750;
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
        log.Said.Clear();
        _now = 9000;
        watch.Message(objectRequest: false);
        watch.LargestGapMilliseconds.ShouldBe(0d);
        log.Said.ShouldBeEmpty();

        watch.Message(objectRequest: true);
        watch.FirstObjectRequestMilliseconds.ShouldBe(9000d);
    }
}
