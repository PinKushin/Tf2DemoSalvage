using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Taking a contact off its pair, and deleting an emptied pair — <c>FUN_180088130</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*): the pair of the contact's two cores is
/// found in either order, the contact leaves the pair's contact vector, and a pair with no contacts left is removed from the
/// system. `FUN_180086b30` — the deciding test — is that vector's count. Synthetic conformance (D38): the bookkeeping is
/// fully determined by the quote, and Ghidra MCP is down this session so no binary-pinned fixture can be cut.
///
/// **Pairs and contacts are built through <see cref="IvpFrictionLinking.LinkContactByCore"/>**, the production filing path, so
/// the pair this removes from is shaped exactly as one filing leaves it.
/// </remarks>
public sealed class IvpFrictionSystemRemoveFromPairTests
{
    [Test]
    public void RemoveFromPair_OneOfTwoContacts_KeepsThePairAndAnswersFalse()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint first = Contact();
        IvpContactPoint second = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(first, movable, world, environment);
        IvpFrictionLinking.LinkContactByCore(second, movable, world, environment);

        IvpFrictionPair pair = system.PairFor(movable, world)!;
        pair.Contacts.Count.ShouldBe(2, "both contacts filed onto the one pair");

        system.RemoveFromPair(first, movable, world).ShouldBeFalse("one contact remains");

        pair.Contacts.ShouldBe([second]);
        system.Pairs.ShouldContain(pair);
    }

    [Test]
    public void RemoveFromPair_TheLastContact_DeletesThePairAndAnswersTrue()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        IvpContactPoint only = Contact();

        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(only, movable, world, environment);

        IvpFrictionPair pair = system.PairFor(movable, world)!;

        system.RemoveFromPair(only, movable, world).ShouldBeTrue("the pair emptied");

        system.Pairs.ShouldNotContain(pair);
        system.PairFor(movable, world).ShouldBeNull();
    }

    [Test]
    public void RemoveFromPair_CoresWithNoPair_Throws()
    {
        IvpRigidBody movable = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpImpactEnvironment environment = Environment();

        // A system with a different pair, so PairFor for these two cores is genuinely absent.
        IvpFrictionSystem system =
            IvpFrictionLinking.LinkContactByCore(Contact(), movable, world, environment);

        IvpRigidBody strangerMovable = new();
        IvpRigidBody strangerWorld = new() { Immovable = true };

        Should.Throw<InvalidOperationException>(
            () => system.RemoveFromPair(Contact(), strangerMovable, strangerWorld));
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
