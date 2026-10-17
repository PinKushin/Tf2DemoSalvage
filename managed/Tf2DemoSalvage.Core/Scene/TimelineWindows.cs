using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A demo walked into checkpoints, from which a window of timeline is built for any span — for callers outside Core.</summary>
/// <remarks>
/// **The public face of <c>DemoTimeline.Checkpoints</c>** (D217 step 4), narrow on purpose: the walk, the checkpoint ticks,
/// a window and what the checkpoints keep. The instruments beside it stay internal.
/// </remarks>
public sealed class TimelineWindows
{
    private readonly DemoTimeline.Checkpoints _checkpoints;

    private readonly int[] _ticks;

    private TimelineWindows(DemoTimeline.Checkpoints checkpoints)
    {
        _checkpoints = checkpoints;
        _ticks = [.. checkpoints.Ticks];
    }

    /// <summary>The spacing between checkpoints, in seconds of demo time — the owner's, 2026-10-17 (D217 step 4).</summary>
    /// <remarks>
    /// **Two seconds**, the owner: *"go with 2 s"*, once part 3 had made the checkpoints small enough; the seek cost and
    /// memory it was decided on are in DECISIONS D217, "Step 4 — checkpoint spacing is 2 s". Its one home.
    /// </remarks>
    public const double SpacingSeconds = 2;

    /// <summary>The ticks the checkpoints were taken at, in order.</summary>
    public IReadOnlyList<int> Ticks => _ticks;

    /// <summary>How many checkpoints.</summary>
    public int Count => _ticks.Length;

    /// <summary>Walks a demo into checkpoints.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="everyTicks">The spacing between checkpoints.</param>
    /// <param name="settings">The viewer's window settings (D220).</param>
    /// <returns>The walk, or null for a demo with no data tables.</returns>
    public static TimelineWindows? Walk(ReadOnlyMemory<byte> file, int everyTicks, WindowSettings settings) =>
        DemoTimeline.Checkpoints.Walk(file, everyTicks, settings) is { } walked ? new TimelineWindows(walked) : null;

    /// <summary>The timeline from the checkpoint at or before <paramref name="tick"/> through <paramref name="through"/>.</summary>
    /// <param name="tick">The first tick the window must answer.</param>
    /// <param name="through">The last tick it must answer.</param>
    /// <param name="cancel">Stops the decode between two commands.</param>
    /// <returns>A timeline answering that span as the whole one does. Safe from several threads at once.</returns>
    /// <exception cref="OperationCanceledException">Cancelled before it was finished.</exception>
    public DemoTimeline Window(int tick, int through, CancellationToken cancel = default) =>
        _checkpoints.Window(tick, through, cancel, out _);

    /// <summary>The index of the checkpoint at or before a tick, or 0 before the first.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The index.</returns>
    public int IndexAt(double tick) => Math.Max(0, Array.FindLastIndex(_ticks, start => start <= tick));

    /// <summary>What every checkpoint together keeps, in estimated bytes — the instrument's own sum (B243), not a GC delta.</summary>
    /// <returns>The bytes, every object counted once.</returns>
    /// <remarks>A walk of every checkpoint's graph: seconds on a real match, so off the UI thread.</remarks>
    public long KeptBytes() => _checkpoints.KeptByOwner().Sum(owner => owner.Bytes);

    /// <summary>Whether no checkpoint was written by two windows built from it — the digest control, for a test.</summary>
    /// <param name="index">Which checkpoint.</param>
    /// <returns>True when the windows left it unchanged.</returns>
    internal bool WindowsLeaveCheckpointUnchanged(int index) => !_checkpoints.WindowsWriteTheirCheckpoint(index);
}
