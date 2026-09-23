using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Assembling the impact island around a collided contact — <c>FUN_180090700</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The impact loop*). Synthetic conformance (D38): estimates are set on the
/// records directly, at the ramp's end, so the drain finds nothing and the assembly is observed alone.
/// </remarks>
public sealed class IvpImpactIslandBuildTests
{
    [Test]
    public void Build_AMovableAgainstTheWorld_GrowsTheMovableAndBringsItToTheEvent()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        IvpRigidBody movable = collided.FirstObject.Core!;

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d);

        island.CoresIntegrated.ShouldBe([movable]);
        island.CoresAtEvent.ShouldBe([movable], "the world is immovable and is never pushed");
        island.Pairs.ShouldBe([pair]);
    }

    [Test]
    public void Build_AMovableCoreCarryingBit0x10_IsNeitherGrownNorBroughtToTheEvent()
    {
        // `FUN_180090700`: "core of cp's first object, unless flags & 0x12: FUN_18008da40", and "pair+0x38, unless flags &
        // 0x12: push on +0x20". Bit 0x10 is `SkipsGravity`, and `BringToEvent` saves no snapshot for such a core, so growing
        // it dereferenced a null `core+0x260` — the playback UI test's crash on z1800.
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        IvpRigidBody movable = collided.FirstObject.Core!;
        movable.SkipsGravity = true;
        movable.PendingSnapshot = null;

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d);

        island.CoresIntegrated.ShouldBeEmpty();
        island.CoresAtEvent.ShouldBeEmpty();
    }

    [Test]
    public void Build_TheOtherContactsOfThePair_AreRevalidatedButNotTheCollidedOne()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        IvpContactRecord kept = collided.Record!;
        IvpContactPoint fresh = Revalidate.Contact(pair.FirstCore, pair.SecondCore);
        IvpFrictionLinking.LinkContactByCore(fresh, pair.FirstCore, pair.SecondCore, environment);

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d);

        pair.Contacts.ShouldNotContain(fresh, "a fresh contact before the edge is outside and removed");
        collided.Record.ShouldBeSameAs(kept, "the collided contact is never rebuilt");
    }

    [Test]
    public void Build_NothingToDrain_CountsOnePassAndRunsNone()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        environment.LoopPasses = 5;

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d);

        island.Passes.ShouldBe(0);
        environment.LoopPasses.ShouldBe(6);
    }

    private static (IvpImpactIsland, IvpImpactEnvironment, IvpFrictionPair, IvpContactPoint) Collided()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpContactRecord record = IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        contact.SetMaterials(Revalidate.Materials.Instance);
        record.Estimated = true;
        record.PredictedGap = IvpCollisionTolerance.RampEnd;
        contact.FirstObject.Core!.PendingSnapshot = new IvpCoreSnapshot(default, default, default);

        return (new IvpImpactIsland(system), system.Environment, pair, contact);
    }
}
