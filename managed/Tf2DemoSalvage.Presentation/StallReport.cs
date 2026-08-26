using System;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a frame spent its time on, measured between the steps that make one.</summary>
/// <param name="Sound">Advancing the sound systems.</param>
/// <param name="Camera">Flying the free camera.</param>
/// <param name="Project">Reprojecting the world when the viewport changed.</param>
/// <param name="Advance">Rebuilding the scene for the current moment.</param>
/// <param name="Capture">Taking an automatic screenshot, when one was asked for.</param>
/// <param name="Hud">Building the overlay.</param>
/// <param name="Draw">Handing the frame to the device.</param>
/// <param name="Total">The whole frame, which is not the sum: see <see cref="Unaccounted"/>.</param>
/// <remarks>
/// **Eight timestamps became one record** (B188), which is the same correction
/// <see cref="MomentPhases"/> already carries. A caller passing eight `long`s in the right order is
/// a caller that can pass them in the wrong one, and the failure is silent: the report still prints
/// seven plausible numbers, against the wrong labels.
/// </remarks>
public readonly record struct FramePhases(
    long Sound,
    long Camera,
    long Project,
    long Advance,
    long Capture,
    long Hud,
    long Draw,
    long Total)
{
    /// <summary>Time inside the frame that no named phase claimed.</summary>
    /// <remarks>
    /// **Derived rather than passed, and `unaccounted` is Valve's own name for it** —
    /// `VPROF_BUDGETGROUP_OTHER_UNACCOUNTED` is `_T("Unaccounted")` in `public/tier0/vprof.h`.
    ///
    /// **It is the most useful number in the line.** B191 was found because every measured column
    /// read about 1 ms while the remainder held 126: each new timer moved the fat column to whatever
    /// was still being subtracted, and that pattern — not any single measurement — was the signal.
    /// </remarks>
    public long Unaccounted =>
        Total - Sound - Camera - Project - Advance - Capture - Hud - Draw;

    // **`Between` was here until 2026-08-26** (B203). It took eight cumulative timestamps and
    // subtracted adjacent pairs, so its PARAMETER NAMES were a second copy of the frame's order —
    // and reordering the stages without reordering that argument list would have relabelled every
    // column silently, reporting a fix as a regression somewhere else.
    //
    // `FrameSequence.Run` builds this record instead, naming each phase at the call that produces
    // it. There is now one statement of the order, and it is the executable one.
}

/// <summary>Names where a slow step went, when one is slow.</summary>
/// <remarks>
/// **These were <c>MainForm.ReportSlowFrame</c>, <c>ReportSlowMoment</c> and
/// <c>ReportSlowSounds</c>** (B188, D90) — arithmetic and formatting with no window in any of it,
/// and no test, because reaching them meant constructing a form.
///
/// **They earned their place: B191 was found by reading these lines.** A freeze the owner described
/// as "every handful of seconds" turned out to be one log line taking a machine-wide mutex, and it
/// was located by watching which column stayed fat as more of them were measured. A report whose own
/// arithmetic was wrong would have sent that hunt somewhere else entirely — which is a good argument
/// for the tests these now have.
///
/// **Every line is a `Warning`, and that is deliberate in a codebase that otherwise logs at
/// `Debug`.** Per-frame diagnostics were moved down to `Debug` when B191 showed the sink itself was
/// the cost; these are not per-frame. They fire only past the budget, which on a healthy run is
/// three times in the first eight seconds and never again.
/// </remarks>
public static class StallReport
{
    /// <summary>How long a whole step may take before it counts as a visible freeze, in seconds.</summary>
    /// <remarks>
    /// **Its own threshold, and NOT <see cref="MomentScene.StallSeconds"/>, even though the numbers
    /// agree.** A constant carries no scope, and both of the other two say so in their own words:
    /// `MomentScene`'s is "applied to one step of a scene rebuild" and `SoundCache`'s to "one decode
    /// blocking the thread that draws". This one is applied to a WHOLE frame, a WHOLE moment or a
    /// WHOLE sound step.
    ///
    /// **Reading that difference is what this constant fixes.** `ReportSlowMoment` used to compare a
    /// whole moment — the rebuild plus the sampling plus the marker pass — against `MomentScene`'s
    /// per-step number. Borrowing a symbol whose documentation says it means something else is how
    /// the two judgements get tied together, and then tuning either silently moves the other.
    ///
    /// **Thirty milliseconds is two frames at the rate this viewer actually runs**, which is where a
    /// person sees a hitch rather than a slightly late frame. Deliberately far below the half-second
    /// the owner reports, so the small stalls are caught too and can be asked whether they share a
    /// cause with the big ones — in B191 they did.
    /// </remarks>
    public const double StallSeconds = 0.03;

    /// <summary>Reports a frame that took too long, naming each phase.</summary>
    /// <param name="phases">What the frame measured.</param>
    /// <param name="log">Where the line goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    public static void Frame(in FramePhases phases, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (Ms(phases.Total) <= StallSeconds * 1000d)
        {
            return;
        }

        log.LogWarning(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"SLOW FRAME {Ms(phases.Total):0} ms: sound {Ms(phases.Sound):0.#}" +
                $", camera {Ms(phases.Camera):0.#}" +
                $", project {Ms(phases.Project):0.#}" +
                $", advance {Ms(phases.Advance):0.#}" +
                $", capture {Ms(phases.Capture):0.#}" +
                $", hud {Ms(phases.Hud):0.#}" +
                $", draw {Ms(phases.Draw):0.#}" +
                $"; unaccounted {Ms(phases.Unaccounted):0.#} ms"));
    }

    /// <summary>Reports a scene rebuild that took too long, naming each phase and sub-phase.</summary>
    /// <param name="phases">What <see cref="MomentScene.Build"/> measured.</param>
    /// <param name="sampleTicks">Reading the tick's players and props off the timeline.</param>
    /// <param name="playerTicks">Building the overhead marker list.</param>
    /// <param name="log">Where the line goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    /// <remarks>
    /// **The frame report says `advance`, and this says which part of it** — the two compose, so a
    /// slow frame names a phase and then a sub-phase rather than a range of 350 lines.
    ///
    /// **The threshold covers the whole moment**, which is the rebuild PLUS the two phases measured
    /// outside it. Thresholding the rebuild's own total would exclude the sampling and the marker
    /// pass from the judgement as well as from the line, so a moment made slow by either would not
    /// be reported at all.
    ///
    /// **The `rest` column is a residual and the others are measured.** Worth knowing when reading a
    /// line: every direct column small while `rest` is large means the cost is in something still
    /// unmeasured, not in bone work.
    /// </remarks>
    public static void Moment(in MomentPhases phases, long sampleTicks, long playerTicks, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        double total = Ms(phases.Total + sampleTicks + playerTicks);

        if (total <= StallSeconds * 1000d)
        {
            return;
        }

        EntityModelSet.PoseCounters pose = phases.Counters;

        double lighting = Ms(pose.Lighting);
        double viewmodel = Ms(phases.Viewmodel);
        double simulate = Ms(pose.Simulate);
        double wornLight = Ms(pose.WornLight);
        double reports = Ms(pose.Report);
        double setup = Ms(pose.Setup);
        double skin = Ms(pose.Skin);

        double rest =
            Ms(phases.Pose) - lighting - viewmodel - simulate - wornLight - reports - setup - skin;

        log.LogWarning(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"SLOW MOMENT {total:0} ms: sample {Ms(sampleTicks):0.#}" +
                $", drawlist {Ms(phases.DrawList):0.#}" +
                $", models {Ms(phases.Models):0.#}" +
                $", pose {Ms(phases.Pose):0.#}" +
                $" (lighting {lighting:0.#}, viewmodel {viewmodel:0.#}" +
                $", simulate {simulate:0.#}" +
                $", wornlight {wornLight:0.#}" +
                $", reports {reports:0.#}" +
                $" (sink {Ms(pose.ReportLog):0.#})" +
                $", setup {setup:0.#}" +
                $", skin {skin:0.#}" +
                $", rest {rest:0.#}" +
                $", built {pose.Built.ToString(CultureInfo.InvariantCulture)}" +
                $" of {phases.Drawn.ToString(CultureInfo.InvariantCulture)}" +
                $", anim {Ms(pose.Animation):0.#}" +
                $" over {pose.AnimationCalls.ToString(CultureInfo.InvariantCulture)})" +
                $", weapons {Ms(phases.Weapons):0.#}" +
                $", players {Ms(playerTicks):0.#}" +
                $"; unaccounted {Ms(phases.Unaccounted):0.#} ms"));
    }

    /// <summary>Reports a sound step that took too long, naming each phase.</summary>
    /// <param name="phases">What the sound step measured.</param>
    /// <param name="totalTicks">The whole step.</param>
    /// <param name="log">Where the line goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    /// <remarks>
    /// **A threshold instrument cannot see a sum, which is why this times the PHASE.** Six frames
    /// froze on sound decode while `SoundCache`'s per-decode stall log fired once, because three
    /// sub-30 ms decodes inside one frame never crossed that threshold individually. The two reports
    /// are not redundant: one asks whether a decode was slow, this asks whether the step was.
    /// </remarks>
    public static void Sounds(in SoundPhases phases, long totalTicks, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        double total = Ms(totalTicks);

        if (total <= StallSeconds * 1000d)
        {
            return;
        }

        log.LogWarning(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"SLOW SOUND {total:0} ms: advance {Ms(phases.Advance):0.#}" +
                $", reclaim {Ms(phases.Reclaim):0.#}" +
                $", loops {Ms(phases.Loops):0.#}" +
                $", soundscape {Ms(phases.Soundscape):0.#}" +
                $", starting {Ms(phases.Starting):0.#}"));
    }

    /// <summary>Stopwatch ticks as milliseconds.</summary>
    private static double Ms(long ticks) => ticks / (double)Stopwatch.Frequency * 1000d;
}
