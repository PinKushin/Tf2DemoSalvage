using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>One pass of the impact loop — <c>FUN_180090bd0(block)</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The impact loop*): the contact with the lowest predicted gap below
/// <c>block[0x42]</c> is solved, a NaN estimate ends the search, and each moved core grows the island. Synthetic conformance
/// (D38); estimates are set on the records directly so the choice is exact.
/// </remarks>
public sealed class IvpImpactIslandDrainTests
{
    [Test]
    public void Drain_NothingBelowTheRampEnd_ReturnsFalse()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(IvpCollisionTolerance.RampEnd);

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeFalse();
        contact.Record!.Impacts.ShouldBe((short)0);
    }

    [Test]
    public void Drain_TheClosestContact_IsTheOneSolved()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint far, IvpFrictionPair pair) =
            Island(IvpCollisionTolerance.RampEnd * 0.5f);
        IvpContactPoint near = Another(island, pair, IvpCollisionTolerance.RampEnd * 0.1f);

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeTrue();

        near.Record!.Impacts.ShouldBe((short)1);
        far.Record!.Impacts.ShouldBe((short)0);
    }

    [Test]
    public void Drain_ANaNEstimateVisitedFirst_HidesTheNextContact()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint low, IvpFrictionPair pair) =
            Island(IvpCollisionTolerance.RampEnd * 0.1f);
        Another(island, pair, float.NaN);

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeFalse();
        low.Record!.Impacts.ShouldBe((short)0);
    }

    /// <remarks>
    /// **`MINSD` answers its second operand when either is NaN**, so after a NaN the next estimate becomes the best again: the
    /// contact right after the NaN is hidden (its compare against NaN fails), and the one after that can still win.
    /// </remarks>
    [Test]
    public void Drain_AfterANaNAndOneMore_TheSearchRecovers()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint lowest, IvpFrictionPair pair) =
            Island(IvpCollisionTolerance.RampEnd * 0.05f);
        IvpContactPoint middle = Another(island, pair, IvpCollisionTolerance.RampEnd * 0.1f);
        Another(island, pair, float.NaN);

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeTrue();

        lowest.Record!.Impacts.ShouldBe((short)1);
        middle.Record!.Impacts.ShouldBe((short)0);
    }

    [Test]
    public void Drain_ACoreAlreadyMoved_IsNotGrownAgainAndItsEstimatesAreCleared()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(0f);
        IvpRigidBody movable = contact.FirstObject.Core!;
        movable.PendingSnapshot = new IvpCoreSnapshot(default, default, default) { Moved = true };

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeTrue();

        island.CoresIntegrated.ShouldBeEmpty();
        contact.Record!.Estimated.ShouldBeFalse();
    }

    [Test]
    public void Drain_AContactNotYetEstimated_IsEstimatedAndCounted()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(0f);
        contact.Record!.Estimated = false;

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d);

        environment.Estimates.ShouldBe(1);
    }

    [Test]
    public void Drain_BothCoresFrozen_SkipsThePair()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(0f);
        contact.FirstObject.Core!.CollisionFreeze = 1;

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeFalse();
    }

    [Test]
    public void Drain_ACoreWithAUnit_IsBroughtToTheEventAndGrowsTheIsland()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(0f);
        IvpRigidBody movable = contact.FirstObject.Core!;
        movable.UnitState = 0;

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeTrue();

        island.CoresAtEvent.ShouldBe([movable]);
        island.CoresIntegrated.ShouldBe([movable]);
        movable.PendingSnapshot!.Moved.ShouldBeTrue();
    }

    [Test]
    public void Drain_ACoreWithoutAUnit_IsNotRebuiltAndItsEstimatesAreCleared()
    {
        (IvpImpactIsland island, IvpImpactEnvironment environment, IvpContactPoint contact, _) = Island(0f);

        island.Drain(environment, Revalidate.Sides, Revalidate.Materials.Instance, now: 1d).ShouldBeTrue();

        contact.FirstObject.Core!.PendingSnapshot.ShouldBeNull();
        island.CoresIntegrated.ShouldBeEmpty();
        contact.Record!.Estimated.ShouldBeFalse();
    }

    private static (IvpImpactIsland, IvpImpactEnvironment, IvpContactPoint, IvpFrictionPair) Island(float predicted)
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Revalidate.Linked(out _);
        Measure(contact, predicted);
        IvpImpactIsland island = new(system);
        island.AddPair(pair);

        return (island, system.Environment, contact, pair);
    }

    private static IvpContactPoint Another(IvpImpactIsland island, IvpFrictionPair pair, float predicted)
    {
        IvpContactPoint contact = Revalidate.Contact(pair.FirstCore, pair.SecondCore);
        IvpFrictionLinking.LinkContactByCore(contact, pair.FirstCore, pair.SecondCore, island.System.Environment);
        Measure(contact, predicted);

        return contact;
    }

    private static void Measure(IvpContactPoint contact, float predicted)
    {
        IvpContactRecord record = IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        contact.SetMaterials(Revalidate.Materials.Instance);
        record.Estimated = true;
        record.PredictedGap = predicted;
    }
}
