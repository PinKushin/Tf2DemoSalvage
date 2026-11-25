using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// `GetClientInterpAmount()` by tick (B849), and the delay in ticks every interpolated variable is sampled behind.
/// </summary>
/// <remarks>
/// `GetClientInterpAmount` (`cdll_bounded_cvars.cpp:127`) is computed each time it is asked, clamping the watcher's
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

    /// <summary>The schedule as it stood through each step: entry k holds steps 0..k, the last is this one (B860).</summary>
    private readonly InterpolationSchedule[] _asOf;

    private InterpolationSchedule(int[] from, double[] amounts, int[] ticks, double interval)
    {
        _from = from;
        _amounts = amounts;
        _ticks = ticks;
        IntervalPerTick = interval;
        _asOf = new InterpolationSchedule[from.Length];
        _asOf[^1] = this;

        for (int last = 0; last + 1 < from.Length; last++)
        {
            _asOf[last] = new InterpolationSchedule(from[..(last + 1)], amounts[..(last + 1)], ticks[..(last + 1)], interval, _asOf);
        }
    }

    private InterpolationSchedule(int[] from, double[] amounts, int[] ticks, double interval, InterpolationSchedule[] asOf)
    {
        _from = from;
        _amounts = amounts;
        _ticks = ticks;
        IntervalPerTick = interval;
        _asOf = asOf;
    }

    /// <summary>Valve's defaults at TF2's tick interval, one instance every track starts from.</summary>
    public static InterpolationSchedule Tf2Default { get; } = Constant(ScenePropTrack.Tf2TickInterval);

    /// <summary>Seconds per tick the delays were converted at.</summary>
    public double IntervalPerTick { get; }

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

            // Two changes on one tick leave two steps there; the lookups take the later, the one that tick runs under.
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
    /// <remarks>A millionth of a tick added before the floor, as `FireTick` does, so an exact tick that division leaves a
    /// hair short is not read as the one before (B848 review 2, Sonnet).</remarks>
    public double SecondsAtTime(double seconds) => TicksAt((seconds / Interval) + 1e-6d) * Interval;

    /// <summary>
    /// The schedule a frame interpolates under when its <c>FRAME_START</c> had read the recording through
    /// <paramref name="frameStart"/>: the steps from then on are not yet known to it (B860).
    /// </summary>
    /// <param name="frameStart">The last tick read before the frame started.</param>
    /// <returns>The schedule through the step holding that tick; the same instance for every tick of one step.</returns>
    /// <remarks>
    /// <c>CEngine::Frame</c> calls <c>ClientDLL_FrameStageNotify( FRAME_START )</c> before <c>HostState_Frame</c> runs the
    /// ticks whose <c>CL_ReadPackets</c> reads the frame's packets (the x64 <c>engine.dll</c>, disassembled: <c>CEngine::Frame</c>, <c>_Host_RunFrame</c>, <c>_Host_RunFrame_Client</c>),
    /// and <c>FRAME_START</c> is where <c>CheckCLInterpChanged</c> hands each variable the new amount
    /// (<c>cdll_client_int.cpp:2344</c>, <c>c_baseentity.cpp:6443-6465</c>). So a change read this frame is interpolated
    /// under from the next.
    /// </remarks>
    public InterpolationSchedule AsOf(double frameStart) => _asOf[IndexAt(frameStart)];

    /// <summary>The ticks the steps after the first start at, ascending: where <see cref="AsOf"/> changes its answer.</summary>
    internal ReadOnlySpan<int> StepStarts => _from.AsSpan(1);

    /// <summary>The start of the last step at or before a tick whose delay is longer than the step before it (B860).</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The step's first tick, or null when the delay has not risen by then.</returns>
    public int? LastRiseAtOrBefore(double tick)
    {
        for (int index = _from.Length - 1; index >= 1; index--)
        {
            if (_from[index] <= tick && _ticks[index] > _ticks[index - 1])
            {
                return _from[index];
            }
        }

        return null;
    }

    /// <summary>
    /// The furthest the delayed target reached on any whole tick before this one, a frame a tick: what every earlier frame
    /// pruned its history against (B860).
    /// </summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The target, or negative infinity before the first tick.</returns>
    /// <remarks>
    /// The frame at tick u interpolates under the delay the tick before it left (<see cref="AsOf"/>): a step from tick s is
    /// first used by the frame at s + 1, and the frame at s still aims <c>s − </c>the step before's delay.
    /// </remarks>
    public double MaxTargetBefore(double tick)
    {
        double last = Math.Ceiling(tick) - 1d;
        double most = double.NegativeInfinity;

        for (int index = 0; index < _from.Length; index++)
        {
            double end = index + 1 < _from.Length ? _from[index + 1] : double.PositiveInfinity;
            double latest = Math.Min(end, last);

            if (latest >= (double)_from[index] + 1d)
            {
                most = Math.Max(most, latest - _ticks[index]);
            }
        }

        return most;
    }

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
    /// The first tick after <paramref name="after"/> at which the delayed target, <c>tick − <see cref="TicksAt"/>(tick)</c>,
    /// comes to reach a changetime it had not reached the tick before, or null when it never does again.
    /// </summary>
    /// <param name="changeTime">The changetime.</param>
    /// <param name="after">The tick asked from: the answer is later.</param>
    /// <returns>The tick, or null.</returns>
    /// <remarks>
    /// Within a step the target rises with the tick, so it reaches a changetime once; a delay that RISES at the next step
    /// can drop it back below one it had passed, and it reaches it again there (B848 review 2, Fable, Sonnet). A reach that
    /// was already true at <paramref name="after"/> is no moment.
    /// </remarks>
    public double? Reaches(double changeTime, double after)
    {
        double first = Math.Floor(after) + 1d;

        for (int index = 0; index < _from.Length; index++)
        {
            double end = index + 1 < _from.Length ? _from[index + 1] : double.PositiveInfinity;
            double at = Math.Max(Math.Max(_from[index], first), changeTime + _ticks[index]);

            if (at < end && (at - 1d) - TicksAt(at - 1d) < changeTime)
            {
                return at;
            }
        }

        return null;
    }

    private double Interval => IntervalPerTick > 0d ? IntervalPerTick : ScenePropTrack.Tf2TickInterval;

    private int IndexAt(double tick)
    {
        // The steps start on whole ticks, so comparing the tick itself is comparing its floor (B848 review 4, Fable).
        int index = 0;

        while (index + 1 < _from.Length && _from[index + 1] <= tick)
        {
            index++;
        }

        return index;
    }
}
