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
public sealed record SoundSkip(int? From, int To, IReadOnlyList<SkipFrame> Frames);

/// <summary>The frames `demo_gototick` renders on its way to a tick (B504).</summary>
public static class DemoSkip
{
    /// <summary>The packets one read loop takes while skipping.</summary>
    public const int PacketsPerFrame = 99;

    /// <summary>The last tick of each batch a skip reads before it renders.</summary>
    /// <param name="first">The first tick the skip reads.</param>
    /// <param name="to">The tick it skips to.</param>
    /// <returns>Each frame's last tick, ascending, ending at <paramref name="to"/>.</returns>
    public static IReadOnlyList<int> Frames(int first, int to) => [];
}
