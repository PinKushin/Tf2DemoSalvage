using System;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entity handle is an index AND a serial, and the serial is what makes it safe.
/// </summary>
/// <remarks>
/// **<c>RecvProxy_IntToEHandle</c>, <c>client/recvproxy.cpp:80</c>** — the engine's own decode:
///
/// <code>
///   if ( pData-&gt;m_Value.m_Int == INVALID_NETWORKED_EHANDLE_VALUE )
///       *pEHandle = INVALID_EHANDLE;
///   else
///   {
///       int iEntity    = pData-&gt;m_Value.m_Int &amp; ((1 &lt;&lt; MAX_EDICT_BITS) - 1);
///       int iSerialNum = pData-&gt;m_Value.m_Int &gt;&gt; MAX_EDICT_BITS;
///       pEHandle-&gt;Init( iEntity, iSerialNum );
///   }
/// </code>
///
/// **Both halves are kept, and dereferencing checks the serial against the slot's current
/// occupant.** That is the entire purpose of the serial: entity slots are reused, so a handle taken
/// before a slot changed hands must resolve to NOTHING rather than to whoever moved in.
///
/// **This project masked the serial away and returned the index**, so a stale handle resolved to a
/// real, existing, different entity — which `EntityState.Slot`'s own remarks had already named as
/// the hazard, for the neighbouring case of masking before testing the invalid sentinel.
///
/// Measured on `cp_fulgur` (B231):
///
/// <code>
///   resupply_locker composed onto 434: parent (2246 2384 59) + local (3440 -2096 240)
///                                    = (5686 288 299)
/// </code>
///
/// Entity 434 is reported at `(6098 -2816 443)` one line earlier in the same run. The locker was
/// hung off a slot that had changed hands, and its own origin — a WORLD position, because it has no
/// live parent — was then added to a stranger's transform. The result is a spawn cabinet several
/// thousand units from where it belongs.
/// </remarks>
public sealed class HandleSerialConformanceTests
{
    /// <summary><c>MAX_EDICT_BITS</c>, the low bits naming the slot.</summary>
    private const int EdictBits = 11;

    [Test]
    public void Resolve_AHandleWhoseSerialMatches_FindsTheEntity()
    {
        // The ordinary case: the handle was taken while this occupant was in the slot.
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            77, ClassId: 0, SerialNumber: 5, EntityUpdateType.Enter, []));

        table.Resolve(Handle(slot: 77, serial: 5)).ShouldBe(77);
    }

    [Test]
    public void Resolve_AHandleWhoseSerialDoesNotMatch_FindsNothing()
    {
        // **The case that matters, and the one masking cannot express.** The slot is occupied — by
        // somebody else. Returning the index here is what put a spawn locker on a door's transform;
        // the engine's handle would dereference to NULL and the entity would simply have no parent.
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            77, ClassId: 0, SerialNumber: 9, EntityUpdateType.Enter, []));

        table.Resolve(Handle(slot: 77, serial: 5)).ShouldBeNull(
            "the slot changed hands, so the handle is dangling and resolves to nothing");
    }

    [Test]
    public void Resolve_AHandleForAnEmptySlot_FindsNothing()
    {
        // Nothing has ever occupied it, so there is no serial to compare and nothing to return.
        EntityStateTable table = new(EntityBaselines.None);

        table.Resolve(Handle(slot: 77, serial: 5)).ShouldBeNull();
    }

    [Test]
    public void Resolve_TheInvalidHandle_FindsNothingAndIsNotMasked()
    {
        // `if ( m_Int == INVALID_NETWORKED_EHANDLE_VALUE ) *pEHandle = INVALID_EHANDLE;` — tested
        // BEFORE the mask, which is the trap `EntityState.Slot` documents: the sentinel is 21 bits
        // of ones and its low 11 mask to 2047, an ordinary-looking slot number.
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            2047, ClassId: 0, SerialNumber: 1023, EntityUpdateType.Enter, []));

        table.Resolve((1 << 21) - 1).ShouldBeNull(
            "the invalid sentinel must be recognised before its low bits are read as a slot");
    }

    [Test]
    public void Resolve_NoHandleAtAll_FindsNothing()
    {
        // An entity that never sent the property is not parented to entity zero.
        new EntityStateTable(EntityBaselines.None).Resolve(null).ShouldBeNull();
    }

    /// <summary>A handle as the wire carries it: serial above, slot in the low bits.</summary>
    private static int Handle(int slot, int serial) => (serial << EdictBits) | slot;
}
