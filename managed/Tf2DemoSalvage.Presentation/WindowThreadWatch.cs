using System;
using System.Globalization;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the window thread did from the process's start to the first frame on screen, read on that thread (B707, B708).</summary>
/// <remarks>
/// **Two gaps, because one log line could not tell two faults apart.** On run 41669140049 `no UI Automation client` came 29 s
/// after the window was shown, and that line is written from the idle path, so the thread had either stopped pumping or never
/// gone idle. <see cref="LargestGapMilliseconds"/> is the longest stretch with no message dispatched at all, which is the first;
/// <see cref="LargestIdleGapMilliseconds"/> is the longest with the thread never entering its idle path, which is the second when
/// the first is small. A long idle handler is not a gap: it is measured from the end of one idle pass to the start of the next.
/// **The time a client's request is SERVED**, not when a later idle pass noticed, is said by a line of its own.
///
/// **Both stretches start at the process's start**, so the first is measured whatever it is. All times here are from there, the same
/// origin as `Program`'s `startup:` lines.
///
/// The window feeds this every message it dispatches and a timer's tick, so an idle thread still beats. **A sender's messages count
/// as messages**: a sampler's `WM_NULL` reaches the same `WndProc`, so with one running this measures the same stalls by the same event
/// and agreement with it shows only that the arithmetic is right. Without one, the timer is what beats. The tick stops with
/// <see cref="Stop"/>, so a measured frame pays for none of it.
/// </remarks>
public sealed class WindowThreadWatch
{
    /// <summary>The start of the line <see cref="Stop"/> writes; the UI suite waits on it as "the opening has finished".</summary>
    public const string ReportLine = "window thread to the first frame on screen:";

    /// <summary>A stretch this long is said when it ends, with the log's time beside it, so it can be read against the load's lines.</summary>
    private const double SayFromMilliseconds = 1000d;

    private readonly ILogger _log;
    private readonly Func<long> _timestamp;
    private readonly long _frequency;
    private readonly long _began;
    private long _lastMessage;
    private long _idleEnded;
    private bool _objectRequested;

    /// <summary>Starts watching.</summary>
    /// <param name="log">Where the lines go.</param>
    /// <param name="timestamp">The clock, in ticks of <paramref name="frequency"/>.</param>
    /// <param name="frequency">Ticks a second.</param>
    /// <param name="sinceProcessStart">How long the process has been running.</param>
    public WindowThreadWatch(ILogger log, Func<long> timestamp, long frequency, TimeSpan sinceProcessStart)
    {
        ArgumentNullException.ThrowIfNull(timestamp);

        _log = log;
        _timestamp = timestamp;
        _frequency = frequency;
        _began = timestamp() - (long)(sinceProcessStart.TotalSeconds * frequency);
        _lastMessage = _began;
        _idleEnded = _began;
    }

    /// <summary>Whether the window should keep the tick running.</summary>
    public bool Watching { get; private set; } = true;

    /// <summary>The longest stretch with no message dispatched.</summary>
    public double LargestGapMilliseconds { get; private set; }

    /// <summary>The longest stretch with the thread not entering its idle path.</summary>
    public double LargestIdleGapMilliseconds { get; private set; }

    /// <summary>When the first <c>WM_GETOBJECT</c> was served, from the process's start, or null before one.</summary>
    public double? FirstObjectRequestMilliseconds { get; private set; }

    /// <summary>The window thread dispatched a message.</summary>
    /// <param name="objectRequest">Whether it is <c>WM_GETOBJECT</c>, a UI Automation or MSAA client asking for the provider.</param>
    public void Message(bool objectRequest)
    {
        if (!Watching)
        {
            HearAfterStop(objectRequest);

            return;
        }

        HearAfterStop(objectRequest);

        long now = _timestamp();
        double gap = Milliseconds(now - _lastMessage);

        LargestGapMilliseconds = Math.Max(LargestGapMilliseconds, gap);

        if (gap >= SayFromMilliseconds)
        {
            Say($"window thread dispatched nothing for {gap:F0} ms, until now");
        }

        _lastMessage = now;
    }

    /// <summary>The thread entered its idle path.</summary>
    public void IdleBegan()
    {
        if (!Watching)
        {
            return;
        }

        double gap = Milliseconds(_timestamp() - _idleEnded);

        LargestIdleGapMilliseconds = Math.Max(LargestIdleGapMilliseconds, gap);

        if (gap >= SayFromMilliseconds)
        {
            Say($"window thread reached no idle pass for {gap:F0} ms, until now");
        }
    }

    /// <summary>The thread left its idle path, however long it stayed.</summary>
    public void IdleEnded() => _idleEnded = _timestamp();

    /// <summary>The first frame is on screen: says what was seen, once, and stops.</summary>
    public void Stop()
    {
        if (!Watching)
        {
            return;
        }

        // The last stretch has no message after it yet: the frame that ended it is this call.
        Message(objectRequest: false);
        Watching = false;

        string asked = FirstObjectRequestMilliseconds is { } served
            ? string.Create(CultureInfo.InvariantCulture, $"first WM_GETOBJECT at {served:F0} ms")
            : "no WM_GETOBJECT";

        Say($"{ReportLine} largest gap between messages {LargestGapMilliseconds:F0} ms, between idle passes {LargestIdleGapMilliseconds:F0} ms; {asked}");
    }

    private void HearAfterStop(bool objectRequest)
    {
        if (objectRequest && !_objectRequested)
        {
            _objectRequested = true;
            FirstObjectRequestMilliseconds = Milliseconds(_timestamp() - _began);
            Say($"first WM_GETOBJECT served, {FirstObjectRequestMilliseconds:F0} ms after the process started");
        }
    }

    private double Milliseconds(long ticks) => ticks * 1000d / _frequency;

    private void Say(FormattableString message) =>
        _log.LogInformation("{Message}", message.ToString(CultureInfo.InvariantCulture));
}
