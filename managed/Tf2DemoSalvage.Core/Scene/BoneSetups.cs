using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One view's record of which tracks had their bones set up, and when (B878).</summary>
/// <remarks>
/// **Owned by the view that draws, passed to <see cref="DemoTimeline.PropsAt"/> and told what it posed by
/// <see cref="DemoTimeline.NoteBonesSetUp"/>** after the frame's sample — the engine's order: a frame interpolates, then sets
/// bones up, and each setup stores the parity (`MaintainSequenceTransitions`, `c_baseanimating.cpp:1835`).
///
/// **Kept across seeks.** A setup recorded at a tick after the one played names no update after it, so it cannot reset
/// anything until playback passes it again (*our choice*: the engine's demo seek is not read, and a rewind plays backwards
/// frame by frame, where forgetting every setup each frame would leave nothing interpolating).
/// </remarks>
public sealed class BoneSetups
{
    private readonly Dictionary<ScenePropTrack, BoneSetup> _seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>What this view last knew of a track's setup; <see cref="BoneSetup.Never"/> when nothing.</summary>
    /// <param name="track">The track.</param>
    /// <returns>Its setup.</returns>
    internal BoneSetup For(ScenePropTrack track) => _seen.TryGetValue(track, out BoneSetup seen) ? seen : BoneSetup.Never;

    /// <summary>
    /// Records a setup: the parity the track's newest update stated then, and the newest reset the setup before it did
    /// not see — which the history keeps until a real one supersedes it.
    /// </summary>
    /// <param name="track">The track posed.</param>
    /// <param name="at">The tick.</param>
    internal void NoteSetUp(ScenePropTrack track, int at)
    {
        BoneSetup was = For(track);

        _seen[track] = new BoneSetup(at, track.SequenceParityAt(at), track.UnseenResetAt(at, was));
    }
}
