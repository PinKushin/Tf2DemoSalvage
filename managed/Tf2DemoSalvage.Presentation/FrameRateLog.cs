using System;
using System.Diagnostics;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// One frame-rate line per second — the rate, and the MEAN cost of every phase behind it.
/// </summary>
/// <remarks>
/// **Written because the viewer had no instrument for a steady state.** The only frame-cost
/// measurement was <see cref="StallReport"/>, whose threshold is <c>StallSeconds = 0.03</c> — so it
/// reports a breakdown for frames slower than 33 per second and is silent about every difference
/// above that. A twenty-second autoplay run of `tf2-2026-pub-pov-clean` logged nothing at all, which
/// reads as "no slow frames" and means only "nothing exceeded 30 ms"; the run could have been at 40
/// fps or 600 and the log would look identical. That is
/// `docs/memory/a-threshold-instrument-cannot-see-a-sum.md` applied to the frame loop.
///
/// **Every phase is a mean over the interval, and the first design got this wrong.** It logged the
/// phases of whichever frame happened to cross the second boundary. The owner: *"a probe that only
/// polls per second is way too slow so that better be a fucking average"* — correct, and it is
/// `docs/memory/log-the-event-not-a-sample-of-it.md`: at 90 fps a per-second sample publishes one
/// frame in ninety and calls it the cost of all of them. What made the mistake easy to miss is that
/// the RATE beside it was already an average — <see cref="FpsMeter"/> smooths it and carries
/// watermarks — so the line looked uniform while half of it was a sample.
///
/// **It reports the watermarks as well as the average rate, because they separate two different
/// faults.** A low average with a low worst frame is uniform cost — too much work every frame. A
/// high average with a bad worst frame is a stall, and the two have nothing in common but the number
/// a person notices.
///
/// **Not <see cref="FpsMeter"/> and not a replacement for it.** The meter is `cl_showfps`, drawn on
/// screen for a person watching in real time. This is the same reading written to a log for someone
/// reading afterwards — including from a headless run, where nobody is watching and the on-screen
/// meter cannot be read even in principle.
/// </remarks>
public sealed class FrameRateLog
{
    /// <summary>How often a line is written.</summary>
    /// <remarks>
    /// One second, matching the interval a person reads the on-screen meter at. Short enough that a
    /// stall lasting a second or two still shows as its own line rather than being averaged away,
    /// long enough that a five-minute demo produces three hundred lines rather than a hundred
    /// thousand.
    /// </remarks>
    public const double IntervalSeconds = 1.0;

    /// <summary>
    /// When the interval currently being timed began, or <c>null</c> before the first reportable
    /// frame.
    /// </summary>
    private double? _since;

    /// <summary>Frames accumulated into <see cref="_totals"/> since the last line.</summary>
    private int _frames;

    /// <summary>Summed phase costs for this interval, in stopwatch ticks.</summary>
    /// <remarks>
    /// Summed rather than averaged incrementally: a running mean would accumulate rounding across
    /// ninety frames, and the sum is exact until it is divided once at the end.
    /// </remarks>
    private FramePhases _totals;

    /// <summary>Offers one frame, and answers with a line when one is due.</summary>
    /// <param name="reading">
    /// This frame's reading, or <c>null</c> when <see cref="FpsMeter"/> has none — the first frame
    /// after the meter is shown has no duration to report.
    /// </param>
    /// <param name="phases">What this frame spent, from <see cref="FrameSequence.Run"/>.</param>
    /// <param name="atSeconds">Monotonic seconds since the run began.</param>
    /// <returns>The line to log, or <c>null</c> when the interval has not elapsed.</returns>
    /// <remarks>
    /// **A frame with no reading does not start the clock.** Passing it through as a zero would
    /// print `0 fps` and be read as a stall, and starting the interval on it would make a run that
    /// spent five seconds loading fire immediately and then again a second later. The interval is
    /// measured from the first frame that had something to say.
    /// </remarks>
    public string? Report(FpsReading? reading, in FramePhases phases, double atSeconds)
    {
        if (reading is not { } frame)
        {
            return null;
        }

        _totals = new FramePhases(
            Sound: _totals.Sound + phases.Sound,
            Camera: _totals.Camera + phases.Camera,
            Project: _totals.Project + phases.Project,
            Advance: _totals.Advance + phases.Advance,
            Capture: _totals.Capture + phases.Capture,
            Hud: _totals.Hud + phases.Hud,
            Draw: _totals.Draw + phases.Draw,
            Total: _totals.Total + phases.Total);

        _frames++;

        if (_since is not { } began)
        {
            _since = atSeconds;
            return null;
        }

        if (atSeconds - began < IntervalSeconds)
        {
            return null;
        }

        int over = _frames;

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"frame rate {frame.Fps} fps ({frame.Low} worst, {frame.High} best)"
            + $", {frame.FrameMilliseconds:0.0} ms"
            + $"; mean over {over} frames: sound {Mean(_totals.Sound, over):0.#}"
            + $", camera {Mean(_totals.Camera, over):0.#}"
            + $", project {Mean(_totals.Project, over):0.#}"
            + $", advance {Mean(_totals.Advance, over):0.#}"
            + $", capture {Mean(_totals.Capture, over):0.#}"
            + $", hud {Mean(_totals.Hud, over):0.#}"
            + $", draw {Mean(_totals.Draw, over):0.#}"
            + $", unaccounted {Mean(_totals.Unaccounted, over):0.#} ms");

        // **Reset, or every line after the first averages the whole run.** A stall in the opening
        // second would then never wash out, and the number would drift toward a lifetime mean that
        // describes no moment of the run.
        _since = atSeconds;
        _frames = 0;
        _totals = default;

        return line;
    }

    /// <summary>Mean milliseconds per frame for one phase.</summary>
    private static double Mean(long ticks, int frames) =>
        ticks / (double)Stopwatch.Frequency * 1000d / frames;
}
