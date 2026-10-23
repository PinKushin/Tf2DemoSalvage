using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>The two <see cref="ViewHalf"/>s: the one the game thread culls into, and the one the draw reads (B581 rung 2).</summary>
/// <remarks>
/// **The half is chosen at the hand-off, not when the draw runs.** `SetCamera` flips <see cref="Cull"/> at its entry, so
/// once the wait moves past the camera (finding 98 §9 step 5) the next frame's cull is in the other half while this
/// frame draws; a draw that read <see cref="Cull"/> would read the next frame's view.
/// </remarks>
/// <param name="replay">Plays a half's recorded context uploads, at the start of its draw (step 2).</param>
internal sealed class ViewHalves(Action<ViewHalf> replay)
{
    // An empty half until the first draw begins: every draw begins its own half first, so nothing draws from this one.
    private static ViewHalf Initial => new();

    private readonly ViewHalf[] _halves = [new(), new()];
    private int _cull;
    private (float X, float Y, float Z, float Distance, float Fade, (float, float, float) Forward)? _detailOnDevice;

    /// <summary>Both halves, for invalidation: either could cull next.</summary>
    public IReadOnlyList<ViewHalf> Both => _halves;

    /// <summary>The half the game thread's current frame culls into and its readers read.</summary>
    public ViewHalf Cull => _halves[_cull];

    /// <summary>The half the draw running now was handed.</summary>
    public ViewHalf Drawn { get; private set; } = Initial;

    /// <summary>Starts a new frame's cull in the other half.</summary>
    public void Flip() => _cull ^= 1;

    /// <summary>Starts drawing <paramref name="half"/>: its uploads replayed, then it is what the draw reads.</summary>
    /// <param name="half">The half handed at the hand-off.</param>
    public void Begin(ViewHalf half)
    {
        Drawn = half;
        replay(half);
    }

    /// <summary>The draw job for <paramref name="half"/>, which begins it when it runs.</summary>
    /// <param name="half">The half handed now, on the game thread.</param>
    /// <param name="draw">The frame.</param>
    /// <returns>The job.</returns>
    public Action Job(ViewHalf half, Action draw) => () =>
    {
        Begin(half);
        draw();
    };

    /// <summary>Whether the device's one detail buffer needs <paramref name="half"/>'s quads, recording that it will have them.</summary>
    /// <param name="half">The half about to draw.</param>
    /// <returns>True when the buffer holds quads built for another key, whichever half built them.</returns>
    /// <remarks>**Keyed on what the quads were built for, not on which half built them**: the build is a pure function of
    /// that key and the map's props (which <see cref="ForgetDetailUpload"/> covers), so a still camera, whose two halves
    /// build the same key, uploads once, and a revisit (A, B, A) uploads again.</remarks>
    public bool TakeDetailUpload(ViewHalf half)
    {
        if (half.DetailBuiltFor is not { } key || _detailOnDevice == key)
        {
            return false;
        }

        _detailOnDevice = key;

        return true;
    }

    /// <summary>Forgets what the detail buffer holds, for a new map's props.</summary>
    public void ForgetDetailUpload() => _detailOnDevice = default;
}
