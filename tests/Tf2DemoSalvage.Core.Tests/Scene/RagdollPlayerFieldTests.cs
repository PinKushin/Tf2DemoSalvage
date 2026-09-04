using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A corpse names its player under two different fields, in two different encodings (B319).
/// </summary>
/// <remarks>
/// **TF2 renamed the field, and the new one is not the same KIND of value.** Measured across the
/// corpus with the CLI's trace:
///
/// | demo | field | values |
/// |---|---|---|
/// | `serveme-627619-stv-2026-08-07` (2026) | <c>m_hPlayer</c> | 24587, 174093, 311301, … |
/// | `z1800` (2020 or later) | <c>m_iPlayerIndex</c> | 2, 3, 4, 5, … |
///
/// The first is a packed ehandle that must go through <c>EntityStateTable.Resolve</c>; the second is
/// a player entity index used as it stands. **`m_iPlayerIndex` is not in the published SDK at all**,
/// not even as a `RECVINFO_NAME` alias — only a demo carries it, which is the premise of this
/// project working as intended.
///
/// **What it cost while unhandled:** the corpse's orientation is the one thing `DT_TFRagdoll` does
/// not send, so it is reached through this field. Reading only the modern name left **0 of 407**
/// corpses in `z1800` with an orientation, all facing north, against 159 of 159 on the demo that
/// happens to use the new name.
/// </remarks>
public sealed class RagdollPlayerFieldTests
{
    [Test]
    public void RagdollPlayerHandle_ForACorpseSendingTheModernField_ReadsThePackedHandle()
    {
        Corpse(handle: 174093).RagdollPlayerHandle().ShouldBe(174093);
    }

    /// <remarks>
    /// **Null rather than zero, because zero is a legitimate entity index.** A reader returning a
    /// default would send every corpse of the other era to entity 0 — the world — which resolves,
    /// draws nothing, and looks like the feature working.
    /// </remarks>
    [Test]
    public void RagdollPlayerHandle_ForACorpseSendingOnlyTheOlderField_IsNull()
    {
        Corpse(index: 6).RagdollPlayerHandle().ShouldBeNull();
    }

    [Test]
    public void RagdollPlayerIndex_ForACorpseSendingTheOlderField_ReadsThePlayerIndex()
    {
        Corpse(index: 6).RagdollPlayerIndex().ShouldBe(6);
    }

    /// <remarks>
    /// **The control in the other direction.** A corpse from a modern demo must not answer under the
    /// retired name, or the two would be indistinguishable and a caller preferring the index would
    /// treat a 174,093 handle as an entity index — plausible, and never a player.
    /// </remarks>
    [Test]
    public void RagdollPlayerIndex_ForACorpseSendingOnlyTheModernField_IsNull()
    {
        Corpse(handle: 174093).RagdollPlayerIndex().ShouldBeNull();
    }

    /// <summary>A corpse sending one field or the other, as its era does.</summary>
    private static EntityState Corpse(int? handle = null, int? index = null)
    {
        EntityState state = new(40, 0, 0, "CTFRagdoll");

        if (handle is { } packed)
        {
            state.Set("DT_TFRagdoll.m_hPlayer", PropertyValue.FromInt(packed));
        }

        if (index is { } player)
        {
            state.Set("DT_TFRagdoll.m_iPlayerIndex", PropertyValue.FromInt(player));
        }

        return state;
    }
}
