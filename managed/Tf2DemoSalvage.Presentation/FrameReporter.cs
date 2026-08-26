using System;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a window knows about a second of frames that nothing else does.</summary>
/// <param name="Playing">Whether the demo was running rather than paused.</param>
/// <param name="Flying">Whether the free camera was being flown.</param>
/// <param name="YieldedTo">The Windows message that ended the idle wait, by name.</param>
/// <remarks>
/// **Three values, and only the last is unavoidably the window's.** A message id named by the window
/// that received it is the one part of the frame line a second frontend could not produce — which is
/// the test for whether something belongs on this side of the seam. `Playing` and `Flying` come with
/// it because they are read off a transport control and a camera mode, both UI state.
/// </remarks>
public readonly record struct FrameView(bool Playing, bool Flying, string YieldedTo);

/// <summary>Counts frames and writes the per-second account of where they went.</summary>
/// <remarks>
/// **This was `MainForm.CountFrame`, `GarbageThisSecond` and the three fields they drove** (B188,
/// D90). The clock, the collection deltas and the counter resets were all in the window; the only
/// piece that had to be was the Windows message name, which is passed in now.
///
/// **It owns the ledger rather than sharing one with the window.** `Yielded` and `Drawing` are
/// reported from elsewhere in the frame, so they come through here — a second holder of the same
/// ledger would be a second thing to keep in step, which is how B196 happened.
///
/// **The clock is <see cref="IElapsedTime"/> so a second can pass without one passing.**
/// `FrameLedger` already took its elapsed seconds as an argument for exactly this reason; this type
/// owns the clock that produces them, and would otherwise be the one piece testable only by
/// sleeping — which this project bans outright.
/// </remarks>
public sealed class FrameReporter
{
    /// <summary>How often the account is written.</summary>
    /// <remarks>
    /// **Once a second, so a log covering a whole session stays readable.** Unguarded at 300 frames
    /// a second this is 300 lines, each taking a lock and a disk flush — which is B191 exactly, where
    /// one log line cost 126 ms of a 133 ms frame.
    /// </remarks>
    private const double ReportEvery = 1d;

    private readonly FrameLedger _ledger;
    private readonly EntityModelSet _models;
    private readonly IElapsedTime _clock;
    private readonly ILogger _render;
    private readonly GarbageCounter _garbage = new();

    /// <summary>Wires a reporter to the counters it reads and the log it writes.</summary>
    /// <param name="ledger">The per-second accumulators.</param>
    /// <param name="models">The model set, whose lighting cost is reset each report.</param>
    /// <param name="clock">The one-second clock.</param>
    /// <param name="render">Where the line goes.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public FrameReporter(FrameLedger ledger, EntityModelSet models, IElapsedTime clock, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(render);

        _ledger = ledger;
        _models = models;
        _clock = clock;
        _render = render;

        // **Started here, or nothing ever elapses.** `StopwatchTime` wraps a stopwatch that is not
        // running until told to, and `Seconds` reports zero while stopped — so a reporter that never
        // restarted would sit below the threshold for ever and write nothing at all. That is the
        // shape of the autoplay regression `FakeElapsedTime` was rewritten to catch.
        _clock.Restart();
    }

    /// <summary>Records a drawn frame, and writes the account when a second is up.</summary>
    /// <param name="frameSeconds">The frame's duration, UNCLAMPED.</param>
    /// <param name="view">What only the window knows.</param>
    /// <remarks>
    /// **Unclamped, and it is worth saying twice.** The reading used to pass through the free
    /// camera's stall clamp, so the worst frame could never be reported as worse than 100 ms — the
    /// ceiling. The owner's report was "everything freezes for a half a second to maybe a second"
    /// and the log for those exact seconds said `longest 100 ms`: not a measurement, the clamp
    /// showing through.
    /// </remarks>
    public void Drew(double frameSeconds, in FrameView view)
    {
        // **Every frame, not just the reporting one.** The rate is frames per second, so a reporter
        // that counted only when it printed would report 1 for ever — a plausible number, which is
        // the worst kind of wrong.
        _ledger.Drew(frameSeconds);

        double elapsed = _clock.Seconds;

        if (elapsed < ReportEvery)
        {
            return;
        }

        // **The worst frame, not just the average, because jitter is a spread and a mean hides it.**
        // Flying the camera used to re-project the whole map every frame (B98); the average barely
        // moved while the longest frame in each second grew enormously, which is exactly what
        // stutter is. A rate on its own could not have shown that, and did not.
        if (_ledger.Report(
                new FrameContext(
                    view.Playing,
                    view.Flying,
                    view.YieldedTo,
                    _models.LightingTicks,
                    _garbage.Since(GarbageReading.FromRuntime())),
                elapsed) is { } line)
        {
            _render.LogDebug("{Message}", line);
        }

        // **Reset on the REPORT, not on the frame.** Clearing every frame would throw away 299
        // frames of lighting cost out of every 300 and leave the column reading near zero for work
        // that is happening.
        _models.LightingTicks = 0;

        _clock.Restart();
    }

    /// <summary>Records that the idle loop yielded once.</summary>
    public void Yielded() => _ledger.Yielded();

    /// <summary>Adds time spent handing the frame to the device.</summary>
    /// <param name="ticks">Stopwatch ticks.</param>
    public void Drawing(long ticks) => _ledger.Drawing(ticks);
}
