namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// One gesture layer's life: how far through it is at a given moment, and when it ends.
/// </summary>
/// <remarks>
/// **A gesture is a layer over the main sequence, and it has its own clock.** The main sequence
/// loops for as long as the player keeps doing the thing; a gesture — a reload, an attack, a
/// double-jump flourish — plays once and then either disappears or holds on its last frame. That
/// distinction is <c>CMultiPlayerAnimState::UpdateGestureLayer</c>
/// (<c>multiplayer_animstate.cpp:1275</c>), whose <c>CLIENT_DLL</c> branch is the demo-playback
/// path:
///
/// <code>
/// flCycle += GetSequenceCycleRate( hdr, seq ) * frametime * GetGesturePlaybackRate() * m_flPlaybackRate;
/// if ( flCycle > 1.0f )
/// {
///     RunGestureSlotAnimEventsToCompletion( pGesture );
///     if ( pGesture->m_bAutoKill ) { ResetGestureSlot( ... ); return; }   // gone
///     else { pGesture->m_pAnimLayer->m_flCycle = 1.0f; }                  // held
/// }
/// </code>
///
/// **Closed form is exact, not a shortcut.** Every factor of the rate is constant on the standard
/// player-gesture path: <c>AddToGestureSlot</c> sets <c>m_flPlaybackRate = 1.0</c>,
/// <c>CTFPlayerAnimState::GetGesturePlaybackRate</c> returns <c>1.0</c> barring an item attribute,
/// and <c>GetSequenceCycleRate</c> is <c>Studio_CPS = 1/duration</c> (<c>bone_setup.cpp:5532</c>)
/// for a fixed sequence. So <c>cycle += (1/duration)·dt</c> sums exactly to
/// <c>elapsed/duration</c>. Integrating it per frame would need the client's own
/// <c>frametime</c>s, which the demo does not record — the closed form is both faithful and the
/// only form a seeking viewer can evaluate.
///
/// This is why the layer weight and blend are absent here: on that same path
/// <c>m_flWeight = 1.0</c> and both <c>m_flBlendIn</c> and <c>m_flBlendOut</c> are zero, so the
/// gesture applies at full strength for its whole life. The per-bone weighting that shapes it is
/// the sequence's own <c>StudioGestureWeights</c> (in the Content assembly), not a layer fade.
/// </remarks>
/// <param name="DurationSeconds">
/// How long the gesture sequence runs, <c>Studio_Duration = (numframes-1)/fps</c>. Zero when the
/// sequence has one frame — the engine's <c>cps == 0</c> case, which never advances.
/// </param>
/// <param name="AutoKill">
/// Whether the gesture removes itself once finished. Most do; the ones that do not
/// (<c>RestartGesture( ..., false )</c> — a stun begin, a reload loop) hold their final frame until
/// the next gesture replaces them in the slot.
/// </param>
public readonly record struct GestureLayer(float DurationSeconds, bool AutoKill)
{
    /// <summary>How far through the gesture is at <paramref name="elapsedSeconds"/>.</summary>
    /// <param name="elapsedSeconds">Seconds since the gesture was triggered.</param>
    /// <returns>
    /// The cycle in <c>[0, 1]</c> while the gesture is on screen, or <see langword="null"/> once an
    /// auto-kill gesture has passed its end and no longer exists.
    /// </returns>
    /// <remarks>
    /// The order matches the engine exactly: advance, then test <c>&gt; 1.0f</c> strictly — at a
    /// cycle of precisely one the gesture has not passed, so it holds rather than dying a frame
    /// early. A zero duration leaves the cycle at zero forever, mirroring <c>cps == 0</c>.
    /// </remarks>
    public float? CycleAt(float elapsedSeconds)
    {
        if (DurationSeconds <= 0f)
        {
            // cps == 0: the rate is zero, so the cycle never leaves its initial value. The gesture
            // plays forever on its first frame rather than dividing by zero or vanishing.
            return 0f;
        }

        float cycle = elapsedSeconds / DurationSeconds;

        if (cycle > 1f)
        {
            return AutoKill ? null : 1f;
        }

        return cycle;
    }
}
