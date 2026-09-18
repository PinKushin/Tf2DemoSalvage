using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A collided mindist's real response — <c>IvpMindist::Collide</c> and <c>FUN_18008ef60</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly, both functions in full** (`docs/HANDOFF.md`, item 3). Not yet pinned by an oracle probe
/// against the shipped binary — this is a wiring test against the read behaviour, not a replay: it checks the chain runs
/// and links what it should, not that the solved impulse matches vphysics.dll to the bit.
/// </remarks>
public sealed class IvpMindistCollideConformanceTests
{
    [Test]
    public void Collide_ABodyRestingOnTheWorld_LinksAContactAndItsFrictionSystem()
    {
        IvpCollisionObject bodyObject = new() { Material = FixedMaterial.Instance };
        IvpCollisionObject worldObject = new() { Material = FixedMaterial.Instance };
        IvpRigidBody body = new() { Immovable = false, UnitState = 0 };
        IvpRigidBody world = new() { Immovable = true };
        bodyObject.Core = body;
        worldObject.Core = world;

        IvpMindist mindist = NewMindist();
        new IvpMindistManager().LinkExact(mindist, bodyObject, worldObject);

        IvpLedgeSide bodySide = IvpContactGeometryConformanceTests.Face((0d, 0d, 0.5d));
        IvpLedgeSide worldSide = IvpContactGeometryConformanceTests.Flat();

        IvpImpactEnvironment environment = NewEnvironment();

        body.Velocity = (0f, 0f, -1f);
        body.PreviousVelocity = (0f, 0f, -1f);

        IvpImpactSolver solver = IvpMindistCollide.Collide(
            mindist, bodyObject, bodySide, worldObject, worldSide, environment, FixedMaterials.Instance, _ => (bodySide, worldSide), _ => { }, _ => { }, now: 1d);

        solver.ShouldNotBeNull();
        solver.RecordRelative.ShouldNotBe((0f, 0f, 0f), "the control: a zero velocity reads the same negated");
        environment.ImpactGeneration.ShouldBe(1);
        environment.LoopPasses.ShouldBeGreaterThan(0, "the impact loop ran");
        body.FrictionInfo!.System.PairFor(body, world)!.LastImpact.ShouldBe(1d);
        body.FrictionInfo.System.FirstContact!.Record!.RelativeVelocity.ShouldBe(
            (-solver.RecordRelative.X, -solver.RecordRelative.Y, -solver.RecordRelative.Z),
            "the first impact's velocity is put back after the loop, negated against an immovable second core");
        body.FrictionInfo.ShouldNotBeNull();
        body.FrictionInfo.System.PairFor(body, world).ShouldNotBeNull();
        body.FrictionInfo.System.FirstContact.ShouldNotBeNull();
        body.FrictionInfo.System.FirstContact.Record.ShouldNotBeNull();
        body.FrictionInfo.System.FirstContact.LastMeasured.ShouldBe(1d);
    }

    /// <remarks>
    /// **Found hanging, 2026-09-14, fixed in <see cref="IvpFrictionLinking.LinkContactByCore"/>.** Relinking an
    /// already-filed contact set its own <see cref="IvpContactPoint.Next"/> to itself — a one-node cycle that hung the
    /// first walk of the friction system's list, which is exactly what this test's own assertion does.
    /// </remarks>
    [Test]
    public void Collide_TheSamePairTwice_ReusesTheSameContactAndSystem()
    {
        IvpCollisionObject bodyObject = new() { Material = FixedMaterial.Instance };
        IvpCollisionObject worldObject = new() { Material = FixedMaterial.Instance };
        IvpRigidBody body = new() { Immovable = false, UnitState = 0 };
        IvpRigidBody world = new() { Immovable = true };
        bodyObject.Core = body;
        worldObject.Core = world;

        IvpMindist mindist = NewMindist();
        new IvpMindistManager().LinkExact(mindist, bodyObject, worldObject);

        IvpLedgeSide bodySide = IvpContactGeometryConformanceTests.Face((0d, 0d, 0.5d));
        IvpLedgeSide worldSide = IvpContactGeometryConformanceTests.Flat();
        IvpImpactEnvironment environment = NewEnvironment();

        IvpMindistCollide.Collide(mindist, bodyObject, bodySide, worldObject, worldSide, environment, FixedMaterials.Instance, _ => (bodySide, worldSide), _ => { }, _ => { }, now: 1d);
        IvpMindistCollide.Collide(mindist, bodyObject, bodySide, worldObject, worldSide, environment, FixedMaterials.Instance, _ => (bodySide, worldSide), _ => { }, _ => { }, now: 2d);

        IvpFrictionSystem system = body.FrictionInfo!.System;
        int contacts = 0;

        for (IvpContactPoint? node = system.FirstContact; node is not null; node = node.Next)
        {
            contacts++;
        }

        contacts.ShouldBe(1, "the second collision reused the same contact rather than allocating another");
        system.FirstContact.ShouldNotBeNull();
        system.FirstContact.LastMeasured.ShouldBe(2d, "the reused contact's last-measured time moved to the later collision");
    }

    /// <remarks>**The negation is on the SECOND core's immovable bit**: with the world first, the velocity is put back as solved.</remarks>
    [Test]
    public void Collide_TheWorldFirst_PutsTheVelocityBackUnnegated()
    {
        IvpCollisionObject bodyObject = new() { Material = FixedMaterial.Instance };
        IvpCollisionObject worldObject = new() { Material = FixedMaterial.Instance };
        IvpRigidBody body = new() { Immovable = false, UnitState = 0, Velocity = (0f, 0f, -1f), PreviousVelocity = (0f, 0f, -1f) };
        IvpRigidBody world = new() { Immovable = true };
        bodyObject.Core = body;
        worldObject.Core = world;

        IvpMindist mindist = NewMindist();
        new IvpMindistManager().LinkExact(mindist, worldObject, bodyObject);

        IvpLedgeSide worldSide = IvpContactGeometryConformanceTests.Face((0d, 0d, 0.5d));
        IvpLedgeSide bodySide = IvpContactGeometryConformanceTests.Flat();

        IvpImpactSolver solver = IvpMindistCollide.Collide(
            mindist, worldObject, worldSide, bodyObject, bodySide, NewEnvironment(), FixedMaterials.Instance, _ => (worldSide, bodySide), _ => { }, _ => { }, now: 1d);

        solver.RecordRelative.ShouldNotBe((0f, 0f, 0f));
        body.FrictionInfo!.System.FirstContact!.Record!.RelativeVelocity.ShouldBe(solver.RecordRelative);
    }

    private static IvpMindist NewMindist() =>
        new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = 0xC0000,
        };

    private static IvpImpactEnvironment NewEnvironment() => new()
    {
        InverseStep = 100d,
        Step = 0.01d,
        Materials = FixedMaterials.Instance,
        Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
        Anomalies = ThrowingAnomalies.Instance,
    };

    private sealed class FixedMaterials : IIvpMaterialManager
    {
        public static readonly FixedMaterials Instance = new();

        public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index) => FixedMaterial.Instance;

        public double FrictionFactor(IvpContactRecord record) => 0.5d;

        public double Elasticity(IvpContactRecord record) => 0.1d;
    }

    private sealed class FixedMaterial : IIvpMaterial
    {
        public static readonly FixedMaterial Instance = new();

        public double FrictionFactor => 1d;

        public double SecondFrictionFactor => 1d;

        public double Elasticity => 0.1d;

        public bool HasSecondFriction => false;
    }

    private sealed class ThrowingAnomalies : IIvpAnomalyManager
    {
        public static readonly ThrowingAnomalies Instance = new();

        public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity) =>
            throw new InvalidOperationException("Not needed for a collide-wiring test.");

        public void MaximumAngularVelocityExceeded(
            IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin) =>
            throw new InvalidOperationException("Not needed for a collide-wiring test.");

        public bool MaximumContactsExceeded(System.Collections.Generic.IReadOnlyList<IvpRigidBody> cores) =>
            throw new InvalidOperationException("Not needed for a collide-wiring test.");

        public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core) =>
            throw new InvalidOperationException("Not needed for a collide-wiring test.");
    }
}
