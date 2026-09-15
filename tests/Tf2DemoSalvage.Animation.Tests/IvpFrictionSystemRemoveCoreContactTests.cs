using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Taking a contact out of one core's share of a friction system, and dropping an emptied share and its core —
/// <c>FUN_180075130</c> then <c>FUN_180077c10</c>/<c>FUN_180088c80</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point* and *What a core keeps per system*): a core's
/// share is found through <c>FUN_180077f00</c>, the contact leaves its ordered contact vector, and an emptied share is detached
/// from the core as the core leaves the system. Synthetic conformance (D38): the bookkeeping is fully determined by the quote,
/// and Ghidra MCP is down this session. Shares are built through <see cref="IvpFrictionLinking.LinkContactByCore"/>, the
/// production filing path — <see cref="IvpFrictionSystem.AddCore"/> is what this removal is the inverse of.
/// </remarks>
public sealed class IvpFrictionSystemRemoveCoreContactTests
{
    [Test]
    public void LinkContactByCore_FilesTheContactOnBothCoresShares()
    {
        // **The producer the removal is the inverse of** — `FUN_180054640` files a contact onto BOTH cores' shares, not
        // just the pair. Asserted on each share directly, because the immovable ("world") core's filing has no other
        // check: without this, dropping its FileOnCore call reddens nothing (found by sabotage).
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint contact = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(contact, movable, world, environment);

        movable.FrictionInfoIn(system)!.Contacts.ShouldContain(contact);
        world.FrictionInfoIn(system)!.Contacts.ShouldContain(contact);
    }

    [Test]
    public void RemoveCoreContact_OneOfTwoOnTheCore_KeepsTheCoreAndAnswersFalse()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint first = Contact();
        IvpContactPoint second = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(first, movable, world, environment);
        IvpFrictionLinking.LinkContactByCore(second, movable, world, environment);

        // Both contacts are on the movable core's one share.
        movable.FrictionInfoIn(system)!.Contacts.Count.ShouldBe(2);

        system.RemoveCoreContact(first, movable).ShouldBeFalse("one contact remains on the core");

        movable.FrictionInfoIn(system)!.Contacts.ShouldBe([second]);
        system.Cores.ShouldContain(movable);
        system.MovableCores.ShouldContain(movable);
    }

    [Test]
    public void RemoveCoreContact_TheLastOnAMovableCore_DropsTheCoreAndItsShare()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint only = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(only, movable, world, environment);

        system.RemoveCoreContact(only, movable).ShouldBeTrue("the core's share emptied");

        system.Cores.ShouldNotContain(movable);
        system.MovableCores.ShouldNotContain(movable);
        movable.FrictionInfo.ShouldBeNull("a movable core's one share is cleared");
        movable.FrictionInfoIn(system).ShouldBeNull();
    }

    [Test]
    public void RemoveCoreContact_TheLastOnAnImmovableCore_DropsItFromTheSystemsHash()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint only = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(only, movable, world, environment);

        system.RemoveCoreContact(only, world).ShouldBeTrue("the immovable core's share emptied");

        system.Cores.ShouldNotContain(world);
        world.FrictionInfos.ShouldNotContainKey(system);
        world.FrictionInfoIn(system).ShouldBeNull();
    }

    [Test]
    public void RemoveCoreContact_ACoreNotInTheSystem_Throws()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpFrictionSystem system =
            IvpFrictionLinking.LinkContactByCore(Contact(), movable, world, environment);

        Should.Throw<InvalidOperationException>(
            () => system.RemoveCoreContact(Contact(), new IvpRigidBody()));
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
