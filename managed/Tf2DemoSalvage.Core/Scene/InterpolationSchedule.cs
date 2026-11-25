using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// `GetClientInterpAmount()` by tick (B849), and the delay in ticks every interpolated variable is sampled behind.
/// </summary>
/// <remarks>
/// `GetClientInterpAmount` (`cdll_bounded_cvars.cpp:126`) is computed each time it is asked, clamping the watcher's
/// `cl_interp_ratio` by the server's `sv_client_min_interp_ratio` and `sv_client_max_interp_ratio` (and `cl_updaterate`
/// by `sv_minupdaterate`/`sv_maxupdaterate`) as the client has them then, every `net_SetConVar` read by that tick. So a
/// server that changes a bound mid-recording changes the delay from that tick on. This was one number for the whole
/// recording, under its last bounds. A step holds from its tick; the steps are few, a server's bounds arriving at
/// sign-on, so a reader scans them.
/// </remarks>
public sealed class InterpolationSchedule
{
    private readonly int[] _from;
    private readonly double[] _amounts;
    private readonly int[] _ticks;

    private InterpolationSchedule(int[] from, double[] amounts, int[] ticks, double interval)
    {
        _from = from;
        _amounts = amounts;
        _ticks = ticks;
        IntervalPerTick = interval;
    }

    /// <summary>Valve's defaults at TF2's tick interval, one instance every track starts from.</summary>
    public static InterpolationSchedule Tf2Default { get; } = Constant(ScenePropTrack.Tf2TickInterval);

    /// <summary>Seconds per tick the delays were converted at.</summary>
    public double IntervalPerTick { get; }

    /// <summary>The largest delay anywhere in the recording, in ticks: how far back a checkpoint's readers can reach.</summary>
    public int MaximumTicks
    {
        get
        {
            int most = 0;

            foreach (int ticks in _ticks)
            {
                most = Math.Max(most, ticks);
            }

            return most;
        }
    }

    /// <summary>One delay for every tick: Valve's defaults, or a track built for a test.</summary>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <param name="amount">`GetClientInterpAmount()` in seconds.</param>
    /// <returns>The schedule.</returns>
    public static InterpolationSchedule Constant(double intervalPerTick, double amount = ScenePropTrack.DefaultInterpolation) =>
        new([int.MinValue], [amount], [ScenePropTrack.DelayTicksFor(intervalPerTick, amount)], intervalPerTick);

    /// <summary>The schedule for a watcher's settings over a recording's `net_SetConVar`s.</summary>
    /// <param name="client">The watcher's `cl_interp`, `cl_interp_ratio` and `cl_updaterate`.</param>
    /// <param name="initial">What the server's ConVars are before the first change.</param>
    /// <param name="changes">The ConVars after each change, by the tick each holds from, in tick order.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <returns>The schedule, one step per change that moves the amount.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static InterpolationSchedule From(
        ClientInterp client, ServerConVars initial, IReadOnlyList<(int Tick, ServerConVars ConVars)> changes, double intervalPerTick)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(changes);

        List<int> from = [int.MinValue];
        List<double> amounts = [client.Amount(initial)];

        foreach ((int tick, ServerConVars conVars) in changes)
        {
            double amount = client.Amount(conVars);

            // The same float arithmetic over the same inputs gives the same double: a tolerance only for the analyzer.
            if (Math.Abs(amount - amounts[^1]) < 1e-12d)
            {
                continue;
            }

            // Two changes on one tick: the later is what that tick runs under.
            if (from[^1] == tick)
            {
                amounts[^1] = amount;
                continue;
            }

            from.Add(tick);
            amounts.Add(amount);
        }

        int[] ticks = new int[amounts.Count];

        for (int index = 0; index < ticks.Length; index++)
        {
            ticks[index] = ScenePropTrack.DelayTicksFor(intervalPerTick, amounts[index]);
        }

        return new InterpolationSchedule([.. from], [.. amounts], ticks, intervalPerTick);
    }

    /// <summary>`GetClientInterpAmount()` at a tick, in seconds.</summary>
    /// <param name="tick">The tick, fractional between two.</param>
    /// <returns>The amount.</returns>
    public double AmountAt(double tick) => _amounts[IndexAt(tick)];

    /// <summary>The delay a variable is sampled behind at a tick, in ticks (<see cref="ScenePropTrack.DelayTicksFor"/>).</summary>
    /// <param name="tick">The tick, fractional between two.</param>
    /// <returns>The delay.</returns>
    public int TicksAt(double tick) => _ticks[IndexAt(tick)];

    /// <summary>The delay at a time on the demo's clock, in seconds, for a reader that keeps time rather than ticks.</summary>
    /// <param name="seconds">The time.</param>
    /// <returns>The delay in seconds, whole ticks as `TICKS_TO_TIME` gives it.</returns>
    public double SecondsAtTime(double seconds) => TicksAt(seconds / Interval) * Interval;

    /// <summary>The first tick after this one at which the delay changes, or null when it never does again.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The tick, or null.</returns>
    public int? NextChangeAfter(double tick)
    {
        foreach (int start in _from)
        {
            if (start > tick)
            {
                return start;
            }
        }

        return null;
    }

    /// <summary>
    /// The first tick at which the delayed target, <c>tick − <see cref="TicksAt"/>(tick)</c>, reaches a changetime.
    /// </summary>
    /// <param name="changeTime">The changetime.</param>
    /// <returns>The tick.</returns>
    public double Reaches(double changeTime)
    {
        for (int index = 0; index < _from.Length; index++)
        {
            double start = _from[index];
            double end = index + 1 < _from.Length ? _from[index + 1] : double.PositiveInfinity;
            double at = Math.Max(start, changeTime + _ticks[index]);

            if (at < end)
            {
                return at;
            }
        }

        return double.PositiveInfinity;
    }

    private double Interval => IntervalPerTick > 0d ? IntervalPerTick : ScenePropTrack.Tf2TickInterval;

    private int IndexAt(double tick)
    {
        double floor = Math.Floor(tick);
        int index = 0;

        while (index + 1 < _from.Length && _from[index + 1] <= floor)
        {
            index++;
        }

        return index;
    }
}
