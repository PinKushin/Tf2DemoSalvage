using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="ViewmodelAnimation.RestartAt"/> against <c>C_BaseViewModel::UpdateAnimationParity</c>.
/// </summary>
/// <remarks>
/// **Every case is read off the engine, not off our output**, which is the point of writing this
/// before the implementation exists. The rule is two lines of <c>c_baseviewmodel.cpp:460</c>:
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
/// with the counter bumped by <c>SendViewModelMatchingSequence</c>
/// (<c>baseviewmodel_shared.cpp:363</c>) whenever the server hands the viewmodel an animation —
/// including the same one it is already playing, which is the case the sequence number cannot
/// express.
/// </remarks>
public sealed class ViewmodelAnimationParityConformanceTests
{
    [Test]
    public void RestartAt_WhenParityIsUnchanged_KeepsTheExistingStart()
    {
        // `m_nOldAnimationParity != m_nAnimationParity` is false, so the engine does nothing and the
        // animation carries on from where it was.
        ViewmodelAnimation.RestartAt(previousParity: 4, parity: 4, previousStart: 120, tick: 300)
            .ShouldBe(120);
    }

    [Test]
    public void RestartAt_WhenParityChanges_StartsAtThisTick()
    {
        // `SetCycle( 0.0f ); m_flAnimTime = curtime;` — the cycle is measured from now.
        ViewmodelAnimation.RestartAt(previousParity: 4, parity: 5, previousStart: 120, tick: 300)
            .ShouldBe(300);
    }

    [Test]
    public void RestartAt_WhenParityWrapsPastSeven_StartsAtThisTick()
    {
        // **Three bits, so 7 is followed by 0 and that is a CHANGE, not a rewind.** The engine
        // compares with `!=` for exactly this reason; a greater-than test would miss every eighth
        // restart, which is the kind of defect that looks like an occasional dropped animation.
        ViewmodelAnimation.RestartAt(previousParity: 7, parity: 0, previousStart: 120, tick: 300)
            .ShouldBe(300);
    }

    [Test]
    public void RestartAt_WhenTheViewmodelIsFirstSeen_StartsAtThisTick()
    {
        // No previous parity at all: the animation has to start somewhere, and the alternative is
        // measuring its cycle from tick zero — which is what the free-running clock did and why a
        // fresh weapon appeared mid-animation.
        ViewmodelAnimation.RestartAt(previousParity: null, parity: 0, previousStart: 0, tick: 300)
            .ShouldBe(300);
    }

    [Test]
    public void RestartAt_WhenParityRepeatsAfterAWrap_KeepsTheExistingStart()
    {
        // **The control for the wrap case above.** If restarting were driven by anything other than
        // inequality — a "parity looks lower" heuristic, say — this case and that one would be
        // indistinguishable. Same value means the server sent no new animation.
        ViewmodelAnimation.RestartAt(previousParity: 0, parity: 0, previousStart: 120, tick: 300)
            .ShouldBe(120);
    }
}
