using System;
using System.IO;

namespace Tf2DemoSalvage.Presentation;

/// <summary>How an attempt to open a demo ended.</summary>
/// <remarks>
/// **Three outcomes rather than a bool, because "superseded" is neither of the other two.** A demo
/// abandoned because the user picked a different one did not fail — nothing is wrong and there is
/// nothing to report to them — but it did not load either, and a caller waiting to act on the new
/// demo needs to know which it got.
///
/// **Moved out of `Viewer3D` on 2026-08-26** (B208), which `docs/HANDOFF.md` had listed as a
/// leftover for as long as the plan existed. Whether a load succeeded, was abandoned, or failed is
/// not a fact about a window — and `Superseded` in particular is a *policy* about racing loads, not
/// a WinForms concern.
/// </remarks>
public enum DemoLoadOutcome
{
    /// <summary>The demo is on screen.</summary>
    Loaded,

    /// <summary>A newer demo was asked for before this one finished, so this one was dropped.</summary>
    Superseded,

    /// <summary>The file could not be opened or was not a demo.</summary>
    Failed,
}

/// <summary>What happened when a demo was asked for.</summary>
/// <param name="Outcome">Whether it loaded.</param>
/// <param name="Message">What the status line was told, ready to show or log.</param>
/// <remarks>
/// **Returned rather than swallowed, and that is the owner's standing rule** — *"we dont async void,
/// we do pass back, at least just pass a sucess or fail message"*. An `async void` load has nowhere
/// to put a failure and nothing to await, so a caller cannot tell a slow demo from a broken one, and
/// a test cannot tell either.
///
/// **The message is the same text the status line gets**, deliberately: two wordings for one event
/// is how a log and a window come to disagree about what happened.
/// </remarks>
public readonly record struct DemoLoadResult(DemoLoadOutcome Outcome, string Message)
{
    /// <summary>Whether the demo is now on screen.</summary>
    public bool Loaded => Outcome == DemoLoadOutcome.Loaded;

    /// <summary>What to say while a demo is being decoded.</summary>
    /// <param name="path">The demo's full path.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// **A line, not a result**, because opening is not an outcome — the decode has not finished and
    /// there is nothing yet to report. Its first draft returned a `DemoLoadResult` with an invented
    /// `Loading` outcome, which would have added a state to the enum for the sake of one status
    /// line. `MapProvider.Fetching` is the shape to match.
    ///
    /// **The file name rather than the path.** A status bar is one line, and a real archive path —
    /// `D:\demos\season31\esea_match_13977649.dem` — pushes the interesting half off the end.
    /// </remarks>
    public static string Opening(string path) =>
        "Opening " + Path.GetFileName(path) + "...";

    /// <summary>What to say when a newer demo overtook this one.</summary>
    /// <param name="path">The demo being abandoned.</param>
    /// <returns>The line, with a `Superseded` outcome.</returns>
    /// <remarks>
    /// **Superseded is not failed**, which is the distinction the outcome exists for: it is the
    /// transport working as designed, and an error on screen for a demo the person deliberately
    /// moved on from would be wrong. Saying nothing would be wrong too — silence reads as a load
    /// that broke.
    /// </remarks>
    public static DemoLoadResult Superseded(string path) =>
        new(
            DemoLoadOutcome.Superseded,
            "discarding " + Path.GetFileName(path) + ": a newer demo was asked for");

    /// <summary>What to say when a demo could not be read.</summary>
    /// <param name="path">The demo.</param>
    /// <param name="failure">Why it could not be read.</param>
    /// <returns>The line, with a `Failed` outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    /// <remarks>
    /// **The reason is the useful half.** "Could not open X" is equally true of a missing file, a
    /// truncated one and a permissions error, and the person's next step differs for each — the same
    /// argument `LeafVis.WhyNothing` makes about naming which silence it is.
    /// </remarks>
    public static DemoLoadResult CouldNotOpen(string path, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new DemoLoadResult(
            DemoLoadOutcome.Failed,
            "Could not open " + Path.GetFileName(path) + ": " + failure.Message);
    }
}
