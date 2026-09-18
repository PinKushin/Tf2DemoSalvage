using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The impact loop's mini-island — the stack <c>block</c> <c>FUN_180090700</c> assembles and
/// <c>FUN_180090bd0</c> drains (B369, D172).
/// </summary>
/// <remarks>
/// **The state object read out of the disassembly, ahead of the loop that fills it.** `FUN_18008ef60`
/// hands `FUN_180090700` an empty block: `+0x0` the environment, `+0x8` a pass count, three growable
/// vectors <c>{capacity, count, pointer}</c> at `+0x10` (cores the loop integrates), `+0x20` (cores it
/// only brought to the event) and `+0x30` (pairs to scan), and `+0x40` the friction system
/// (`docs/findings/51`, *The impact loop*). Only the behaviours the quote fixes with no ambiguity are
/// pinned here, so the loop that consumes it is built against a settled shape:
///
/// - **the pairs and both core lists are drained LAST to first** — every walk in `FUN_180090700`,
///   `FUN_180090bd0` and `FUN_1800909d0` reads its vector `last to first`;
/// - **a pair is added only when it is not already present** — `FUN_18008da40` pushes a touched pair
///   `not already on +0x30`;
/// - **the pass count caps at `0x1388`** — `block+0x8 > 0x1388` asks the mindist to stop.
///
/// **The environment (`+0x0`) is not carried yet** — only the tail `FUN_1800909d0` reads it (for the
/// step span and the impact counter), so it joins when that lands rather than forcing every test to
/// fabricate a whole environment for a container that does not touch it.
/// </remarks>
public sealed class IvpImpactIslandTests
{
    [Test]
    public void PassCap_IsTheEnginesOwnValue_Is5000()
    {
        // `block+0x8 > 0x1388` is the loop's own bound (`FUN_180090700`). 0x1388 == 5000.
        IvpImpactIsland.PassCap.ShouldBe(5000);
    }

    [Test]
    public void Pairs_AddedTwice_HeldOnce()
    {
        IvpImpactIsland island = new(System());

        IvpFrictionPair pair = new(new IvpRigidBody(), new IvpRigidBody());

        island.AddPair(pair).ShouldBeTrue("first add is new");
        island.AddPair(pair).ShouldBeFalse("already on the list, per FUN_18008da40's guard");

        island.Pairs.Count.ShouldBe(1);
    }

    [Test]
    public void Pairs_Drained_ComeBackLastToFirst()
    {
        IvpImpactIsland island = new(System());

        IvpFrictionPair first = new(new IvpRigidBody(), new IvpRigidBody());
        IvpFrictionPair second = new(new IvpRigidBody(), new IvpRigidBody());

        island.AddPair(first);
        island.AddPair(second);

        // Every consumer of +0x30 reads it last to first.
        island.PairsLastToFirst().ShouldBe([second, first]);
    }

    [Test]
    public void CoresIntegrated_AndCoresAtEvent_AreDistinctLists()
    {
        IvpImpactIsland island = new(System());

        island.AddIntegrated(new IvpRigidBody());

        island.CoresIntegrated.Count.ShouldBe(1);
        island.CoresAtEvent.Count.ShouldBe(0, "+0x10 and +0x20 are separate vectors");
    }

    [Test]
    public void NewIsland_WithNoSystem_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => new IvpImpactIsland(null!));
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
