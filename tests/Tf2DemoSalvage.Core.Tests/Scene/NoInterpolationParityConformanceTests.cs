using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A discontinuity CUTS the animation instead of cross-fading it (B346).
/// </summary>
/// <remarks>
/// **`CheckForSequenceChange` clears the whole transition queue on either of TWO conditions**
/// (`sequence_Transitioner.cpp:41`):
///
/// <code>
///   if ((seqdesc.flags &amp; STUDIO_SNAP) || !bInterpolate )
///   {
///       // remove all entries
///       m_animationQueue.RemoveAll();
///   }
/// </code>
///
/// Only the first half was implemented. `bInterpolate` is `!IsNoInterpolationFrame()`
/// (`c_baseanimating.cpp:1832`), and that is
/// `m_ubOldInterpolationFrame != m_ubInterpolationFrame` (`c_baseentity.h:2166`) — a NETWORKED
/// parity counter, `SendPropInt(SENDINFO(m_ubInterpolationFrame), NOINTERP_PARITY_MAX_BITS,
/// SPROP_UNSIGNED)` (`baseentity.cpp:273`), bumped by
/// `IncrementInterpolationFrame` (`baseentity.cpp:8471`), whose comment at its declaration reads
/// "Call this to cause a discontinuity (teleport)" (`baseentity.h:878`).
///
/// **Four things bump it, and one of them is every teleporter in the game:**
/// `CBaseEntity::Teleport` when given a new position (`baseentity.cpp:4955`),
/// `CBaseAnimating::CopyAnimationDataFrom` (`baseanimating.cpp:3374`),
/// `CBaseCombatCharacter`'s death fade (`basecombatcharacter.cpp:304`), and TF2's own
/// `CTFPlayer::PlayerDeathThink` the moment a dead player becomes respawnable, immediately after
/// `StopAnimation()` (`tf_player.cpp:14005`).
///
/// **A transition only exists when the sequence changed, so the divergence bites where the two
/// coincide — which is exactly what a respawn is.** Without this the pre-death pose fades into the
/// spawn animation over the fade window instead of cutting.
///
/// **Measured on the wire rather than assumed reachable.** `DT_BaseEntity.m_ubInterpolationFrame`
/// is in a real protocol-24 demo's schema, and `tf2-2026-pub-pov-cheater` sends it 13,261 times
/// across four distinct values — 12,830 zero, then 102, 149 and 180 of 1, 2 and 3. It cycles.
/// </remarks>
public sealed class NoInterpolationParityConformanceTests
{
    /// <remarks>
    /// **A counter, not a flag, and that is why it cannot be derived.** The engine compares this
    /// frame's value with the last one it saw; the value itself means nothing. Two teleports in
    /// consecutive snapshots read 1 then 2, and only the change says the second happened.
    /// </remarks>
    [Test]
    public void NoInterpolationParity_APropertyTheDemoCarries_IsReadFromBaseEntity()
    {
        State(("DT_BaseEntity.m_ubInterpolationFrame", 3)).NoInterpolationParity().ShouldBe(3);
    }

    /// <remarks>
    /// **Zero is a real value, not an absence.** The parity spends most of a match at zero —
    /// 12,830 of 13,261 sends in the measured demo — so a reader answering null for it would treat
    /// the common case as "not sent" and never notice a cycle back round to zero.
    /// </remarks>
    [Test]
    public void NoInterpolationParity_AZeroValue_IsReadRatherThanTreatedAsAbsent()
    {
        State(("DT_BaseEntity.m_ubInterpolationFrame", 0)).NoInterpolationParity().ShouldBe(0);
    }

    /// <remarks>
    /// An entity that never sends the field — an era before it existed, or a class that excludes
    /// it — has no discontinuity to report, which is not the same as reporting zero.
    /// </remarks>
    [Test]
    public void NoInterpolationParity_APropertyTheDemoOmits_IsNull()
    {
        State(("DT_BaseEntity.m_nModelIndex", 4)).NoInterpolationParity().ShouldBeNull();
    }

    /// <summary>An entity carrying the given properties.</summary>
    private static EntityState State(params (string Name, int Value)[] properties)
    {
        EntityState state = new(1, 0, 0, "CTFPlayer");

        foreach ((string name, int value) in properties)
        {
            state.Set(name, PropertyValue.FromInt(value));
        }

        return state;
    }
}
