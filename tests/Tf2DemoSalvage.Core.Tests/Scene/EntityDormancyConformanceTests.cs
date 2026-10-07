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
}
