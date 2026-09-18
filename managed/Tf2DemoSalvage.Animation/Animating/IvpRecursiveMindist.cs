using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's larger mindist, the 0x100 bytes <c>IvpRecursiveMindist::IvpRecursiveMindist</c> (<c>1800b21f0</c>) builds for a pair either of
/// whose ledges is a hull with children: a plain mindist until it would freeze, or collide on the hull's virtual face, and then the
/// pair's mindists beneath the ledge it opens (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The larger mindist in full*): <c>IvpRecursiveMindist::vftable</c>
/// (<c>1800fe960</c>) — slot 0 <c>IvpRecursiveMindist::Delete</c>, slot 7 <c>IvpRecursiveMindist::Freeze</c>, slot 8
/// <c>IvpRecursiveMindist::Collide</c> — its own delegator at <c>+0xe0</c> (<c>IvpRecursiveMindist::Delegation::vftable</c>,
/// <c>1800fe9a8</c>), the mindists beneath it at <c>+0xe8</c>, their total at <c>+0xfc</c>, and the side it opens at <c>+0xf8</c>.
/// **Slot 6, <c>FUN_180028aa0</c>, which answers 1, is not carried**: nothing ported reads it, and what the answer means is not read.
/// </remarks>
public sealed class IvpRecursiveMindist : IvpMindist
{
    /// <summary>
    /// <c>IvpRecursiveMindist::Limit</c> (<c>18012d66c</c>) — zero in the image, <c>0x3e8</c> once <c>FUN_180002540</c> runs
    /// (<c>180002570</c>): past this many mindists beneath the outermost larger mindist, none opens any further.
    /// </summary>
    public const int Limit = 1000;

    private const int OpenClears = 0x2C0000;
    private const int FrozenBits = 0xC000;
    private const int ParkedBits = 0x3000;

    /// <summary><c>IvpRecursiveMindist::NoNodeRadius</c> (<c>1800fea24</c>), <c>0x58635fa9</c>, <c>1e15f</c>: the radius of a ledge that names no node.</summary>
    private static readonly float NoNodeRadius = BitConverter.Int32BitsToSingle(0x58635fa9);

    private readonly List<IvpCollision> _children = [];

    /// <summary>A larger mindist between two features, synapse A being the first — <c>IvpRecursiveMindist::IvpRecursiveMindist</c> past <c>IvpMindist::IvpMindist</c>.</summary>
    /// <param name="first">Synapse record 0.</param>
    /// <param name="second">Synapse record 1.</param>
    /// <param name="extraRadius">The pair's extra radius, <c>+0x98</c>.</param>
    public IvpRecursiveMindist(IvpSynapse first, IvpSynapse second, float extraRadius)
        : base(first, second, extraRadius) => ChildDelegator = new Delegation(this);

    /// <summary>The mindists beneath it, <c>+0xe8</c>, with their back-indices.</summary>
    public IReadOnlyList<IvpCollision> Children => _children;

    /// <summary><c>+0xfc</c>: the mindists beneath it at every depth, as its delegator's slot 2 has counted them.</summary>
    public int Total { get; private set; }

    /// <summary><c>+0xf8</c>: the record whose ledge it opens, 0 or 1; −1 until it first opens.</summary>
    public int OpenSide { get; private set; } = -1;

    /// <summary>Its own delegator, <c>+0xe0</c> — what each mindist beneath it tells, and asks.</summary>
    public IIvpCollisionDelegator ChildDelegator { get; }

    /// <summary>
    /// Deletes it — slot 0, <c>IvpRecursiveMindist::Delete</c> (<c>1800b2250</c>): the mindists beneath it
    /// (<c>IvpRecursiveMindist::DeleteChildren</c>), then <c>IvpMindist::Destroy</c>.
    /// </summary>
    public override void Delete()
    {
        DeleteChildren();
        base.Delete();
    }

    /// <summary>Slot 7, <c>IvpRecursiveMindist::Freeze</c> (<c>1800b2700</c>): what a frozen minimize makes of it.</summary>
    /// <param name="manager">The environment's mindist manager.</param>
    /// <param name="queue">The time manager's queue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A record, object or ledge lacks what the engine reads.</exception>
    /// <remarks>
    /// <code>
    /// the count (Delegation::MindistsBeneath) &gt; Limit → IvpMindistManager::Unlink, IvpMindistManager::LinkInvalid — a plain mindist's invalidation
    /// record 0's ledge without children (+0x8 &amp; 3) → +0xf8 = 1;  else record 1's → 0;  else the radii of their nodes:  rA ≤ rB
    /// IvpMindistManager::Unlink;  IvpRangeManager::PairRange(A, B);  IvpMindistHull::FileRecursive((float)(rA + rB));
    ///     flags &amp; 0xffd3ffff | 0x100000;  IvpRecursiveMindist::RefreshChildren(rA + rB)
    /// </code>
    /// </remarks>
    public override void Freeze(IvpMindistManager manager, IvpMinList<IIvpTimeEvent> queue)
    {
        ArgumentNullException.ThrowIfNull(manager);

        (IvpCollisionObject first, IvpCollisionObject second) = Objects;

        if (ChildDelegator.MindistsBeneath() > Limit)
        {
            manager.Unlink(this, queue);
            manager.LinkInvalid(this, first, second);
            return;
        }

        PhysicsLedgeTreeNode firstLedge = LedgeOf(0);
        PhysicsLedgeTreeNode secondLedge = LedgeOf(1);

        if (firstLedge.LedgeChildren == 0)
        {
            OpenSide = 1;
        }
        else if (secondLedge.LedgeChildren == 0)
        {
            OpenSide = 0;
        }
        else
        {
            OpenSide = LargerSide(firstLedge, secondLedge);
        }

        Open(manager, queue, first, second);
    }

    /// <summary>
    /// Slot 8, <c>IvpRecursiveMindist::Collide</c> (<c>1800b2460</c>): the collision event, which opens a ledge when the contact is on a
    /// hull's virtual face or edge.
    /// </summary>
    /// <param name="impact">The plain mindist's collision, <c>IvpMindist::Collide</c>, which is not ported and is handed in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="impact"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The feature kinds are a pair the engine asserts against, or something read is missing.</exception>
    /// <remarks>
    /// <code>
    /// the count &gt; Limit → IvpMindist::Collide (a tail call)
    /// by record 0's kind (+0x5a) then record 1's (+0x92), "virtual" meaning a negative word:
    ///     point or ball:  point or ball → collide;  edge → its edge word virtual → open 1;  triangle → its header virtual → open 1
    ///     edge:  point or ball → record 0's header virtual → open 0;  edge → record 1's edge word virtual and record 0's not → 1,
    ///         both → the radii as slot 7, record 0's alone → 0, neither → collide;  triangle → asserted
    ///     triangle:  its header virtual → open 0
    ///     anything not opened collides;  an unknown kind is asserted
    /// opening:  +0xf8;  IvpMindistManager::Unlink(env+0x20);  IvpRangeManager::PairRange;  IvpMindistHull::FileRecursive;  flags;
    ///     IvpRecursiveMindist::RefreshChildren — slot 7's tail
    /// </code>
    /// </remarks>
    public override void Collide(Action<IvpMindist> impact)
    {
        ArgumentNullException.ThrowIfNull(impact);

        if (ChildDelegator.MindistsBeneath() > Limit || SideTouched() is not int side)
        {
            impact(this);
            return;
        }

        (IvpCollisionObject first, IvpCollisionObject second) = Objects;
        IvpCollisionEnvironment environment = EnvironmentOf(first);

        OpenSide = side;
        Open(environment.MindistManager, environment.EventQueue, first, second);
    }

    /// <summary>
    /// Told a hull passed a record while open — <c>IvpRecursiveMindist::HullPassed</c> (<c>1800b28a0</c>), where
    /// <c>IvpMindistHull::HullPassed</c> sends the recursive state.
    /// </summary>
    /// <param name="recheck">The minimize with no step budget, <c>IvpMindistMinimize::MinimizeWithoutBudget</c>.</param>
    /// <returns>True when it closed back into a plain exact pair; false when it refreshed the mindists beneath it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recheck"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A record, object or ledge lacks what the engine reads.</exception>
    /// <remarks>
    /// <code>
    /// IvpMindistMinimize::MinimizeWithoutBudget;  no bits of 0xc000 and IvpCollisionTolerance::ContactGap &lt; +0xa8 (COMISS, JNC: a NaN
    ///     length closes) → IvpRecursiveMindist::DeleteChildren, IvpMindistHull::UnfileRecords, IvpMindistManager::Revalidate,
    ///     flags &amp;= ~0x3000
    /// else:  IvpRangeManager::PairRange(A, B);  env+0xbc += 1;  IvpRecursiveMindist::RefreshChildren(rA + rB);
    ///     IvpHullManager::Reinstall of record 0 over now with rA, then record 1 with rB
    /// </code>
    /// </remarks>
    public bool HullPassed(Action<IvpMindist> recheck)
    {
        ArgumentNullException.ThrowIfNull(recheck);

        recheck(this);

        (IvpCollisionObject first, IvpCollisionObject second) = Objects;
        IvpCollisionEnvironment environment = EnvironmentOf(first);

        if ((Flags & FrozenBits) == 0 && !(IvpCollisionTolerance.ContactGap >= Length))
        {
            DeleteChildren();
            first.Hull.Remove(HullRecord(0));
            second.Hull.Remove(HullRecord(1));
            environment.MindistManager.Revalidate(this, first, second);
            Flags &= ~ParkedBits;
            return true;
        }

        (double firstRange, double secondRange) = IvpRangeManager.PairRange(
            IvpRangeManager.Bounds(CoreOf(first)), IvpRangeManager.Bounds(CoreOf(second)), environment.Step);

        environment.WatcherRefreshes++;
        RefreshChildren(IvpMath.Addsd(firstRange, secondRange));

        double now = environment.Now;

        first.Hull.Reinstall(HullRecord(0), now, firstRange);
        second.Hull.Reinstall(HullRecord(1), now, secondRange);
        return false;
    }

    /// <summary>
    /// 1 when record 0's ledge's node is no larger than record 1's — <c>COMISS</c> then <c>SETBE</c>, so a NaN opens record 1 — else 0.
    /// </summary>
    private static int LargerSide(PhysicsLedgeTreeNode first, PhysicsLedgeTreeNode second) =>
        NodeRadius(first) > NodeRadius(second) ? 0 : 1;

    /// <summary>A ledge's own node's radius — <c>ledge + ledge+0x4</c>, then <c>+0x14</c> — or <c>1e15f</c> when its word is zero.</summary>
    private static float NodeRadius(PhysicsLedgeTreeNode ledge)
    {
        if (ledge.LedgeNodeOffset is null)
        {
            return NoNodeRadius;
        }

        PhysicsLedgeTreeNode node = ledge.LedgeNode ?? throw new InvalidOperationException(
            "A larger mindist's ledge names an offset where no node lies, where the engine reads the bytes there as one.");

        return node.Radius;
    }

    /// <summary>The side a contact on a virtual face or edge opens, or null when the contact is on real geometry.</summary>
    private int? SideTouched()
    {
        IvpFeatureKind secondKind = Synapse(1).Kind;

        switch (Synapse(0).Kind)
        {
            case IvpFeatureKind.Point:
            case IvpFeatureKind.Ball:
                return secondKind switch
                {
                    IvpFeatureKind.Point or IvpFeatureKind.Ball => null,
                    IvpFeatureKind.Edge => When(EdgeVirtual(1), 1),
                    IvpFeatureKind.Triangle => When(TriangleVirtual(1), 1),
                    _ => throw Asserted(),
                };
            case IvpFeatureKind.Edge:
                return secondKind switch
                {
                    IvpFeatureKind.Point or IvpFeatureKind.Ball => When(TriangleVirtual(0), 0),
                    IvpFeatureKind.Edge => BothEdges(),
                    _ => throw Asserted(),
                };
            case IvpFeatureKind.Triangle:
                return When(TriangleVirtual(0), 0);
            default:
                throw Asserted();
        }
    }

    /// <summary>Two edges: record 1's word read first, then record 0's.</summary>
    private int? BothEdges()
    {
        bool second = EdgeVirtual(1);
        bool first = EdgeVirtual(0);

        if (second)
        {
            return first ? LargerSide(LedgeOf(0), LedgeOf(1)) : 1;
        }

        return When(first, 0);
    }

    private static int? When(bool isVirtual, int side) => isVirtual ? side : null;

    private static InvalidOperationException Asserted() =>
        new("A larger mindist's contact pairs feature kinds IvpRecursiveMindist::Collide asserts against.");

    /// <summary>Whether a record's feature's triangle header is negative — the word at <c>feature &amp; ~0xf</c>.</summary>
    private bool TriangleVirtual(int record)
    {
        IReadOnlyList<bool> triangles = DecodedLedgeOf(record).VirtualTriangles
            ?? throw new InvalidOperationException("A larger mindist's ledge was decoded without its triangles' bit 31.");

        return triangles[Synapse(record).Feature.Triangle];
    }

    /// <summary>Whether a record's feature's own edge word is negative — the word at <c>feature</c>.</summary>
    private bool EdgeVirtual(int record)
    {
        IvpLedgeEdge edge = Synapse(record).Feature;
        IReadOnlyList<(bool A, bool B, bool C)> edges = DecodedLedgeOf(record).VirtualEdges
            ?? throw new InvalidOperationException("A larger mindist's ledge was decoded without its edges' bit 31.");
        (bool a, bool b, bool c) = edges[edge.Triangle];

        return edge.Slot switch
        {
            0 => a,
            1 => b,
            2 => c,
            _ => throw new InvalidOperationException("A larger mindist's edge names a slot past a triangle's three."),
        };
    }

    /// <summary>Opens the ledge <see cref="OpenSide"/> names — the tail slots 7 and 8 share.</summary>
    private void Open(IvpMindistManager manager, IvpMinList<IIvpTimeEvent> queue, IvpCollisionObject first, IvpCollisionObject second)
    {
        manager.Unlink(this, queue);

        IvpCollisionEnvironment environment = EnvironmentOf(first);
        IvpCoreBounds firstBounds = IvpRangeManager.Bounds(CoreOf(first));
        IvpCoreBounds secondBounds = IvpRangeManager.Bounds(CoreOf(second));
        (double firstRange, double secondRange) = IvpRangeManager.PairRange(firstBounds, secondBounds, environment.Step);
        double gap = IvpMath.Addsd(firstRange, secondRange);
        Action<IvpMindist, float> hullPassed = environment.HullPassed
            ?? throw new InvalidOperationException("A larger mindist opens in an environment with no hull-passed handler for its records.");

        IvpMindistHull.FileRecursive(this, new IvpFarFiling(manager, first, second, hullPassed), firstBounds, secondBounds, (float)gap);
        Flags = (Flags & ~OpenClears) | IvpMindistHull.RecursiveState;
        RefreshChildren(gap);
    }

    /// <summary>
    /// <c>IvpRecursiveMindist::RefreshChildren</c> (<c>1800b29b0</c>): the mindists beneath the open side's ledge refreshed — that side
    /// queried beneath its ledge as ROOT, the other handed its ledge as LEDGE — and the change in their number told through the
    /// delegator's slot 2.
    /// </summary>
    private void RefreshChildren(double gap)
    {
        if (ChildDelegator.MindistsBeneath() > Limit)
        {
            return;
        }

        if (OpenSide is not (0 or 1))
        {
            throw new InvalidOperationException("A larger mindist refreshes beneath a side it never chose, where the engine reads past its two records.");
        }

        (IvpCollisionObject first, IvpCollisionObject second) = Objects;
        int saved = _children.Count;

        IvpPairMindists.Refresh(
            first,
            second,
            gap,
            _children,
            OpenSide == 1 ? Ledge(0) : null,
            OpenSide == 0 ? Ledge(1) : null,
            OpenSide == 0 ? Ledge(0) : null,
            OpenSide == 1 ? Ledge(1) : null,
            ChildDelegator);
        ChildDelegator.MindistsAdded(_children.Count - saved);
    }

    /// <summary>
    /// <c>IvpRecursiveMindist::DeleteChildren</c> (<c>1800b23a0</c>): every mindist beneath it deleted, last first, then minus their
    /// number told through slot 2.
    /// </summary>
    private void DeleteChildren()
    {
        int count = _children.Count;

        for (int index = count - 1; index >= 0; index--)
        {
            _children[index].Delete();
        }

        ChildDelegator.MindistsAdded(-count);
    }

    private PhysicsLedgeTreeNode LedgeOf(int record) =>
        Ledge(record) ?? throw new InvalidOperationException("A larger mindist's record has no ledge.");

    private PhysicsLedge DecodedLedgeOf(int record) =>
        LedgeOf(record).Ledge ?? throw new InvalidOperationException("A larger mindist's ledge was not decoded, where the engine reads its words.");

    private static IvpCollisionEnvironment EnvironmentOf(IvpCollisionObject collisionObject) =>
        collisionObject.Environment ?? throw new InvalidOperationException("A larger mindist's object has no environment.");

    private static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
        collisionObject.Core ?? throw new InvalidOperationException("A larger mindist's object has no core.");

    /// <summary>A larger mindist's delegator, <c>IvpRecursiveMindist::Delegation::vftable</c> (<c>1800fe9a8</c>) at <c>+0xe0</c>.</summary>
    private sealed class Delegation(IvpRecursiveMindist owner) : IIvpCollisionDelegator
    {
        /// <summary>Slot 0, <c>IvpRecursiveMindist::Delegation::CollisionRemoved</c> (<c>1800b2320</c>): a mindist beneath it out of the vector by its back-index.</summary>
        public void CollisionRemoved(IvpCollision collision) => IvpCollisionList.Remove(owner._children, collision);

        /// <summary>Slot 2, <c>IvpRecursiveMindist::Delegation::MindistsAdded</c> (<c>1800b2300</c>): <c>+0xfc</c> moved, then the outer delegator's slot 2 (a tail call).</summary>
        public void MindistsAdded(int change)
        {
            owner.Total += change;
            Outer.MindistsAdded(change);
        }

        /// <summary>
        /// Slot 3, <c>IvpRecursiveMindist::Delegation::MindistsBeneath</c> (<c>1800b2860</c>): the outer delegator's count when above zero —
        /// asked again, in a tail call — else <c>+0xfc</c>.
        /// </summary>
        public int MindistsBeneath() => Outer.MindistsBeneath() > 0 ? Outer.MindistsBeneath() : owner.Total;

        private IIvpCollisionDelegator Outer => owner.Delegator
            ?? throw new InvalidOperationException("A larger mindist with no delegator asks outward, where the engine calls through a null table.");
    }
}
