using System.Linq;
using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A friction system falling into two once a pair empties — <c>IvpFrictionSystem::DetachedRoot</c> (<c>FUN_1800877b0</c>) and
/// <c>IvpFrictionSystem::Split</c> (<c>FUN_180086e80</c>) (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler, both in full, and checked against `docs/findings/51`** (*The split*). The union joins the two
/// cores of every pair with no immovable core, so two bodies resting on the same world stay in separate sets; the answer is the
/// root of the lowest-index movable core whose root is not the lowest-index movable core's own. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpFrictionSplitConformanceTests
{
    [Test]
    public void DetachedRoot_TwoBodiesTouchingEachOther_IsNull()
    {
        (IvpFrictionSystem system, _, _, _, _) = TwoBodiesOnTheWorld();

        system.DetachedRoot().ShouldBeNull();
    }

    /// <remarks>
    /// **The answer is the set's ROOT, and the union points a pair's second core's root at its first's**: with the first body cut
    /// loose, the second and third are joined by their own pair (second, third), so the set they form is rooted at the second.
    /// </remarks>
    [Test]
    public void DetachedRoot_ADetachedPairOfBodies_IsTheRootTheirPairJoinedInto()
    {
        IvpImpactEnvironment environment = Revalidate.Environment();
        IvpRigidBody world = new() { Immovable = true };
        IvpRigidBody[] bodies = [new(), new(), new()];
        IvpFrictionSystem system = Link(bodies[0], world, environment);
        Link(bodies[1], world, environment);
        Link(bodies[2], world, environment);
        IvpContactPoint cut = Revalidate.Contact(bodies[0], bodies[1]);
        IvpFrictionLinking.LinkContactByCore(cut, bodies[0], bodies[1], environment);
        IvpFrictionLinking.LinkContactByCore(Revalidate.Contact(bodies[1], bodies[2]), bodies[1], bodies[2], environment);
        system.PairFor(bodies[1], bodies[2])!.FirstCore.ShouldBeSameAs(bodies[1], "the control: the pair's core order");
        system.RemoveContact(cut, bodies[0], bodies[1], now: 0d);

        system.DetachedRoot().ShouldBeSameAs(bodies[1]);
    }

    /// <remarks>
    /// **A world whose share of a side is left empty leaves that side**: a body on one world and a body on another, cut apart,
    /// end with each world in its own body's system only.
    /// </remarks>
    [Test]
    public void Split_EachBodyOnItsOwnWorld_LeavesEachWorldOnlyInItsBodysSystem()
    {
        IvpImpactEnvironment environment = Revalidate.Environment();
        IvpRigidBody first = new();
        IvpRigidBody second = new();
        IvpRigidBody firstWorld = new() { Immovable = true };
        IvpRigidBody secondWorld = new() { Immovable = true };
        IvpFrictionSystem system = Link(first, firstWorld, environment);
        Link(second, secondWorld, environment);
        IvpContactPoint between = Revalidate.Contact(first, second);
        IvpFrictionLinking.LinkContactByCore(between, first, second, environment);
        system.RemoveContact(between, first, second, now: 0d);

        system.Split(system.DetachedRoot()!);

        IvpFrictionSystem split = second.FrictionInfo!.System;
        system.Cores.ShouldBe([first, firstWorld], ignoreOrder: true);
        split.Cores.ShouldBe([second, secondWorld], ignoreOrder: true);
        firstWorld.FrictionInfoIn(split).ShouldBeNull();
        secondWorld.FrictionInfoIn(system).ShouldBeNull();
    }

    /// <remarks>
    /// **A pair with the world does not join its bodies**: once the pair between them goes, the second body (index 2, after the
    /// first and the world) is the set that no longer touches the first.
    /// </remarks>
    [Test]
    public void DetachedRoot_TwoBodiesJoinedOnlyThroughTheWorld_IsTheLaterBodysRoot()
    {
        (IvpFrictionSystem system, IvpRigidBody first, IvpRigidBody second, _, IvpContactPoint between) = TwoBodiesOnTheWorld();
        system.Cores.IndexOf(first).ShouldBeLessThan(system.Cores.IndexOf(second), "the control: the fixture's core order");

        system.RemoveContact(between, first, second, now: 0d);

        system.SplitCheckDue.ShouldBeTrue("the control: the emptied pair asks for the check");
        system.DetachedRoot().ShouldBeSameAs(second);
    }

    /// <remarks>
    /// **The detached body leaves with its pair and its contacts; the world is in both**, holding in each only the contacts of
    /// that system's pairs.
    /// </remarks>
    [Test]
    public void Split_ABodyDetached_MovesItItsPairAndItsContactsIntoANewSystem()
    {
        (IvpFrictionSystem system, IvpRigidBody first, IvpRigidBody second, IvpRigidBody world, IvpContactPoint between) =
            TwoBodiesOnTheWorld();
        system.RemoveContact(between, first, second, now: 0d);
        IvpContactPoint firstOnWorld = system.PairFor(first, world)!.Contacts[0];
        IvpContactPoint secondOnWorld = system.PairFor(second, world)!.Contacts[0];

        system.Split(system.DetachedRoot()!);

        IvpFrictionSystem split = second.FrictionInfo!.System;
        split.ShouldNotBeSameAs(system);
        first.FrictionInfo!.System.ShouldBeSameAs(system);
        system.Cores.ShouldBe([first, world], ignoreOrder: true);
        split.Cores.ShouldBe([world, second], ignoreOrder: true);
        system.MovableCores.ShouldBe([first]);
        split.MovableCores.ShouldBe([second]);
        split.Pairs.Count.ShouldBe(1);
        split.PairFor(second, world)!.Contacts.ShouldBe([secondOnWorld]);
        system.PairFor(second, world).ShouldBeNull();
        secondOnWorld.FrictionSystem.ShouldBeSameAs(split);
        split.ContactCount.ShouldBe((short)1);
        system.ContactCount.ShouldBe((short)1);
        split.FirstContact.ShouldBeSameAs(secondOnWorld);
        system.FirstContact.ShouldBeSameAs(firstOnWorld);
        world.FrictionInfoIn(system)!.Contacts.ShouldBe([firstOnWorld]);
        world.FrictionInfoIn(split)!.Contacts.ShouldBe([secondOnWorld]);
        second.FrictionInfo.Contacts.ShouldBe([secondOnWorld]);
        second.Controllers.ShouldBe(split.Faces, "the old system's faces left the body and the new one's were filed");
    }

    /// <remarks>
    /// **The split repeats until the check finds nothing**, so three bodies that each rest only on the world end in three systems,
    /// the first body kept by the one that was split.
    /// </remarks>
    [Test]
    public void Split_ThreeBodiesEachOnlyOnTheWorld_EndsInThreeSystems()
    {
        IvpImpactEnvironment environment = Revalidate.Environment();
        IvpRigidBody world = new() { Immovable = true };
        IvpRigidBody[] bodies = [new(), new(), new()];
        IvpFrictionSystem system = Link(bodies[0], world, environment);
        Link(bodies[1], world, environment);
        Link(bodies[2], world, environment);
        IvpContactPoint first = Revalidate.Contact(bodies[0], bodies[1]);
        IvpContactPoint second = Revalidate.Contact(bodies[1], bodies[2]);
        IvpFrictionLinking.LinkContactByCore(first, bodies[0], bodies[1], environment);
        IvpFrictionLinking.LinkContactByCore(second, bodies[1], bodies[2], environment);
        system.MovableCores.Count.ShouldBe(3, "the control: the bodies' contacts merged all three into one system");
        system.RemoveContact(first, bodies[0], bodies[1], now: 0d);
        system.RemoveContact(second, bodies[1], bodies[2], now: 0d);

        system.Split(system.DetachedRoot()!);

        bodies[0].FrictionInfo!.System.ShouldBeSameAs(system);
        bodies.Select(body => body.FrictionInfo!.System).Distinct().Count().ShouldBe(3);
        system.Cores.ShouldBe([bodies[0], world], ignoreOrder: true);
    }

    /// <remarks>
    /// **The normal pass runs the split it was asked for** (<c>FUN_180084320</c>: <c>+0x80</c> set → cleared, the check, the split).
    /// </remarks>
    [Test]
    public void Advance_TheNormalPassWithABodyDetached_SplitsTheSystem()
    {
        (IvpFrictionSystem system, IvpRigidBody first, IvpRigidBody second, IvpRigidBody world, IvpContactPoint between) =
            TwoBodiesOnTheWorld();
        system.RemoveContact(between, first, second, now: 0d);

        foreach (IvpFrictionPair pair in system.Pairs)
        {
            IvpContactPoint contact = pair.Contacts[0];
            IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);

            // Inside its feature and its resting gap, or the pass drops it (`record+0x76`, `block[0x47]`); the fabricated geometry
            // measures outside and far.
            contact.Record!.Outside = false;
            contact.Gap = IvpCollisionTolerance.ContactGap;
        }

        new IvpNormalFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        system.SplitCheckDue.ShouldBeFalse();
        second.FrictionInfo!.System.ShouldNotBeSameAs(system);
        world.FrictionInfoIn(second.FrictionInfo.System).ShouldNotBeNull();
    }

    /// <summary>Two bodies on the world and on each other, all in one system — first, world, second, in that core order.</summary>
    private static (IvpFrictionSystem System, IvpRigidBody First, IvpRigidBody Second, IvpRigidBody World, IvpContactPoint Between)
        TwoBodiesOnTheWorld()
    {
        IvpImpactEnvironment environment = Revalidate.Environment();
        IvpRigidBody first = new();
        IvpRigidBody second = new();
        IvpRigidBody world = new() { Immovable = true };
        IvpFrictionSystem system = Link(first, world, environment);
        Link(second, world, environment);
        IvpContactPoint between = Revalidate.Contact(first, second);
        IvpFrictionLinking.LinkContactByCore(between, first, second, environment).ShouldBeSameAs(system);

        return (system, first, second, world, between);
    }

    private static IvpFrictionSystem Link(IvpRigidBody body, IvpRigidBody world, IvpImpactEnvironment environment) =>
        IvpFrictionLinking.LinkContactByCore(Revalidate.Contact(body, world), body, world, environment);
}
