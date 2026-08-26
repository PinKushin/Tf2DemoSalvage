using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The leaf outline for `mat_leafvis`, and the one warning it may write per map.</summary>
/// <remarks>
/// **The latch was `MainForm._reportedNoLeafBox`** (B188, D90) — a bool in a window whose only job
/// was to stop a per-frame warning becoming a per-frame warning. It belongs with the reporter, which
/// is the pattern `MomentScene` already uses for "no player appearance".
///
/// **Why it must be once per map and not once per frame.** `LeafBoxLines` runs on every frame the
/// overlay is on, and a warning written from there unguarded is B191 exactly: one log line per frame
/// taking a machine-wide lock and a disk flush, which cost 120 ms of a 133 ms frame the last time.
///
/// **And why it must say WHICH silence it is** (D83). An overlay switched on that draws nothing is
/// indistinguishable by eye from standing in a leaf whose box is off screen, and "no leaf box" is
/// true of all three causes and useful for none. Naming the measurement is the difference between a
/// diagnostic and a shrug — <c>docs/memory/a-log-must-name-what-it-measured.md</c>.
/// </remarks>
public sealed class LeafBoxes(ILogger render)
{
    private readonly ILogger _render = render ?? throw new ArgumentNullException(nameof(render));

    private bool _reported;

    /// <summary>Forgets the warning, so the next map gets its own answer.</summary>
    /// <remarks>Called from the level shutdown, beside everything else a map leaves behind.</remarks>
    public void Forget() => _reported = false;

    /// <summary>The edges of the leaf the camera stands in, warning once when there are none.</summary>
    /// <param name="map">The level, or null when none is open.</param>
    /// <param name="eye">Where the camera is, in world units.</param>
    /// <returns>The outline's edges, empty when there is nothing to draw.</returns>
    public IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> Lines(
        LoadedMap? map,
        (float X, float Y, float Z) eye)
    {
        BspLeafTree? leaves = map?.Level.Leaves;

        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> lines =
            LeafVis.Lines(leaves, eye);

        if (lines.Count > 0 || _reported)
        {
            return lines;
        }

        _reported = true;

        _render.LogWarning("{Message}", LeafVis.WhyNothing(map is not null, leaves));

        return lines;
    }
}
