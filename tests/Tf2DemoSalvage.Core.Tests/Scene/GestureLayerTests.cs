using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The gesture-layer lifecycle, against <c>CMultiPlayerAnimState::UpdateGestureLayer</c>.
/// </summary>
/// <remarks>
/// **A gesture is not the main sequence — it is a layer laid over it, and it dies on a schedule.**
/// <c>UpdateGestureLayer</c> (<c>multiplayer_animstate.cpp:1275</c>, the <c>CLIENT_DLL</c> branch —
/// which is the demo-playback path) advances the layer's own cycle each frame and, the moment that
/// cycle passes one, either removes the gesture (<c>m_bAutoKill</c>) or clamps it to the last frame:
///
/// <code>
/// flCycle += GetSequenceCycleRate( hdr, seq ) * frametime * GetGesturePlaybackRate() * m_flPlaybackRate;
/// ...
/// if ( flCycle > 1.0f )
/// {
///     RunGestureSlotAnimEventsToCompletion( pGesture );
///     if ( pGesture->m_bAutoKill ) { ResetGestureSlot( ... ); return; }
///     else { pGesture->m_pAnimLayer->m_flCycle = 1.0f; }
/// }
/// </code>
///
/// **Closed form is exact here, not an approximation.** Every factor in the rate is constant: the
/// standard player-gesture path (<c>AddToGestureSlot</c>) sets <c>m_flPlaybackRate = 1.0</c>,
/// <c>CTFPlayerAnimState::GetGesturePlaybackRate</c> is <c>1.0</c> barring an item attribute, and
/// <c>GetSequenceCycleRate</c> is <c>Studio_CPS = 1/duration</c> for a fixed sequence. So the
/// per-frame integration <c>cycle += (1/duration)·dt</c> sums to <c>elapsed/duration</c> — and the
/// closed form is what a seeking viewer wants anyway, since the client's exact per-frame
/// <c>frametime</c>s are not recorded in the demo and cannot be replayed.
///
/// Predictions below are arithmetic on that formula, computed here rather than read back from the
/// port.
/// </remarks>
public sealed class GestureLayerTests
{
    [Test]
    public void GestureLayer_Cycle_IsElapsedOverDuration()
    {
        // rate = 1/2 per second; at one second the layer is halfway through.
        GestureLayer layer = new(DurationSeconds: 2f, AutoKill: true);

        layer.CycleAt(1f)!.Value.ShouldBe(0.5f, 0.0001f);
    }

    [Test]
    public void GestureLayer_FreshlyTriggered_HasCycleZero()
    {
        GestureLayer layer = new(DurationSeconds: 2f, AutoKill: true);

        layer.CycleAt(0f)!.Value.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void GestureLayer_ExactlyAtDuration_IsStillActiveAtCycleOne()
    {
        // The engine's guard is `> 1.0f`, strictly — at cycle exactly one it has not passed, so the
        // gesture is neither killed nor clamped-early. It holds on its final frame.
        GestureLayer layer = new(DurationSeconds: 2f, AutoKill: true);

        layer.CycleAt(2f)!.Value.ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void GestureLayer_PastTheEndWithAutoKill_IsGone()
    {
        // **The discriminator.** Same time, same duration — only the auto-kill flag differs, and it
        // decides whether the layer still exists. A transposed branch would swap these two.
        GestureLayer layer = new(DurationSeconds: 2f, AutoKill: true);

        layer.CycleAt(3f).ShouldBeNull();
    }

    [Test]
    public void GestureLayer_PastTheEndWithoutAutoKill_HoldsOnItsLastFrame()
    {
        // `else { m_flCycle = 1.0f; }` — a gesture without auto-kill freezes at the end rather than
        // vanishing, waiting to be replaced by the next RestartGesture in the same slot.
        GestureLayer layer = new(DurationSeconds: 2f, AutoKill: false);

        layer.CycleAt(3f)!.Value.ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void GestureLayer_AZeroDuration_NeverAdvances()
    {
        // Studio_Duration returns 0 when cps is 0, so GetSequenceCycleRate is 0 and the cycle never
        // moves off its initial zero — the gesture plays forever on its first frame rather than
        // dividing by zero. Faithful to rate 0, not a special case invented here.
        GestureLayer layer = new(DurationSeconds: 0f, AutoKill: true);

        layer.CycleAt(5f)!.Value.ShouldBe(0f, 0.0001f);
    }
}
