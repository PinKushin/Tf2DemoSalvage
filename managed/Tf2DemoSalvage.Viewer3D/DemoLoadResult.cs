namespace Tf2DemoSalvage.Viewer3D;

/// <summary>How an attempt to open a demo ended.</summary>
/// <remarks>
/// **Three outcomes rather than a bool, because "superseded" is neither of the other two.** A demo
/// abandoned because the user picked a different one did not fail — nothing is wrong and there is
/// nothing to report to them — but it did not load either, and a caller waiting to act on the new
/// demo needs to know which it got.
/// </remarks>
internal enum DemoLoadOutcome
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
internal readonly record struct DemoLoadResult(DemoLoadOutcome Outcome, string Message)
{
    /// <summary>Whether the demo is now on screen.</summary>
    public bool Loaded => Outcome == DemoLoadOutcome.Loaded;
}
