using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A measurement's slow frames beside its heartbeat gaps, so each stall says whether the whole process stopped.</summary>
/// <remarks>
/// **The question it answers** (finding 88, after finding 87's observation): a stall of about 400 ms appeared in sound reads, in the HUD and in the draw.
/// A stall that lands in a different phase every time is either ours in each place, or a pause from outside the frame:
/// CPU starvation, the scheduler, memory pressure, a collector suspension. A thread that only sleeps a millisecond at a
/// time stops too in the second case and not in the first, so a heartbeat gap over the same interval is the evidence.
///
/// **Every time is carried from <see cref="System.Diagnostics.Stopwatch"/> ticks where it was taken** (B243): the frame's
/// end and duration from the frame clock, the gap's end and length from the heartbeat's own reads. Nothing is
/// recomputed by a second route.
/// </remarks>
/// <param name="frequency">Stopwatch ticks a second.</param>
public sealed class PauseLedger(long frequency)
{
    /// <summary>A frame counts as a stall past this, in milliseconds.</summary>
    public const double StallMilliseconds = 100d;

    /// <summary>A heartbeat interval counts as a gap past this, in milliseconds.</summary>
    public const double GapMilliseconds = 50d;

    /// <summary>A frame counts as slow past this, in milliseconds: counted, summed and listed with its detail (B595).</summary>
    public const double SlowMilliseconds = 50d;

    private readonly List<(long End, long Length, string Phase, double PausedMs, string Detail)> _stalls = [];
    private readonly List<(long End, long Length)> _gaps = [];
    private readonly Lock _gate = new();
    private long _slowTicks;
    private int _slowCount;

    /// <summary>Whether a frame this long is slow, so a caller builds its detail only then.</summary>
    /// <param name="length">The frame, in ticks.</param>
    /// <returns>True past <see cref="SlowMilliseconds"/>.</returns>
    public bool IsSlow(long length) => Ms(length) > SlowMilliseconds;

    /// <summary>Records a frame if it was slow.</summary>
    /// <param name="end">When the frame ended, in ticks.</param>
    /// <param name="length">How long it took, in ticks.</param>
    /// <param name="phase">The phase that held most of it.</param>
    /// <param name="pausedMs">The collector's pause over the same interval, in milliseconds.</param>
    /// <param name="detail">What the frame's own counters said, carried from where they were read; printed as given.</param>
    public void Frame(long end, long length, string phase, double pausedMs, string detail = "")
    {
        if (IsSlow(length))
        {
            _slowCount++;
            _slowTicks += length;
            _stalls.Add((end, length, phase, pausedMs, detail));
        }
    }

    /// <summary>Records one heartbeat interval if it was a gap. Called from the heartbeat's thread.</summary>
    /// <param name="previous">The heartbeat's previous read, in ticks.</param>
    /// <param name="now">This read, in ticks.</param>
    public void Beat(long previous, long now)
    {
        if (Ms(now - previous) > GapMilliseconds)
        {
            lock (_gate)
            {
                _gaps.Add((now, now - previous));
            }
        }
    }

    /// <summary>The report: each stall with the gap that overlaps it, then the gaps no stall explains.</summary>
    /// <param name="start">When the measurement began, in ticks; times print as seconds after it.</param>
    /// <returns>Lines to print.</returns>
    public string Report(long start)
    {
        List<(long End, long Length)> gaps;

        lock (_gate)
        {
            gaps = [.. _gaps];
        }

        StringBuilder text = new();
        HashSet<int> matched = [];

        int stalls = _stalls.FindAll(stall => Ms(stall.Length) > StallMilliseconds).Count;

        text.Append(CultureInfo.InvariantCulture, $"pauses: {stalls} frames over {StallMilliseconds:0} ms, {gaps.Count} heartbeat gaps over {GapMilliseconds:0} ms");
        text.Append(CultureInfo.InvariantCulture, $"; {_slowCount} frames over {SlowMilliseconds:0} ms, {Ms(_slowTicks):0} ms in them");

        foreach ((long end, long length, string phase, double paused, string detail) in _stalls)
        {
            int overlap = gaps.FindIndex(gap => gap.End - gap.Length < end && end - length < gap.End);
            string kind = Ms(length) > StallMilliseconds ? "stall" : "slow";

            text.Append(CultureInfo.InvariantCulture, $"\n  {kind} at {Seconds(end - start):0.000} s: {Ms(length):0} ms, mostly {phase}, gc paused {paused:0} ms; ");

            if (overlap >= 0)
            {
                matched.Add(overlap);
                text.Append(CultureInfo.InvariantCulture, $"heartbeat gap {Ms(gaps[overlap].Length):0} ms ending {Seconds(gaps[overlap].End - start):0.000} s");
            }
            else if (kind == "stall")
            {
                text.Append("no heartbeat gap");
            }

            if (detail.Length > 0)
            {
                text.Append(kind == "stall" || overlap >= 0 ? "; " : string.Empty).Append(detail);
            }
        }

        for (int index = 0; index < gaps.Count; index++)
        {
            if (!matched.Contains(index))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n  gap without a stall at {Seconds(gaps[index].End - start):0.000} s: {Ms(gaps[index].Length):0} ms");
            }
        }

        return text.ToString();
    }

    /// <summary>The phase that held most of a frame, with the time outside the sequence named <c>between</c>.</summary>
    /// <param name="phases">What the frame's sequence measured.</param>
    /// <param name="frameTicks">The whole frame by the frame clock.</param>
    /// <returns>The phase's name.</returns>
    public static string LargestPhase(in FramePhases phases, long frameTicks)
    {
        (string Name, long Ticks)[] parts =
        [
            ("sound", phases.Sound), ("camera", phases.Camera), ("project", phases.Project), ("advance", phases.Advance),
            ("capture", phases.Capture), ("hud", phases.Hud), ("draw", phases.Draw),
            ("unaccounted", phases.Unaccounted), ("between", frameTicks - phases.Total),
        ];

        (string Name, long Ticks) largest = parts[0];

        foreach ((string Name, long Ticks) part in parts)
        {
            if (part.Ticks > largest.Ticks)
            {
                largest = part;
            }
        }

        return largest.Name;
    }

    private double Ms(long ticks) => ticks * 1000d / frequency;

    private double Seconds(long ticks) => ticks / (double)frequency;
}
