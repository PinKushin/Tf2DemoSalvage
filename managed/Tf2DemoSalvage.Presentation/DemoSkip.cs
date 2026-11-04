using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One frame a skip renders: the last tick its batch of packets read, and the sounds the client made in it.</summary>
/// <param name="Through">The batch's last tick.</param>
/// <param name="Live">The HUD and animation-event sounds the frame made, in the order they were made.</param>
public readonly record struct SkipFrame(int Through, IReadOnlyList<SceneSound> Live);

/// <summary>What the client made during one skip, frame by frame (B504).</summary>
/// <param name="From">Where playback was, or null when it was nowhere.</param>
/// <param name="To">The tick skipped to.</param>
/// <param name="Frames">The frames, in order.</param>
public sealed record SoundSkip(int? From, int To, IReadOnlyList<SkipFrame> Frames)
{
    /// <summary>What it read before each reload a later `demo_gototick` made it take, in order; each dealt before this.</summary>
    public IReadOnlyList<SoundSkip> Reloaded { get; init; } = [];
}

/// <summary>The frames `demo_gototick` renders on its way to a tick (B504).</summary>
public static class DemoSkip
{
    /// <summary>The packets one read loop takes while skipping.</summary>
    public const int PacketsPerFrame = 99;

    /// <summary>The most demo seconds a frame can carry: `Host_FilterTime` (engine.dll FUN_18019ffe0) caps a demo frame at 0.1 s.</summary>
    private const double LongestFrameSeconds = 0.1;

    /// <summary>How many read loops a frame runs: one per tick the frame built up, and one with none.</summary>
    /// <param name="frameSeconds">The frame's length.</param>
    /// <param name="intervalPerTick">Seconds per tick; zero for a demo that does not say.</param>
    /// <returns>At least one.</returns>
    /// <remarks>
    /// `_Host_RunFrame` (FUN_1801a4570) divides its accumulated time by the interval and runs `_Host_RunFrame_Client` that many
    /// times; with no tick it still runs it once, when the demo player says it is skipping.
    /// </remarks>
    public static int BatchesThisFrame(double frameSeconds, float intervalPerTick) =>
        intervalPerTick > 0f
            ? Math.Max(1, (int)(Math.Min(frameSeconds, LongestFrameSeconds) / intervalPerTick))
            : 1;

    /// <summary>The last tick of each batch a skip reads and renders before the frame that reaches its target.</summary>
    /// <param name="first">The first tick the skip reads.</param>
    /// <param name="to">The tick it skips to.</param>
    /// <returns>Each frame's last tick, ascending, every one before <paramref name="to"/>.</returns>
    /// <remarks>
    /// `CDemoPlayer::ReadPacket` (engine.dll FUN_180072ee0) counts its calls while skipping and returns null at the
    /// 100th, ending the read loop; `_Host_RunFrame` (FUN_1801a4570) then renders. The batch that reaches the target is
    /// the frame the skip lands on — the viewer's own landing frame, whose sounds play — so it is not listed.
    /// *Interpolated, twice:* one packet per tick, and one read loop per frame — the engine runs one per tick the host
    /// frame built up, so a frame slower than a tick reads 198 or more.
    /// </remarks>
    public static IReadOnlyList<int> Frames(int first, int to)
    {
        List<int> frames = [];

        for (int through = first + PacketsPerFrame - 1; through < to; through += PacketsPerFrame)
        {
            frames.Add(through);
        }

        return frames;
    }
}
