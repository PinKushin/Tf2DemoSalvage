namespace Tf2DemoSalvage.Presentation;

/// <summary>One move of playback to a tick, and whether it was a skip by <see cref="SoundSchedule.IsSkip"/> (D221).</summary>
/// <param name="Tick">The tick moved to.</param>
/// <param name="Skipped">Whether the move was a seek rather than playing.</param>
/// <param name="Generation">Counts the moves, so each reader takes a move once.</param>
/// <remarks>
/// **Decided once, where a moment is shown, and read by everything that asks** — the seek replay, the particles and the
/// sound — so three cursors with three cadences cannot answer one frame three ways.
/// </remarks>
public sealed record FrameSkip(int Tick, bool Skipped, int Generation)
{
    /// <summary>The move's answer the first time a reader asks, marking it taken.</summary>
    /// <param name="taken">The reader's last generation taken.</param>
    /// <returns>Whether the move was a skip, once per move and reader; null when this reader has taken it already.</returns>
    public bool? Take(ref int taken)
    {
        bool fresh = taken != Generation;

        taken = Generation;

        return fresh ? Skipped : null;
    }
}
