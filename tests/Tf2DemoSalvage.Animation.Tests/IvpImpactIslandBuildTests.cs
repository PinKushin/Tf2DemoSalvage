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

    /// <remarks>
    /// `FUN_180090700` at `18009087e`: past the cap, a non-null mindist (the second argument, `R12`) is called at its slot 0 with 1
    /// — `IVP_Mindist`'s destructor (`0x1800fe960`), so the collided mindist is deleted — and the loop stops. The cap is lowered
    /// so one pass crosses it.
    /// </remarks>
    [Test]
    public void Build_PastThePassCap_DeletesTheCollidedMindist()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        collided.Record!.PredictedGap = IvpCollisionTolerance.RampEnd * 0.1f;
        island.Cap = 0;
        CountingMindist mindist = new();

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d, mindist);

        island.Passes.ShouldBe(1);
        mindist.Deletes.ShouldBe(1);
    }

    [Test]
    public void Build_UnderThePassCap_KeepsTheMindist()
    {
        // The control: the same mindist, no pass run, no delete.
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpFrictionPair pair, IvpContactPoint collided) = Collided();
        island.Cap = 0;
        CountingMindist mindist = new();

        island.Build(environment, pair, collided, Revalidate.Sides, Revalidate.Materials.Instance, _ => { }, _ => { }, now: 1d, mindist);

        mindist.Deletes.ShouldBe(0);
    }

    private sealed class CountingMindist() : IvpMindist(
        new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point), new IvpSynapse(new IvpLedgeEdge(0, 1), IvpFeatureKind.Point), 0f)
    {
        public int Deletes { get; private set; }

        public override void Delete() => Deletes++;
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
