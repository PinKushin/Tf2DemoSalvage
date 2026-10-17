namespace Tf2DemoSalvage.Presentation;

/// <summary>The last move of playback to a tick, and how many of the moves so far were skips (D221).</summary>
/// <param name="Tick">The tick last moved to.</param>
/// <param name="Generation">Counts the moves.</param>
/// <param name="Skips">Counts the moves that were skips — a transport jump, or the first move.</param>
/// <remarks>
/// **Decided where a moment is shown, and read by everything that asks** — the seek replay, the particles and the
/// sound — so three cursors with three cadences cannot answer one frame three ways. **A reader asks whether a skip
/// happened since it last asked, not whether the LAST move was one**: a frame can make two moves before a reader runs —
/// the opening state's jump, then the frame's played step — and the reader must still see the jump.
/// </remarks>
public sealed record FrameSkip(int Tick, int Generation, int Skips)
{
    /// <summary>Whether the last move was a skip.</summary>
    public bool Skipped { get; init; }

    /// <summary>Whether a skip happened since this reader last asked, once per move; null when nothing has moved since.</summary>
    /// <param name="taken">The reader's last generation and skip count seen.</param>
    /// <returns>True when a skip happened since; false when only played moves did; null when nothing moved.</returns>
    public bool? Take(ref (int Generation, int Skips) taken)
    {
        bool fresh = taken.Generation != Generation;
        bool skipped = taken.Skips != Skips;

        taken = (Generation, Skips);

        return fresh ? skipped : null;
    }
}
