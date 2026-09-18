using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Growing the impact island around a core an impact moved — <c>FUN_18008da40(block, core, pair)</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The impact loop*): the core is pushed on <c>+0x10</c> and its snapshot's
/// <c>+0x30</c> set; then every pair of the system, last to first, that touches the core, is not the pair given and is not already
/// on <c>+0x30</c>, is revalidated (<c>FUN_180083b30</c>) and pushed when contacts remain. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpImpactIslandGrowTests
{
    [Test]
    public void Grow_TheCore_IsIntegratedAndItsSnapshotMarkedMoved()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpRigidBody core = Moving(contact);
        IvpImpactIsland island = new(system);

        island.Grow(core, pair, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d);

        island.CoresIntegrated.ShouldBe([core]);
        core.PendingSnapshot!.Moved.ShouldBeTrue();
        island.Pairs.ShouldBeEmpty("the pair given is never scanned");
        system.ContactCount.ShouldBe((short)1, "so its fresh outside contact is not revalidated away");
    }

    [Test]
    public void Grow_AnotherPairWithAContactLeft_IsAdded()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpRigidBody core = Moving(contact);
        IvpFrictionPair other = SecondPair(system, core, measured: true);
        IvpImpactIsland island = new(system);

        island.Grow(core, pair, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d);

        island.Pairs.ShouldBe([other]);
    }

    [Test]
    public void Grow_AnotherPairLeftEmpty_IsNotAddedAndIsDeleted()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpRigidBody core = Moving(contact);
        IvpFrictionPair other = SecondPair(system, core, measured: false);
        IvpImpactIsland island = new(system);

        island.Grow(core, pair, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d);

        island.Pairs.ShouldBeEmpty();
        system.Pairs.ShouldNotContain(other);
    }

    [Test]
    public void Grow_APairAlreadyOnTheIsland_IsNotRevalidatedAgain()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpRigidBody core = Moving(contact);
        IvpFrictionPair other = SecondPair(system, core, measured: false);
        IvpImpactIsland island = new(system);
        island.AddPair(other);

        island.Grow(core, pair, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d);

        system.Pairs.ShouldContain(other, "its fresh outside contact was never measured");
        island.Pairs.ShouldBe([other]);
    }

    private static IvpRigidBody Moving(IvpContactPoint contact)
    {
        IvpRigidBody core = contact.FirstObject.Core!;
        core.PendingSnapshot = new IvpCoreSnapshot(default, default, default);

        return core;
    }

    private static IvpFrictionPair SecondPair(IvpFrictionSystem system, IvpRigidBody core, bool measured)
    {
        IvpRigidBody wall = new() { Immovable = true };
        IvpContactPoint contact = Revalidate.Contact(core, wall);
        IvpFrictionLinking.LinkContactByCore(contact, core, wall, system.Environment);

        if (measured)
        {
            IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        }

        return system.PairFor(core, wall)!;
    }
}
