using System;
using System.Diagnostics;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What only the window knows about the second being reported.</summary>
/// <param name="Playing">Whether playback is running rather than paused.</param>
/// <param name="Flying">Whether the free camera is being flown.</param>
/// <param name="YieldedTo">What ended the idle burst, named by the platform.</param>
/// <param name="LightingTicks">Stopwatch ticks the lighting pass took, from the model set.</param>
/// <param name="Garbage">The collector's account of the second, or empty when it was quiet.</param>
/// <remarks>
/// **`YieldedTo` is a STRING because the answer is Windows-specific.** It is a message id — WM_TIMER
/// and friends — and naming one is the one part of this report that a second frontend could not
/// produce. So the window names it and the ledger repeats it, rather than the ledger knowing what a
/// Windows message is.
/// </remarks>
public readonly record struct FrameContext(
    bool Playing,
    bool Flying,
    string YieldedTo,
    long LightingTicks,
    string Garbage);

/// <summary>A once-a-second account of where a second of frames went.</summary>
/// <remarks>
/// **This was <c>MainForm.CountFrame</c>** (B188, D90): an accumulator, a threshold and a format
/// string. None of it needs a window and none of it had a test.
///
/// **It is not <see cref="FpsMeter"/>, and merging them would lose both.** That is TF2's
/// `cl_showfps` panel — a smoothed average with colour thresholds, drawn on screen for a person
/// watching. This is a diagnostic ledger written to the log, and its value is the BREAKDOWN: B191
/// was found by reading which column stayed fat as the others were measured away, which no single
/// averaged number can show.
///
/// **Every counter is per-second and reset on report.** One that survived would make the next second
/// read as worse than it was, and a ledger that accumulates for ever eventually reports the whole
/// session as one bad second.
/// </remarks>
public sealed class FrameLedger
{
    private int _frames;
    private double _longestFrameSeconds;
    private long _sampling;
    private long _posing;
    private long _drawing;
    private long _yields;

    /// <summary>Records a drawn frame and how long it took.</summary>
    /// <param name="frameSeconds">The frame's duration, UNCLAMPED.</param>
    /// <remarks>
    /// **Unclamped, and this is the one rule worth stating twice.** The reading used to pass through
    /// the free camera's stall clamp, so the worst frame could never be reported as worse than
    /// 100 ms — the ceiling. The owner's report was "everything freezes for a half a second to maybe
    /// a second" and the log for those exact seconds said `longest 100 ms`: not a measurement, the
    /// clamp showing through. A saturating instrument is worse than a missing one, because the
    /// ceiling looks like a number somebody measured.
    ///
    /// **The LONGEST rather than a mean**, because a mean hides the stall a person actually sees.
    /// Sixty good frames and one 120 ms freeze average to 17 ms, which reads as healthy.
    /// </remarks>
    public void Drew(double frameSeconds)
    {
        _frames++;
        _longestFrameSeconds = Math.Max(_longestFrameSeconds, frameSeconds);
    }

    /// <summary>Adds time spent reading a tick's players and props off the timeline.</summary>
    /// <param name="ticks">Stopwatch ticks.</param>
    public void Sampled(long ticks) => _sampling += ticks;

    /// <summary>What has been charged to sampling since the last report.</summary>
    /// <remarks>
    /// **Exposed for one wiring test, and the reason it needs an accessor is resolution.** The
    /// obvious way to observe this is <see cref="Report"/>, which prints the column — but it prints
    /// `sampling {value:0.#} ms`, and a stub source samples in microseconds, so a charged ledger and
    /// an uncharged one both format as `0`. That is the effect-size-below-resolution failure: an
    /// instrument insensitive to the manipulation it exists to detect.
    ///
    /// **The manipulation worth detecting is a dropped call**, not a wrong number. If
    /// <see cref="MomentPresenter"/> ever stops charging, the column reads zero for ever and looks
    /// like fast sampling — the B191 shape, where a clean-reading instrument was the defect.
    /// </remarks>
    public long SampledTicks => _sampling;

    /// <summary>Adds time spent posing models.</summary>
    /// <param name="ticks">Stopwatch ticks.</param>
    public void Posed(long ticks) => _posing += ticks;

    /// <summary>Adds time spent handing the frame to the device.</summary>
    /// <param name="ticks">Stopwatch ticks.</param>
    public void Drawing(long ticks) => _drawing += ticks;

    /// <summary>Records that the idle loop yielded once.</summary>
    public void Yielded() => _yields++;

    /// <summary>The line for this second, or null when a second has not passed.</summary>
    /// <param name="context">What only the window knows.</param>
    /// <param name="elapsedSeconds">How long since the last report.</param>
    /// <returns>The report, or null.</returns>
    /// <remarks>
    /// **The caller owns the clock**, so this needs no stopwatch of its own and is testable without
    /// waiting a second — which is the difference between a test that runs in microseconds and one
    /// that cannot exist.
    /// </remarks>
    public string? Report(FrameContext context, double elapsedSeconds)
    {
        if (elapsedSeconds < 1d)
        {
            return null;
        }

        // **One interpolated literal rather than concatenated pieces**, because `string.Create`
        // takes an interpolation HANDLER: a `+` between two interpolated strings produces an
        // ordinary `string` first, which does not bind to that overload. The conditionals therefore
        // live inside the holes.
        string playing = context.Playing ? ", playing" : ", paused";
        string flying = context.Flying ? ", flying" : string.Empty;

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{_frames / elapsedSeconds:0.#} frames a second, longest {_longestFrameSeconds * 1000d:0.##} ms{playing}{flying}; drawing {Ms(_drawing):0.#} ms; yielded {_yields} times to {context.YieldedTo}; sampling {Ms(_sampling):0.#} ms, posing {Ms(_posing):0.#} ms (lighting {Ms(context.LightingTicks):0.#} ms) of the second{context.Garbage}");

        _frames = 0;
        _longestFrameSeconds = 0d;
        _sampling = 0;
        _posing = 0;
        _drawing = 0;
        _yields = 0;

        return line;
    }

    /// <summary>Stopwatch ticks as milliseconds.</summary>
    private static double Ms(long ticks) => ticks / (double)Stopwatch.Frequency * 1000d;
}
