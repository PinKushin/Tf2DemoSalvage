using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Taking a contact out of a friction system's list — <c>FUN_180088ce0</c>, the inverse of the head-insert link (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*): the list is a doubly-linked chain with its
/// head at <c>system+0x40</c>, each contact's next at <c>cp+0x0</c> and previous at <c>cp+0x8</c>, and the count at
/// <c>system+0x7a</c>. Not pinned by an oracle probe against the shipped binary — the list surgery is fully determined by the
/// quote, so this is synthetic conformance against the read behaviour (D38), the first callee <c>FUN_180083e40</c> needs.
///
/// **The contacts are real, built through <see cref="IvpFrictionLinking.FindOrAllocate"/>** rather than fabricated, so the
/// links this exercises are the ones production writes.
/// </remarks>
public sealed class IvpFrictionSystemUnlinkTests
{
    [Test]
    public void Unlink_TheMiddleContact_JoinsItsNeighboursAndDropsTheCount()
    {
        IvpFrictionSystem system = System();
        IvpContactPoint first = Contact();
        IvpContactPoint middle = Contact();
        IvpContactPoint last = Contact();

        // Head-insert order: last-linked is the head, so the list reads last, middle, first.
        system.Link(first);
        system.Link(middle);
        system.Link(last);

        system.Unlink(middle);

        system.ContactCount.ShouldBe((short)2);
        Chain(system).ShouldBe([last, first]);
        middle.Next.ShouldBeNull();
        middle.Previous.ShouldBeNull();
        middle.FrictionSystem.ShouldBeNull();
    }

    [Test]
    public void Unlink_TheHead_MovesTheHeadToTheNext()
    {
        IvpFrictionSystem system = System();
        IvpContactPoint first = Contact();
        IvpContactPoint head = Contact();

        system.Link(first);
        system.Link(head);

        system.Unlink(head);

        system.FirstContact.ShouldBe(first);
        first.Previous.ShouldBeNull("the new head has no previous");
        Chain(system).ShouldBe([first]);
    }

    [Test]
    public void Unlink_TheLastRemaining_EmptiesTheList()
    {
        IvpFrictionSystem system = System();
        IvpContactPoint only = Contact();

        system.Link(only);

        system.Unlink(only);

        system.FirstContact.ShouldBeNull();
        system.ContactCount.ShouldBe((short)0);
    }

    [Test]
    public void Unlink_WithNoContact_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => System().Unlink(null!));
    }

    /// <summary>The list from the head along the next links.</summary>
    private static IvpContactPoint[] Chain(IvpFrictionSystem system)
    {
        System.Collections.Generic.List<IvpContactPoint> chain = [];

        for (IvpContactPoint? at = system.FirstContact; at is not null; at = at.Next)
        {
            chain.Add(at);
        }

        return [.. chain];
    }

    /// <summary>A real contact point, built the way production builds one.</summary>
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

    private static IvpFrictionSystem System()
    {
        IvpImpactEnvironment environment = new()
        {
            InverseStep = 0d,
            Step = 0d,
            Limits = new IvpAnomalyLimits(0f, 0, 0f, 0, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(
                new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            GravityLength = 0f,
        };

        return new IvpFrictionSystem(environment);
    }
}
