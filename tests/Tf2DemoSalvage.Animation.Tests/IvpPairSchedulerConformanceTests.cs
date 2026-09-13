using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's pair scheduler — <c>FUN_180099380(mindist, removeFar, recheckMode)</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The scheduler's near branch and the dispatch into the search* and
/// *The event queue, and where the far branch hands a pair off*). A queued mindist leaves the queue; a pair past
/// `2.1` steps of its total bound plus the margin is far; a near pair is left alone when it is not closing, cannot close
/// its length this PSI, or holds the parked flag bits; otherwise its margin class decays every fourth look, its time of
/// impact runs, and an event is queued — at its time, or at a recheck when it is within a microsecond of now.
///
/// **The fixture**: record 0's core falls along `−Z` at fifty inches a second onto record 1's still core, the normal
/// `+Z`, so the closing speed is fifty; the length is `0.9`, one step is `0.015` from `now = 1` to the next PSI, and the
/// queue's base is `1`. Units are inches and seconds.
/// </remarks>
public sealed class IvpPairSchedulerConformanceTests
{
    private const double Now = 1d;

    private const double Step = 0.015d;

    private static readonly IvpSchedulerCore Falling = new(
        new IvpCoreBounds(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 50f, SurfaceSpeedBound: 0f),
        Velocity: (0f, 0f, -50f),
        RotationAxis: (1f, 0f, 0f));

    private static readonly IvpSchedulerCore Still = new(
        new IvpCoreBounds(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f),
        Velocity: (0f, 0f, 0f),
        RotationAxis: (1f, 0f, 0f));

    /// <remarks>
    /// **A queued mindist leaves the queue first, and a pair past `(double)(float)step · bound · 2.1f + margin` is far.**
    /// With a total bound of fifty that threshold is about `1.82`: a length of two is far, nothing is searched, and the
    /// queue is empty; a length of `0.9` is near and searched.
    /// </remarks>
    [TestCase(2f, true)]
    [TestCase(0.9f, false)]
    public void Examine_APairPastTwoPointOneStepsOfItsBound_IsFarAndLeavesTheQueue(float length, bool far)
    {
        Fixture fixture = new(length);
        fixture.Mindist.QueueSlot = fixture.Environment.Queue.Add(fixture.Mindist, 5f);

        IvpScheduleOutcome outcome = fixture.Examine(IvpRecheck.AtNow);

        (outcome == IvpScheduleOutcome.Far).ShouldBe(far);
        (fixture.Searches.Count == 0).ShouldBe(far);

        if (far)
        {
            fixture.Mindist.QueueSlot.ShouldBeNull();
            fixture.Environment.Queue.Count.ShouldBe(0);
        }
    }

    /// <remarks>
    /// **Asked to remove a far pair, the engine files each record with its object's hull manager**, the gap `(float)(length
    /// − margin)` split by speed: each side's speed is `+0x254 + +0x1dc + 1e-10f`, its weight its own speed plus a tenth of
    /// the other's, and its allowance the gap over both weights times its own — falling at fifty against a still core,
    /// fifty fifty-fifths of the gap for record 0 and five for record 1. Each key is the manager's value spread to now plus
    /// the allowance in double, and the mindist keeps the two hulls past their centers, `0.5 + 3.5`, at `+0xa0`.
    /// </remarks>
    [Test]
    public void Examine_AFarPairAskedToBeRemoved_FilesBothRecordsSplitBySpeed()
    {
        Fixture fixture = new(2f);
        fixture.LinkExact();
        IvpHullManager firstHull = fixture.FirstObject.Hull;
        firstHull.Time = Now - 0.5d;
        firstHull.Gradient = 4f;
        firstHull.CenterGradient = 1f;
        firstHull.Value = 3f;
        firstHull.CenterValue = 1f;
        IvpHullManager secondHull = fixture.SecondObject.Hull;
        secondHull.Time = Now;
        secondHull.Value = 2f;
        secondHull.CenterValue = 1.5f;

        fixture.Examine(IvpRecheck.AtNow, removeFar: true).ShouldBe(IvpScheduleOutcome.Filed);

        float gap = (float)((double)2f - (double)IvpCollisionTolerance.MarginFor(0));
        float firstSpeed = 50f + 1e-10f;
        float secondSpeed = 0f + 1e-10f;
        float firstWeight = (secondSpeed * 0.1f) + firstSpeed;
        float secondWeight = (firstSpeed * 0.1f) + secondSpeed;
        float share = gap / (secondWeight + firstWeight);

        (fixture.Mindist.Flags & 0x3C0000).ShouldBe(0x140000);
        firstHull.Synapses.Minimum.ShouldBe((float)(5d + (double)(share * firstWeight)));
        secondHull.Synapses.Minimum.ShouldBe((float)(2d + (double)(share * secondWeight)));
        fixture.Mindist.HullPastCenters.ShouldBe(4d);
        fixture.Mindist.HullRecord(0).HullSlot.ShouldBe(0);
        fixture.Mindist.HullRecord(1).HullSlot.ShouldBe(0);
    }

    /// <remarks>
    /// **A side whose object's byte `+0x78` has its low three bits clear takes no allowance, and the other side the whole
    /// gap** — record 0 checked first, so with both clear record 1 takes it.
    /// </remarks>
    [TestCase(0, 1, 0f, 1f)]
    [TestCase(8, 1, 0f, 1f)]
    [TestCase(1, 0, 1f, 0f)]
    [TestCase(0, 0, 0f, 1f)]
    public void Examine_AFarPairWithAStillSide_GivesTheOtherSideTheWholeGap(
        int firstState, int secondState, float firstShare, float secondShare)
    {
        Fixture fixture = new(2f);
        fixture.FirstObject.MovementState = firstState;
        fixture.SecondObject.MovementState = secondState;
        fixture.LinkExact();

        fixture.Examine(IvpRecheck.AtNow, removeFar: true);

        float gap = (float)((double)2f - (double)IvpCollisionTolerance.MarginFor(0));
        fixture.FirstObject.Hull.Synapses.Minimum.ShouldBe(gap * firstShare);
        fixture.SecondObject.Hull.Synapses.Minimum.ShouldBe(gap * secondShare);
        fixture.Mindist.HullPastCenters.ShouldBe(0d);
    }

    /// <remarks>
    /// **Filing a far pair first unfiles it** (`FUN_180098dd0`): off the exact list, its records off their objects' lists,
    /// and out of the rechecked array, the last entry moving into its place.
    /// </remarks>
    [Test]
    public void Examine_AFarPairAskedToBeRemoved_LeavesTheExactListsAndTheRecheckedArray()
    {
        Fixture fixture = new(2f);
        fixture.LinkExact();
        IvpMindist other = Fixture.NewMindist(0.9f);
        IvpMindist last = Fixture.NewMindist(0.9f);
        fixture.Manager.LinkExact(other, fixture.FirstObject, fixture.SecondObject);
        fixture.Manager.AddRechecked(fixture.Mindist);
        fixture.Manager.AddRechecked(other);
        fixture.Manager.AddRechecked(last);

        fixture.Examine(IvpRecheck.AtNow, removeFar: true);

        fixture.Manager.Exact.ShouldBe([other]);
        fixture.FirstObject.Synapses.ShouldBe([other.HullRecord(0)]);
        fixture.SecondObject.Synapses.ShouldBe([other.HullRecord(1)]);
        fixture.Manager.Rechecked.ShouldBe([last, other]);
    }

    /// <remarks>
    /// **A pair closing under the threshold is searched only if it is closing at all** — under `1e-19` it is left alone.
    /// At rest it is left alone; at one inch a second, under the threshold of twenty, it is searched — at a length of
    /// `0.2`, which one inch a second can still cover this PSI.
    /// </remarks>
    [TestCase(0f, false)]
    [TestCase(-1f, true)]
    public void Examine_AClosingSpeedUnderTheThreshold_IsSearchedOnlyIfClosing(float velocity, bool searched)
    {
        Fixture fixture = new(0.2f, first: Falling with { Velocity = (0f, 0f, velocity) });

        fixture.Examine(IvpRecheck.AtNow);

        (fixture.Searches.Count == 1).ShouldBe(searched);
    }

    /// <remarks>
    /// **A pair that cannot close its length before the next PSI is left alone**: `(float)(end − now) · 50 + margin` is
    /// about `0.99989`, which a length of one is not under and `0.9` is.
    /// </remarks>
    [TestCase(1f, false)]
    [TestCase(0.9f, true)]
    public void Examine_ALengthTheClosingSpeedCannotCoverThisPsi_IsLeftAlone(float length, bool searched)
    {
        Fixture fixture = new(length);

        IvpScheduleOutcome outcome = fixture.Examine(IvpRecheck.AtNow);

        (outcome == IvpScheduleOutcome.LeftAlone).ShouldBe(!searched);
        (fixture.Searches.Count == 1).ShouldBe(searched);
    }

    /// <remarks>**Flags whose `0x3000` bits are exactly `0x1000` are left alone**; `0x3000` is searched.</remarks>
    [TestCase(0x1000, false)]
    [TestCase(0x3000, true)]
    public void Examine_FlagsWhoseParkedBitsAreSet_AreLeftAlone(int bits, bool searched)
    {
        Fixture fixture = new(0.9f);
        fixture.Mindist.Flags |= bits;

        fixture.Examine(IvpRecheck.AtNow);

        (fixture.Searches.Count == 1).ShouldBe(searched);
    }

    /// <remarks>
    /// **A margin class over zero drops by one every fourth look**, before the search: the environment's counter goes
    /// 1, 2, 3, and on the fourth look its old value is over two, so the class is decremented and the counter zeroed. The
    /// search sees `3, 3, 3, 2`. A class of zero leaves the counter alone.
    /// </remarks>
    [Test]
    public void Examine_AMarginClassOverZero_DecaysOneClassEveryFourthLook()
    {
        Fixture fixture = new(0.9f);
        fixture.Mindist.Flags |= 3 << 22;

        for (int look = 0; look < 4; look++)
        {
            fixture.Examine(IvpRecheck.AtNow);
        }

        fixture.Searches.ConvertAll(search => search.Mindist.MarginClass).ShouldBe([3, 3, 3, 2]);
        fixture.Environment.MarginDecayCounter.ShouldBe(0);
        ((fixture.Mindist.Flags >> 22) & 0xFF).ShouldBe(2);

        Fixture unclassed = new(0.9f);
        unclassed.Examine(IvpRecheck.AtNow);
        unclassed.Environment.MarginDecayCounter.ShouldBe(0);
    }

    /// <remarks>
    /// **The search is handed the closing speed, the total bound and the PSI**, with synapse A's core the one the flags'
    /// bit 8 names. The closing speed is `√(1.001f − axisB·n²)·B+0x254 + √(1.001f − axisA·n²)·A+0x254 + (n·vB − n·vA)`:
    /// with A's axis along the normal, A's surface bound four and B's two, that is `√1.001·2 + √0.001·4 + 50`, where the
    /// cores exchanged would give `√1.001·4 + √0.001·2 + 50`. The total bound is `(A+0x1dc + (B+0x254 + A+0x254)) +
    /// B+0x1dc`, fifty-six. Record 1 holding the falling core behind bit 8 gives the same.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void Examine_ANearPair_HandsTheSearchTheClosingSpeedTotalBoundAndPsi(bool recordOneIsA)
    {
        IvpSchedulerCore falling = Falling with
        {
            Bounds = Falling.Bounds with { SurfaceSpeedBound = 4f },
            RotationAxis = (0f, 0f, 1f),
        };

        IvpSchedulerCore still = Still with { Bounds = Still.Bounds with { SurfaceSpeedBound = 2f } };

        Fixture fixture = recordOneIsA
            ? new(0.9f, first: still, second: falling, flags: 0x100)
            : new(0.9f, first: falling, second: still);

        fixture.Examine(IvpRecheck.AtNow);

        IvpImpactContext context = fixture.Searches[0].Context;
        double expected = (Math.Sqrt((double)1.001f) * 2d) + (Math.Sqrt((double)1.001f - 1d) * 4d) + 50d;

        context.ApproachSpeed.ShouldBe(expected, 1e-12d);
        context.TotalBound.ShouldBe(56d);
        context.Start.ShouldBe(Now);
        context.End.ShouldBe(Now + Step);
    }

    /// <remarks>No event from the search: nothing is queued.</remarks>
    [Test]
    public void Examine_ASearchRaisingNothing_QueuesNothing()
    {
        Fixture fixture = new(0.9f, impact: new IvpImpact(null, Now + Step));

        fixture.Examine(IvpRecheck.AtNow).ShouldBe(IvpScheduleOutcome.NoEvent);

        fixture.Environment.Queue.Count.ShouldBe(0);
        fixture.Mindist.QueueSlot.ShouldBeNull();
    }

    /// <remarks>
    /// **An event a microsecond or more after now is queued at its time**, relative to the queue's base and narrowed to
    /// float, its slot stored at `+0x8` and its kind in the flags' low byte.
    /// </remarks>
    [Test]
    public void Examine_AnEventLaterThanAMicrosecond_IsQueuedAtItsTime()
    {
        Fixture fixture = new(0.9f, impact: new IvpImpact(0x20, Now + 0.01d));

        fixture.Examine(IvpRecheck.AfterMiss).ShouldBe(IvpScheduleOutcome.Queued);

        fixture.Environment.Queue.Minimum.ShouldBe((float)0.01d, 1e-6f);
        fixture.Mindist.QueueSlot.ShouldNotBeNull();
        (fixture.Mindist.Flags & 0xFF).ShouldBe(0x20);
    }

    /// <remarks>**Within a microsecond of now, mode 0 queues the event at now.**</remarks>
    [Test]
    public void Examine_AnEventWithinAMicrosecondInModeZero_IsQueuedAtNow()
    {
        Fixture fixture = new(0.9f, impact: new IvpImpact(0x20, Now + 1e-7d));

        fixture.Examine(IvpRecheck.AtNow).ShouldBe(IvpScheduleOutcome.Queued);

        fixture.Environment.Queue.Minimum.ShouldBe(0f);
    }

    /// <remarks>
    /// **Within a microsecond of now, a recheck replaces the time**, from the gap `length − 0.1·d`, about `0.87501`, over a
    /// total bound of a hundred — at fifty, mode 2's recheck would fall past the step and be dropped: mode 1 on a collision
    /// kind is `(gap · 0.1f)/bound + now + 1e-7f · step`, about `0.000875012`; mode 2, or any kind with low bits set, is
    /// `gap/bound + now + 1e-4f · step`, about `0.0087516`, the step's share of `1.5e-6` being well over the tolerance.
    /// *The close recheck's `1e-7f · step`, `1.5e-9`, is under it and not pinned.*
    /// </remarks>
    [TestCase(IvpRecheck.AfterFeatureChange, 0x20, 0.000875012d)]
    [TestCase(IvpRecheck.AfterFeatureChange, 0x21, 0.0087516d)]
    [TestCase(IvpRecheck.AfterMiss, 0x20, 0.0087516d)]
    public void Examine_AnEventAtNowInARecheckMode_IsRequeuedFromTheGapOverTheBound(
        IvpRecheck recheck, int kind, double expected)
    {
        Fixture fixture = new(
            0.9f,
            first: Falling with { Bounds = Falling.Bounds with { LinearSpeed = 100f } },
            impact: new IvpImpact(kind, Now));

        fixture.Examine(recheck).ShouldBe(IvpScheduleOutcome.Queued);

        fixture.Environment.Queue.Minimum.ShouldBe((float)expected, 1e-7f);
    }

    /// <remarks>
    /// **A gap under `1e-12` metres rechecks at a fixed share of the step**: with the length exactly `0.1·d`, mode 1 on a
    /// collision kind is `now + 1e-5f · step`, and mode 2 `now + 0.001f · step`.
    /// </remarks>
    [TestCase(IvpRecheck.AfterFeatureChange, 1.5e-7d)]
    [TestCase(IvpRecheck.AfterMiss, 1.5e-5d)]
    public void Examine_ARecheckOfAGapUnderTheFloor_IsAShareOfTheStep(IvpRecheck recheck, double expected)
    {
        Fixture fixture = new(IvpCollisionTolerance.Epsilon, impact: new IvpImpact(0x20, Now));

        fixture.Examine(recheck).ShouldBe(IvpScheduleOutcome.Queued);

        fixture.Environment.Queue.Minimum.ShouldBe((float)expected, 1e-8f);
    }

    /// <remarks>
    /// **A recheck at or past the next PSI is dropped.** With a total bound of twenty-one — still near, since `0.015 · 21 ·
    /// 2.1 + margin` is about `0.911` — mode 2's `gap/bound` is about `0.042`, past the step.
    /// </remarks>
    [Test]
    public void Examine_ARecheckPastTheNextPsi_IsDropped()
    {
        Fixture fixture = new(
            0.9f,
            first: Falling with { Bounds = Falling.Bounds with { LinearSpeed = 21f } },
            impact: new IvpImpact(0x20, Now));

        fixture.Examine(IvpRecheck.AfterMiss).ShouldBe(IvpScheduleOutcome.Dropped);

        fixture.Environment.Queue.Count.ShouldBe(0);
    }

    /// <summary>One pair, its environment, and a search that records what it was handed.</summary>
    private sealed class Fixture
    {
        private readonly IvpSchedulerCore _first;
        private readonly IvpSchedulerCore _second;
        private readonly IvpImpact _impact;

        public Fixture(
            float length,
            IvpSchedulerCore? first = null,
            IvpSchedulerCore? second = null,
            int flags = 0,
            IvpImpact? impact = null)
        {
            _first = first ?? Falling;
            _second = second ?? Still;
            _impact = impact ?? new IvpImpact(null, Now + Step);

            Mindist = NewMindist(length, flags);

            Environment = new IvpSchedulerEnvironment
            {
                Step = Step,
                Now = Now,
                NextPsi = Now + Step,
                ClosingSpeedThreshold = 20f,
                Queue = new IvpMinList<IvpMindist>(),
                QueueBase = Now,
            };
        }

        public IvpMindist Mindist { get; }

        public IvpSchedulerEnvironment Environment { get; }

        public IvpMindistManager Manager { get; } = new();

        /// <summary>Record 0's object, moving.</summary>
        public IvpCollisionObject FirstObject { get; } = new() { MovementState = 1 };

        /// <summary>Record 1's object, moving.</summary>
        public IvpCollisionObject SecondObject { get; } = new() { MovementState = 1 };

        public List<(IvpImpactContext Context, IvpMindistState Mindist)> Searches { get; } = [];

        public static IvpMindist NewMindist(float length, int flags = 0) =>
            new(
                new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
                new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
                extraRadius: 0f)
            {
                Flags = flags,
                Length = length,
                Normal = (0f, 0f, 1f),
            };

        /// <summary>Makes the pair exact, as every caller that asks for a far pair's removal has.</summary>
        public void LinkExact() => Manager.LinkExact(Mindist, FirstObject, SecondObject);

        public IvpScheduleOutcome Examine(IvpRecheck recheck, bool removeFar = false) =>
            IvpPairScheduler.Examine(
                Mindist,
                _first,
                _second,
                Environment,
                removeFar ? new IvpFarFiling(Manager, FirstObject, SecondObject, (_, _) => { }) : null,
                recheck,
                (context, mindist) =>
                {
                    Searches.Add((context, mindist));
                    return _impact;
                });
    }
}
