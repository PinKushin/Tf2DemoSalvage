using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Removing a contact from a friction system entirely — <c>FUN_180083e40</c>, composed of the four callees beneath it
/// (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*): both cores' anchors are reset to the
/// environment's time, the contact leaves the system's list, its pair loses it (and is deleted when it empties, setting the
/// split flag), and each core's share loses it (dropping the core when its share empties). Synthetic conformance (D38); the
/// state is built through <see cref="IvpFrictionLinking.LinkContactByCore"/>, the production filing path.
/// </remarks>
public sealed class IvpFrictionSystemRemoveContactTests
{
    [Test]
    public void RemoveContact_TheOnlyContact_EmptiesTheSystemAndFlagsTheSplitCheck()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint only = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(only, movable, world, environment);

        system.RemoveContact(only, movable, world, now: 7d);

        system.FirstContact.ShouldBeNull("off the list");
        system.ContactCount.ShouldBe((short)0);
        system.Pairs.ShouldBeEmpty("the pair emptied and was deleted");
        system.SplitCheckDue.ShouldBeTrue("system+0x80 is set when a pair goes");
        system.Cores.ShouldBeEmpty("both shares emptied, so both cores left");
        movable.FrictionInfo.ShouldBeNull();
        world.FrictionInfos.ShouldNotContainKey(system);
    }

    [Test]
    public void RemoveContact_ResetsBothCoresAnchorsToTheGivenTime()
    {
        // `FUN_180078820(core)` on each core, before anything is unlinked: core+0x200 and core+0x208 both take env+0x188.
        IvpRigidBody movable = new() { RestAnchorTime = 1d, SettleAnchorTime = 2d };
        IvpRigidBody world = new() { Immovable = true, RestAnchorTime = 3d, SettleAnchorTime = 4d };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint only = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(only, movable, world, environment);

        system.RemoveContact(only, movable, world, now: 9.5d);

        movable.RestAnchorTime.ShouldBe(9.5d);
        movable.SettleAnchorTime.ShouldBe(9.5d);
        world.RestAnchorTime.ShouldBe(9.5d);
        world.SettleAnchorTime.ShouldBe(9.5d);
    }

    [Test]
    public void RemoveContact_OneOfTwo_LeavesThePairTheCoresAndNoSplitCheck()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint first = Contact();
        IvpContactPoint second = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(first, movable, world, environment);
        IvpFrictionLinking.LinkContactByCore(second, movable, world, environment);

        system.RemoveContact(first, movable, world, now: 7d);

        system.ContactCount.ShouldBe((short)1);
        system.FirstContact.ShouldBe(second);
        system.Pairs.Count.ShouldBe(1, "the pair still holds the other contact");
        system.SplitCheckDue.ShouldBeFalse("no pair was deleted");
        system.Cores.ShouldContain(movable);
        system.Cores.ShouldContain(world);
    }

    [Test]
    public void RemoveContact_WithNoContact_Refuses()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };

        IvpFrictionSystem system =
            IvpFrictionLinking.LinkContactByCore(Contact(), movable, world, Environment());

        Should.Throw<ArgumentNullException>(() => system.RemoveContact(null!, movable, world, now: 0d));
    }

    private static IvpContactPoint Contact()
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = 0xC0000,
        };

        new IvpMindistManager().LinkExact(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        return IvpFrictionLinking.FindOrAllocate(mindist, first, side, second, side, now: 0d);
    }

    private static IvpImpactEnvironment Environment() =>
        new()
        {
            InverseStep = 0d,
            Step = 0d,
            Limits = new IvpAnomalyLimits(0f, 0, 0f, 0, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(
                new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            GravityLength = 0f,
        };
}
