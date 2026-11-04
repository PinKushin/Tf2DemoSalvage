using System;
using System.Globalization;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the window thread did from startup to the first frame on screen, read on that thread (B707, B708).</summary>
/// <remarks>
/// **Two gaps, because one log line could not tell two faults apart.** On run 41669140049 `no UI Automation client` came 29 s
/// after the window was shown, and that line is written from the idle path, so the thread had either stopped pumping or never
/// gone idle. <see cref="LargestGapMilliseconds"/> is the longest stretch with no message dispatched at all, which is the first;
/// <see cref="LargestIdleGapMilliseconds"/> is the longest with no idle pass, which is the second when the first is small.
/// **The time a client's request is SERVED**, not when a later idle pass noticed, is said by a line of its own.
///
/// The window feeds this every message it dispatches and a timer's tick, so an idle thread still beats. The tick stops with
/// <see cref="Stop"/>, so a measured frame pays for none of it.
/// </remarks>
/// <param name="log">Where the lines go.</param>
/// <param name="timestamp">The clock, in ticks of <paramref name="frequency"/>.</param>
/// <param name="frequency">Ticks a second.</param>
public sealed class WindowThreadWatch(ILogger log, Func<long> timestamp, long frequency)
{
    /// <summary>The start of the line <see cref="Stop"/> writes; the UI suite waits on it as "the opening has finished".</summary>
    public const string ReportLine = "window thread to the first frame on screen:";

    /// <summary>A stretch this long is said when it ends, with the log's time beside it, so it can be read against the load's lines.</summary>
    private const double SayFromMilliseconds = 1000d;

    private readonly long _began = timestamp();
    private long _lastMessage = -1;
    private long _lastIdle = -1;
    private bool _objectRequested;

    /// <summary>Whether the window should keep the tick running.</summary>
    public bool Watching { get; private set; } = true;

    /// <summary>The longest stretch with no message dispatched.</summary>
    public double LargestGapMilliseconds { get; private set; }

    /// <summary>The longest stretch with no idle pass, from the first.</summary>
    public double LargestIdleGapMilliseconds { get; private set; }

    /// <summary>When the first <c>WM_GETOBJECT</c> was served, from this watch's start, or null before one.</summary>
    public double? FirstObjectRequestMilliseconds { get; private set; }

    /// <summary>The window thread dispatched a message.</summary>
    /// <param name="objectRequest">Whether it is <c>WM_GETOBJECT</c>, a UI Automation or MSAA client asking for the provider.</param>
    public void Message(bool objectRequest)
    {
        if (objectRequest && !_objectRequested)
        {
            _objectRequested = true;
            FirstObjectRequestMilliseconds = Milliseconds(timestamp() - _began);
            Say($"first WM_GETOBJECT served, {FirstObjectRequestMilliseconds:F0} ms after the session opened");
        }

        if (!Watching)
        {
            return;
        }

        long now = timestamp();

        if (_lastMessage >= 0)
        {
            double gap = Milliseconds(now - _lastMessage);

            LargestGapMilliseconds = Math.Max(LargestGapMilliseconds, gap);

            if (gap >= SayFromMilliseconds)
            {
                Say($"window thread dispatched nothing for {gap:F0} ms, until now");
            }
        }

        _lastMessage = now;
    }

    /// <summary>The window thread's queue emptied and the idle path ran.</summary>
    public void Idled()
    {
        if (!Watching)
        {
            return;
        }

        long now = timestamp();

        if (_lastIdle >= 0)
        {
            double gap = Milliseconds(now - _lastIdle);

            LargestIdleGapMilliseconds = Math.Max(LargestIdleGapMilliseconds, gap);

            if (gap >= SayFromMilliseconds)
            {
                Say($"window thread reached no idle pass for {gap:F0} ms, until now");
            }
        }

        _lastIdle = now;
    }

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

    private double Milliseconds(long ticks) => ticks * 1000d / frequency;

    private void Say(FormattableString message) =>
        log.LogInformation("{Message}", message.ToString(CultureInfo.InvariantCulture));
}
