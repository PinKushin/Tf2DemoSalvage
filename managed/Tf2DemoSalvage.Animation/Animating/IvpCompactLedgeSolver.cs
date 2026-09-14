using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One side of a pair the minimize measures: a ledge, and where its object is at the current time.</summary>
/// <param name="Points">The ledge's points, in the object's frame.</param>
/// <param name="Topology">The ledge's triangles, edge words and headers.</param>
/// <param name="Current">The object's matrix now — the cache object's <c>+0x40</c>.</param>
/// <param name="CorePosition">The core's position now — the cache object's <c>+0x00</c>.</param>
/// <remarks>
/// **The side structure `FUN_180094c70` builds per synapse** holds the point array, the ledge, the cache object, the
/// real object and the synapse record (`docs/findings/51`, *The solver, the sides, and what the minimize writes*). The
/// helpers read the first three; the synapse is the minimize's own business.
/// </remarks>
public sealed record IvpLedgeSide(
    IReadOnlyList<(float X, float Y, float Z)> Points,
    IvpLedgeTopology Topology,
    IvpMatrix Current,
    (double X, double Y, double Z) CorePosition)
{
    /// <summary>The point an edge starts at.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The point, in the object's frame.</returns>
    public (float X, float Y, float Z) StartOf(IvpLedgeEdge edge) => Points[Topology.Start(edge)];

    /// <summary>The point an edge ends at — the start of the triangle's next edge.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The point, in the object's frame.</returns>
    public (float X, float Y, float Z) EndOf(IvpLedgeEdge edge) => StartOf(Topology.Next(edge));

    /// <summary>The normal of an edge's triangle, not scaled — <c>FUN_18007b940</c>.</summary>
    /// <param name="edge">The edge, whose triangle's start points are taken from it in their stored winding.</param>
    /// <returns>The normal, in the object's frame.</returns>
    public (double X, double Y, double Z) FaceNormal(IvpLedgeEdge edge) =>
        IvpVector.FaceNormal(StartOf(edge), EndOf(edge), StartOf(Topology.Previous(edge)));

    /// <summary>Builds a side from a decoded ledge and an object's current placement — what a live PSI needs each collision.</summary>
    /// <param name="ledge">The ledge, decoded from the <c>.phy</c>'s compact surface — <see cref="PhysicsLedgeTreeNode.Ledge"/>.</param>
    /// <param name="current">The object's matrix now.</param>
    /// <param name="corePosition">The core's position now.</param>
    /// <returns>The side.</returns>
    /// <remarks>
    /// **Not read from the disassembly — this project's own assembly of already-decoded pieces.** The native builds this
    /// structure per synapse inside `FUN_180094c70`'s caller, which nothing in this port has needed until a live running
    /// path does; <see cref="PhysicsLedge"/> already carries everything <see cref="IvpLedgeTopology"/> needs.
    /// </remarks>
    public static IvpLedgeSide FromLedge(PhysicsLedge ledge, IvpMatrix current, (double X, double Y, double Z) corePosition) =>
        new(
            ledge.Points.Select(point => (point.X, point.Y, point.Z)).ToList(),
            new IvpLedgeTopology(ledge.Triangles, ledge.EdgeOffsets, ledge.PierceTriangles, ledge.MaterialIndices),
            current,
            corePosition);
}

/// <summary>An edge's two weights for a point — <c>FUN_18007d070</c>'s output.</summary>
/// <param name="Start">How far past the edge's start the point projects, times the edge's length, plus <c>FLT_MIN</c>.</param>
/// <param name="End">How far short of its end, likewise.</param>
public readonly record struct IvpEdgeWeights(float Start, float End)
{
    /// <summary>Whether neither weight's sign bit is set — the <c>OR</c>/<c>JL</c> test the callers make.</summary>
    public bool Inside => (BitConverter.SingleToInt32Bits(Start) | BitConverter.SingleToInt32Bits(End)) >= 0;
}

/// <summary>A triangle's weights for a point — <c>FUN_18007cdf0</c>'s output.</summary>
/// <param name="Edge">The passed edge's: the barycentric weight of the vertex across from it, plus <c>FLT_MIN</c>.</param>
/// <param name="Next">The next edge's.</param>
/// <param name="Previous">The previous edge's.</param>
/// <param name="Determinant">The unnormalized area term the weights are scaled by.</param>
public readonly record struct IvpTriangleWeights(float Edge, float Next, float Previous, float Determinant)
{
    /// <summary>Whether none of the three edge weights has its sign bit set.</summary>
    public bool Inside =>
        (BitConverter.SingleToInt32Bits(Previous) | BitConverter.SingleToInt32Bits(Edge) |
         BitConverter.SingleToInt32Bits(Next)) >= 0;
}

/// <summary>What <c>FUN_18007b300</c> fills for an edge <c>K</c> measured against an edge <c>L</c>.</summary>
/// <param name="K">The first edge.</param>
/// <param name="KSide">Its side.</param>
/// <param name="L">The second edge, whose frame the doubles are in.</param>
/// <param name="LSide">Its side.</param>
/// <param name="LStart">L's start point, as stored.</param>
/// <param name="LEnd">L's end point, as stored.</param>
/// <param name="KStart">K's start in L's frame.</param>
/// <param name="KEnd">K's end in L's frame.</param>
/// <param name="KDirection">K's float edge vector turned into the world by K and back by L.</param>
/// <param name="LDirection">L's float edge vector, widened.</param>
/// <param name="Cross"><c>KDirection × LDirection</c>.</param>
public sealed record IvpEdgeEdgeInput(
    IvpLedgeEdge K,
    IvpLedgeSide KSide,
    IvpLedgeEdge L,
    IvpLedgeSide LSide,
    (float X, float Y, float Z) LStart,
    (float X, float Y, float Z) LEnd,
    (double X, double Y, double Z) KStart,
    (double X, double Y, double Z) KEnd,
    (double X, double Y, double Z) KDirection,
    (double X, double Y, double Z) LDirection,
    (double X, double Y, double Z) Cross);

/// <summary><c>FUN_18007c870</c>'s four weights and whether it found the edges crossing.</summary>
/// <param name="Crossing">The routine's return: true when the cross product was long enough to use.</param>
/// <param name="KStart">K's start weight (<c>out[0]</c>).</param>
/// <param name="KEnd">K's end weight (<c>out[4]</c>).</param>
/// <param name="LStart">L's start weight (<c>out[8]</c>).</param>
/// <param name="LEnd">L's end weight (<c>out[0xc]</c>).</param>
public readonly record struct IvpEdgeEdgeWeights(bool Crossing, float KStart, float KEnd, float LStart, float LEnd);

/// <summary>
/// The compact-ledge helpers IVP's minimize measures features with — <c>ivp_compact_ledge_solver.cxx</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *The minimize, routine by routine*).
/// Where a point is subtracted in float and where in double, where `FLT_MIN` is added and where not, and which way each
/// comparison takes a NaN, are carried as read — the minimize's decisions rest on their sign bits.
/// </remarks>
public static class IvpCompactLedgeSolver
{
    /// <summary><c>DAT_1800fd258</c>: <c>FLT_MIN</c> as a double, added to a weight before it is narrowed.</summary>
    private const double SmallestNormalFloat = 1.1754943508222875E-38d;

    /// <summary><c>DAT_1800fd268</c>: <c>1e-18f</c> widened, added to a line's squared length before dividing.</summary>
    private const double LineLengthFloor = (double)1e-18f;

    /// <summary><c>DAT_1800fce80</c>: the squared cross product below which two edges' line distance is not used.</summary>
    private const double CrossFloor = 1e-24d;

    /// <summary><c>DAT_1800eedc0</c>: the search's starting minimum.</summary>
    private const double Unreached = 1e101d;

    /// <summary>
    /// <c>DAT_1800fd260</c>: the squared cross product above which edges cross — <b>one ulp under <c>1e-18</c></b>,
    /// so it is written by its bits.
    /// </summary>
    private static readonly double CrossingFloor = BitConverter.Int64BitsToDouble(0x3C32725DD1D243ABL);

    /// <summary>The parallel search's sample fractions, <c>DAT_1800fd270</c> onward and one more on the stack.</summary>
    /// <remarks>K is sampled at the first eleven and L at the first nine; the last two are never read.</remarks>
    private static readonly float[] SampleFractions =
        [-1f, 0.5f, 2f, 0f, 1f, -0.001f, 0.001f, 0.999f, 1.001f, -1e-6f, 1e-6f, 0.999999f, BitConverter.Int32BitsToSingle(0x3F800008)];

    private const int KSamples = 11;

    private const int LSamples = 9;

    /// <summary>An edge's start point of one side in another side's frame — <c>FUN_18007ba70</c>.</summary>
    /// <param name="from">The side the edge belongs to.</param>
    /// <param name="edge">The edge.</param>
    /// <param name="to">The side whose frame the point is wanted in.</param>
    /// <returns>The point.</returns>
    public static (double X, double Y, double Z) PointInFrame(IvpLedgeSide from, IvpLedgeEdge edge, IvpLedgeSide to)
    {
        ArgumentNullException.ThrowIfNull(from);

        return PointInFrame(from.StartOf(edge), from, to);
    }

    /// <summary>A stored point of one side in another side's frame — <c>FUN_18007d480</c>.</summary>
    /// <param name="point">The point, in <paramref name="from"/>'s frame.</param>
    /// <param name="from">The side it is stored in.</param>
    /// <param name="to">The side whose frame it is wanted in.</param>
    /// <returns>The point.</returns>
    /// <remarks>Widened, into the world through <paramref name="from"/>, and out of it through <paramref name="to"/>.</remarks>
    public static (double X, double Y, double Z) PointInFrame(
        (float X, float Y, float Z) point, IvpLedgeSide from, IvpLedgeSide to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return to.Current.ToObject(from.Current.ToWorld(Widen(point)));
    }

    /// <summary>An edge's start point in the world — <c>FUN_18007d2e0</c>, a jump to <c>FUN_180080720</c>.</summary>
    /// <param name="side">The side the edge belongs to.</param>
    /// <param name="edge">The edge.</param>
    /// <returns>The point.</returns>
    public static (double X, double Y, double Z) PointInWorld(IvpLedgeSide side, IvpLedgeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(side);

        return side.Current.ToWorld(Widen(side.StartOf(edge)));
    }

    /// <summary>An edge's two weights for a point in the edge's frame — <c>FUN_18007d070</c>.</summary>
    /// <param name="side">The side the edge belongs to.</param>
    /// <param name="edge">The edge.</param>
    /// <param name="point">The point, in the side's frame.</param>
    /// <returns>The weights.</returns>
    /// <remarks>
    /// The edge vector is subtracted in float and widened; each end is widened BEFORE the point is subtracted from it.
    /// </remarks>
    public static IvpEdgeWeights EdgeWeights(IvpLedgeSide side, IvpLedgeEdge edge, (double X, double Y, double Z) point)
    {
        ArgumentNullException.ThrowIfNull(side);

        (float X, float Y, float Z) start = side.StartOf(edge);
        (float X, float Y, float Z) end = side.EndOf(edge);
        (double X, double Y, double Z) along = FloatDifference(end, start);

        double fromStart =
            ((point.Y - start.Y) * along.Y) + ((point.X - start.X) * along.X) + ((point.Z - start.Z) * along.Z);
        double toEnd =
            ((end.Y - point.Y) * along.Y) + ((end.X - point.X) * along.X) + ((end.Z - point.Z) * along.Z);

        return new IvpEdgeWeights((float)(fromStart + SmallestNormalFloat), (float)(toEnd + SmallestNormalFloat));
    }

    /// <summary>A triangle's weights for a point in its frame — <c>FUN_18007cdf0</c>.</summary>
    /// <param name="side">The side the triangle belongs to.</param>
    /// <param name="edge">Any edge of the triangle; it decides only which weight lands where.</param>
    /// <param name="point">The point, in the side's frame.</param>
    /// <returns>The passed edge's weight, the next's, the previous's and the determinant.</returns>
    /// <remarks>
    /// **Barycentric about the start `A` of the triangle's SECOND edge word**, whatever edge is passed: `u = B − A` and
    /// `v = C − A` over the first and third edge words' starts, subtracted in float; `w = p − A` in double. The weight of
    /// the vertex across from an edge is that edge's weight.
    /// </remarks>
    public static IvpTriangleWeights TriangleWeights(
        IvpLedgeSide side, IvpLedgeEdge edge, (double X, double Y, double Z) point)
    {
        ArgumentNullException.ThrowIfNull(side);

        (float X, float Y, float Z) b = side.StartOf(new IvpLedgeEdge(edge.Triangle, 0));
        (float X, float Y, float Z) a = side.StartOf(new IvpLedgeEdge(edge.Triangle, 1));
        (float X, float Y, float Z) c = side.StartOf(new IvpLedgeEdge(edge.Triangle, 2));

        (double X, double Y, double Z) u = FloatDifference(b, a);
        (double X, double Y, double Z) v = FloatDifference(c, a);
        (double X, double Y, double Z) w = (point.X - a.X, point.Y - a.Y, point.Z - a.Z);

        double uu = (u.Y * u.Y) + (u.X * u.X) + (u.Z * u.Z);
        double vv = (v.Y * v.Y) + (v.X * v.X) + (v.Z * v.Z);
        double uv = (v.Y * u.Y) + (v.X * u.X) + (v.Z * u.Z);
        double wu = (w.Y * u.Y) + (w.X * u.X) + (w.Z * u.Z);
        double wv = (w.Y * v.Y) + (w.X * v.X) + (w.Z * v.Z);

        double determinant = (vv * uu) - (uv * uv);
        double acrossB = (wu * vv) - (wv * uv);
        double acrossC = (wv * uu) - (wu * uv);

        float[] bySlot =
        [
            (float)(acrossC + SmallestNormalFloat),
            (float)(acrossB + SmallestNormalFloat),
            (float)(((determinant - acrossB) - acrossC) + SmallestNormalFloat),
        ];

        return new IvpTriangleWeights(
            bySlot[edge.Slot], bySlot[(edge.Slot + 1) % 3], bySlot[(edge.Slot + 2) % 3], (float)determinant);
    }

    /// <summary>A point's squared distance from an edge's line — <c>FUN_18007d300</c>.</summary>
    /// <param name="side">The side the edge belongs to.</param>
    /// <param name="edge">The edge.</param>
    /// <param name="point">The point, in the side's frame.</param>
    /// <returns><c>|(p − S) × (E − S)|² / (|S − E|² + 1e-18f)</c>.</returns>
    /// <remarks>
    /// **The cross product's edge is subtracted in DOUBLE and the divisor's in FLOAT**, so the two lengths are not the
    /// same number.
    /// </remarks>
    public static double LineDistanceSquared(IvpLedgeSide side, IvpLedgeEdge edge, (double X, double Y, double Z) point)
    {
        ArgumentNullException.ThrowIfNull(side);

        (float X, float Y, float Z) start = side.StartOf(edge);
        (float X, float Y, float Z) end = side.EndOf(edge);

        (double X, double Y, double Z) along = ((double)end.X - start.X, (double)end.Y - start.Y, (double)end.Z - start.Z);
        (double X, double Y, double Z) from = (point.X - start.X, point.Y - start.Y, point.Z - start.Z);

        double crossX = (from.Y * along.Z) - (from.Z * along.Y);
        double crossY = (from.Z * along.X) - (from.X * along.Z);
        double crossZ = (from.X * along.Y) - (from.Y * along.X);

        (double X, double Y, double Z) back = FloatDifference(start, end);

        double numerator = (crossY * crossY) + (crossX * crossX) + (crossZ * crossZ);
        double denominator = (back.X * back.X) + (back.Y * back.Y) + (back.Z * back.Z) + LineLengthFloor;

        return numerator / denominator;
    }

    /// <summary>A point's squared distance from an edge as a segment — <c>FUN_18007c1a0</c>.</summary>
    /// <param name="side">The side the edge belongs to.</param>
    /// <param name="edge">The edge.</param>
    /// <param name="point">The point, in the side's frame.</param>
    /// <returns>The line's distance when both weights are inside; otherwise the nearer end's.</returns>
    /// <remarks>
    /// The start is taken when its weight is not at or above zero — NaN included, `COMISS`/`JNC` — and the end otherwise,
    /// each summed `(y² + x²) + z²`.
    /// </remarks>
    public static double SegmentDistanceSquared(
        IvpLedgeSide side, IvpLedgeEdge edge, (double X, double Y, double Z) point)
    {
        ArgumentNullException.ThrowIfNull(side);

        IvpEdgeWeights weights = EdgeWeights(side, edge, point);

        if (weights.Inside)
        {
            return LineDistanceSquared(side, edge, point);
        }

        (float X, float Y, float Z) end = weights.Start >= 0f ? side.EndOf(edge) : side.StartOf(edge);

        double x = point.X - end.X;
        double y = point.Y - end.Y;
        double z = point.Z - end.Z;

        return (y * y) + (x * x) + (z * z);
    }

    /// <summary>A triangle's unit plane — <c>FUN_18007b780</c>.</summary>
    /// <param name="side">The side the triangle belongs to.</param>
    /// <param name="edge">The edge whose start is the plane's reference point.</param>
    /// <returns>The unit normal and the offset <c>d</c>, so that <c>n·p + d</c> is the signed distance.</returns>
    /// <remarks>
    /// `FUN_18006ddb0` crosses the triangle in <see cref="IvpVector.FaceNormal"/>'s order and sets `d = −n·p₀`; then
    /// `FUN_18006f6c0` scales all four by `1.0 / √|n|²` — an exact root, and no threshold.
    /// </remarks>
    public static (double X, double Y, double Z, double D) Plane(IvpLedgeSide side, IvpLedgeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(side);

        (float X, float Y, float Z) origin = side.StartOf(edge);
        (double X, double Y, double Z) normal = side.FaceNormal(edge);

        double offset = -((normal.Y * origin.Y) + (normal.X * origin.X) + (normal.Z * origin.Z));
        double scale = 1d / Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));

        return (normal.X * scale, normal.Y * scale, normal.Z * scale, scale * offset);
    }

    /// <summary>The input for measuring edge <c>K</c> against edge <c>L</c> — <c>FUN_18007b300</c>.</summary>
    /// <param name="k">The first edge.</param>
    /// <param name="kSide">Its side.</param>
    /// <param name="l">The second edge.</param>
    /// <param name="lSide">Its side.</param>
    /// <returns>The filled input.</returns>
    public static IvpEdgeEdgeInput EdgeEdge(IvpLedgeEdge k, IvpLedgeSide kSide, IvpLedgeEdge l, IvpLedgeSide lSide)
    {
        ArgumentNullException.ThrowIfNull(kSide);
        ArgumentNullException.ThrowIfNull(lSide);

        (double X, double Y, double Z) kDirection =
            lSide.Current.RotateInverse(kSide.Current.Rotate(FloatDifference(kSide.EndOf(k), kSide.StartOf(k))));

        (float X, float Y, float Z) lStart = lSide.StartOf(l);
        (float X, float Y, float Z) lEnd = lSide.EndOf(l);
        (double X, double Y, double Z) lDirection = FloatDifference(lEnd, lStart);

        return new IvpEdgeEdgeInput(
            k,
            kSide,
            l,
            lSide,
            lStart,
            lEnd,
            PointInFrame(kSide, k, lSide),
            PointInFrame(kSide, kSide.Topology.Next(k), lSide),
            kDirection,
            lDirection,
            IvpVector.Cross(kDirection, lDirection));
    }

    /// <summary>Where two edges cross, as each one's weights — <c>FUN_18007c870</c>.</summary>
    /// <param name="input">The input from <see cref="EdgeEdge"/>.</param>
    /// <returns>The four weights, and whether the edges were far enough from parallel to cross.</returns>
    /// <remarks>
    /// **Crossing:** `a = KDirection × cross` levels K's line, so L's ends and K's start dotted with it give L's two
    /// weights, WITHOUT `FLT_MIN`; `b = LDirection × cross` does the same for K's, with it.
    ///
    /// **Parallel, or a zero-length edge:** K is sampled at eleven fractions against L's line, keeping the first least
    /// distance (`&lt;`, NaN taken) with its fraction, `1 − f`, and L's edge weights there; then L, carried into K's frame,
    /// at the first nine against K's line with the same running minimum, writing its fraction and K's weights inline —
    /// `FLT_MIN − (S − q)·d` and `(E − q)·d + FLT_MIN`, the latter's `z` term added to `FLT_MIN` first.
    /// </remarks>
    public static IvpEdgeEdgeWeights EdgeEdgeWeights(IvpEdgeEdgeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        (double X, double Y, double Z) cross = input.Cross;

        if ((cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z) > CrossingFloor)
        {
            return Crossing(input);
        }

        return Sampled(input);
    }

    /// <summary>Two edges' squared distance from their input — <c>FUN_18007c2a0</c>.</summary>
    /// <param name="input">The input from <see cref="EdgeEdge"/>.</param>
    /// <returns>Along the common normal above <c>1e-24</c>; K's start against L's segment otherwise.</returns>
    public static double EdgeEdgeDistanceSquared(IvpEdgeEdgeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        (double X, double Y, double Z) cross = input.Cross;
        double squared = (cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z);

        if (squared > CrossFloor)
        {
            return AlongNormal(input, squared);
        }

        return SegmentDistanceSquared(input.LSide, input.L, input.KStart);
    }

    /// <summary>Two edges' squared distance — <c>FUN_18007bbd0</c>.</summary>
    /// <param name="k">The first edge.</param>
    /// <param name="kSide">Its side.</param>
    /// <param name="l">The second edge.</param>
    /// <param name="lSide">Its side.</param>
    /// <returns>The squared distance, by where the crossing falls.</returns>
    /// <remarks>
    /// **L inside:** K's start weight not `&gt; 0` → K's start against L's segment; K's end weight not `&gt; 0` → K's end;
    /// otherwise along the normal as <see cref="EdgeEdgeDistanceSquared(IvpEdgeEdgeInput)"/> takes it. **K inside:** L's
    /// start or end against K's segment. **Both outside:** the least of four segment distances by `MINSD`, L's start
    /// and K's start, then L's end and K's end.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// K inside with both of L's weights positive — the assertion at <c>ivp_compact_ledge_solver.cxx:674</c>, which the
    /// sign-bit tests make unreachable.
    /// </exception>
    public static double EdgeEdgeDistanceSquared(IvpLedgeEdge k, IvpLedgeSide kSide, IvpLedgeEdge l, IvpLedgeSide lSide)
    {
        IvpEdgeEdgeInput input = EdgeEdge(k, kSide, l, lSide);
        IvpEdgeEdgeWeights weights = EdgeEdgeWeights(input);

        if ((BitConverter.SingleToInt32Bits(weights.LEnd) | BitConverter.SingleToInt32Bits(weights.LStart)) >= 0)
        {
            if (!(weights.KStart > 0f))
            {
                return SegmentDistanceSquared(lSide, l, input.KStart);
            }

            if (!(weights.KEnd > 0f))
            {
                return SegmentDistanceSquared(lSide, l, input.KEnd);
            }

            return EdgeEdgeDistanceSquared(input);
        }

        if ((BitConverter.SingleToInt32Bits(weights.KEnd) | BitConverter.SingleToInt32Bits(weights.KStart)) >= 0)
        {
            if (!(weights.LStart > 0f))
            {
                return SegmentDistanceSquared(kSide, k, PointInFrame(lSide, l, kSide));
            }

            if (!(weights.LEnd > 0f))
            {
                return SegmentDistanceSquared(kSide, k, PointInFrame(lSide, lSide.Topology.Next(l), kSide));
            }

            throw new InvalidOperationException(
                "Edge K is inside edge L's crossing while both of L's weights are positive (ivp_compact_ledge_solver.cxx:674).");
        }

        double least = Unreached;

        foreach ((IvpLedgeEdge lEnd, (double X, double Y, double Z) kEnd) in
                 new[] { (l, input.KStart), (lSide.Topology.Next(l), input.KEnd) })
        {
            double fromL = SegmentDistanceSquared(kSide, k, PointInFrame(lSide, lEnd, kSide));
            double fromK = SegmentDistanceSquared(lSide, l, kEnd);

            double lesser = fromL < least ? fromL : least;
            least = fromK < lesser ? fromK : lesser;
        }

        return least;
    }

    private static IvpEdgeEdgeWeights Crossing(IvpEdgeEdgeInput input)
    {
        (double X, double Y, double Z) a = IvpVector.Cross(input.KDirection, input.Cross);
        (double X, double Y, double Z) b = IvpVector.Cross(input.LDirection, input.Cross);

        (double X, double Y, double Z) lStart = Widen(input.LStart);
        (double X, double Y, double Z) lEnd = Widen(input.LEnd);
        (double X, double Y, double Z) kStart = input.KStart;
        (double X, double Y, double Z) kEnd = input.KEnd;

        double lStartLevel = (lStart.Y * a.Y) + (lStart.X * a.X) + (lStart.Z * a.Z);
        double lEndLevel = (lEnd.Y * a.Y) + (lEnd.X * a.X) + (lEnd.Z * a.Z);
        double kLevel = (a.Y * kStart.Y) + (a.X * kStart.X) + (a.Z * kStart.Z);

        double lSpan = lStartLevel - lEndLevel;

        double kStartLevel = (b.X * kStart.X) + (b.Y * kStart.Y) + (b.Z * kStart.Z);
        double kEndLevel = (b.Y * kEnd.Y) + (b.X * kEnd.X) + (b.Z * kEnd.Z);
        double lLevel = (lStart.Y * b.Y) + (lStart.X * b.X) + (lStart.Z * b.Z);

        double kSpan = kStartLevel - kEndLevel;

        return new IvpEdgeEdgeWeights(
            Crossing: true,
            KStart: (float)(((kStartLevel - lLevel) * kSpan) + SmallestNormalFloat),
            KEnd: (float)(((lLevel - kEndLevel) * kSpan) + SmallestNormalFloat),
            LStart: (float)((lStartLevel - kLevel) * lSpan),
            LEnd: (float)((kLevel - lEndLevel) * lSpan));
    }

    private static IvpEdgeEdgeWeights Sampled(IvpEdgeEdgeInput input)
    {
        double least = Unreached;
        float kStart = 0f;
        float kEnd = 0f;
        float lStart = 0f;
        float lEnd = 0f;

        for (int sample = 0; sample < KSamples; sample++)
        {
            float fraction = SampleFractions[sample];
            (double X, double Y, double Z) along = IvpVector.Lerp(input.KStart, input.KEnd, fraction);
            double squared = LineDistanceSquared(input.LSide, input.L, along);

            if (!(squared >= least))
            {
                least = squared;
                kStart = fraction;
                kEnd = 1f - fraction;
                (lStart, lEnd) = EdgeWeights(input.LSide, input.L, along);
            }
        }

        (double X, double Y, double Z) lStartInK = PointInFrame(input.LStart, input.LSide, input.KSide);
        (double X, double Y, double Z) lEndInK = PointInFrame(input.LEnd, input.LSide, input.KSide);

        for (int sample = 0; sample < LSamples; sample++)
        {
            float fraction = SampleFractions[sample];
            (double X, double Y, double Z) along = IvpVector.Lerp(lStartInK, lEndInK, fraction);
            double squared = LineDistanceSquared(input.KSide, input.K, along);

            if (!(squared >= least))
            {
                least = squared;
                lStart = fraction;
                lEnd = 1f - fraction;
                (kStart, kEnd) = InlineEdgeWeights(input.KSide, input.K, along);
            }
        }

        return new IvpEdgeEdgeWeights(false, kStart, kEnd, lStart, lEnd);
    }

    /// <summary>The K weights the parallel search writes inline, which round differently from <see cref="EdgeWeights"/>.</summary>
    private static (float Start, float End) InlineEdgeWeights(
        IvpLedgeSide side, IvpLedgeEdge edge, (double X, double Y, double Z) point)
    {
        (float X, float Y, float Z) start = side.StartOf(edge);
        (float X, float Y, float Z) end = side.EndOf(edge);
        (double X, double Y, double Z) along = FloatDifference(end, start);

        double toStart =
            (((double)start.Y - point.Y) * along.Y) + (((double)start.X - point.X) * along.X) +
            (((double)start.Z - point.Z) * along.Z);

        double toEndZ = (((double)end.Z - point.Z) * along.Z) + SmallestNormalFloat;
        double toEnd =
            ((((double)end.Y - point.Y) * along.Y) + (((double)end.X - point.X) * along.X)) + toEndZ;

        return ((float)(SmallestNormalFloat - toStart), (float)toEnd);
    }

    private static double AlongNormal(IvpEdgeEdgeInput input, double squared)
    {
        (double X, double Y, double Z) cross = input.Cross;
        (double X, double Y, double Z) kStart = input.KStart;
        (double X, double Y, double Z) lStart = Widen(input.LStart);

        double apart =
            ((cross.Y * kStart.Y) + (cross.X * kStart.X) + (cross.Z * kStart.Z)) -
            ((lStart.Y * cross.Y) + (lStart.X * cross.X) + (lStart.Z * cross.Z));

        return (apart * apart) / squared;
    }

    private static (double X, double Y, double Z) Widen((float X, float Y, float Z) point) => (point.X, point.Y, point.Z);

    /// <summary>Two stored points subtracted in float, then widened — <c>SUBSS</c>, then <c>CVTPS2PD</c>.</summary>
    private static (double X, double Y, double Z) FloatDifference((float X, float Y, float Z) to, (float X, float Y, float Z) from)
    {
        float x = to.X - from.X;
        float y = to.Y - from.Y;
        float z = to.Z - from.Z;

        return (x, y, z);
    }
}
