using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// One frame-rate line per second, for a log somebody reads afterwards.
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
/// **It reports the watermarks as well as the average, because they separate two different
/// faults.** A low average with a low worst frame is uniform cost — too much work every frame. A
/// high average with a bad worst frame is a stall, and the two have nothing in common but the
/// number a person notices. <see cref="FpsReading"/> already carries both every frame; the gap this
/// closes is that nothing wrote them down.
///
/// **Not <see cref="FpsMeter"/> and not a replacement for it.** The meter is `cl_showfps`, drawn on
/// screen for a person watching in real time. This is the same reading, written once a second, for
/// a person reading a log after the fact — including from a headless run, where nobody is watching
/// at all and where the on-screen meter cannot be read even in principle.
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

    /// <summary>Offers one frame's reading, and answers with a line when one is due.</summary>
    /// <param name="reading">
    /// This frame's reading, or <c>null</c> when <see cref="FpsMeter"/> has none — the first frame
    /// after the meter is shown has no duration to report.
    /// </param>
    /// <param name="atSeconds">Monotonic seconds since the run began.</param>
    /// <returns>The line to log, or <c>null</c> when the interval has not elapsed.</returns>
    /// <remarks>
    /// **A frame with no reading does not start the clock.** Passing it through as a zero would
    /// print `0 fps` and be read as a stall, and starting the interval on it would make a run that
    /// spent five seconds loading fire immediately and then again a second later. The interval is
    /// measured from the first frame that had something to say.
    /// </remarks>
    public string? Report(FpsReading? reading, double atSeconds)
    {
        if (reading is not { } frame)
        {
            return null;
        }

        if (_since is not { } began)
        {
            _since = atSeconds;
            return null;
        }

        if (atSeconds - began < IntervalSeconds)
        {
            return null;
        }

        _since = atSeconds;

        // Named rather than positional — `(44, 310)` is what the on-screen meter shows, where the
        // reader has the panel's own layout to go by, and a log line has nothing.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"frame rate {frame.Fps} fps ({frame.Low} worst, {frame.High} best)"
            + $", {frame.FrameMilliseconds:0.0} ms");
    }
}
