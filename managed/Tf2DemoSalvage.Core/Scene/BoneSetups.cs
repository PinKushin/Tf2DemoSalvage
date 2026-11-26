using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One view's record of which tracks had their bones set up, and when (B878).</summary>
/// <remarks>
/// **Owned by the view that draws, passed to <see cref="DemoTimeline.PropsAt"/> and told what it posed by
/// <see cref="DemoTimeline.NoteBonesSetUp"/>.** The order is the engine's: a
/// frame interpolates (the sample, which records each track's anchor as pending here) and then sets bones up (the pose,
/// which promotes it) — `InterpolateServerEntities` before the render (`cdll_client_int.cpp:2184-2189`), and
/// `MaintainSequenceTransitions` inside `SetupBones` storing the parity (`c_baseanimating.cpp:1835`).
/// </remarks>
public sealed class BoneSetups
{
    private readonly Dictionary<ScenePropTrack, BoneSetup> _seen = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<ScenePropTrack, int> _pending = new(ReferenceEqualityComparer.Instance);

    /// <summary>What this view last knew of a track's setup; <see cref="BoneSetup.Never"/> when nothing.</summary>
    /// <param name="track">The track.</param>
    /// <returns>Its setup.</returns>
    internal BoneSetup For(ScenePropTrack track) => _seen.TryGetValue(track, out BoneSetup seen) ? seen : BoneSetup.Never;

    /// <summary>Records the anchor a sample used, for the setup that follows it to keep.</summary>
    /// <param name="track">The track sampled.</param>
    /// <param name="anchoredAt">The anchor's arrival, or null.</param>
    internal void NotePending(ScenePropTrack track, int? anchoredAt)
    {
        if (anchoredAt is { } arrival)
        {
            _pending[track] = arrival;
        }
        else
        {
            _pending.Remove(track);
        }
    }

    /// <summary>Records a setup: the parity the track's newest update stated then, and the anchor the sample before it used.</summary>
    /// <param name="track">The track posed.</param>
    /// <param name="at">The tick.</param>
    internal void NoteSetUp(ScenePropTrack track, int at)
    {
        int? anchored = _pending.Remove(track, out int pending) ? pending : For(track).AnchoredAt;

        _seen[track] = new BoneSetup(at, track.SequenceParityAt(at), anchored);
    }

    /// <summary>Forgets every setup, as after a seek: nothing has been set up since (*our choice*, B878).</summary>
    public void Forget()
    {
        _seen.Clear();
        _pending.Clear();
    }
}
