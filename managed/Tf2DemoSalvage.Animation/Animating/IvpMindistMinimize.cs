using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What a synapse's feature is — the word at a synapse record's <c>+0x32</c>.</summary>
public enum IvpFeatureKind
{
    /// <summary>A point: the start of the feature's edge.</summary>
    Point = 0,

    /// <summary>An edge.</summary>
    Edge = 1,

    /// <summary>A triangle, named by one of its edges.</summary>
    Triangle = 2,

    /// <summary>A ball; the routines for it are not ported, a ragdoll's ledges having none.</summary>
    Ball = 3,

    /// <summary>A triangle the other feature was found behind, until the backside walk replaces it.</summary>
    Backside = 5,
}

/// <summary>One of a mindist's two synapse records: a feature and its kind.</summary>
/// <param name="Feature">The edge that names the feature — its start for a point, its triangle for a triangle.</param>
/// <param name="Kind">What the feature is.</param>
public readonly record struct IvpSynapse(IvpLedgeEdge Feature, IvpFeatureKind Kind);

/// <summary>The fields of an IVP mindist the minimize reads and writes (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The solver, the sides, and what the minimize writes*): the flags at
/// `+0x20`, whose bits 8–9 name synapse A; the two synapse records from `+0x28`; the extra radius at `+0x98`; `+0x9c`;
/// the length at `+0xa8`; the normal at `+0xb0`; and the step the minimize last ran in, at `+0xc0`.
/// </remarks>
public class IvpMindist : IvpCollision, IIvpTimeEvent
{
    private const int PhantomBits = 0xc00;
    private const int PhantomKeeps = unchecked((int)0xfffcf3ff);
    private const int InvalidState = 2;
    private const int ExactState = 3;
    private const int RecursiveState = 4;
    private const int FiledState = 5;

    private readonly IvpSynapse[] _synapses;
    private readonly IvpMindistHullRecord[] _hullRecords;
    private readonly PhysicsLedgeTreeNode?[] _ledges = new PhysicsLedgeTreeNode?[2];

    /// <summary>A mindist between two features, synapse A being the first.</summary>
    /// <param name="first">Synapse record 0.</param>
    /// <param name="second">Synapse record 1.</param>
    /// <param name="extraRadius">The pair's extra radius, <c>+0x98</c>.</param>
    public IvpMindist(IvpSynapse first, IvpSynapse second, float extraRadius)
    {
        _synapses = [first, second];
        _hullRecords = [new IvpMindistHullRecord(this, 0), new IvpMindistHullRecord(this, 1)];
        ExtraRadius = extraRadius;
    }

    /// <summary>
    /// <c>+0xa0</c>: the two hulls past their centers when the pair was last filed with its hull managers, moved by every
    /// rebase since.
    /// </summary>
    public double HullPastCenters { get; set; }

    /// <summary>The mindist's place in the manager's exact list — its <c>+0xc8</c>/<c>+0xd0</c> links — or null.</summary>
    internal LinkedListNode<IvpMindist>? ListNode { get; set; }

    /// <summary>The flags at <c>+0x20</c>.</summary>
    public int Flags { get; set; }

    /// <summary>Which synapse record is synapse A — <c>(flags &gt;&gt; 8) &amp; 3</c>.</summary>
    public int SynapseA => (Flags >> 8) & 3;

    /// <summary>The margin class — the flags' byte at bits 22–29, which picks <see cref="IvpCollisionTolerance.MarginFor"/>'s margin.</summary>
    public int MarginClass => (Flags >> 22) & 0xFF;

    /// <summary>The extra radius at <c>+0x98</c>, which every length is written less.</summary>
    public float ExtraRadius { get; }

    /// <summary>The length at <c>+0xa8</c>.</summary>
    public float Length { get; set; }

    /// <summary>The normal at <c>+0xb0</c>, pointing from the second feature's body to the first's.</summary>
    public (float X, float Y, float Z) Normal { get; set; }

    /// <summary>
    /// <c>+0x9c</c>: the first body's core position less the second's, dotted with the normal in float. The hull-passed
    /// handler <c>FUN_180097f00</c> reads it back against the cores' positions at now, and rewrites it when it files a far
    /// pair again.
    /// </summary>
    public float ContactDot { get; set; }

    /// <summary>The environment's step counter when the minimize last ran, <c>+0xc0</c>; null before it ever has.</summary>
    public int? MinimizedAt { get; set; }

    /// <summary>The time manager's slot at <c>+0x8</c>, or null when the mindist is not queued — the engine's <c>0xffff</c>.</summary>
    public int? QueueSlot { get; set; }

    /// <summary>A synapse record.</summary>
    /// <param name="index">0 or 1.</param>
    /// <returns>The record.</returns>
    public IvpSynapse Synapse(int index) => _synapses[index];

    /// <summary>A synapse record as its object's list and hull manager see it.</summary>
    /// <param name="index">0 or 1.</param>
    /// <returns>The record.</returns>
    public IvpMindistHullRecord HullRecord(int index) => _hullRecords[index];

    /// <summary>Replaces a synapse record.</summary>
    /// <param name="index">0 or 1.</param>
    /// <param name="synapse">The new record.</param>
    public void SetSynapse(int index, IvpSynapse synapse) => _synapses[index] = synapse;

    /// <summary>What the mindist tells when it goes, <c>+0x10</c>, or null.</summary>
    public IIvpCollisionDelegator? Delegator { get; set; }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">A record was never given its object.</exception>
    public override (IvpCollisionObject First, IvpCollisionObject Second) Objects =>
        (_hullRecords[0].CollisionObject ?? throw new InvalidOperationException("A mindist's first record has no object."),
         _hullRecords[1].CollisionObject ?? throw new InvalidOperationException("A mindist's second record has no object."));

    /// <summary>A synapse record's ledge — what slot 3, <c>FUN_180097510</c>, finds from the record's feature.</summary>
    /// <param name="index">0 or 1.</param>
    /// <returns>The ledge's node, or null for a mindist built from features alone.</returns>
    public PhysicsLedgeTreeNode? Ledge(int index) => _ledges[index];

    /// <summary>Gives the two records their objects and ledges — what <c>FUN_1800975d0</c> writes for two polygons.</summary>
    /// <param name="first">Record 0's object.</param>
    /// <param name="firstLedge">Record 0's ledge.</param>
    /// <param name="second">Record 1's object.</param>
    /// <param name="secondLedge">Record 1's ledge.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Attach(IvpCollisionObject first, PhysicsLedgeTreeNode firstLedge, IvpCollisionObject second, PhysicsLedgeTreeNode secondLedge)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(firstLedge);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(secondLedge);

        _hullRecords[0].CollisionObject = first;
        _hullRecords[1].CollisionObject = second;
        _ledges[0] = firstLedge;
        _ledges[1] = secondLedge;
    }

    /// <summary>Deletes the mindist — its slot 0, <c>FUN_180096250</c>, over the destructor <c>FUN_180095fb0</c>.</summary>
    /// <exception cref="InvalidOperationException">
    /// A record has no object, the objects no environment, or a phantom must be told, which is not ported.
    /// </exception>
    /// <remarks>
    /// <code>
    /// env+0xb0 −= 1, +0xb8 += 1;  flags &amp; 0xc00 → flags &amp; 0xfffcf3ff, each object's phantom told (FUN_18008b0a0)
    /// by the state, flags bits 18–21:  2 → off the invalid lists (FUN_180098f30);  3 → unfiled (FUN_180098dd0);
    ///     4 or 5 → each record out of its object's hull manager
    /// each record's ledge released through slot 8 (nothing for polygons);  the delegator's slot 0
    /// </code>
    /// </remarks>
    public override void Delete()
    {
        (IvpCollisionObject first, IvpCollisionObject second) = Objects;
        IvpCollisionEnvironment environment = first.Environment ?? throw new InvalidOperationException("A mindist's object has no environment.");

        environment.LiveMindists--;
        environment.DeletedMindists++;

        if ((Flags & PhantomBits) != 0)
        {
            Flags &= PhantomKeeps;

            if (first.HasPhantom || second.HasPhantom)
            {
                throw new InvalidOperationException("A deleted mindist tells its objects' phantoms through FUN_18008b0a0, which is not ported.");
            }
        }

        switch ((Flags << 10) >> 28)
        {
            case InvalidState:
                environment.MindistManager.UnlinkInvalid(this);
                break;
            case ExactState:
                environment.MindistManager.Unlink(this, environment.EventQueue);
                break;
            case RecursiveState:
            case FiledState:
                first.Hull.Remove(_hullRecords[0]);
                second.Hull.Remove(_hullRecords[1]);
                break;
        }

        Delegator?.CollisionRemoved(this);
    }

    /// <summary>Slot 7 (<c>+0x38</c>): what a frozen minimize makes of the mindist — <c>FUN_180097440</c> for a plain one.</summary>
    /// <param name="manager">The environment's mindist manager.</param>
    /// <param name="queue">The time manager's queue, which an exact mindist leaves when it is unfiled.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">A record has no object, or the mindist is not exact.</exception>
    /// <remarks>
    /// **Called through the mindist's own table** — by `FUN_1800977f0` at `180097914`, by `FUN_1800983e0` and by `FUN_180098710` — so
    /// a plain mindist is unfiled and made invalid (<see cref="IvpMindistManager.Invalidate"/>) and a larger one opens its ledge
    /// instead.
    /// </remarks>
    public virtual void Freeze(IvpMindistManager manager, IvpMinList<IvpMindist> queue)
    {
        ArgumentNullException.ThrowIfNull(manager);

        (IvpCollisionObject first, IvpCollisionObject second) = Objects;

        manager.Invalidate(this, first, second, queue);
    }

    /// <summary>Slot 8 (<c>+0x40</c>): the collision event <c>FUN_1800992e0</c> raises — <c>FUN_18008ecb0</c> for a plain one.</summary>
    /// <param name="impact">The plain mindist's collision, <c>FUN_18008ecb0</c>, which is not ported and is handed in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="impact"/> is null.</exception>
    /// <remarks>A larger mindist's slot 8 opens its ledge when the contact is on a hull's face or edge, and runs this otherwise.</remarks>
    public virtual void Collide(Action<IvpMindist> impact)
    {
        ArgumentNullException.ThrowIfNull(impact);

        impact(this);
    }
}

/// <summary>What <c>FUN_180095cb0</c> returns, and whether it called the mindist's virtual <c>+0x28</c>.</summary>
/// <param name="Result">1 settled, 2 gave up, 3 backside, 4 already minimized this step.</param>
/// <param name="NotifiesEnvironment">
/// Whether a result other than settled called the mindist's virtual <c>+0x28</c>, which for a plain mindist hands it to
/// the environment's <c>+0x40</c> object (<c>FUN_1800947e0</c>). <i>What that object does with it is not read.</i>
/// </param>
public readonly record struct IvpMinimizeOutcome(int Result, bool NotifiesEnvironment);

/// <summary>The minimize's loop check — <c>FUN_180094600</c>'s pair store.</summary>
/// <remarks>
/// **Consulted only once the step budget is spent.** The engine ORs each feature's address with its kind, orders the pair by
/// signed value and scans its keys; a pair already stored, or a store of 256, is reported seen, and anything else is
/// stored. The keys here carry the side too, since two ledges' addresses are not one address space.
/// </remarks>
internal sealed class IvpMinimizeLoopCheck
{
    private const int Capacity = 256;

    private readonly List<(long High, long Low)> _pairs = [];

    /// <summary>Whether a pair of keys has been seen, storing it if not.</summary>
    /// <param name="first">One feature's key.</param>
    /// <param name="second">The other's.</param>
    /// <returns>True when the pair is stored already or the store is full.</returns>
    public bool Seen(long first, long second)
    {
        (long High, long Low) pair = first >= second ? (first, second) : (second, first);

        for (int index = _pairs.Count - 1; index >= 0; index--)
        {
            if (_pairs[index] == pair)
            {
                return true;
            }
        }

        if (_pairs.Count >= Capacity)
        {
            return true;
        }

        _pairs.Add(pair);
        return false;
    }
}

/// <summary>
/// IVP's minimize: the closest features of a mindist's two ledges at the current time — <c>ivp_mindist_minimize.cxx</c>
/// (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, routine by routine** (`docs/findings/51`, *The minimize, routine by routine*): the entry
/// `FUN_180095cb0`, the dispatchers `FUN_180094c70` and `FUN_180094f70`, the eight feature routines and the backside walk
/// `FUN_180094e30`. Each routine is a method named for the pair it measures; they call each other as the engine's do,
/// flipping the flags' bit 8 whenever the next call puts the other side first.
/// </remarks>
public static class IvpMindistMinimize
{
    /// <summary>The features are the closest pair; the length and normal are theirs.</summary>
    public const int Settled = 1;

    /// <summary>The budget ran out on a pair already visited, or two points coincided. <i>The name is INFERRED.</i></summary>
    public const int GaveUp = 2;

    /// <summary>A point was found behind a face; the solver's point is where. <i>The name is INFERRED.</i></summary>
    public const int Backside = 3;

    /// <summary>The minimize already ran in this step.</summary>
    public const int AlreadyMinimized = 4;

    /// <summary><c>FUN_180095cb0</c>'s <c>MOV [RSP+0x28], 0x14</c>: the steps taken before the loop check is consulted.</summary>
    public const int StepBudget = 20;

    /// <summary>Minimizes a mindist's features at the current time, as <c>FUN_180095cb0</c> does.</summary>
    /// <param name="mindist">The mindist, updated in place.</param>
    /// <param name="first">The side synapse record 0 belongs to.</param>
    /// <param name="second">The side synapse record 1 belongs to.</param>
    /// <param name="step">The environment's step counter, <c>env+0x1a0</c>.</param>
    /// <returns>The result, and whether the environment was notified.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The engine would assert: an unrouted pair of kinds, or an unknown result.</exception>
    /// <remarks>
    /// Settled clears bits 14–15 and returns. Anything else sets bit 14 and clears bit 15; a backside has its synapse walked
    /// to a triangle and the dispatch retried, at most twice; and unless `flags &amp; 0x3000` is `0x1000` or
    /// `flags &amp; 0x3C0000` is `0x100000`, the mindist's virtual `+0x28` runs.
    /// </remarks>
    public static IvpMinimizeOutcome Minimize(IvpMindist mindist, IvpLedgeSide first, IvpLedgeSide second, int step) =>
        Minimize(mindist, first, second, step, StepBudget);

    /// <summary>
    /// The minimize with a given step budget: <c>FUN_180095cb0</c>'s is <see cref="StepBudget"/>, and <c>FUN_180095ad0</c> — the
    /// same routine instruction for instruction but for <c>MOV [RSP+0x28], 0</c> — has none, so its loop check runs from the first
    /// step.
    /// </summary>
    /// <param name="mindist">The mindist, updated in place.</param>
    /// <param name="first">The side synapse record 0 belongs to.</param>
    /// <param name="second">The side synapse record 1 belongs to.</param>
    /// <param name="step">The environment's step counter, <c>env+0x1a0</c>.</param>
    /// <param name="budget">The steps taken before the loop check is consulted.</param>
    /// <returns>The result, and whether the environment was notified.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The engine would assert: an unrouted pair of kinds, or an unknown result.</exception>
    public static IvpMinimizeOutcome Minimize(IvpMindist mindist, IvpLedgeSide first, IvpLedgeSide second, int step, int budget)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (mindist.MinimizedAt == step)
        {
            return new IvpMinimizeOutcome(AlreadyMinimized, false);
        }

        mindist.MinimizedAt = step;

        Solver solver = new(mindist, first, second, budget);
        int retries = 0;
        int result;

        while (true)
        {
            result = solver.Dispatch();

            if (result == Settled)
            {
                mindist.Flags &= ~0xC000;
                return new IvpMinimizeOutcome(Settled, false);
            }

            mindist.Flags = (mindist.Flags & ~0x8000) | 0x4000;

            if (result == GaveUp)
            {
                break;
            }

            if (result != Backside)
            {
                throw new InvalidOperationException("The minimize returned a result it has no path for (ivp_mindist_minimize.cxx:312).");
            }

            int a = solver.A;
            int behind = mindist.Synapse(a).Kind == IvpFeatureKind.Backside ? a : 1 - a;
            IvpLedgeEdge walked = BacksideWalk(solver.Side(behind), mindist.Synapse(behind).Feature, solver.Point);
            mindist.SetSynapse(behind, new IvpSynapse(walked, IvpFeatureKind.Triangle));

            if (++retries >= 2)
            {
                break;
            }
        }

        int flags = mindist.Flags;
        bool notifies = (flags & 0x3000) != 0x1000 && (flags & 0x3C0000) != 0x100000;

        return new IvpMinimizeOutcome(result, notifies);
    }

    /// <summary>The triangle a backside walk stops in — <c>FUN_180094e30</c>.</summary>
    /// <param name="side">The side the backside feature belongs to.</param>
    /// <param name="feature">The backside feature.</param>
    /// <param name="point">Where the other feature was found, in the side's frame.</param>
    /// <returns>The edge the walk stopped in.</returns>
    /// <remarks>
    /// From the first edge of the triangle the feature's header names, each triangle entered is marked, and of its edge,
    /// next and previous in turn the first whose weight is at or below zero (`COMISS 0, w; JC`, so NaN does not move) and
    /// whose twin's triangle is unmarked is crossed. A triangle with no such edge ends the walk.
    /// </remarks>
    internal static IvpLedgeEdge BacksideWalk(IvpLedgeSide side, IvpLedgeEdge feature, (double X, double Y, double Z) point)
    {
        IvpLedgeTopology topology = side.Topology;
        bool[] entered = new bool[topology.TriangleCount];
        IvpLedgeEdge current = topology.Pierce(feature);

        while (true)
        {
            entered[current.Triangle] = true;

            IvpTriangleWeights weights = IvpCompactLedgeSolver.TriangleWeights(side, current, point);
            float[] inOrder = [weights.Edge, weights.Next, weights.Previous];
            IvpLedgeEdge edge = current;
            IvpLedgeEdge? crossed = null;

            foreach (float weight in inOrder)
            {
                if (weight <= 0f)
                {
                    IvpLedgeEdge twin = topology.Hop(edge);

                    if (!entered[twin.Triangle])
                    {
                        crossed = twin;
                        break;
                    }
                }

                edge = topology.Next(edge);
            }

            if (crossed is not IvpLedgeEdge next)
            {
                return current;
            }

            current = next;
        }
    }

    /// <summary>The solver structure <c>FUN_180095cb0</c> keeps on its stack, and the routines that share it.</summary>
    private sealed class Solver
    {
        /// <summary><c>DAT_1800f4f28</c>: the point-point routine's squared-distance floor, and the edge ring's scale.</summary>
        private const double Coincident = 1e-12d;

        /// <summary><c>DAT_1800f4f20</c>: the point-edge routine's squared-distance floor.</summary>
        private const double OnTheLine = IvpVector.DirectionThreshold;

        /// <summary><c>DAT_1800fb100</c>: below this steepest rise the point-edge routine checks the edges cross first.</summary>
        private const double ShallowRise = 1e-8d;

        /// <summary><c>DAT_1800fe958</c>: the edge-edge routine's starting cosine.</summary>
        private const double CosineStart = -4e-12d;

        /// <summary><c>DAT_1800eedc0</c>: the searches' starting minimum.</summary>
        private const double Unreached = 1e101d;

        /// <summary><c>DAT_1800fde98</c>: the face-face routine's handicap on every candidate after the point pairs.</summary>
        private const double Handicap = 1.000000000001d;

        /// <summary><c>DAT_1800fd268</c>: <c>1e-18f</c>, added to a rising edge's squared length.</summary>
        private const double RiseFloor = (double)1e-18f;

        private readonly IvpMindist _mindist;
        private readonly IvpLedgeSide[] _sides;
        private readonly IvpMinimizeLoopCheck _loop = new();
        private int _budget;

        // budget: the step budget at solver+0x08.
        public Solver(IvpMindist mindist, IvpLedgeSide first, IvpLedgeSide second, int budget)
        {
            _mindist = mindist;
            _sides = [first, second];
            _budget = budget;
        }

        /// <summary>The point at <c>solver+0x10</c>, written with a backside.</summary>
        public (double X, double Y, double Z) Point { get; private set; }

        /// <summary>The synapse record the flags name as synapse A.</summary>
        /// <exception cref="InvalidOperationException">Bit 9 is set, naming a record past the two.</exception>
        public int A
        {
            get
            {
                int a = _mindist.SynapseA;

                if (a > 1)
                {
                    throw new InvalidOperationException("A mindist's flags name a synapse record past its two.");
                }

                return a;
            }
        }

        public IvpLedgeSide Side(int index) => _sides[index];

        /// <summary><c>DAT_18012d4b0[kind(B) + 4·kind(A)]</c>.</summary>
        public int Dispatch()
        {
            int a = A;
            IvpFeatureKind kindA = _mindist.Synapse(a).Kind;
            IvpFeatureKind kindB = _mindist.Synapse(1 - a).Kind;

            switch ((kindA, kindB))
            {
                case (IvpFeatureKind.Point, IvpFeatureKind.Point):
                case (IvpFeatureKind.Point, IvpFeatureKind.Edge):
                case (IvpFeatureKind.Point, IvpFeatureKind.Triangle):
                case (IvpFeatureKind.Edge, IvpFeatureKind.Edge):
                case (IvpFeatureKind.Triangle, IvpFeatureKind.Triangle):
                    return Route();

                case (IvpFeatureKind.Edge, IvpFeatureKind.Point):
                case (IvpFeatureKind.Triangle, IvpFeatureKind.Point):
                    // FUN_180094f70: flip bit 8 and route, so the point is first.
                    _mindist.Flags ^= 0x100;
                    return Route();

                default:
                    throw new InvalidOperationException(
                        $"The minimize's table does not route a {kindA} against a {kindB} (FUN_180094e10's assertion, or a ball).");
            }
        }

        /// <summary><c>FUN_180094c70</c>: both sides built, and routed again on the same index.</summary>
        private int Route()
        {
            int a = A;
            int b = 1 - a;
            IvpSynapse first = _mindist.Synapse(a);
            IvpSynapse second = _mindist.Synapse(b);

            return (first.Kind, second.Kind) switch
            {
                (IvpFeatureKind.Point, IvpFeatureKind.Point) => PointPoint(first.Feature, second.Feature, a, b),
                (IvpFeatureKind.Point, IvpFeatureKind.Edge) => PointEdge(first.Feature, second.Feature, a, b),
                (IvpFeatureKind.Point, IvpFeatureKind.Triangle) => PointFace(first.Feature, second.Feature, a, b),
                (IvpFeatureKind.Edge, IvpFeatureKind.Edge) => EdgeEdge(first.Feature, second.Feature, a, b),
                _ => FaceFace(first.Feature, second.Feature, a, b),
            };
        }

        /// <summary><c>FUN_1800b1b80</c>: a point against a point.</summary>
        private int PointPoint(IvpLedgeEdge p, IvpLedgeEdge q, int sideP, int sideQ)
        {
            if (LoopsBack(sideP, p, IvpFeatureKind.Point, sideQ, q, IvpFeatureKind.Point))
            {
                return GaveUp;
            }

            IvpLedgeSide sp = _sides[sideP];
            IvpLedgeSide sq = _sides[sideQ];

            (double X, double Y, double Z) wp = IvpCompactLedgeSolver.PointInWorld(sp, p);
            (double X, double Y, double Z) wq = IvpCompactLedgeSolver.PointInWorld(sq, q);
            (double X, double Y, double Z) pInQ = sq.Current.ToObject(wp);
            (double X, double Y, double Z) qInP = sp.Current.ToObject(wq);

            double x = wp.X - wq.X;
            double y = wp.Y - wq.Y;
            double z = wp.Z - wq.Z;
            double squared = (y * y) + (x * x) + (z * z);

            if (!(squared > Coincident))
            {
                return GaveUp;
            }

            double scale = IvpVector.ReciprocalSquareRoot(squared, IvpVector.FiveSteps);
            _mindist.Length = (float)((scale * squared) - _mindist.ExtraRadius);
            _mindist.Normal = ((float)(x * scale), (float)(y * scale), (float)(z * scale));
            WriteContactDot(sideP, sideQ);

            double steepest = 0d;
            IvpLedgeEdge? best = null;
            int bestSide = sideP;
            (double X, double Y, double Z) otherPoint = qInP;
            IvpLedgeEdge otherFeature = q;
            int otherSide = sideQ;

            if (Steepest(sp, p, qInP, ref steepest) is IvpLedgeEdge fromP)
            {
                best = fromP;
            }

            if (Steepest(sq, q, pInQ, ref steepest) is IvpLedgeEdge fromQ)
            {
                best = fromQ;
                bestSide = sideQ;
                otherPoint = pInQ;
                otherFeature = p;
                otherSide = sideP;
            }

            if (best is not IvpLedgeEdge edge)
            {
                Settle(sideP, p, IvpFeatureKind.Point, sideQ, q, IvpFeatureKind.Point);
                return Settled;
            }

            IvpEdgeWeights weights = IvpCompactLedgeSolver.EdgeWeights(_sides[bestSide], edge, otherPoint);

            if (!(weights.End > 0f))
            {
                Settle(sideP, p, IvpFeatureKind.Point, sideQ, q, IvpFeatureKind.Point);
                return Settled;
            }

            if (!(weights.Start >= 0f))
            {
                FlipFor(bestSide);
                return PointPoint(edge, otherFeature, bestSide, otherSide);
            }

            FlipFor(otherSide);
            return PointEdgeProximity(otherFeature, edge, otherSide, bestSide);
        }

        /// <summary><c>FUN_1800b1aa0</c>: a point against an edge, handed on by where it projects.</summary>
        private int PointEdge(IvpLedgeEdge p, IvpLedgeEdge k, int sideP, int sideK)
        {
            IvpLedgeSide sk = _sides[sideK];
            (double X, double Y, double Z) point = IvpCompactLedgeSolver.PointInFrame(_sides[sideP], p, sk);
            IvpEdgeWeights weights = IvpCompactLedgeSolver.EdgeWeights(sk, k, point);

            if (!(weights.Start >= 0f))
            {
                return PointPoint(p, k, sideP, sideK);
            }

            if (!(weights.End >= 0f))
            {
                return PointPoint(p, sk.Topology.Next(k), sideP, sideK);
            }

            return PointEdgeProximity(p, k, sideP, sideK);
        }

        /// <summary><c>FUN_1800b1910</c>: a point against a face, handed on by where it projects.</summary>
        private int PointFace(IvpLedgeEdge p, IvpLedgeEdge f, int sideP, int sideF)
        {
            IvpLedgeSide sf = _sides[sideF];
            IvpLedgeTopology topology = sf.Topology;
            (double X, double Y, double Z) point = IvpCompactLedgeSolver.PointInFrame(_sides[sideP], p, sf);

            if (IvpCompactLedgeSolver.TriangleWeights(sf, f, point).Inside)
            {
                return PointFaceProximity(p, point, f, sideP, sideF);
            }

            double first = IvpCompactLedgeSolver.SegmentDistanceSquared(sf, f, point);
            double least = !(first >= Unreached) ? first : Unreached;
            IvpLedgeEdge? best = first >= Unreached ? null : f;
            double afterFirst = least;

            IvpLedgeEdge next = topology.Next(f);
            double second = IvpCompactLedgeSolver.SegmentDistanceSquared(sf, next, point);

            if (!(second >= least))
            {
                least = second;
            }

            if (afterFirst > second)
            {
                best = next;
            }

            IvpLedgeEdge last = topology.Next(next);

            if (least > IvpCompactLedgeSolver.SegmentDistanceSquared(sf, last, point))
            {
                best = last;
            }

            return PointEdge(p, best ?? throw Unreachable("PointFace found no edge"), sideP, sideF);
        }

        /// <summary><c>FUN_1800afa40</c>: an edge against an edge, handed on by where they cross.</summary>
        private int EdgeEdge(IvpLedgeEdge k, IvpLedgeEdge l, int sideK, int sideL)
        {
            IvpLedgeSide sk = _sides[sideK];
            IvpLedgeSide sl = _sides[sideL];
            IvpEdgeEdgeInput input = IvpCompactLedgeSolver.EdgeEdge(k, sk, l, sl);
            IvpEdgeEdgeWeights weights = IvpCompactLedgeSolver.EdgeEdgeWeights(input);

            if ((Bits(weights.LStart) | Bits(weights.LEnd)) >= 0)
            {
                if (!(weights.KStart >= 0f))
                {
                    return PointEdge(k, l, sideK, sideL);
                }

                if (!(weights.KEnd >= 0f))
                {
                    return PointEdge(sk.Topology.Next(k), l, sideK, sideL);
                }

                return EdgeEdgeProximity(input, weights, sideK, sideL);
            }

            if ((Bits(weights.KStart) | Bits(weights.KEnd)) >= 0)
            {
                FlipFor(sideL);

                return weights.LStart >= 0f
                    ? PointEdge(sl.Topology.Next(l), k, sideL, sideK)
                    : PointEdge(l, k, sideL, sideK);
            }

            IvpLedgeEdge kNear = weights.KStart >= weights.KEnd ? sk.Topology.Next(k) : k;
            IvpLedgeEdge kFar = weights.KStart >= weights.KEnd ? k : sk.Topology.Next(k);
            IvpLedgeEdge lNear = weights.LStart >= weights.LEnd ? sl.Topology.Next(l) : l;
            IvpLedgeEdge lFar = weights.LStart >= weights.LEnd ? l : sl.Topology.Next(l);

            IvpEdgeWeights kAgainstL =
                IvpCompactLedgeSolver.EdgeWeights(sl, l, IvpCompactLedgeSolver.PointInFrame(sk, kNear, sl));

            if (kAgainstL.Inside)
            {
                return PointEdge(kNear, l, sideK, sideL);
            }

            IvpEdgeWeights lAgainstK =
                IvpCompactLedgeSolver.EdgeWeights(sk, k, IvpCompactLedgeSolver.PointInFrame(sl, lNear, sk));

            if (lAgainstK.Inside)
            {
                FlipFor(sideL);
                return PointEdge(lNear, k, sideL, sideK);
            }

            if (!(kAgainstL.Start * weights.LStart >= 0f))
            {
                return PointPoint(kNear, lFar, sideK, sideL);
            }

            if (!(lAgainstK.Start * weights.KStart >= 0f))
            {
                FlipFor(sideL);
                return PointPoint(lNear, kFar, sideL, sideK);
            }

            return PointPoint(kNear, lNear, sideK, sideL);
        }

        /// <summary><c>FUN_1800b11c0</c>: a point in an edge's region.</summary>
        private int PointEdgeProximity(IvpLedgeEdge p, IvpLedgeEdge k, int sideP, int sideK)
        {
            if (LoopsBack(sideP, p, IvpFeatureKind.Point, sideK, k, IvpFeatureKind.Edge))
            {
                return GaveUp;
            }

            IvpLedgeSide sp = _sides[sideP];
            IvpLedgeSide sk = _sides[sideK];
            IvpLedgeTopology topology = sk.Topology;

            (double X, double Y, double Z) point = IvpCompactLedgeSolver.PointInFrame(sp, p, sk);

            (float X, float Y, float Z) start = sk.StartOf(k);
            IvpLedgeEdge twin = topology.Hop(k);

            (double X, double Y, double Z) along = FloatDifference(sk.EndOf(k), start);
            (double X, double Y, double Z) from = (point.X - start.X, point.Y - start.Y, point.Z - start.Z);
            (double X, double Y, double Z) toThird = FloatDifference(sk.StartOf(topology.Previous(k)), start);
            (double X, double Y, double Z) toTwinsThird = FloatDifference(sk.StartOf(topology.Previous(twin)), start);

            (double X, double Y, double Z) faceNormal = IvpVector.Cross(along, toThird);
            (double X, double Y, double Z) twinsNormal = IvpVector.Cross(toTwinsThird, along);

            double overFace = (faceNormal.Y * from.Y) + (faceNormal.X * from.X) + (faceNormal.Z * from.Z);
            double overTwin = (twinsNormal.Y * from.Y) + (twinsNormal.X * from.X) + (twinsNormal.Z * from.Z);

            IvpTriangleWeights face = IvpCompactLedgeSolver.TriangleWeights(sk, k, point);
            IvpTriangleWeights twinFace = IvpCompactLedgeSolver.TriangleWeights(sk, twin, point);

            if (face.Edge > 0f)
            {
                return PointFace(p, twinFace.Edge > 0f && overTwin > 0d ? twin : k, sideP, sideK);
            }

            if (twinFace.Edge > 0f)
            {
                return PointFace(p, twin, sideP, sideK);
            }

            if (!(overFace >= 0d) && !(overTwin >= 0d))
            {
                Settle(sideP, p, IvpFeatureKind.Point, sideK, k, IvpFeatureKind.Backside);
                Point = point;
                return Backside;
            }

            (double X, double Y, double Z) across = IvpVector.Cross(along, from);
            double inverse = 1d / ((along.Y * along.Y) + (along.X * along.X) + (along.Z * along.Z));
            double squared = ((across.X * across.X) + (across.Y * across.Y) + (across.Z * across.Z)) * inverse;

            (double X, double Y, double Z) direction;

            if (squared > OnTheLine)
            {
                double scale = IvpVector.ReciprocalSquareRoot(squared, IvpVector.FiveSteps);
                _mindist.Length = (float)((scale * squared) - _mindist.ExtraRadius);

                (double X, double Y, double Z) turned = sk.Current.Rotate(IvpVector.Cross(along, across));
                double toUnit = -(scale * inverse);
                _mindist.Normal = ((float)(turned.X * toUnit), (float)(turned.Y * toUnit), (float)(turned.Z * toUnit));
                direction = turned;
            }
            else
            {
                _mindist.Length = -_mindist.ExtraRadius;

                (double X, double Y, double Z) sideways = IvpVector.Perpendicular(along);
                IvpVector.TryScaleToUnitLength(ref sideways, IvpVector.FiveSteps);

                // Written in the edge's frame and not turned into the world: the engine does not.
                _mindist.Normal = ((float)sideways.X, (float)sideways.Y, (float)sideways.Z);
                direction = across;
            }

            WriteContactDot(sideP, sideK);

            (double X, double Y, double Z) up = sp.Current.RotateInverse(direction);
            double limit = squared * Coincident;
            IvpLedgeEdge? best = null;
            (float X, float Y, float Z) vertex = sp.StartOf(p);

            foreach (IvpLedgeEdge edge in sp.Topology.EndingAt(p))
            {
                (double X, double Y, double Z) toNeighbor = FloatDifference(sp.StartOf(edge), vertex);
                double rise = (up.Y * toNeighbor.Y) + (up.X * toNeighbor.X) + (up.Z * toNeighbor.Z);

                if (rise > 0d)
                {
                    rise *= IvpVector.ReciprocalSquareRoot((float)SquaredLength(toNeighbor));

                    if (rise > limit)
                    {
                        limit = rise;
                        best = edge;
                    }
                }
            }

            if (best is not IvpLedgeEdge rising)
            {
                Settle(sideP, p, IvpFeatureKind.Point, sideK, k, IvpFeatureKind.Edge);
                return Settled;
            }

            if (!(limit >= ShallowRise))
            {
                IvpEdgeEdgeWeights crossing =
                    IvpCompactLedgeSolver.EdgeEdgeWeights(IvpCompactLedgeSolver.EdgeEdge(k, sk, rising, sp));

                if (!crossing.Crossing || !(crossing.LStart >= 0f))
                {
                    Settle(sideP, p, IvpFeatureKind.Point, sideK, k, IvpFeatureKind.Edge);
                    return Settled;
                }
            }

            return EdgeEdge(rising, k, sideP, sideK);
        }

        /// <summary><c>FUN_1800b0c20</c>: a point in a face's region.</summary>
        private int PointFaceProximity(
            IvpLedgeEdge p, (double X, double Y, double Z) point, IvpLedgeEdge f, int sideP, int sideF)
        {
            if (LoopsBack(sideP, p, IvpFeatureKind.Point, sideF, f, IvpFeatureKind.Triangle))
            {
                return GaveUp;
            }

            IvpLedgeSide sp = _sides[sideP];
            IvpLedgeSide sf = _sides[sideF];
            IvpLedgeTopology topology = sf.Topology;

            (float X, float Y, float Z) origin = sf.StartOf(f);
            (double X, double Y, double Z) normal = sf.FaceNormal(f);
            IvpVector.TryScaleToUnitLength(ref normal);

            (double X, double Y, double Z) world = sf.Current.Rotate(normal);
            (double X, double Y, double Z) up = sp.Current.RotateInverse(world);

            double height = ((normal.Y * point.Y) + (normal.X * point.X) + (normal.Z * point.Z)) -
                            ((origin.Y * normal.Y) + (origin.X * normal.X) + (origin.Z * normal.Z));

            _mindist.Length = (float)height;
            _mindist.Normal = ((float)world.X, (float)world.Y, (float)world.Z);

            if (0f > _mindist.Length)
            {
                up = (up.X * -1d, up.Y * -1d, up.Z * -1d);
            }

            _mindist.Length -= _mindist.ExtraRadius;
            WriteContactDot(sideP, sideF);

            IvpLedgeEdge? best = null;
            double lowest = 0d;
            (float X, float Y, float Z) vertex = sp.StartOf(p);

            foreach (IvpLedgeEdge edge in sp.Topology.EndingAt(p))
            {
                (double X, double Y, double Z) toNeighbor = FloatDifference(sp.StartOf(edge), vertex);
                double rise = (up.Y * toNeighbor.Y) + (up.X * toNeighbor.X) + (up.Z * toNeighbor.Z);

                if (!(rise >= 0d))
                {
                    rise *= IvpVector.ReciprocalSquareRoot((float)SquaredLength(toNeighbor));

                    if (!(rise >= lowest))
                    {
                        lowest = rise;
                        best = edge;
                    }
                }
            }

            if (best is not IvpLedgeEdge falling)
            {
                Settle(sideP, p, IvpFeatureKind.Point, sideF, f, IvpFeatureKind.Triangle);

                if (!(_mindist.Length + _mindist.ExtraRadius >= 0f))
                {
                    Point = point;
                    _mindist.SetSynapse(sideF, new IvpSynapse(f, IvpFeatureKind.Backside));
                    return Backside;
                }

                return Settled;
            }

            (double X, double Y, double Z) neighbor = IvpCompactLedgeSolver.PointInFrame(sp, falling, sf);
            IvpTriangleWeights weights = IvpCompactLedgeSolver.TriangleWeights(sf, f, neighbor);

            if (weights.Inside)
            {
                return PointFaceProximity(falling, neighbor, f, sideP, sideF);
            }

            float[] byEdge = [weights.Edge, weights.Next, weights.Previous];
            int outside = 0;

            foreach (float weight in byEdge)
            {
                if (!(weight >= 0f))
                {
                    outside++;
                }
            }

            if (outside == 1)
            {
                IvpLedgeEdge candidate = f;

                foreach (float weight in byEdge)
                {
                    if (0f > weight)
                    {
                        return EdgeEdge(falling, candidate, sideP, sideF);
                    }

                    candidate = topology.Next(candidate);
                }
            }

            double least = Unreached;
            IvpLedgeEdge? pick = null;
            IvpLedgeEdge next = topology.Next(f);
            IvpLedgeEdge last = topology.Next(next);

            if (!(weights.Edge > 0f))
            {
                double distance = IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(falling, sp, f, sf);

                if (!(distance >= least))
                {
                    least = distance;
                    pick = f;
                }
            }

            if (!(weights.Next > 0f))
            {
                double distance = IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(falling, sp, next, sf);

                if (!(distance >= least))
                {
                    least = distance;
                    pick = next;
                }
            }

            if (!(weights.Previous > 0f) && least > IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(falling, sp, last, sf))
            {
                pick = last;
            }

            return EdgeEdge(falling, pick ?? throw Unreachable("PointFaceProximity found no edge"), sideP, sideF);
        }

        /// <summary><c>FUN_1800b0280</c>: two edges in each other's region.</summary>
        private int EdgeEdgeProximity(IvpEdgeEdgeInput input, IvpEdgeEdgeWeights weights, int sideK, int sideL)
        {
            IvpLedgeEdge k = input.K;
            IvpLedgeEdge l = input.L;

            if (LoopsBack(sideK, k, IvpFeatureKind.Edge, sideL, l, IvpFeatureKind.Edge))
            {
                return GaveUp;
            }

            IvpLedgeSide sk = _sides[sideK];
            IvpLedgeSide sl = _sides[sideL];
            IvpLedgeTopology kTopology = sk.Topology;
            IvpLedgeTopology lTopology = sl.Topology;

            (double X, double Y, double Z) across = input.Cross;
            (float X, float Y, float Z) lStart = input.LStart;
            (double X, double Y, double Z) kStart = input.KStart;

            float apart = (float)(((lStart.Y - kStart.Y) * across.Y) + ((lStart.X - kStart.X) * across.X) +
                                  ((lStart.Z - kStart.Z) * across.Z));
            int sign = SignBit(apart);

            double scale = IvpVector.ReciprocalSquareRoot(SquaredLength(across), IvpVector.FiveSteps);
            (double X, double Y, double Z) world = sl.Current.Rotate(across);
            (double X, double Y, double Z) acrossInK = sk.Current.RotateInverse(world);

            _mindist.Length = (float)Math.Abs((double)apart * scale);

            float bit = sign;
            float toward = ((bit - 0.5f) + bit) - 0.5f;

            _mindist.Length -= _mindist.ExtraRadius;

            double toUnit = (double)toward * scale;
            _mindist.Normal = ((float)(world.X * toUnit), (float)(world.Y * toUnit), (float)(world.Z * toUnit));
            WriteContactDot(sideK, sideL);

            (double X, double Y, double Z) lStartInK = IvpCompactLedgeSolver.PointInFrame(lStart, sl, sk);
            (double X, double Y, double Z) lEndInK = IvpCompactLedgeSolver.PointInFrame(input.LEnd, sl, sk);

            Region[] regions =
            [
                new(kTopology.Hop(k), sideK, lStartInK, sideL, l, acrossInK, Inverted: false),
                new(k, sideK, lEndInK, sideL, lTopology.Hop(l), acrossInK, Inverted: false),
                new(lTopology.Hop(l), sideL, kStart, sideK, k, across, Inverted: true),
                new(l, sideL, input.KEnd, sideK, kTopology.Hop(k), across, Inverted: true),
            ];

            (double X, double Y, double Z)[] normals = new (double X, double Y, double Z)[regions.Length];
            int[] facing = new int[regions.Length];

            for (int index = 0; index < regions.Length; index++)
            {
                Region region = regions[index];
                IvpLedgeSide owner = _sides[region.TriangleSide];

                (double X, double Y, double Z) faceNormal = owner.FaceNormal(region.Triangle);

                normals[index] = faceNormal;

                int bits = BitConverter.SingleToInt32Bits((float)(
                    (faceNormal.Y * region.Against.Y) + (faceNormal.X * region.Against.X) +
                    (faceNormal.Z * region.Against.Z)));

                facing[index] = (region.Inverted ? (int)((uint)~bits >> 31) : (int)((uint)bits >> 31)) ^ sign;
            }

            int chosen = -1;
            IvpTriangleWeights chosenWeights = default;
            int chosenRegion = -1;
            double cosine = CosineStart;

            for (int index = 0; index < regions.Length; index++)
            {
                int pointIndex = facing[index] ^ sign ^ index;
                (double X, double Y, double Z) from = regions[sign ^ index].Point;
                (double X, double Y, double Z) to = regions[sign ^ index ^ 1].Point;
                (double X, double Y, double Z) edge = (from.X - to.X, from.Y - to.Y, from.Z - to.Z);
                (double X, double Y, double Z) faceNormal = normals[index];

                double along = (faceNormal.Y * edge.Y) + (faceNormal.X * edge.X) + (faceNormal.Z * edge.Z);

                if (along >= 0d)
                {
                    continue;
                }

                double normalScale = IvpVector.ReciprocalSquareRoot((float)(
                    (faceNormal.X * faceNormal.X) + (faceNormal.Y * faceNormal.Y) + (faceNormal.Z * faceNormal.Z)));
                double edgeScale = IvpVector.ReciprocalSquareRoot((float)SquaredLength(edge));
                double candidate = (edgeScale * along) * normalScale;

                if (candidate >= cosine)
                {
                    continue;
                }

                Region region = regions[index];
                IvpTriangleWeights inside = IvpCompactLedgeSolver.TriangleWeights(
                    _sides[region.TriangleSide], region.Triangle, regions[pointIndex].Point);

                if (inside.Edge > 0f)
                {
                    chosen = pointIndex;
                    chosenRegion = index;
                    chosenWeights = inside;
                    cosine = candidate;
                }
            }

            if (chosen < 0)
            {
                Settle(sideK, k, IvpFeatureKind.Edge, sideL, l, IvpFeatureKind.Edge);

                if (facing[0] + facing[1] == 2)
                {
                    Settle(sideK, k, IvpFeatureKind.Backside, sideL, l, IvpFeatureKind.Triangle);
                    Point = IvpVector.Lerp(lStartInK, lEndInK, weights.LStart / (weights.LStart + weights.LEnd));
                    return Backside;
                }

                if (facing[2] + facing[3] == 2)
                {
                    Settle(sideK, k, IvpFeatureKind.Triangle, sideL, l, IvpFeatureKind.Backside);
                    Point = IvpVector.Lerp(input.KStart, input.KEnd, weights.KStart / (weights.KStart + weights.KEnd));
                    return Backside;
                }

                return Settled;
            }

            Region pointRegion = regions[chosen];
            Region triangleRegion = regions[chosenRegion];
            IvpLedgeEdge pointEdge = pointRegion.PointEdge;
            int pointSide = pointRegion.PointSide;
            IvpLedgeEdge triangle = triangleRegion.Triangle;
            int triangleSide = triangleRegion.TriangleSide;
            IvpLedgeTopology triangleTopology = _sides[triangleSide].Topology;

            FlipFor(pointSide);

            if (chosenWeights.Inside)
            {
                (double X, double Y, double Z) inFace =
                    IvpCompactLedgeSolver.PointInFrame(_sides[pointSide], pointEdge, _sides[triangleSide]);

                return PointFaceProximity(pointEdge, inFace, triangle, pointSide, triangleSide);
            }

            if (chosenWeights.Previous >= 0f)
            {
                return EdgeEdge(pointEdge, triangleTopology.Next(triangle), pointSide, triangleSide);
            }

            if (chosenWeights.Next >= 0f)
            {
                return EdgeEdge(pointEdge, triangleTopology.Previous(triangle), pointSide, triangleSide);
            }

            double toNext = IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(
                pointEdge, _sides[pointSide], triangleTopology.Next(triangle), _sides[triangleSide]);
            double toPrevious = IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(
                pointEdge,
                _sides[pointSide],
                triangleTopology.Hop(triangleTopology.Previous(triangle)),
                _sides[triangleSide]);

            return EdgeEdge(
                pointEdge,
                toNext > toPrevious ? triangleTopology.Previous(triangle) : triangleTopology.Next(triangle),
                pointSide,
                triangleSide);
        }

        /// <summary><c>FUN_180094f80</c>: two faces, reduced to their closest feature pair and handed on.</summary>
        private int FaceFace(IvpLedgeEdge f1, IvpLedgeEdge f2, int sideA, int sideB)
        {
            IvpLedgeSide sa = _sides[sideA];
            IvpLedgeSide sb = _sides[sideB];
            double least = Unreached;

            IvpLedgeEdge a = f1;

            for (int i = 0; i < 3; i++)
            {
                IvpLedgeEdge b = f2;

                for (int j = 0; j < 3; j++)
                {
                    (double X, double Y, double Z) wa = IvpCompactLedgeSolver.PointInWorld(sa, a);
                    (double X, double Y, double Z) wb = IvpCompactLedgeSolver.PointInWorld(sb, b);

                    double x = wa.X - wb.X;
                    double y = wa.Y - wb.Y;
                    double z = wa.Z - wb.Z;
                    double squared = (y * y) + (x * x) + (z * z);

                    if (!(squared >= least))
                    {
                        least = squared;
                        Settle(sideA, a, IvpFeatureKind.Point, sideB, b, IvpFeatureKind.Point);
                    }

                    b = sb.Topology.Next(b);
                }

                a = sa.Topology.Next(a);
            }

            foreach ((int pointSide, int faceSide, IvpLedgeEdge points, IvpLedgeEdge face) in
                     new[] { (sideA, sideB, f1, f2), (sideB, sideA, f2, f1) })
            {
                IvpLedgeSide owner = _sides[faceSide];
                (double X, double Y, double Z, double D) plane = IvpCompactLedgeSolver.Plane(owner, face);
                IvpLedgeEdge vertex = points;

                for (int i = 0; i < 3; i++)
                {
                    (double X, double Y, double Z) point =
                        IvpCompactLedgeSolver.PointInFrame(_sides[pointSide], vertex, owner);

                    if (IvpCompactLedgeSolver.TriangleWeights(owner, face, point).Inside)
                    {
                        double height = ((plane.X * point.X) + (plane.Y * point.Y) + (plane.Z * point.Z)) + plane.D;
                        double squared = height * height;

                        if (!(squared * Handicap >= least))
                        {
                            least = squared;
                            Settle(pointSide, vertex, IvpFeatureKind.Point, faceSide, face, IvpFeatureKind.Triangle);
                        }
                    }

                    vertex = _sides[pointSide].Topology.Next(vertex);
                }
            }

            foreach ((int pointSide, int edgeSide, IvpLedgeEdge points, IvpLedgeEdge edges) in
                     new[] { (sideB, sideA, f2, f1), (sideA, sideB, f1, f2) })
            {
                IvpLedgeSide owner = _sides[edgeSide];
                IvpLedgeEdge vertex = points;

                for (int i = 0; i < 3; i++)
                {
                    (double X, double Y, double Z) point =
                        IvpCompactLedgeSolver.PointInFrame(_sides[pointSide], vertex, owner);
                    IvpLedgeEdge edge = edges;

                    for (int j = 0; j < 3; j++)
                    {
                        if (IvpCompactLedgeSolver.EdgeWeights(owner, edge, point).Inside)
                        {
                            double squared = IvpCompactLedgeSolver.LineDistanceSquared(owner, edge, point);

                            if (!(squared * Handicap >= least))
                            {
                                least = squared;
                                Settle(edgeSide, edge, IvpFeatureKind.Edge, pointSide, vertex, IvpFeatureKind.Point);
                            }
                        }

                        edge = owner.Topology.Next(edge);
                    }

                    vertex = _sides[pointSide].Topology.Next(vertex);
                }
            }

            IvpLedgeEdge k = f1;

            for (int i = 0; i < 3; i++)
            {
                IvpLedgeEdge l = f2;

                for (int j = 0; j < 3; j++)
                {
                    IvpEdgeEdgeInput input = IvpCompactLedgeSolver.EdgeEdge(k, sa, l, sb);
                    IvpEdgeEdgeWeights weights = IvpCompactLedgeSolver.EdgeEdgeWeights(input);

                    if ((Bits(weights.KEnd) | Bits(weights.KStart)) >= 0 &&
                        (Bits(weights.LEnd) | Bits(weights.LStart)) >= 0)
                    {
                        double squared = IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(input);

                        if (!(squared * Handicap >= least))
                        {
                            least = squared;
                            Settle(sideA, k, IvpFeatureKind.Edge, sideB, l, IvpFeatureKind.Edge);
                        }
                    }

                    l = sb.Topology.Next(l);
                }

                k = sa.Topology.Next(k);
            }

            int first = sideA;
            int second = sideB;

            if (_mindist.Synapse(sideB).Kind == IvpFeatureKind.Point && _mindist.Synapse(sideA).Kind != IvpFeatureKind.Point)
            {
                (first, second) = (sideB, sideA);
            }

            FlipFor(first);

            IvpSynapse leading = _mindist.Synapse(first);
            IvpSynapse trailing = _mindist.Synapse(second);

            return (leading.Kind, trailing.Kind) switch
            {
                (IvpFeatureKind.Point, IvpFeatureKind.Point) => PointPoint(leading.Feature, trailing.Feature, first, second),
                (IvpFeatureKind.Point, IvpFeatureKind.Edge) => PointEdge(leading.Feature, trailing.Feature, first, second),
                (IvpFeatureKind.Point, IvpFeatureKind.Triangle) => PointFace(leading.Feature, trailing.Feature, first, second),
                (IvpFeatureKind.Edge, IvpFeatureKind.Edge) => EdgeEdge(leading.Feature, trailing.Feature, first, second),
                _ => throw new InvalidOperationException(
                    "The face-face routine left a pair it does not hand on (ivp_mindist_minimize.cxx:473)."),
            };
        }

        /// <summary>The steepest rise toward a target over the edges ending at a vertex, the point-point ring.</summary>
        private static IvpLedgeEdge? Steepest(
            IvpLedgeSide side, IvpLedgeEdge vertex, (double X, double Y, double Z) target, ref double steepest)
        {
            (float X, float Y, float Z) at = side.StartOf(vertex);
            double wx = target.X - at.X;
            double wy = target.Y - at.Y;
            double wz = target.Z - at.Z;
            double level = (at.Y * wy) + (at.X * wx) + (at.Z * wz);
            IvpLedgeEdge? best = null;

            foreach (IvpLedgeEdge edge in side.Topology.EndingAt(vertex))
            {
                (float X, float Y, float Z) neighbor = side.StartOf(edge);
                double rise = ((neighbor.Y * wy) + (neighbor.X * wx) + (neighbor.Z * wz)) - level;

                if (!(rise > 0d))
                {
                    continue;
                }

                (double X, double Y, double Z) toNeighbor = FloatDifference(neighbor, at);
                double squared = (toNeighbor.Y * toNeighbor.Y) + (toNeighbor.X * toNeighbor.X) +
                                 ((toNeighbor.Z * toNeighbor.Z) + RiseFloor);

                rise *= IvpVector.ReciprocalSquareRoot((float)squared);

                if (rise > steepest)
                {
                    steepest = rise;
                    best = edge;
                }
            }

            return best;
        }

        /// <summary>The budget decremented, and the loop check asked once it is spent.</summary>
        private bool LoopsBack(
            int firstSide, IvpLedgeEdge first, IvpFeatureKind firstKind, int secondSide, IvpLedgeEdge second, IvpFeatureKind secondKind)
        {
            _budget--;

            return _budget < 0 && _loop.Seen(Key(firstSide, first, firstKind), Key(secondSide, second, secondKind));
        }

        private long Key(int side, IvpLedgeEdge edge, IvpFeatureKind kind) =>
            ((long)side << 32) | (uint)(_sides[side].Topology.AddressOf(edge) | (int)kind);

        private void FlipFor(int side)
        {
            if (side != A)
            {
                _mindist.Flags ^= 0x100;
            }
        }

        private void Settle(
            int firstSide, IvpLedgeEdge first, IvpFeatureKind firstKind, int secondSide, IvpLedgeEdge second, IvpFeatureKind secondKind)
        {
            _mindist.SetSynapse(firstSide, new IvpSynapse(first, firstKind));
            _mindist.SetSynapse(secondSide, new IvpSynapse(second, secondKind));
        }

        /// <summary><c>+0x9c</c>: the cores' difference narrowed to float and dotted with the float normal.</summary>
        private void WriteContactDot(int firstSide, int secondSide)
        {
            (double X, double Y, double Z) first = _sides[firstSide].CorePosition;
            (double X, double Y, double Z) second = _sides[secondSide].CorePosition;
            (float X, float Y, float Z) normal = _mindist.Normal;

            float x = (float)(first.X - second.X);
            float y = (float)(first.Y - second.Y);
            float z = (float)(first.Z - second.Z);

            _mindist.ContactDot = (y * normal.Y) + (x * normal.X) + (z * normal.Z);
        }

        private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

        private static int SignBit(float value) => (int)((uint)BitConverter.SingleToInt32Bits(value) >> 31);

        private static double SquaredLength((double X, double Y, double Z) vector) =>
            (vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z);

        private static (double X, double Y, double Z) FloatDifference((float X, float Y, float Z) to, (float X, float Y, float Z) from)
        {
            float x = to.X - from.X;
            float y = to.Y - from.Y;
            float z = to.Z - from.Z;

            return (x, y, z);
        }

        private static InvalidOperationException Unreachable(string what) =>
            new($"{what}: every distance compared was at or above 1e101, where the engine would dereference a null edge.");

        /// <summary>One of the edge-edge routine's four regions: a triangle beside an edge and the other edge's end.</summary>
        private readonly record struct Region(
            IvpLedgeEdge Triangle,
            int TriangleSide,
            (double X, double Y, double Z) Point,
            int PointSide,
            IvpLedgeEdge PointEdge,
            (double X, double Y, double Z) Against,
            bool Inverted);
    }
}
