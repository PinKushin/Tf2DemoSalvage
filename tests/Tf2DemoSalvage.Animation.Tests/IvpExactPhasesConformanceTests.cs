using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The PSI's walks over the exact mindists — phase 3 <c>FUN_1800983e0</c>, the rechecked array's <c>FUN_180098610</c> with
/// <c>FUN_180098710</c>, and phase 4 <c>FUN_1800985a0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*). Phase
/// 3 and the rechecked array minimize every pair and hand a frozen one to its own slot 7 — for a plain pair `FUN_180097440`,
/// which makes it invalid; phase 4 hands every exact pair to the scheduler with `removeFar` set, which is how a plain pair
/// goes back to far.
/// </remarks>
public sealed class IvpExactPhasesConformanceTests
{
    /// <remarks>**Phase 3 minimizes the exact list head first** — the latest linked first.</remarks>
    [Test]
    public void MinimizeExact_ThreeExactPairs_MinimizesThemHeadFirst()
    {
        Phases phases = new(3);

        phases.Manager.MinimizeExact(phases.Minimize, phases.Queue);

        phases.Minimized.ShouldBe([phases.Pairs[2], phases.Pairs[1], phases.Pairs[0]]);
        phases.Manager.Invalid.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A plain pair whose minimize leaves bits of `0xc000` is made invalid**, the walk having read its next link first, so
    /// the pair after it is still minimized.
    /// </remarks>
    [Test]
    public void MinimizeExact_AFrozenPair_IsInvalidatedAndTheWalkGoesOn()
    {
        Phases phases = new(3);
        phases.FreezeOnMinimize.Add(phases.Pairs[1]);

        phases.Manager.MinimizeExact(phases.Minimize, phases.Queue);

        phases.Minimized.ShouldBe([phases.Pairs[2], phases.Pairs[1], phases.Pairs[0]]);
        phases.Manager.Invalid.ShouldBe([phases.Pairs[1]]);
        phases.Manager.Exact.ShouldBe([phases.Pairs[2], phases.Pairs[0]]);
    }

    /// <remarks>
    /// **A frozen pair is handed to its OWN slot 7** (`+0x38`), with the manager and the queue — `FUN_1800983e0` calls through the
    /// mindist's table rather than making it invalid itself, which is how a larger mindist opens its ledge instead.
    /// </remarks>
    [Test]
    public void MinimizeExact_AFrozenPairOfItsOwnKind_IsFrozenThroughItsOwnSlotSeven()
    {
        RecordingMindist own = new();
        Phases phases = new(1, make: () => own);
        phases.FreezeOnMinimize.Add(own);

        phases.Manager.MinimizeExact(phases.Minimize, phases.Queue);

        own.Slots.ShouldBe(["freeze"]);
        own.FrozenBy.ShouldBeSameAs(phases.Manager);
        own.FrozenQueue.ShouldBeSameAs(phases.Queue);
        phases.Manager.Exact.ShouldBe([own], ignoreOrder: false, customMessage: "the plain mindist's invalidation did not run");
    }

    /// <remarks>
    /// **A pair with bits of `0x3000` goes on to the phantom path** — `FUN_180098dd0` and `FUN_180097d60` through the phantom
    /// listeners — which is not ported: refused.
    /// </remarks>
    [Test]
    public void MinimizeExact_APhantomsPair_IsRefused()
    {
        Phases phases = new(1);
        phases.Pairs[0].Flags |= 0x1000;

        Should.Throw<NotSupportedException>(() => phases.Manager.MinimizeExact(phases.Minimize, phases.Queue));
    }

    /// <remarks>
    /// **The rechecked array is walked from its last entry to its first**, each minimized and a frozen plain pair made
    /// invalid; invalidating one moves the last entry into its place, which the walk, already past it, does not revisit.
    /// </remarks>
    [Test]
    public void RecheckEveryPsi_ThreeRecheckedWithTheMiddleFrozen_WalksLastFirstAndInvalidatesTheMiddle()
    {
        Phases phases = new(3, rechecked: true);
        phases.FreezeOnMinimize.Add(phases.Pairs[1]);

        phases.Manager.RecheckEveryPsi(phases.Minimize, phases.Queue);

        phases.Minimized.ShouldBe([phases.Pairs[2], phases.Pairs[1], phases.Pairs[0]]);
        phases.Manager.Invalid.ShouldBe([phases.Pairs[1]]);
        phases.Manager.Rechecked.ShouldBe([phases.Pairs[0], phases.Pairs[2]]);
    }

    /// <remarks>
    /// **Phase 4 hands every exact pair to the scheduler head first**, reading the next link before each call — so a pair
    /// the scheduler files far, and so unfiles, does not end the walk.
    /// </remarks>
    [Test]
    public void ExamineExact_PairsTheSchedulerFilesFar_AreEachExaminedHeadFirst()
    {
        Phases phases = new(3);
        List<IvpMindist> examined = [];

        phases.Manager.ExamineExact(mindist =>
        {
            examined.Add(mindist);
            phases.Manager.Unlink(mindist, phases.Queue);
        });

        examined.ShouldBe([phases.Pairs[2], phases.Pairs[1], phases.Pairs[0]]);
        phases.Manager.Exact.ShouldBeEmpty();
    }

    /// <summary>A manager with exact pairs, and a minimize that records what it was handed.</summary>
    private sealed class Phases
    {
        private readonly IvpCollisionObject _first = new();
        private readonly IvpCollisionObject _second = new();

        public Phases(int count, bool rechecked = false, Func<IvpMindist>? make = null)
        {
            for (int index = 0; index < count; index++)
            {
                IvpMindist mindist = make?.Invoke() ?? new IvpMindist(
                    new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
                    new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
                    extraRadius: 0f);
                Manager.LinkExact(mindist, _first, _second);

                if (rechecked)
                {
                    Manager.AddRechecked(mindist);
                }

                Pairs.Add(mindist);
            }
        }

        public IvpMindistManager Manager { get; } = new();

        public IvpMinList<IvpMindist> Queue { get; } = new();

        public List<IvpMindist> Pairs { get; } = [];

        public List<IvpMindist> Minimized { get; } = [];

        public HashSet<IvpMindist> FreezeOnMinimize { get; } = [];

        public void Minimize(IvpMindist mindist)
        {
            Minimized.Add(mindist);

            if (FreezeOnMinimize.Contains(mindist))
            {
                mindist.Flags |= 0x4000;
            }
        }
    }
}
