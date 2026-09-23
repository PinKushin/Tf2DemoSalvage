using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The impact loop's tail — <c>FUN_1800909d0(block)</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *the tail, read whole*): a core only brought to the event is put back as it
/// was; a moved core is stepped over the rest of the PSI and its contacts' estimates cleared. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpImpactIslandTailTests
{
    [Test]
    public void Tail_ACoreBroughtToTheEventButNotMoved_IsPutBack()
    {
        (IvpImpactIsland island, IvpRigidBody core, _) = Island();
        core.PendingSnapshot = new IvpCoreSnapshot((1f, 2f, 3f), (0d, 0d, 0d, 1d), (0d, 0d, 0d, 1d));
        core.AngularVelocity = (9f, 9f, 9f);
        island.AddAtEvent(core);

        island.Tail(Environment(psiEnd: 1.5d), _ => { }, _ => { }, now: 1d);

        core.AngularVelocity.ShouldBe((1f, 2f, 3f));
        core.PendingSnapshot.ShouldBeNull();
    }

    [Test]
    public void Tail_ACoreBroughtToTheEventAndMoved_IsNotPutBack()
    {
        (IvpImpactIsland island, IvpRigidBody core, _) = Island();
        core.PendingSnapshot = new IvpCoreSnapshot((1f, 2f, 3f), (0d, 0d, 0d, 1d), (0d, 0d, 0d, 1d)) { Moved = true };
        core.AngularVelocity = (9f, 9f, 9f);
        island.AddAtEvent(core);

        island.Tail(Environment(psiEnd: 1.5d), _ => { }, _ => { }, now: 1d);

        core.AngularVelocity.ShouldBe((9f, 9f, 9f));
        core.PendingSnapshot.ShouldBeNull();
    }

    [Test]
    public void Tail_AMovedCore_IsSteppedFromItsLastStepAndItsEstimatesCleared()
    {
        (IvpImpactIsland island, IvpRigidBody core, IvpContactPoint contact) = Island();
        core.PendingSnapshot = new IvpCoreSnapshot(default, (0d, 0d, 0d, 1d), (0d, 0d, 0d, 1d)) { Moved = true };
        core.PreviousVelocity = (2f, 0f, 0f);
        core.LastStepped = 0.25d;
        contact.Record!.Estimated = true;
        island.AddIntegrated(core);

        island.Tail(Environment(psiEnd: 1.5d), _ => { }, _ => { }, now: 1d);

        core.Position.X.ShouldBe(1.5d, "position moves by the last velocity over now less the last step");
        core.LastStepped.ShouldBe(1d);
        core.InverseStep.ShouldBe(2f, "the orientation step is the PSI's remainder");
        core.PendingSnapshot.ShouldBeNull();
        contact.Record.Estimated.ShouldBeFalse();
    }

    [Test]
    public void Tail_AMovedCoreWhoseHullIsPassed_TellsTheFiledSynapse()
    {
        (IvpImpactIsland island, IvpRigidBody core, _) = Island();
        core.PendingSnapshot = new IvpCoreSnapshot(default, (0d, 0d, 0d, 1d), (0d, 0d, 0d, 1d)) { Moved = true };
        core.Velocity = (3f, 4f, 0f);
        IvpCollisionObject body = new();
        core.Objects.Add(body);
        Listener listener = new();
        body.Hull.Install(listener, now: 0d, allowance: 0d);
        island.AddIntegrated(core);

        island.Tail(Environment(psiEnd: 1.5d), _ => { }, _ => { }, now: 1d);

        listener.Told.ShouldBeGreaterThan(0, "the hull pass tells what the step pushed");
    }

    [Test]
    public void Tail_AMovedCoreWithAPair_StampsItAndRechecksThePair()
    {
        (IvpImpactIsland island, IvpRigidBody core, _) = Island();
        core.PendingSnapshot = new IvpCoreSnapshot(default, (0d, 0d, 0d, 1d), (0d, 0d, 0d, 1d)) { Moved = true };
        IvpCollisionObject body = new() { Core = core };
        IvpCollisionObject other = new() { Core = new IvpRigidBody() };
        core.Objects.Add(body);
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f);
        new IvpMindistManager().LinkExact(mindist, body, other);
        island.AddIntegrated(core);
        IvpImpactEnvironment environment = Environment(psiEnd: 1.5d);
        environment.ImpactGeneration = 7;
        System.Collections.Generic.List<IvpMindist> minimized = [];

        island.Tail(environment, minimized.Add, _ => { }, now: 1d);

        core.ImpactStamp.ShouldBe(7);
        minimized.ShouldBe([mindist]);
    }

    private sealed class Listener : IIvpHullSynapse
    {
        public int Told { get; private set; }

        public int? HullSlot { get; set; }

        public void HullPassed(IvpHullManager manager, float overshoot) => Told++;

        public void Rebased(float valueShift, float centerShift)
        {
        }

        public void ManagerDeleted(IvpHullManager manager) => manager.Remove(this);
    }

    private static (IvpImpactIsland, IvpRigidBody, IvpContactPoint) Island()
    {
        (IvpFrictionSystem system, _, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        IvpRigidBody core = contact.FirstObject.Core!;
        core.Orientation = (0d, 0d, 0d, 1d);
        core.WorkingOrientation = (0d, 0d, 0d, 1d);

        return (new IvpImpactIsland(system), core, contact);
    }

    private static IvpImpactEnvironment Environment(double psiEnd) =>
        new()
        {
            InverseStep = 0d,
            Step = 0d,
            Limits = new IvpAnomalyLimits(1000f, 0, 1000f, 250, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            PsiEnd = psiEnd,
        };
}
