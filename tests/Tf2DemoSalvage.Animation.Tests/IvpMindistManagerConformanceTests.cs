using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's mindist manager — the environment's <c>+0x20</c>: its exact list, its rechecked array, and each object's list of
/// exact synapse records — and a mindist's records as its objects' hull managers see them (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*): the
/// linking half of `FUN_1800977f0`, the unfiling `FUN_180098dd0`, and the records' listener slot 3 `FUN_1800975a0`.
/// </remarks>
public sealed class IvpMindistManagerConformanceTests
{
    /// <remarks>
    /// **Becoming exact sets the state bits and puts the mindist at the head of the exact list and each record at the head
    /// of its object's list**: flags `&amp; ~0x300000 | 0xc0000`, so `0x2401ff` becomes `0x0c01ff`, and the later of two
    /// mindists heads every list.
    /// </remarks>
    [Test]
    public void LinkExact_TwoMindists_HeadsEveryListWithTheLater()
    {
        IvpMindistManager manager = new();
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist earlier = NewMindist();
        IvpMindist later = NewMindist(flags: 0x2401FF);

        manager.LinkExact(earlier, first, second);
        manager.LinkExact(later, first, second);

        manager.Exact.ShouldBe([later, earlier]);
        first.Synapses.ShouldBe([later.HullRecord(0), earlier.HullRecord(0)]);
        second.Synapses.ShouldBe([later.HullRecord(1), earlier.HullRecord(1)]);
        later.Flags.ShouldBe(0x0C01FF);
    }

    /// <remarks>
    /// **Unfiling takes a queued mindist out of the time manager, off the exact list, and its records off their objects'
    /// lists**, leaving every other entry where it was.
    /// </remarks>
    [Test]
    public void Unlink_AQueuedExactMindist_LeavesTheQueueTheExactListAndItsObjects()
    {
        IvpMindistManager manager = new();
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMinList<IvpMindist> queue = new();
        IvpMindist other = NewMindist();
        IvpMindist mindist = NewMindist();
        manager.LinkExact(other, first, second);
        manager.LinkExact(mindist, first, second);
        mindist.QueueSlot = queue.Add(mindist, 1f);

        manager.Unlink(mindist, queue);

        queue.Count.ShouldBe(0);
        mindist.QueueSlot.ShouldBeNull();
        manager.Exact.ShouldBe([other]);
        first.Synapses.ShouldBe([other.HullRecord(0)]);
        second.Synapses.ShouldBe([other.HullRecord(1)]);
    }

    /// <remarks>
    /// **The rechecked array is searched from its end and the last entry moves into the unfiled one's place**: `[a, b, c]`
    /// without `a` is `[c, b]`, and without `b` then — the last — `[c]`.
    /// </remarks>
    [Test]
    public void Unlink_OneOfThreeRechecked_MovesTheLastIntoItsPlace()
    {
        IvpMindistManager manager = new();
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMinList<IvpMindist> queue = new();
        IvpMindist a = NewMindist();
        IvpMindist b = NewMindist();
        IvpMindist c = NewMindist();

        foreach (IvpMindist mindist in new[] { a, b, c })
        {
            manager.LinkExact(mindist, first, second);
            manager.AddRechecked(mindist);
        }

        manager.Unlink(a, queue);
        manager.Rechecked.ShouldBe([c, b]);

        manager.Unlink(b, queue);
        manager.Rechecked.ShouldBe([c]);
    }

    /// <remarks>
    /// **The engine would clear the exact list's head**: unlinking a mindist with no links writes its null next into the
    /// head. Every caller unfiles an exact mindist, so anything else is refused.
    /// </remarks>
    [Test]
    public void Unlink_AMindistNotOnTheExactList_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => new IvpMindistManager().Unlink(NewMindist(), new IvpMinList<IvpMindist>()));

    /// <remarks>
    /// **A record told of a rebase adds the value shift less the center shift, taken in float, to the mindist's `+0xa0`**
    /// (`SUBSS`, `CVTSS2SD`, `ADDSD`): `2^24 − (−1)` is a tie in float and rounds to `2^24`, where a double difference would
    /// be `2^24 + 1`.
    /// </remarks>
    [Test]
    public void Rebased_AMindistsRecord_AddsTheShiftsDifferenceInFloatToItsHullPastCenters()
    {
        IvpMindist mindist = NewMindist();
        mindist.HullPastCenters = 0.25d;

        mindist.HullRecord(1).Rebased(16777216f, -1f);

        mindist.HullPastCenters.ShouldBe(16777216.25d);
    }

    /// <remarks>
    /// **A record filed far hands its slot 1 to the filing's handler** (`FUN_180097570` finding the mindist from the
    /// record), with the shortfall it was told.
    /// </remarks>
    [Test]
    public void HullPassed_AFarFiledRecord_CallsItsFilingsHandler()
    {
        IvpMindistManager manager = new();
        IvpCollisionObject first = new() { MovementState = 1 };
        IvpCollisionObject second = new() { MovementState = 1 };
        IvpMindist mindist = NewMindist();
        List<(IvpMindist Mindist, float Overshoot)> told = [];
        IvpCoreBounds bounds = new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 1f, SurfaceSpeedBound: 0f);
        IvpMindistHull.FileFar(
            mindist,
            new IvpFarFiling(manager, first, second, (pair, overshoot) => told.Add((pair, overshoot))),
            bounds,
            bounds,
            now: 0d,
            gap: 1f);

        mindist.HullRecord(1).HullPassed(second.Hull, -2f);

        told.ShouldBe([(mindist, -2f)]);
    }

    /// <remarks>A record never filed far has no handler to hand its slot 1 to: refused.</remarks>
    [Test]
    public void HullPassed_ARecordNeverFiledFar_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => NewMindist().HullRecord(0).HullPassed(new IvpHullManager(), -1f));

    /// <remarks>
    /// **Invalidating an exact pair unfiles it and puts it and its records at the heads of the invalid lists** — the
    /// manager's `+0x28` and each object's `+0x48` — with the flags `&amp; ~0x340000 | 0x80000`.
    /// </remarks>
    [Test]
    public void Invalidate_AnExactMindist_HeadsTheInvalidListsAndLeavesTheExactOnes()
    {
        IvpMindistManager manager = new();
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist invalid = NewMindist();
        IvpMindist exact = NewMindist();
        manager.LinkExact(invalid, first, second);
        manager.LinkExact(exact, first, second);

        manager.Invalidate(invalid, first, second, new IvpMinList<IvpMindist>());

        manager.Exact.ShouldBe([exact]);
        manager.Invalid.ShouldBe([invalid]);
        first.Synapses.ShouldBe([exact.HullRecord(0)]);
        first.InvalidSynapses.ShouldBe([invalid.HullRecord(0)]);
        second.InvalidSynapses.ShouldBe([invalid.HullRecord(1)]);
        invalid.Flags.ShouldBe(0x80000);
    }

    private static IvpMindist NewMindist(int flags = 0) =>
        new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = flags,
        };
}
