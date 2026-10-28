namespace Tf2DemoSalvage.Scene;

/// <summary>Which of two halves a frame's pose writes, so the frame in flight keeps drawing from the other (B581 rung 3).</summary>
/// <remarks>
/// **The engine's two queued contexts** (finding 99 §2-3): `CMaterialSystem::EndFrame` flips its context index once per
/// hand-off and the game thread records the next frame into the context not being played; per-frame render data lives
/// in that context's own memory (`imaterialsystem.h:1623-1626`). Here the pose's outputs are kept twice over and this
/// says which copy a pose may write: **the one not handed with the frame in flight**.
///
/// **It moves only when a hand-off happened since the last pose**, not on every hand-off: a paused viewer hands one posed
/// list several times, and a hand-off parity would then land the next pose on it; a seek's replays pose several times
/// before the next hand-off, and a toggle per pose would land the second on the half in flight. The rule covers both.
/// Held by <c>PoseHalvesConformanceTests</c>.
/// </remarks>
internal sealed class PoseHalves
{
    private long? _handOffsAtLastPose;

    /// <summary>The half the last pose wrote, 0 or 1; 0 before any.</summary>
    public int Current { get; private set; }

    /// <summary>The half this pose writes.</summary>
    /// <param name="handOffs">Frames handed to the draw so far (<see cref="IViewerDevice.HandOffs"/>).</param>
    /// <returns>0 or 1.</returns>
    public int Next(long handOffs)
    {
        if (_handOffsAtLastPose is { } seen && seen != handOffs)
        {
            Current ^= 1;
        }

        _handOffsAtLastPose = handOffs;

        return Current;
    }
}
