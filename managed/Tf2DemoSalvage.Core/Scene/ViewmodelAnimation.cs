namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// When a viewmodel's animation restarts, which the sequence number cannot say on its own.
/// </summary>
/// <remarks>
/// **The engine cannot express "play that again" by changing a value that does not change**, so it
/// flips a counter instead. <c>CBaseViewModel::SendViewModelMatchingSequence</c>
/// (<c>baseviewmodel_shared.cpp:358</c>) sets the sequence and then bumps parity in the same breath:
///
/// <code>
///   SetSequence( sequence );
///   m_nAnimationParity = ( m_nAnimationParity + 1 ) &amp; ( (1 &lt;&lt; VIEWMODEL_ANIMATION_PARITY_BITS) - 1 );
///   ...
///   // Force frame interpolation to start at exactly frame zero
///   m_flAnimTime = gpGlobals->curtime;
/// </code>
///
/// and the client acts on the difference in <c>C_BaseViewModel::UpdateAnimationParity</c>
/// (<c>c_baseviewmodel.cpp:460</c>), called from <c>OnDataChanged</c> at <c>:170</c>:
///
/// <code>
///   // Purpose: If the animation parity of the weapon has changed, we reset cycle to avoid popping
///   if ( m_nOldAnimationParity != m_nAnimationParity &amp;&amp; !GetPredictable() )
///   {
///       SetCycle( 0.0f );
///       m_flAnimTime = curtime;
///   }
/// </code>
///
/// **<c>!GetPredictable()</c> is why this matters here specifically.** A spectated viewmodel during
/// demo playback is never predictable, so that branch always runs — the parity path is the ONLY
/// thing telling a demo viewer that an animation restarted.
///
/// **What its absence looks like**: firing the same weapon twice sets the same sequence number
/// twice, so nothing downstream sees a change and the animation never replays. The cycle here runs
/// off absolute demo time, so a new sequence also begins at whatever phase that clock happens to be
/// at rather than at frame zero.
/// </remarks>
public static class ViewmodelAnimation
{
    /// <summary>Three bits, <c>VIEWMODEL_ANIMATION_PARITY_BITS</c>, so it wraps at eight.</summary>
    /// <remarks>
    /// **A counter that wraps means equality is the only safe test.** It is sent as three bits
    /// (<c>SendPropInt( SENDINFO( m_nAnimationParity ), 3, SPROP_UNSIGNED )</c>), so it returns to a
    /// value it held before; comparing with greater-than would call a wrap a rewind.
    /// </remarks>
    public const int ParityBits = 3;

    /// <summary>The tick a viewmodel's current animation started on.</summary>
    /// <param name="previousParity">Parity when this viewmodel was last sampled, or null if never.</param>
    /// <param name="parity">Parity now.</param>
    /// <param name="previousStart">The tick the previous animation started on.</param>
    /// <param name="tick">Now.</param>
    /// <returns>The tick to measure this animation's cycle from.</returns>
    public static int RestartAt(int? previousParity, int parity, int previousStart, int tick) =>
        previousParity is { } was && was == parity ? previousStart : tick;
}
