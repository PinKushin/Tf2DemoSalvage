using System;
using System.Diagnostics;
using System.Globalization;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// The mean cost of a scene rebuild, written every hundred rebuilds.
/// </summary>
/// <remarks>
/// **This is the breakdown of `advance`, and `advance` is the frame.** Measured on
/// `tf2-2026-pub-pov-clean` at 96 fps, an 11 ms frame is `advance 7.7, draw 1.7, sound 0.4` — so
/// seventy per cent of the time is the scene rebuild and the GPU is very nearly idle. That number
/// came from <see cref="FrameRateLog"/>; this says which part of the rebuild it is.
///
/// **<see cref="StallReport.Moment"/> already names those parts and cannot report this**, because
/// it fires only past 30 ms. At 11 ms it never fires, so the columns exist, are measured every
/// rebuild, and are discarded — the same shape as the frame phases before `FrameRateLog`.
///
/// **Counted in rebuilds rather than timed**, because this runs inside the rebuild and has no clock
/// to consult. At the rate above a hundred rebuilds is about a second. The line states how many it
/// averaged, so a reader never has to assume the count.
///
/// **A mean, never the last rebuild.** The owner, on the first version of the frame log: *"a probe
/// that only polls per second is way too slow so that better be a fucking average"* — the same rule
/// and the same trap, since one rebuild in a hundred is a sample dressed as a measurement
/// (`docs/memory/log-the-event-not-a-sample-of-it.md`).
/// </remarks>
public sealed class MomentCostLog
{
    /// <summary>How many rebuilds each line averages.</summary>
    public const int DefaultEvery = 100;

    private readonly int _every;

    private int _rebuilds;
    private long _sample;
    private long _total;
    private long _drawList;
    private long _models;
    private long _pose;
    private long _weapons;
    private long _viewmodel;

    // The parts of `pose`, which is the largest column of the largest column. Measured every
    // rebuild by EntityModelSet and discarded by the same threshold as everything else here.
    private long _lighting;
    private long _simulate;
    private long _wornLight;
    private long _setup;
    private long _skin;
    private long _animation;
    private long _report;
    private long _drawn;

    /// <summary>Starts a log.</summary>
    /// <param name="every">
    /// Rebuilds per line; <see cref="DefaultEvery"/> when not given. Taken as a parameter so a test
    /// can use a count it can write out by hand rather than driving a hundred rebuilds to see one
    /// line.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="every"/> is not positive.</exception>
    public MomentCostLog(int every = DefaultEvery)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(every);

        _every = every;
    }

    /// <summary>Offers one rebuild, and answers with a line when enough have accumulated.</summary>
    /// <param name="phases">What <c>MomentScene.Build</c> measured.</param>
    /// <param name="sampleTicks">
    /// Reading the tick's players and props off the timeline, which is measured outside
    /// <paramref name="phases"/> and is its own column — 2 to 6.5 ms in the breakdowns seen so far,
    /// so dropping it would hide a real cost.
    /// </param>
    /// <returns>The line to log, or <c>null</c> when fewer than <see cref="DefaultEvery"/> have.</returns>
    public string? Report(in MomentPhases phases, long sampleTicks)
    {
        _sample += sampleTicks;
        _total += phases.Total;
        _drawList += phases.DrawList;
        _models += phases.Models;
        _pose += phases.Pose;
        _weapons += phases.Weapons;
        _viewmodel += phases.Viewmodel;

        _lighting += phases.Counters.Lighting;
        _simulate += phases.Counters.Simulate;
        _wornLight += phases.Counters.WornLight;
        _setup += phases.Counters.Setup;
        _skin += phases.Counters.Skin;
        _animation += phases.Counters.Animation;
        _report += phases.Counters.Report;

        // **How many of the posed props survive to be drawn.** The engine never poses the others:
        // `CClientLeafSystem::CollateRenderablesInLeaf` gathers renderables out of VISIBLE leaves,
        // so `SetupBones` is reached only for what the PVS and frustum already kept. This project
        // poses every prop the tick carries, so the ratio is the size of that divergence.
        _drawn += phases.Drawn;

        if (++_rebuilds < _every)
        {
            return null;
        }

        int over = _rebuilds;

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"moment cost, mean over {over} rebuilds: {Mean(_total + _sample, over):0.#} ms"
            + $" = sample {Mean(_sample, over):0.#}"
            + $", drawlist {Mean(_drawList, over):0.#}"
            + $", models {Mean(_models, over):0.#}"
            + $", pose {Mean(_pose, over):0.#}"
            + $", weapons {Mean(_weapons, over):0.#}"
            + $", viewmodel {Mean(_viewmodel, over):0.#}"

            // **`rest` is a residual and every other pose column is measured**, which is what makes
            // it the one worth reading: all of them small with this large means the cost is in
            // something no timer covers yet.
            + $"; pose = lighting {Mean(_lighting, over):0.#}"
            + $", simulate {Mean(_simulate, over):0.#}"
            + $", wornlight {Mean(_wornLight, over):0.#}"
            + $", setup {Mean(_setup, over):0.#}"
            + $", skin {Mean(_skin, over):0.#}"
            + $", anim {Mean(_animation, over):0.#}"
            + $", rest {Mean(Rest(), over):0.#}"
            + $"; drawn {_drawn / (double)over:0.#} per rebuild");

        _rebuilds = 0;
        _sample = 0;
        _total = 0;
        _drawList = 0;
        _models = 0;
        _pose = 0;
        _weapons = 0;
        _viewmodel = 0;
        _lighting = 0;
        _simulate = 0;
        _wornLight = 0;
        _setup = 0;
        _skin = 0;
        _animation = 0;
        _report = 0;
        _drawn = 0;

        return line;
    }

    /// <summary>The part of <c>pose</c> no timer covers, by subtraction.</summary>
    /// <remarks>
    /// **`anim` is deliberately not subtracted, and the first version of this subtracted it.** It
    /// came out at `rest -0.4`, which is impossible for a residual and is how the mistake announced
    /// itself: <c>Animation</c> is time already inside another column rather than a sibling of them,
    /// so taking it out again counts it twice. The set subtracted here is exactly
    /// <see cref="StallReport.Moment"/>'s, which is the formula that has been read against real
    /// numbers.
    /// </remarks>
    private long Rest() =>
        _pose - _lighting - _viewmodel - _simulate - _wornLight - _report - _setup - _skin;

    /// <summary>Mean milliseconds per rebuild.</summary>
    private static double Mean(long ticks, int rebuilds) =>
        ticks / (double)Stopwatch.Frequency * 1000d / rebuilds;
}
