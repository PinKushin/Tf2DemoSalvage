using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A far pair told its hull passed, and a pair becoming exact — <c>FUN_180097f00</c> and <c>FUN_1800977f0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*).
///
/// **The pass fixture**: synapse A's body sits ten metres up the normal and falls at two metres a second, last stepped
/// half a second ago, so at `now = 1` it is at nine; synapse B's is still at the origin. A's hull has gradients `3` and `2`
/// and values `1` and `0.5` from half a second ago, so it is `1` past its center; B's is at rest. The mindist was filed
/// `10` apart with `0.25` past the centers, and is `20` long.
/// </remarks>
public sealed class IvpMindistHullConformanceTests
{
    private const double Now = 1d;

    private const double Step = 0.015d;

    /// <remarks>
    /// **A pair still over six steps of its speeds from touching is filed again with what it has left, shared by speed.**
    /// The hull past the centers grew by `1 − 0.25` and the bodies closed by `10 − 9`, so the length is `20 − 0.75 − 1 =
    /// 18.25`; with the shortfall of `−0.5` that leaves `17.75` over speeds `3 + 1`, each side taking `4.4375` times its
    /// speed: A's key is its value at now, `2.5`, plus `13.3125`, and B's `0` plus `4.4375`. The mindist keeps the hull past
    /// the centers, the distance along the normal and the length.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void HullPassed_APairStillFar_RefilesBothRecordsWithWhatIsLeftSharedBySpeed(bool recordOneIsA)
    {
        PassFixture fixture = new(20f, recordOneIsA);

        fixture.Run(-0.5f).ShouldBe(IvpHullPassOutcome.Refiled);

        fixture.Mindist.HullPastCenters.ShouldBe(1d);
        fixture.Mindist.ContactDot.ShouldBe(9f);
        fixture.Mindist.Length.ShouldBe(18.25f);
        fixture.Moving.Hull.Synapses.Minimum.ShouldBe(15.8125f);
        fixture.Moving.Hull.Synapses.Count.ShouldBe(1);
        fixture.Still.Hull.Synapses.Minimum.ShouldBe(4.4375f);
        fixture.HandedOff.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A pair whose shortfall plus new length is not over `(float)step · speeds · 6.0` is unfiled and handed off**, its
    /// fields left as they were: at a length of `2.1` what is left is about `−0.15`, under `0.36`.
    /// </remarks>
    [Test]
    public void HullPassed_WithinSixStepsOfItsSpeeds_UnfilesBothRecordsAndHandsThePairOff()
    {
        PassFixture fixture = new(2.1f);

        fixture.Run(-0.5f).ShouldBe(IvpHullPassOutcome.HandedOff);

        fixture.HandedOff.ShouldBe([fixture.Mindist]);
        fixture.Moving.Hull.Synapses.Count.ShouldBe(0);
        fixture.Still.Hull.Synapses.Count.ShouldBe(0);
        fixture.Mindist.HullRecord(0).HullSlot.ShouldBeNull();
        fixture.Mindist.HullRecord(1).HullSlot.ShouldBeNull();
        fixture.Mindist.Length.ShouldBe(2.1f);
        fixture.Mindist.ContactDot.ShouldBe(10f);
        fixture.Mindist.HullPastCenters.ShouldBe(0.25d);
    }

    /// <remarks>
    /// **The threshold is six steps of the speeds**, `(double)0.015f · 4 · 6`, about `0.36`: a length of `2.6` leaves about
    /// `0.35` and is handed off, `2.65` leaves about `0.40` and is filed again — so a factor outside roughly `5.8` to `6.7`
    /// would move one of them.
    /// </remarks>
    [TestCase(2.6f, IvpHullPassOutcome.HandedOff)]
    [TestCase(2.65f, IvpHullPassOutcome.Refiled)]
    public void HullPassed_LengthsEitherSideOfSixSteps_AreHandedOffOrRefiled(float length, IvpHullPassOutcome outcome) =>
        new PassFixture(length).Run(-0.5f).ShouldBe(outcome);

    /// <remarks>**Flags holding either of `0x30000`'s bits hand the pair off without measuring it**, however far it is.</remarks>
    [TestCase(0x10000)]
    [TestCase(0x20000)]
    public void HullPassed_FlagsHoldingBitsOf0x30000_HandTheFarPairOffAtOnce(int bits)
    {
        PassFixture fixture = new(20f, flags: IvpMindistHull.FiledState | bits);

        fixture.Run(-0.5f).ShouldBe(IvpHullPassOutcome.HandedOff);

        fixture.Mindist.Length.ShouldBe(20f);
        fixture.HandedOff.ShouldBe([fixture.Mindist]);
    }

    /// <remarks>
    /// **The recursive state, `0x100000`, goes to `FUN_1800b28a0` on the flags alone**, so a plain mindist carrying it is refused
    /// rather than measured as a far pair (`IvpRecursiveMindistConformanceTests` sends a larger one).
    /// </remarks>
    [Test]
    public void HullPassed_APlainMindistInTheRecursiveState_Throws() =>
        Should.Throw<InvalidOperationException>(() => new PassFixture(20f, flags: 0x100000).Run(-0.5f));

    /// <remarks>
    /// **A phantom's pair, flags `0x3000` exactly `0x1000`, is handed to `FUN_180097940`**, which is not ported: refused
    /// when it would be handed off, and filed again as any pair when it would not.
    /// </remarks>
    [TestCase(2.1f, true)]
    [TestCase(20f, false)]
    public void HullPassed_APhantomsPair_IsRefusedOnlyAtTheHandoff(float length, bool refused)
    {
        PassFixture fixture = new(length, flags: IvpMindistHull.FiledState | 0x1000);

        if (refused)
        {
            Should.Throw<NotSupportedException>(() => fixture.Run(-0.5f));
        }
        else
        {
            fixture.Run(-0.5f).ShouldBe(IvpHullPassOutcome.Refiled);
        }
    }

    /// <remarks>
    /// **The pass speed floor is `1e-19`, `DAT_1800f4f20`** (<see cref="IvpMindistHull"/> remarks): with both cores
    /// entirely still, `speedA` and `speedB` are the floor alone, so `speeds = 2e-19` and the six-step threshold
    /// (`(float)step · speeds · 6`, step `1`) is `1.2e-18`. A length of `1e-17` clears that and is refiled. The
    /// inches-scaled floor, `3.937e-18`, raises the threshold to `4.7244e-17`, which the same length does not
    /// clear, so a mutant multiplying the floor by <see cref="IvpTransform.InchesPerMetre"/> hands the pair off
    /// instead.
    /// </remarks>
    [Test]
    public void HullPassed_BothCoresStillAtTheSpeedFloor_RefilesAtALengthTheInchesScaledFloorWouldHandOff()
    {
        IvpMindist mindist = NewMindist(IvpMindistHull.FiledState);
        mindist.Normal = (0f, 0f, 1f);
        mindist.Length = 1e-17f;

        IvpCollisionObject first = new() { MovementState = 1 };
        IvpCollisionObject second = new() { MovementState = 1 };
        IvpRigidBody firstBody = new() { LastStepped = 1d };
        IvpRigidBody secondBody = new() { LastStepped = 1d };

        first.Hull.Install(mindist.HullRecord(0), 1d, 0d);
        second.Hull.Install(mindist.HullRecord(1), 1d, 0d);

        IvpCoreBounds still = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f);
        List<IvpMindist> handedOff = [];

        IvpHullPassOutcome outcome = IvpMindistHull.HullPassed(
            mindist,
            overshoot: 0f,
            new IvpHullPass
            {
                Now = 1d,
                Step = 1d,
                First = first,
                Second = second,
                FirstBody = firstBody,
                SecondBody = secondBody,
                FirstBounds = still,
                SecondBounds = still,
                HandOff = handedOff.Add,
                Recheck = _ => throw new InvalidOperationException("A plain far pair is not rechecked."),
            });

        outcome.ShouldBe(IvpHullPassOutcome.Refiled);
        mindist.Length.ShouldBe(1e-17f);
        handedOff.ShouldBeEmpty();
    }

    /// <remarks>
    /// **Each side's speed floor is `1e-10` metres a second, carried directly**: with record 0 at a nanometre a second and
    /// record 1 still, the split is about `0.841 : 0.159`.
    /// </remarks>
    [Test]
    public void SplitGap_SpeedsNearTheFloor_AddTheFloorInMetres()
    {
        IvpCoreBounds creeping = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 1e-9f, SurfaceSpeedBound: 0f);
        IvpCoreBounds still = creeping with { LinearSpeed = 0f };

        (float first, float second) = IvpMindistHull.SplitGap(1f, creeping, still);

        first.ShouldBe(0.8409f, 0.001f);
        second.ShouldBe(0.1591f, 0.001f);
    }

    /// <remarks>
    /// **An opened mindist's records are filed at the next PSI, a side at rest taking the split's floor** — `FUN_180097d60` tests
    /// record 0's object's `+0x78 &amp; 7` first and hands `FUN_180097e20` `1e-10f` for that side, never zero, and the gap for the
    /// other, each added in float to its manager's next PSI value (`docs/findings/51`, *The larger mindist in full*). With both
    /// next PSI values at zero, the side at rest's key is `1e-10f` itself, which `FileFar`'s zero would not give. Both objects at
    /// rest take the first branch.
    /// </remarks>
    [TestCase(0, 1)]
    [TestCase(0, 0)]
    public void FileRecursive_ARecordZeroObjectAtRest_TakesTheFloorAndGivesTheGapToRecordOne(int firstState, int secondState)
    {
        RecursiveFiling filing = new(firstState, secondState);

        filing.Run(0.5f);

        filing.KeyOf(0).ShouldBe(1e-10f);
        filing.KeyOf(1).ShouldBe(0.5f);
    }

    /// <remarks>The mirror: record 0's object moving and record 1's at rest, so record 1 takes the floor and record 0 the gap.</remarks>
    [Test]
    public void FileRecursive_ARecordOneObjectAtRest_TakesTheFloorAndGivesTheGapToRecordZero()
    {
        RecursiveFiling filing = new(1, 0);

        filing.Run(0.5f);

        filing.KeyOf(0).ShouldBe(0.5f);
        filing.KeyOf(1).ShouldBe(1e-10f);
    }

    /// <remarks>
    /// **Both moving, the gap is split by speed** — `FUN_180097d60`'s split is <see cref="IvpMindistHull.SplitGap"/>'s, operand for
    /// operand — and each share lands on its manager's next PSI value, here `2` and `3`. The flags become filed, the other bits kept:
    /// `0xc0100 &amp; ~0x280000 | 0x140000` is `0x140100`. Either record's slot 1 hands the mindist to the filing's handler.
    /// </remarks>
    [Test]
    public void FileRecursive_BothObjectsMoving_SplitsTheGapBySpeedAtTheNextPsi()
    {
        RecursiveFiling filing = new(1, 1, firstNext: 2f, secondNext: 3f);

        filing.Run(0.5f);

        (float firstShare, float secondShare) = IvpMindistHull.SplitGap(0.5f, filing.FirstBounds, filing.SecondBounds);

        filing.KeyOf(0).ShouldBe(2f + firstShare);
        filing.KeyOf(1).ShouldBe(3f + secondShare);
        filing.Mindist.Flags.ShouldBe(0x140100);

        filing.Mindist.HullRecord(0).HullPassed(filing.First.Hull, -2f);
        filing.Mindist.HullRecord(1).HullPassed(filing.Second.Hull, -1f);

        filing.Passed.ShouldBe([(filing.Mindist, -2f), (filing.Mindist, -1f)]);
    }

    /// <remarks>
    /// **Becoming exact links the pair, minimizes it, and — the minimize settling — examines it**, the state bits set to
    /// exact.
    /// </remarks>
    [Test]
    public void BecomeExact_ASettledMinimize_LinksThePairAndExaminesIt()
    {
        HandoffFixture fixture = new();

        fixture.Run().ShouldBe(IvpExactOutcome.Examined);

        fixture.Manager.Exact.ShouldBe([fixture.Mindist]);
        fixture.First.Synapses.ShouldBe([fixture.Mindist.HullRecord(0)]);
        fixture.Second.Synapses.ShouldBe([fixture.Mindist.HullRecord(1)]);
        fixture.Minimized.ShouldBe(1);
        (fixture.Mindist.Flags & IvpMindistHull.StateMask).ShouldBe(IvpMindistHull.ExactState);
    }

    /// <remarks>
    /// **The scheduler is asked to remove a far pair when both cores' state bytes ORed are under `0x21`** (`CMP`, `SETC`):
    /// `0x10 | 0x01` is, `0x20 | 0x01` is not.
    /// </remarks>
    [TestCase(0x10, 0x01, true)]
    [TestCase(0x20, 0x00, true)]
    [TestCase(0x20, 0x01, false)]
    [TestCase(0xFF, 0x00, false)]
    public void BecomeExact_TheCoresStateBytes_AskForRemovalOnlyWhenTheirOrIsUnder0x21(int first, int second, bool removeFar)
    {
        HandoffFixture fixture = new();

        fixture.Run(firstState: first, secondState: second);

        fixture.Examined.ShouldBe([removeFar]);
    }

    /// <remarks>**A pair either of whose cores has its `+0x58` set is appended to the rechecked array.**</remarks>
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void BecomeExact_ACoreRecheckedEveryPsi_AppendsThePairToTheRecheckedArray(bool first, bool second, bool rechecked)
    {
        HandoffFixture fixture = new();

        fixture.Run(firstRechecked: first, secondRechecked: second);

        fixture.Manager.Rechecked.Count.ShouldBe(rechecked ? 1 : 0);
    }

    /// <remarks>
    /// **A minimize leaving bits of `0xc000` makes the pair invalid** (the mindist's `+0x38`, `FUN_180097440`): unfiled —
    /// out of the rechecked array it was just appended to — the flags `&amp; ~0x340000 | 0x80000`, `0xc4000` becoming
    /// `0x84000`, and the pair and its records at the heads of the invalid lists. It is not examined.
    /// </remarks>
    [Test]
    public void BecomeExact_AFrozenMinimize_MovesThePairToTheInvalidLists()
    {
        HandoffFixture fixture = new() { MinimizeSets = 0x4000 };

        fixture.Run(firstRechecked: true).ShouldBe(IvpExactOutcome.Frozen);

        fixture.Manager.Exact.ShouldBeEmpty();
        fixture.Manager.Invalid.ShouldBe([fixture.Mindist]);
        fixture.Manager.Rechecked.ShouldBeEmpty();
        fixture.First.Synapses.ShouldBeEmpty();
        fixture.First.InvalidSynapses.ShouldBe([fixture.Mindist.HullRecord(0)]);
        fixture.Second.InvalidSynapses.ShouldBe([fixture.Mindist.HullRecord(1)]);
        fixture.Examined.ShouldBeEmpty();
        fixture.Mindist.Flags.ShouldBe(0x84000);
    }

    /// <remarks>
    /// **A frozen minimize hands the pair to its OWN slot 7** — `FUN_1800977f0` calls the mindist's `+0x38` with the manager at
    /// `180097914` — so a larger mindist frozen at birth opens its ledge rather than going invalid, and the plain invalidation
    /// does not run.
    /// </remarks>
    [Test]
    public void BecomeExact_AFrozenMinimizeOfItsOwnKind_FreezesThroughItsOwnSlotSeven()
    {
        RecordingMindist own = new() { Flags = IvpMindistHull.FiledState };
        HandoffFixture fixture = new(own) { MinimizeSets = 0x4000 };

        fixture.Run().ShouldBe(IvpExactOutcome.Frozen);

        own.Slots.ShouldBe(["freeze"]);
        own.FrozenBy.ShouldBeSameAs(fixture.Manager);
        fixture.Manager.Exact.ShouldBe([own], ignoreOrder: false, customMessage: "the plain invalidation did not run");
        fixture.Examined.ShouldBeEmpty();
    }

    private static IvpMindist NewMindist(int flags) =>
        new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = flags,
        };

    /// <summary>A filed pair, its two objects and bodies, and a handoff that records what it was handed.</summary>
    private sealed class PassFixture
    {
        private readonly IvpRigidBody _movingBody = new()
        {
            Position = (0d, 0d, 10d),
            PreviousVelocity = (0f, 0f, -2f),
            LastStepped = 0.5d,
        };

        private readonly IvpRigidBody _stillBody = new() { LastStepped = Now };

        private readonly bool _recordOneIsA;

        public PassFixture(float length, bool recordOneIsA = false, int flags = IvpMindistHull.FiledState)
        {
            _recordOneIsA = recordOneIsA;
            Mindist = NewMindist(flags | (recordOneIsA ? 0x100 : 0));
            Mindist.Length = length;
            Mindist.Normal = (0f, 0f, 1f);
            Mindist.ContactDot = 10f;
            Mindist.HullPastCenters = 0.25d;

            Moving.Hull.Time = 0.5d;
            Moving.Hull.Gradient = 3f;
            Moving.Hull.CenterGradient = 2f;
            Moving.Hull.Value = 1f;
            Moving.Hull.CenterValue = 0.5f;

            Moving.Hull.Install(Mindist.HullRecord(recordOneIsA ? 1 : 0), 0.5d, 0d);
            Still.Hull.Install(Mindist.HullRecord(recordOneIsA ? 0 : 1), 0d, 0d);
        }

        public IvpMindist Mindist { get; }

        /// <summary>Synapse A's object.</summary>
        public IvpCollisionObject Moving { get; } = new() { MovementState = 1 };

        /// <summary>Synapse B's object.</summary>
        public IvpCollisionObject Still { get; } = new() { MovementState = 1 };

        public List<IvpMindist> HandedOff { get; } = [];

        public IvpHullPassOutcome Run(float overshoot)
        {
            IvpCoreBounds moving = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 2f, SurfaceSpeedBound: 1f);
            IvpCoreBounds still = moving with { LinearSpeed = 1f, SurfaceSpeedBound = 0f };

            return IvpMindistHull.HullPassed(
                Mindist,
                overshoot,
                new IvpHullPass
                {
                    Now = Now,
                    Step = Step,
                    First = _recordOneIsA ? Still : Moving,
                    Second = _recordOneIsA ? Moving : Still,
                    FirstBody = _recordOneIsA ? _stillBody : _movingBody,
                    SecondBody = _recordOneIsA ? _movingBody : _stillBody,
                    FirstBounds = _recordOneIsA ? still : moving,
                    SecondBounds = _recordOneIsA ? moving : still,
                    HandOff = HandedOff.Add,
                    Recheck = _ => throw new InvalidOperationException("A plain far pair is not rechecked."),
                });
        }
    }

    /// <summary>An opened pair about to be filed at the next PSI, two objects in given states, and a handler that records its calls.</summary>
    private sealed class RecursiveFiling
    {
        public RecursiveFiling(int firstState, int secondState, float firstNext = 0f, float secondNext = 0f)
        {
            First = new IvpCollisionObject { MovementState = firstState };
            Second = new IvpCollisionObject { MovementState = secondState };
            First.Hull.NextPsiValue = firstNext;
            Second.Hull.NextPsiValue = secondNext;
        }

        public IvpMindist Mindist { get; } = NewMindist(IvpMindistHull.ExactState | 0x100);

        public IvpCollisionObject First { get; }

        public IvpCollisionObject Second { get; }

        public IvpCoreBounds FirstBounds { get; } = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 2f, SurfaceSpeedBound: 1f);

        public IvpCoreBounds SecondBounds { get; } = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0.5f, SurfaceSpeedBound: 0f);

        public List<(IvpMindist Mindist, float Overshoot)> Passed { get; } = [];

        public void Run(float gap) =>
            IvpMindistHull.FileRecursive(
                Mindist,
                new IvpFarFiling(new IvpMindistManager(), First, Second, (mindist, overshoot) => Passed.Add((mindist, overshoot))),
                FirstBounds,
                SecondBounds,
                gap);

        public float KeyOf(int record)
        {
            IvpHullManager hull = record == 0 ? First.Hull : Second.Hull;

            return hull.Synapses.ValueOf(Mindist.HullRecord(record).HullSlot.ShouldNotBeNull());
        }
    }

    /// <summary>A pair about to become exact, and a minimize and scheduler that record what they were asked.</summary>
    private sealed class HandoffFixture
    {
        public HandoffFixture()
            : this(NewMindist(IvpMindistHull.FiledState))
        {
        }

        public HandoffFixture(IvpMindist mindist) => Mindist = mindist;

        public IvpMindistManager Manager { get; } = new();

        public IvpCollisionObject First { get; } = new();

        public IvpCollisionObject Second { get; } = new();

        public IvpMindist Mindist { get; }

        public List<bool> Examined { get; } = [];

        public int Minimized { get; private set; }

        /// <summary>The flag bits the minimize leaves set.</summary>
        public int MinimizeSets { get; init; }

        public IvpExactOutcome Run(
            bool firstRechecked = false, bool secondRechecked = false, int firstState = 0, int secondState = 0) =>
            IvpMindistHull.BecomeExact(
                Mindist,
                new IvpExactHandoff
                {
                    Manager = Manager,
                    First = First,
                    Second = Second,
                    Queue = new IvpMinList<IIvpTimeEvent>(),
                    FirstRechecked = firstRechecked,
                    SecondRechecked = secondRechecked,
                    FirstCoreState = firstState,
                    SecondCoreState = secondState,
                    Minimize = mindist =>
                    {
                        Minimized++;
                        mindist.Flags |= MinimizeSets;
                    },
                    Examine = Examined.Add,
                });
    }
}
