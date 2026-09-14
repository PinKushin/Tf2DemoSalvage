using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Finding or building the persistent friction contact a collided mindist joins, and the friction system it belongs to —
/// <c>IvpContactPoint::Allocate</c> and <c>IvpFrictionSystem::LinkContactByCore</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, both functions in full** (`docs/HANDOFF.md`, item 3). Not yet pinned by an oracle probe
/// against the shipped binary — these are synthetic conformance tests against the read behaviour, not a replay.
/// </remarks>
public sealed class IvpFrictionLinkingConformanceTests
{
    [Test]
    public void FindOrAllocate_AMindistThatIsNotExact_Throws()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = NewMindist(flags: 0);
        Attach(mindist, first, second);
        mindist.Flags = 0;

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        Should.Throw<InvalidOperationException>(() =>
            IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 0d));
    }

    [Test]
    public void FindOrAllocate_NoExistingContact_BuildsANewOneLinkedToBothObjects()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = NewMindist(flags: 0xC0000);
        Attach(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        IvpContactPoint point = IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 5d);

        point.FirstObject.ShouldBe(first);
        point.SecondObject.ShouldBe(second);
        first.ContactPoints.ShouldContain(point);
        second.ContactPoints.ShouldContain(point);
    }

    [Test]
    public void FindOrAllocate_AMatchingContactAlreadyOnTheFirstObject_ReusesItRatherThanBuildingAnother()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = NewMindist(flags: 0xC0000);
        Attach(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        IvpContactPoint built = IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 5d);
        IvpContactPoint found = IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 6d);

        found.ShouldBeSameAs(built);
    }

    [Test]
    public void FindOrAllocate_TheObjectsInTheOppositeOrder_StillFindsTheSameContact()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = NewMindist(flags: 0xC0000);
        Attach(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        IvpContactPoint built = IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 5d);

        // A second mindist between the same two objects and features, but read the other way round.
        IvpMindist again = NewMindist(flags: 0xC0000);
        Attach(again, first, second);

        IvpContactPoint found = IvpFrictionLinking.FindOrAllocate(again, second, side, first, side, now: 7d);

        found.ShouldBeSameAs(built);
    }

    [Test]
    public void LinkContactByCore_BothCoresAreMovable_ThrowsForTheUnportedMerge()
    {
        IvpContactPoint point = AnyContactPoint();
        IvpRigidBody first = new() { Immovable = false };
        IvpRigidBody second = new() { Immovable = false };

        Should.Throw<NotSupportedException>(() =>
            IvpFrictionLinking.LinkContactByCore(point, first, second, ThrowingEnvironment()));
    }

    [Test]
    public void LinkContactByCore_BothCoresAreImmovable_ThrowsForTheUnportedMerge()
    {
        IvpContactPoint point = AnyContactPoint();
        IvpRigidBody first = new() { Immovable = true };
        IvpRigidBody second = new() { Immovable = true };

        Should.Throw<NotSupportedException>(() =>
            IvpFrictionLinking.LinkContactByCore(point, first, second, ThrowingEnvironment()));
    }

    [Test]
    public void LinkContactByCore_NeitherCoreHasASystemYet_BuildsOneWithBothCoresAndTheirPair()
    {
        IvpContactPoint point = AnyContactPoint();
        IvpRigidBody movable = new() { Immovable = false };
        IvpRigidBody world = new() { Immovable = true };

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(point, movable, world, NewEnvironment());

        movable.FrictionInfo.ShouldNotBeNull();
        movable.FrictionInfo.System.ShouldBeSameAs(system);
        world.FrictionInfoIn(system).ShouldNotBeNull();
        system.FirstContact.ShouldBeSameAs(point);
    }

    [Test]
    public void LinkContactByCore_TheMovableCoreAlreadyHasASystem_AddsTheOtherCoreToItInsteadOfMakingAnother()
    {
        IvpRigidBody movable = new() { Immovable = false };
        IvpRigidBody firstWorld = new() { Immovable = true };
        IvpRigidBody secondWorld = new() { Immovable = true };

        IvpFrictionSystem first = IvpFrictionLinking.LinkContactByCore(AnyContactPoint(), movable, firstWorld, NewEnvironment());
        IvpFrictionSystem second = IvpFrictionLinking.LinkContactByCore(AnyContactPoint(), movable, secondWorld, NewEnvironment());

        second.ShouldBeSameAs(first);
        secondWorld.FrictionInfoIn(first).ShouldNotBeNull();
    }

    [Test]
    public void LinkContactByCore_TheSamePairTwice_FilesOnlyOnePair()
    {
        IvpRigidBody movable = new() { Immovable = false };
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = NewEnvironment();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(AnyContactPoint(), movable, world, environment);
        IvpFrictionLinking.LinkContactByCore(AnyContactPoint(), movable, world, environment);

        system.PairFor(movable, world).ShouldNotBeNull();
        int contacts = 0;

        for (IvpContactPoint? node = system.FirstContact; node is not null; node = node.Next)
        {
            contacts++;
        }

        contacts.ShouldBe(2);
    }

    private static IvpContactPoint AnyContactPoint()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = NewMindist(flags: 0xC0000);
        Attach(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        return new IvpContactPoint(mindist, first, side, second, side, now: 0d);
    }

    private static void Attach(IvpMindist mindist, IvpCollisionObject first, IvpCollisionObject second) =>
        new IvpMindistManager().LinkExact(mindist, first, second);

    private static IvpMindist NewMindist(int flags) =>
        new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = flags,
        };

    private static IvpImpactEnvironment NewEnvironment() => new()
    {
        InverseStep = 1d,
        Step = 1d,
        Materials = ThrowingMaterials.Instance,
        Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
        Anomalies = ThrowingAnomalies.Instance,
    };

    private static IvpImpactEnvironment ThrowingEnvironment() => NewEnvironment();

    private sealed class ThrowingMaterials : IIvpMaterialManager
    {
        public static readonly ThrowingMaterials Instance = new();

        public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");

        public double FrictionFactor(IvpContactRecord record) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");

        public double Elasticity(IvpContactRecord record) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");
    }

    private sealed class ThrowingAnomalies : IIvpAnomalyManager
    {
        public static readonly ThrowingAnomalies Instance = new();

        public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");

        public void MaximumAngularVelocityExceeded(
            IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");

        public bool MaximumContactsExceeded(System.Collections.Generic.IReadOnlyList<IvpRigidBody> cores) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");

        public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core) =>
            throw new InvalidOperationException("Not needed for a friction-linking test.");
    }
}
