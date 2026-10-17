using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A demo walked into checkpoints, from which a window of timeline is built for any span — for callers outside Core.</summary>
/// <remarks>
/// **The public face of <c>DemoTimeline.Checkpoints</c>** (D217 step 4): just the walk, the checkpoint ticks and a window,
/// so the viewer's cache depends on what it uses and nothing of the instruments beside it.
/// </remarks>
public sealed class TimelineWindows
{
    private readonly DemoTimeline.Checkpoints _checkpoints;

    private TimelineWindows(DemoTimeline.Checkpoints checkpoints)
    {
        _checkpoints = checkpoints;
        _ticks = [.. checkpoints.Ticks];
    }

    private readonly int[] _ticks;

    /// <summary>The decided spacing between checkpoints in seconds: <c>DemoTimeline.Checkpoints.SpacingSeconds</c>.</summary>
    public const double SpacingSeconds = DemoTimeline.Checkpoints.SpacingSeconds;

    /// <summary>The ticks the checkpoints were taken at, in order.</summary>
    public IReadOnlyList<int> Ticks => _ticks;

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
    /// <returns>A timeline answering that span as the whole one does. Safe from several threads at once.</returns>
    public DemoTimeline Window(int tick, int through) => _checkpoints.Window(tick, through);

    /// <summary>Whether no checkpoint was written by a window built from it — the digest control, for a test.</summary>
    /// <param name="index">Which checkpoint.</param>
    /// <returns>True when the windows left it unchanged.</returns>
    public bool WindowsLeaveCheckpointUnchanged(int index) => !_checkpoints.WindowsWriteTheirCheckpoint(index);

    /// <summary>How many checkpoints.</summary>
    public int Count => Ticks.Count;

    /// <summary>The index of the checkpoint at or before a tick, or 0 before the first.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The index.</returns>
    public int IndexAt(double tick) => Math.Max(0, Array.FindLastIndex(_ticks, start => start <= tick));
}
