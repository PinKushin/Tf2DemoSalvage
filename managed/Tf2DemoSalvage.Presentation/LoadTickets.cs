using System.Threading;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Which of several loads in flight is still the one wanted.</summary>
/// <remarks>
/// **A newer request wins.** Double-clicking two demos in a row starts two decodes, and the slower
/// one must not overwrite the faster: each takes a ticket and only the newest is shown. Without it,
/// opening a big demo and changing your mind leaves you looking at the big one — a 24-minute match
/// takes nearly five seconds to decode, so the window is wide.
///
/// **This was `MainForm._loadsRequested` and four bare comparisons against it** (B188, D90). It is
/// three lines of policy that no window is needed to state, and it was reachable only by racing two
/// real loads against a real demo.
///
/// **Both load paths share one counter, deliberately.** The synchronous `LoadDemo` — used by the
/// command line, the `--shot` capture and the tests — takes a ticket it never asks about, purely so
/// that starting it supersedes any async load already decoding. They both end by assigning the same
/// fields, so the one that is not wanted has to know.
/// </remarks>
public sealed class LoadTickets
{
    /// <summary>How many loads have been asked for.</summary>
    /// <remarks>
    /// **Starts at zero and the first ticket is one**, which is not an off-by-one to tidy away: zero
    /// is what an uninitialised <c>int</c> holds, so a scheme whose first ticket was zero would
    /// answer "still current" to a load nobody started.
    /// </remarks>
    private int _requested;

    /// <summary>Claims the next ticket, superseding everything before it.</summary>
    /// <returns>The new ticket.</returns>
    /// <remarks>
    /// **Interlocked because the callers are not all on the UI thread.** The synchronous path runs
    /// wherever it was called, and the async one starts on the UI thread but may be entered again
    /// before the first finishes.
    /// </remarks>
    public int Take() => Interlocked.Increment(ref _requested);

    /// <summary>Whether a ticket is still the newest one taken.</summary>
    /// <param name="ticket">The ticket a load is holding.</param>
    /// <returns>Whether that load is still wanted.</returns>
    /// <remarks>
    /// **Asking does not consume.** The async path asks up to three times for one ticket — after the
    /// decode, after the map read, and again from the failure handler — so a check that advanced
    /// anything would make a load abandon itself halfway through.
    /// </remarks>
    public bool IsCurrent(int ticket) => ticket == Volatile.Read(ref _requested);
}
