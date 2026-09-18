using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A core made for its object, and rechecked after an impact — <c>FUN_1800782d0</c> and <c>FUN_1800792b0</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly**: the constructor pushes its first object on <c>+0x68</c> and leaves <c>core+0x1</c> at 8
/// (headless `DecompAt` of <c>1800782d0</c>, 2026-09-15); the recheck is `docs/findings/51`'s quote. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpRigidBodyRecheckTests
{
    [Test]
    public void Constructor_ForAnObject_ListsItAndStartsAtUnitStateEight()
    {
        IvpCollisionObject body = new();

        IvpRigidBody core = new(body);

        core.Objects.ShouldBe([body]);
        core.UnitState.ShouldBe(8);
    }

    [Test]
    public void Recheck_APairAgainstAnUnstampedCore_IsMinimizedAndRescheduled()
    {
        (IvpRigidBody core, _, IvpMindist mindist) = Pair();
        List<IvpMindist> minimized = [];
        List<IvpMindist> rescheduled = [];

        core.Recheck(3, minimized.Add, rescheduled.Add);

        core.ImpactStamp.ShouldBe(3);
        minimized.ShouldBe([mindist]);
        rescheduled.ShouldBe([mindist]);
    }

    [Test]
    public void Recheck_APairWhoseOtherCoreWasStampedThisImpact_IsSkipped()
    {
        (IvpRigidBody core, IvpRigidBody other, _) = Pair();
        other.ImpactStamp = 3;
        List<IvpMindist> minimized = [];

        core.Recheck(3, minimized.Add, _ => { });

        minimized.ShouldBeEmpty();
    }

    [Test]
    public void Recheck_AFrozenPair_IsMinimizedButNotRescheduled()
    {
        (IvpRigidBody core, _, IvpMindist mindist) = Pair();
        mindist.Flags |= 0x4000;
        List<IvpMindist> minimized = [];
        List<IvpMindist> rescheduled = [];

        core.Recheck(3, minimized.Add, rescheduled.Add);

        minimized.ShouldBe([mindist]);
        rescheduled.ShouldBeEmpty();
    }

    private static (IvpRigidBody Core, IvpRigidBody Other, IvpMindist Mindist) Pair()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpRigidBody core = new(first);
        IvpRigidBody other = new(second);
        first.Core = core;
        second.Core = other;
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f);

        new IvpMindistManager().LinkExact(mindist, first, second);

        return (core, other, mindist);
    }
}
