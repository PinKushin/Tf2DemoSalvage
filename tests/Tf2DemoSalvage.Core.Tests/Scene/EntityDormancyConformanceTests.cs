using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>IsDormant()</c> as the client keeps it: <c>LeavePVS</c> sets it, <c>EnterPVS</c> and a delete end it
/// (`cl_ents_parse.cpp`, <c>CL_CopyExistingEntity</c> / <c>CL_DeleteDLLEntity</c>) — what the rope's
/// <c>DrawModel</c> asks of its two ends (`c_rope.cpp:1462-1467`, B478).
/// </summary>
public sealed class EntityDormancyConformanceTests
{
    [Test]
    public void IsDormant_FromALeaveUntilTheNextEnter_IsTrue()
    {
        EntityDormancy dormancy = new();

        dormancy.Observe(7, EntityUpdateType.Enter, 10);
        dormancy.Observe(7, EntityUpdateType.Leave, 20);
        dormancy.Observe(7, EntityUpdateType.Enter, 30);

        dormancy.IsDormant(7, 19).ShouldBeFalse();
        dormancy.IsDormant(7, 20).ShouldBeTrue();
        dormancy.IsDormant(7, 29).ShouldBeTrue();
        dormancy.IsDormant(7, 30).ShouldBeFalse();
        dormancy.IsDormant(8, 25).ShouldBeFalse("another entity left nothing");
    }

    /// <remarks>**A delete ends dormancy** — the entity is gone, not dormant — and a leave never re-entered stays.</remarks>
    [Test]
    public void IsDormant_AfterADeleteOrAnUnendedLeave_IsAsTheClientHolds()
    {
        EntityDormancy dormancy = new();

        dormancy.Observe(3, EntityUpdateType.Leave, 5);
        dormancy.Observe(3, EntityUpdateType.Delete, 8);
        dormancy.Observe(4, EntityUpdateType.Leave, 5);
        dormancy.Observe(4, EntityUpdateType.Delta, 6);

        dormancy.IsDormant(3, 7).ShouldBeTrue();
        dormancy.IsDormant(3, 8).ShouldBeFalse();
        dormancy.IsDormant(4, 1000).ShouldBeTrue("a delta does not end dormancy");
    }

    /// <remarks>
    /// **An entity's end: its delete, or its slot entered under another serial** — `CL_DeleteDLLEntity` or the recreate
    /// `CL_ParsePacketEntities` makes of a slot whose serial changed. Re-entering the PVS under the same serial is the
    /// same entity, the control.
    /// </remarks>
    [Test]
    public void DeletedAfter_ADeleteOrANewSerial_EndsTheEntityButAReturnDoesNot()
    {
        EntityDormancy dormancy = new();

        dormancy.Observe(7, EntityUpdateType.Enter, 10, serial: 1);
        dormancy.Observe(7, EntityUpdateType.Leave, 20);
        dormancy.Observe(7, EntityUpdateType.Enter, 30, serial: 1);
        dormancy.Observe(7, EntityUpdateType.Enter, 50, serial: 2);
        dormancy.Observe(8, EntityUpdateType.Enter, 10, serial: 4);
        dormancy.Observe(8, EntityUpdateType.Delete, 40);

        dormancy.DeletedAfter(7, 10).ShouldBe(50, "the same serial's return is not an end");
        dormancy.DeletedAfter(7, 50).ShouldBeNull();
        dormancy.DeletedAfter(8, 10).ShouldBe(40);
    }

    /// <remarks>
    /// **The tick of a delete is not after it** — `DeletedAfter(e, t)` answers a delete LATER than t. A temp entity at the
    /// delete's own tick is fired after the snapshot that deleted its target — *interpolated*: `CL_FireEvents` runs at the
    /// frame's end, after the packet's entity updates; the engine's order is not in the 2013 SDK — so an effect dispatched at
    /// that tick names whatever holds the slot then, and the delete before it is no end of that one.
    /// </remarks>
    [Test]
    public void DeletedAfter_AtTheDeletesOwnTick_LooksPastIt()
    {
        EntityDormancy dormancy = new();

        dormancy.Observe(7, EntityUpdateType.Delete, 40);
        dormancy.Observe(7, EntityUpdateType.Delete, 90);

        dormancy.DeletedAfter(7, 39).ShouldBe(40);
        dormancy.DeletedAfter(7, 40).ShouldBe(90);
    }

    /// <remarks>A delete and a new-serial enter in one tick are one end, recorded once.</remarks>
    [Test]
    public void DeletedAfter_ADeleteAndANewSerialOnOneTick_IsOneEnd()
    {
        EntityDormancy dormancy = new();

        dormancy.Observe(7, EntityUpdateType.Enter, 10, serial: 1);
        dormancy.Observe(7, EntityUpdateType.Delete, 50);
        dormancy.Observe(7, EntityUpdateType.Enter, 50, serial: 2);
        dormancy.Observe(7, EntityUpdateType.Delete, 80);

        dormancy.DeletedAfter(7, 10).ShouldBe(50);
        dormancy.DeletedAfter(7, 50).ShouldBe(80, "had 50 been recorded twice, this would still be 80 — the count below is the test");
        dormancy.DeleteCount(7).ShouldBe(2);
    }
}
