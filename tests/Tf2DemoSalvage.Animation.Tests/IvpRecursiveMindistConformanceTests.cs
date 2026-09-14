using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's larger mindist — <c>FUN_1800b21f0</c>'s table: slot 0 <c>FUN_1800b2250</c>, slot 7 <c>FUN_1800b2700</c>, slot 8
/// <c>FUN_1800b2460</c>, the hull-passed handler <c>FUN_1800b28a0</c>, the refresh <c>FUN_1800b29b0</c> and its delegator (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The larger mindist in full*). Each object's tree is a hull ledge over two
/// leaves, read through <see cref="PhysicsHull.Tree"/>; the hull's one triangle takes the header and middle-edge bit 31 a case asks
/// for, the other two edges the opposite, so a read of the wrong word or slot answers differently. The surface managers hand back
/// the leaves and record the ledge each query started beneath.
/// </remarks>
public sealed class IvpRecursiveMindistConformanceTests
{
    /// <remarks>
    /// **Either ledge with children makes the larger mindist, and both kinds then take the same writes** — `FUN_180096680` calls
    /// `FUN_1800b21f0` for a ledge whose `+0x8 &amp; 3` is set, and both paths reach `FUN_1800975d0` at `180096cc7`.
    /// </remarks>
    [TestCase(1, true)]
    [TestCase(0, false)]
    public void Refresh_ALedgeWithChildren_BuildsALargerMindist(int children, bool larger)
    {
        Pair pair = new(firstChildren: children);
        List<IvpCollision> made = [];

        IvpPairMindists.Refresh(
            pair.First, pair.Second, 1d, made, pair.FirstTree.Root, pair.SecondTree.Root.Left.ShouldNotBeNull(), null, null, pair.Outer);

        IvpCollision collision = made.ShouldHaveSingleItem();
        IvpMindist mindist = collision.ShouldBeAssignableTo<IvpMindist>().ShouldNotBeNull();

        (collision is IvpRecursiveMindist).ShouldBe(larger);
        mindist.Flags.ShouldBe(0x0fc00000);
        mindist.Delegator.ShouldBeSameAs(pair.Outer);
        pair.Created.ShouldBe([mindist]);
    }

    /// <remarks>
    /// **Record 0's ledge without children opens record 1; else record 1's without opens record 0; else the smaller node opens the
    /// other** — `+0xf8 = rA ≤ rB` by `COMISS` and `SETBE`, so a tie and a NaN open record 1. The radii in the children cases say
    /// the opposite of the answer, so a test of the radii first reddens.
    /// </remarks>
    [TestCase(0, 1, 2f, 1f, 1)]
    [TestCase(1, 0, 1f, 2f, 0)]
    [TestCase(0, 0, 2f, 1f, 1)]
    [TestCase(1, 1, 1f, 2f, 1)]
    [TestCase(1, 1, 2f, 1f, 0)]
    [TestCase(1, 1, 2f, 2f, 1)]
    [TestCase(1, 1, float.NaN, 1f, 1)]
    [TestCase(1, 1, 1f, float.NaN, 1)]
    public void Freeze_TheLedgesChildrenAndNodeRadii_ChooseTheSideOpened(
        int firstChildren, int secondChildren, float firstRadius, float secondRadius, int side)
    {
        Pair pair = new(firstChildren, secondChildren, firstRadius, secondRadius);

        pair.Freeze();

        pair.Mindist.OpenSide.ShouldBe(side);
    }

    /// <remarks>
    /// **A ledge whose `+0x4` is zero names no node, and is taken as `1e15f` across** (`DAT_1800fea24`): the named side's radius a
    /// decade either way of it decides both orders.
    /// </remarks>
    [TestCase(false, 1e16f, 1)]
    [TestCase(false, 1e14f, 0)]
    [TestCase(true, 1e16f, 0)]
    [TestCase(true, 1e14f, 1)]
    public void Freeze_ALedgeNamingNoNode_TakesARadiusOf1e15(bool secondNamesNone, float namedRadius, int side)
    {
        Pair pair = new(
            1,
            1,
            firstRadius: secondNamesNone ? namedRadius : 1f,
            secondRadius: secondNamesNone ? 1f : namedRadius,
            firstNamesNode: secondNamesNone,
            secondNamesNode: !secondNamesNone);

        pair.Freeze();

        pair.Mindist.OpenSide.ShouldBe(side);
    }

    /// <remarks>
    /// **Opening unfiles the pair, files both records at the next PSI with the ranges' sum split by speed, marks it recursive, and
    /// refreshes beneath the open side** — that side queried beneath its own ledge, the other handed its ledge — telling the count
    /// through the delegator's slot 2. The flags go `0x0fcc0100` to `0x0fd00100`, and a record's slot 1 reaches the environment's
    /// handler. Slot 7 does not count a refresh at `env+0xbc`.
    /// </remarks>
    [TestCase(1, 0, 0)]
    [TestCase(0, 1, 1)]
    public void Freeze_OpeningASide_FilesAtTheNextPsiAndRefreshesBeneathItsLedge(int firstChildren, int secondChildren, int side)
    {
        Pair pair = new(firstChildren, secondChildren);
        pair.First.Hull.NextPsiValue = 0.5f;
        pair.Second.Hull.NextPsiValue = 0.25f;

        pair.Freeze();

        (double firstRange, double secondRange) = pair.Ranges();
        (float firstShare, float secondShare) = IvpMindistHull.SplitGap(
            (float)(firstRange + secondRange), IvpRangeManager.Bounds(pair.FirstCore), IvpRangeManager.Bounds(pair.SecondCore));
        PhysicsLedgeTree openTree = side == 0 ? pair.FirstTree : pair.SecondTree;
        PhysicsLedgeTree otherTree = side == 0 ? pair.SecondTree : pair.FirstTree;
        List<IvpMindist> children = [.. pair.Mindist.Children.Cast<IvpMindist>()];

        pair.Environment.MindistManager.Exact.ShouldBeEmpty();
        pair.Mindist.Flags.ShouldBe(0x0fd00100);
        pair.KeyOf(0).ShouldBe(0.5f + firstShare);
        pair.KeyOf(1).ShouldBe(0.25f + secondShare);
        (side == 0 ? pair.FirstSurface : pair.SecondSurface).Roots.ShouldBe([openTree.Root]);
        (side == 0 ? pair.SecondSurface : pair.FirstSurface).Roots.ShouldBeEmpty();
        children.Select(child => child.Ledge(side)).ShouldBe([openTree.Root.Left, openTree.Root.Right]);
        children.Select(child => child.Ledge(1 - side)).ShouldBe([otherTree.Root, otherTree.Root]);
        children.ShouldAllBe(child => child.Delegator == pair.Mindist.ChildDelegator);
        pair.Mindist.Total.ShouldBe(2);
        pair.Outer.Told.ShouldBe([("added", 2)]);
        pair.Environment.WatcherRefreshes.ShouldBe(0);

        pair.Mindist.HullRecord(1).HullPassed(pair.Second.Hull, -1f);

        pair.Passed.ShouldBe([(pair.Mindist, -1f)]);
    }

    /// <remarks>
    /// **Past `DAT_18012d66c` (1000) mindists beneath the outermost larger mindist, slot 7 is a plain mindist's** — `FUN_180098dd0`
    /// then `FUN_180097ce0` — and at 1000 it still opens (`JLE`).
    /// </remarks>
    [TestCase(1000, false)]
    [TestCase(1001, true)]
    public void Freeze_TheOutermostCountAgainstTheLimit_GoesInvalidOnlyPastIt(int count, bool invalid)
    {
        Pair pair = new(outerAnswer: count);

        pair.Freeze();

        pair.Environment.MindistManager.Exact.ShouldBeEmpty();

        if (invalid)
        {
            pair.Environment.MindistManager.Invalid.ShouldBe([pair.Mindist]);
            pair.First.InvalidSynapses.ShouldBe([pair.Mindist.HullRecord(0)]);
            (pair.Mindist.Flags & IvpMindistHull.StateMask).ShouldBe(IvpMindistHull.InvalidState);
            pair.Mindist.OpenSide.ShouldBe(-1);
            pair.FirstSurface.Roots.ShouldBeEmpty();
        }
        else
        {
            pair.Mindist.OpenSide.ShouldBe(0);
            pair.Mindist.Children.Count.ShouldBe(2);
        }
    }

    /// <remarks>
    /// **An outer count not above zero is answered with the mindist's own total** (`FUN_1800b2860`), and the watcher's slots —
    /// the interface's defaults, a bare `RET` and −1 — make its own total the one that counts.
    /// </remarks>
    [TestCase(false, 0)]
    [TestCase(true, -1)]
    public void MindistsBeneath_AnOuterCountNotAboveZero_AnswersTheOwnTotal(bool bare, int outerAnswer)
    {
        Pair pair = new(outerAnswer: outerAnswer, bare: bare);

        pair.Mindist.ChildDelegator.MindistsAdded(1001);

        pair.Mindist.ChildDelegator.MindistsBeneath().ShouldBe(1001);

        pair.Freeze();

        pair.Environment.MindistManager.Invalid.ShouldBe([pair.Mindist]);
    }

    /// <remarks>**An outer count above zero is the answer, asked for again** — the branch tail-calls the outer slot 3 a second time.</remarks>
    [Test]
    public void MindistsBeneath_AnOuterCountAboveZero_AsksItTwiceAndAnswersIt()
    {
        Pair pair = new(outerAnswer: 5);

        pair.Mindist.ChildDelegator.MindistsAdded(1001);

        pair.Mindist.ChildDelegator.MindistsBeneath().ShouldBe(5);
        pair.Outer.Asked.ShouldBe(2);
    }

    /// <remarks>**Slot 2, `FUN_1800b2300`, moves `+0xfc` and tells the outer delegator the same change.**</remarks>
    [Test]
    public void MindistsAdded_TwoChanges_MoveTheTotalAndAreEachToldOutward()
    {
        Pair pair = new();

        pair.Mindist.ChildDelegator.MindistsAdded(5);
        pair.Mindist.ChildDelegator.MindistsAdded(-2);

        pair.Mindist.Total.ShouldBe(3);
        pair.Outer.Told.ShouldBe([("added", 5), ("added", -2)]);
    }

    /// <remarks>
    /// **Slot 8 opens a side only for a contact on a hull's virtual face or edge** (`FUN_1800b2460`): a triangle or a point against
    /// an edge reads the triangle's header, an edge feature its own word; two virtual edges open by the radii as slot 7 does (here
    /// record 0's node is the larger, so record 1 is not the answer); every real contact collides. −1 is a collision.
    /// </remarks>
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Point, true, true, true, true, -1)]
    [TestCase(IvpFeatureKind.Ball, IvpFeatureKind.Ball, true, true, true, true, -1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Ball, true, true, true, true, -1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Edge, false, false, false, true, 1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Edge, true, true, true, false, -1)]
    [TestCase(IvpFeatureKind.Ball, IvpFeatureKind.Edge, false, false, false, true, 1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Triangle, false, false, true, false, 1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Triangle, true, true, false, true, -1)]
    [TestCase(IvpFeatureKind.Ball, IvpFeatureKind.Triangle, false, false, true, false, 1)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Point, true, false, false, false, 0)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Point, false, true, true, true, -1)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Ball, true, false, false, false, 0)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, false, false, false, true, 1)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, false, true, false, false, 0)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, false, false, false, false, -1)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, true, false, true, false, -1)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, false, true, false, true, 0)]
    [TestCase(IvpFeatureKind.Triangle, IvpFeatureKind.Point, true, false, false, false, 0)]
    [TestCase(IvpFeatureKind.Triangle, IvpFeatureKind.Triangle, true, false, true, false, 0)]
    [TestCase(IvpFeatureKind.Triangle, IvpFeatureKind.Edge, false, true, true, true, -1)]
    public void Collide_TheFeatureKindsAndVirtualBits_OpenASideOrCollide(
        IvpFeatureKind firstKind, IvpFeatureKind secondKind, bool firstTriangle, bool firstEdge, bool secondTriangle, bool secondEdge, int side)
    {
        Pair pair = Touching(firstKind, secondKind, (firstTriangle, firstEdge), (secondTriangle, secondEdge));
        List<IvpMindist> collided = [];

        pair.Mindist.Collide(collided.Add);

        pair.Mindist.OpenSide.ShouldBe(side);

        if (side < 0)
        {
            collided.ShouldBe([pair.Mindist]);
            pair.Environment.MindistManager.Exact.ShouldBe([pair.Mindist]);
        }
        else
        {
            collided.ShouldBeEmpty();
            pair.Environment.MindistManager.Exact.ShouldBeEmpty();
            (pair.Mindist.Flags & IvpMindistHull.StateMask).ShouldBe(IvpMindistHull.RecursiveState);
            pair.Mindist.Children.Count.ShouldBe(2);
            pair.Environment.WatcherRefreshes.ShouldBe(0);
        }
    }

    /// <remarks>**Kinds the switch has no case for are asserted against**: an edge with a triangle, and an unknown kind on either side.</remarks>
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Triangle)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Backside)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Backside)]
    [TestCase(IvpFeatureKind.Backside, IvpFeatureKind.Point)]
    public void Collide_KindsTheEngineAssertsAgainst_Throw(IvpFeatureKind firstKind, IvpFeatureKind secondKind)
    {
        Pair pair = Touching(firstKind, secondKind, (true, true), (true, true));

        Should.Throw<InvalidOperationException>(() => pair.Mindist.Collide(_ => { }));
    }

    /// <remarks>**Past the limit, slot 8 collides whatever it touches** — the count is read before the switch.</remarks>
    [Test]
    public void Collide_PastTheLimit_CollidesOnAVirtualFace()
    {
        Pair pair = Touching(IvpFeatureKind.Triangle, IvpFeatureKind.Triangle, (true, true), (true, true), outerAnswer: 1001);
        List<IvpMindist> collided = [];

        pair.Mindist.Collide(collided.Add);

        collided.ShouldBe([pair.Mindist]);
        pair.Mindist.OpenSide.ShouldBe(-1);
    }

    /// <remarks>
    /// **Told its hull passed, an open larger mindist minimizes first, then closes when nothing froze and the length is past
    /// `DAT_18012d64c`** (`COMISS`, `JNC`, so a NaN length closes and a length at the gap refreshes): the mindists beneath it deleted,
    /// both records out of their hull managers, and linked exact. The length is written by the recheck, so a read before it answers
    /// differently.
    /// </remarks>
    [TestCase(0, 1f, false, true)]
    [TestCase(0x4000, 1f, false, false)]
    [TestCase(0x8000, 1f, false, false)]
    [TestCase(0, float.NaN, false, true)]
    [TestCase(0, 0f, true, false)]
    [TestCase(0, 0f, false, false)]
    public void HullPassed_TheRecheckedFlagsAndLength_CloseOrRefresh(int frozen, float length, bool atTheGap, bool closes)
    {
        Pair pair = Opened();
        List<IvpMindist> rechecked = [];

        pair.Mindist.HullPassed(mindist =>
        {
            rechecked.Add(mindist);
            mindist.Flags |= frozen;
            mindist.Length = atTheGap ? IvpCollisionTolerance.ContactGap : length;
        }).ShouldBe(closes);

        rechecked.ShouldBe([pair.Mindist]);
        pair.Environment.WatcherRefreshes.ShouldBe(closes ? 0 : 1);
    }

    /// <remarks>**Closing deletes beneath, tells minus the count outward, takes both records out of their hull managers and links it exact.**</remarks>
    [Test]
    public void HullPassed_Closing_DeletesBeneathAndLinksAPlainExactPair()
    {
        Pair pair = Opened();

        pair.Mindist.HullPassed(mindist => mindist.Length = 1f).ShouldBeTrue();

        pair.Mindist.Children.ShouldBeEmpty();
        pair.Mindist.Total.ShouldBe(0);
        pair.Outer.Told.ShouldBe([("added", 2), ("added", -2)]);
        pair.Environment.DeletedMindists.ShouldBe(2);
        pair.Mindist.HullRecord(0).HullSlot.ShouldBeNull();
        pair.Mindist.HullRecord(1).HullSlot.ShouldBeNull();
        pair.First.Hull.Synapses.Count.ShouldBe(0);
        pair.Second.Hull.Synapses.Count.ShouldBe(0);
        pair.Environment.MindistManager.Exact.ShouldBe([pair.Mindist]);
        pair.First.Synapses.ShouldBe([pair.Mindist.HullRecord(0)]);
        (pair.Mindist.Flags & IvpMindistHull.StateMask).ShouldBe(IvpMindistHull.ExactState);
    }

    /// <remarks>
    /// **`FUN_180097ae0` reads the flags before `&amp; ~0x3000` clears them**: a phantom's pair (`0x1000`) stays off the rechecked array
    /// though its core is flagged, and `0x2000`, which is not a phantom's, joins it — both cleared after.
    /// </remarks>
    [TestCase(0x1000, 0)]
    [TestCase(0x2000, 1)]
    public void HullPassed_ClosingWithParkedBits_ChecksThemForTheRecheckedArrayThenClearsThem(int bits, int rechecked)
    {
        Pair pair = Opened();
        pair.FirstCore.HasOffset58 = true;
        pair.Mindist.Flags |= bits;

        pair.Mindist.HullPassed(mindist => mindist.Length = 1f);

        pair.Environment.MindistManager.Rechecked.Count.ShouldBe(rechecked);
        (pair.Mindist.Flags & 0x3000).ShouldBe(0);
    }

    /// <remarks>
    /// **Refreshing counts `env+0xbc` after the range call, refreshes beneath the same side, and files each record again over now
    /// with its own range** — here with the hulls' time at now and nothing grown, so each key is its range narrowed.
    /// </remarks>
    [Test]
    public void HullPassed_Refreshing_RefreshesBeneathAndFilesEachRecordAgainWithItsRange()
    {
        Pair pair = Opened();
        pair.First.Hull.Time = pair.Environment.Now;
        pair.Second.Hull.Time = pair.Environment.Now;

        pair.Mindist.HullPassed(mindist => mindist.Length = 0f).ShouldBeFalse();

        (double firstRange, double secondRange) = pair.Ranges();

        pair.KeyOf(0).ShouldBe((float)firstRange);
        pair.KeyOf(1).ShouldBe((float)secondRange);
        pair.FirstSurface.Roots.ShouldBe([pair.FirstTree.Root, pair.FirstTree.Root]);
        pair.Mindist.Children.Count.ShouldBe(2);
        pair.Outer.Told.ShouldBe([("added", 2), ("added", 0)]);
        (pair.Mindist.Flags & IvpMindistHull.StateMask).ShouldBe(IvpMindistHull.RecursiveState);
    }

    /// <remarks>
    /// **Past the limit, refreshing still counts `env+0xbc` and files both records again, but nothing beneath is refreshed** —
    /// `FUN_1800b29b0` reads the count first and returns, telling no change.
    /// </remarks>
    [Test]
    public void HullPassed_RefreshingPastTheLimit_FilesTheRecordsButRefreshesNothingBeneath()
    {
        Pair pair = Opened();
        pair.Outer.Answer = 1001;

        pair.Mindist.HullPassed(mindist => mindist.Length = 0f).ShouldBeFalse();

        pair.Environment.WatcherRefreshes.ShouldBe(1);
        pair.FirstSurface.Roots.ShouldBe([pair.FirstTree.Root]);
        pair.Outer.Told.ShouldBe([("added", 2)]);
        pair.Mindist.HullRecord(0).HullSlot.ShouldNotBeNull();
        pair.Mindist.HullRecord(1).HullSlot.ShouldNotBeNull();
    }

    /// <remarks>**`FUN_180097f00` sends the recursive state to `FUN_1800b28a0`** with the recheck, handing nothing off.</remarks>
    [Test]
    public void HullPassed_TheHullManagersHandler_SendsTheRecursiveStateToTheLargerMindist()
    {
        Pair pair = Opened();
        List<IvpMindist> rechecked = [];
        List<IvpMindist> handedOff = [];

        IvpHullPassOutcome outcome = IvpMindistHull.HullPassed(
            pair.Mindist,
            -1f,
            new IvpHullPass
            {
                Now = pair.Environment.Now,
                Step = pair.Environment.Step,
                First = pair.First,
                Second = pair.Second,
                FirstBody = pair.FirstCore,
                SecondBody = pair.SecondCore,
                FirstBounds = IvpRangeManager.Bounds(pair.FirstCore),
                SecondBounds = IvpRangeManager.Bounds(pair.SecondCore),
                HandOff = handedOff.Add,
                Recheck = mindist =>
                {
                    rechecked.Add(mindist);
                    mindist.Length = 1f;
                },
            });

        outcome.ShouldBe(IvpHullPassOutcome.Recursive);
        rechecked.ShouldBe([pair.Mindist]);
        handedOff.ShouldBeEmpty();
        pair.Environment.MindistManager.Exact.ShouldBe([pair.Mindist]);
    }

    /// <remarks>
    /// **Slot 0 deletes the mindists beneath it last first, tells minus their number, then runs a plain mindist's destructor** —
    /// taking an open pair's records out of their hull managers and telling the outer delegator last. Three beneath, so a walk from
    /// the front runs past the shrinking vector.
    /// </remarks>
    [Test]
    public void Delete_AnOpenedLargerMindist_DeletesBeneathLastFirstThenGoesItself()
    {
        Pair pair = new(found: 3);
        pair.Freeze();
        pair.Mindist.Children.Count.ShouldBe(3);

        pair.Mindist.Delete();

        pair.Mindist.Children.ShouldBeEmpty();
        pair.Outer.Told.ShouldBe([("added", 3), ("added", -3), ("removed", 0)]);
        pair.Environment.DeletedMindists.ShouldBe(4);
        pair.Mindist.HullRecord(0).HullSlot.ShouldBeNull();
        pair.Mindist.HullRecord(1).HullSlot.ShouldBeNull();
    }

    private static Pair Opened()
    {
        Pair pair = new();

        pair.Freeze();
        return pair;
    }

    private static Pair Touching(
        IvpFeatureKind firstKind, IvpFeatureKind secondKind, (bool Triangle, bool Edge) firstVirtual, (bool Triangle, bool Edge) secondVirtual,
        int outerAnswer = -1)
    {
        Pair pair = new(1, 1, 2f, 1f, outerAnswer: outerAnswer, firstVirtual: firstVirtual, secondVirtual: secondVirtual);

        pair.Mindist.SetSynapse(0, new IvpSynapse(new IvpLedgeEdge(0, 1), firstKind));
        pair.Mindist.SetSynapse(1, new IvpSynapse(new IvpLedgeEdge(0, 1), secondKind));
        return pair;
    }

    /// <summary>A larger mindist linked exact between two moving objects, each over its own tree, and what the environment was told.</summary>
    private sealed class Pair
    {
        public Pair(
            int firstChildren = 1,
            int secondChildren = 0,
            float firstRadius = 1f,
            float secondRadius = 1f,
            bool firstNamesNode = true,
            bool secondNamesNode = true,
            int outerAnswer = -1,
            bool bare = false,
            int found = 2,
            (bool Triangle, bool Edge) firstVirtual = default,
            (bool Triangle, bool Edge) secondVirtual = default)
        {
            Environment = new IvpCollisionEnvironment
            {
                Filter = (_, _) => true,
                BecomeExact = Created.Add,
                HullPassed = (mindist, overshoot) => Passed.Add((mindist, overshoot)),
                Step = 0.015d,
                Now = 2d,
            };
            FirstTree = Tree(firstChildren, firstRadius, firstNamesNode, firstVirtual);
            SecondTree = Tree(secondChildren, secondRadius, secondNamesNode, secondVirtual);
            FirstSurface = new Surface(FirstTree, found);
            SecondSurface = new Surface(SecondTree, found);
            First = new IvpCollisionObject { Core = FirstCore, Environment = Environment, Surface = FirstSurface, MovementState = 1 };
            Second = new IvpCollisionObject { Core = SecondCore, Environment = Environment, Surface = SecondSurface, MovementState = 1 };
            Outer = new Recording(outerAnswer);

            IvpSynapse point = new(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point);

            Mindist = new IvpRecursiveMindist(point, point, 0f) { Flags = 0x0fc00100, Delegator = bare ? new Bare() : Outer };
            Mindist.Attach(First, FirstTree.Root, Second, SecondTree.Root);
            Environment.MindistManager.LinkExact(Mindist, First, Second);
        }

        public List<IvpMindist> Created { get; } = [];

        public List<(IvpMindist Mindist, float Overshoot)> Passed { get; } = [];

        public IvpCollisionEnvironment Environment { get; }

        public PhysicsLedgeTree FirstTree { get; }

        public PhysicsLedgeTree SecondTree { get; }

        public Surface FirstSurface { get; }

        public Surface SecondSurface { get; }

        public IvpRigidBody FirstCore { get; } = new() { Radius = 1f, LinearSpeed = 2f, SurfaceSpeedBound = 1f };

        public IvpRigidBody SecondCore { get; } = new() { Radius = 0.5f, LinearSpeed = 0.5f };

        public IvpCollisionObject First { get; }

        public IvpCollisionObject Second { get; }

        public Recording Outer { get; }

        public IvpRecursiveMindist Mindist { get; }

        public void Freeze() => Mindist.Freeze(Environment.MindistManager, Environment.EventQueue);

        public (double First, double Second) Ranges() =>
            IvpRangeManager.PairRange(IvpRangeManager.Bounds(FirstCore), IvpRangeManager.Bounds(SecondCore), Environment.Step);

        public float KeyOf(int record)
        {
            IvpHullManager hull = record == 0 ? First.Hull : Second.Hull;

            return hull.Synapses.ValueOf(Mindist.HullRecord(record).HullSlot.ShouldNotBeNull());
        }

        /// <summary>
        /// A surface whose root is an inner node with a hull ledge over two leaves, each leaf with a ledge of its own. The hull's triangle
        /// header takes bit 31 when asked, its middle edge bit 31 when asked and its outer edges the opposite.
        /// </summary>
        private static PhysicsLedgeTree Tree(int children, float radius, bool namesNode, (bool Triangle, bool Edge) hullVirtual)
        {
            const int hull = 0x30;
            const int leftLedge = 0x80;
            const int rightLedge = 0xA0;
            const int root = 0xC0;
            const int left = root + 0x1C;
            const int right = root + 0x38;

            byte[] surface = new byte[right + 0x1C];

            Write(surface, 0x20, root);
            WriteLedge(surface, hull, namesNode ? root : null, children, hullVirtual.Triangle, (!hullVirtual.Edge, hullVirtual.Edge, !hullVirtual.Edge));
            WriteLedge(surface, leftLedge, left, 0, false, default);
            WriteLedge(surface, rightLedge, right, 0, false, default);
            WriteNode(surface, root, right - root, hull, radius);
            WriteNode(surface, left, 0, leftLedge, 1f);
            WriteNode(surface, right, 0, rightLedge, 1f);

            return PhysicsHull.Tree(surface).ShouldNotBeNull();
        }

        /// <summary>A ledge of one triangle over the three points at <c>0x50</c>.</summary>
        private static void WriteLedge(byte[] surface, int at, int? node, int children, bool virtualTriangle, (bool A, bool B, bool C) virtualEdges)
        {
            const int points = 0x50;

            Write(surface, at, points - at);
            Write(surface, at + 4, node is int named ? named - at : 0);
            Write(surface, at + 8, children);
            Write(surface, at + 0xC, 1);
            Write(surface, at + 0x10, virtualTriangle ? int.MinValue : 0);
            Write(surface, at + 0x14, EdgeWord(0, virtualEdges.A));
            Write(surface, at + 0x18, EdgeWord(1, virtualEdges.B));
            Write(surface, at + 0x1C, EdgeWord(2, virtualEdges.C));
        }

        private static int EdgeWord(int point, bool isVirtual) => isVirtual ? point | int.MinValue : point;

        private static void WriteNode(byte[] surface, int at, int right, int ledge, float radius)
        {
            Write(surface, at, right);
            Write(surface, at + 4, ledge - at);
            BitConverter.TryWriteBytes(surface.AsSpan(at + 0x14), radius);
            surface.AsSpan(at + 0x18, 3).Fill(250);
        }

        private static void Write(byte[] bytes, int at, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
    }

    /// <summary>A surface manager that hands back the tree's leaves, alternately, and records the ledge each query started beneath.</summary>
    private sealed class Surface(PhysicsLedgeTree tree, int found) : IIvpSurfaceManager
    {
        public List<PhysicsLedgeTreeNode?> Roots { get; } = [];

        public void LedgesWithin(
            (double X, double Y, double Z) center, double radius, PhysicsLedgeTreeNode? root, ICollection<PhysicsLedgeTreeNode> into)
        {
            Roots.Add(root);

            for (int index = 0; index < found; index++)
            {
                into.Add((index % 2 == 0 ? tree.Root.Left : tree.Root.Right).ShouldNotBeNull());
            }
        }
    }

    /// <summary>An outer delegator that records what it is told and answers slot 3 with a given count.</summary>
    private sealed class Recording(int answer) : IIvpCollisionDelegator
    {
        public List<(string Slot, int Change)> Told { get; } = [];

        public int Asked { get; private set; }

        /// <summary>What slot 3 answers.</summary>
        public int Answer { get; set; } = answer;

        public void CollisionRemoved(IvpCollision collision) => Told.Add(("removed", 0));

        public void MindistsAdded(int change) => Told.Add(("added", change));

        public int MindistsBeneath()
        {
            Asked++;
            return Answer;
        }
    }

    /// <summary>An outer delegator with the watcher's slots 2 and 3: the interface's defaults.</summary>
    private sealed class Bare : IIvpCollisionDelegator
    {
        public void CollisionRemoved(IvpCollision collision)
        {
            // Nothing is removed from an outer delegator here.
        }
    }
}
