using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The compact-ledge helpers IVP's minimize measures features with — <c>ivp_compact_ledge_solver.cxx</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The minimize, routine by routine*, the helper table). Each
/// expectation is an exact value the helper's own float and double boundaries produce, chosen so a helper that
/// subtracts in the wrong width, drops `FLT_MIN`, or orders its outputs differently gives a different number.
///
/// **Every fixture body is unrotated at a whole-number position** unless a test says otherwise, so the frames add no
/// rounding of their own.
/// </remarks>
public sealed class IvpCompactLedgeSolverConformanceTests
{
    /// <summary><c>FLT_MIN</c>'s bits: the smallest normal float, which the weights add before narrowing.</summary>
    private const int SmallestNormalFloatBits = 0x0080_0000;

    /// <remarks>
    /// **Into the world through one body, into the other body by the transpose** (`18007ba70`). A is turned half a turn
    /// about `z` and sits at `(1, 2, 3)`; its point `(1, 0, 0)` is at `(0, 2, 3)` in the world, which is `(0, 2, 2)` from B
    /// at `(0, 0, 1)`. Leaving out either body's translation, or turning the wrong way, lands elsewhere.
    /// </remarks>
    [Test]
    public void PointInFrame_AnEdgesStart_GoesThroughTheWorldIntoTheOtherBody()
    {
        IvpLedgeSide a = Side([(1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f)], (1d, 2d, 3d), (0f, 0f, 1f, 0f));
        IvpLedgeSide b = Side([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)], (0d, 0d, 1d));

        IvpCompactLedgeSolver.PointInFrame(a, new IvpLedgeEdge(0, 0), b).ShouldBe((0d, 2d, 2d));
    }

    /// <remarks>
    /// **An edge's two weights are the point's projection past its start and short of its end, each plus `FLT_MIN`**
    /// (`18007d070`). Along `(0, 0, 0) → (2, 0, 0)`: half a unit in gives `1` and `3`; at the start exactly, the start
    /// weight is `FLT_MIN` — positive, which the minimize's sign-bit tests read as inside — and a unit before it gives
    /// `−2`.
    /// </remarks>
    [TestCase(0.5d, 1d, 1f, 3f)]
    [TestCase(-1d, 0d, -2f, 6f)]
    public void EdgeWeights_APointAlongTheEdge_IsItsProjectionFromEachEnd(double x, double y, float start, float end)
    {
        IvpEdgeWeights weights = IvpCompactLedgeSolver.EdgeWeights(Ramp(), new IvpLedgeEdge(0, 0), (x, y, 0d));

        weights.ShouldBe(new IvpEdgeWeights(start, end));
    }

    [Test]
    public void EdgeWeights_AtTheEdgesStart_IsTheSmallestNormalFloatNotZero()
    {
        IvpEdgeWeights weights = IvpCompactLedgeSolver.EdgeWeights(Ramp(), new IvpLedgeEdge(0, 0), (0d, 1d, 0d));

        BitConverter.SingleToInt32Bits(weights.Start).ShouldBe(SmallestNormalFloatBits);
        weights.End.ShouldBe(4f);
    }

    /// <remarks>
    /// **A triangle's weights come out as the passed edge's, the next edge's, then the previous edge's** (`18007cdf0`),
    /// whichever edge is passed — the arithmetic is always about the start of the triangle's second edge word, and a
    /// table picks where each lands. In `(0, 0, 0), (4, 0, 0), (0, 4, 0)` the point `(1, 0.5, 0)` is `0.5` from the
    /// first edge, `2.5` from the second and `1` from the third, which the determinant `256` scales to `32`, `160`, `64`.
    /// </remarks>
    [TestCase(0, 32f, 160f, 64f)]
    [TestCase(1, 160f, 64f, 32f)]
    [TestCase(2, 64f, 32f, 160f)]
    public void TriangleWeights_EachEdgeOfATriangle_AreThatEdgeThenTheNextThenThePrevious(
        int slot, float edge, float next, float previous)
    {
        IvpTriangleWeights weights =
            IvpCompactLedgeSolver.TriangleWeights(Wide(), new IvpLedgeEdge(0, slot), (1d, 0.5d, 0d));

        weights.ShouldBe(new IvpTriangleWeights(edge, next, previous, 256f));
    }

    /// <remarks>
    /// **On an edge, that edge's weight is `FLT_MIN`, not zero**, so a point on the boundary is inside by the sign-bit
    /// test.
    /// </remarks>
    [Test]
    public void TriangleWeights_OnAnEdge_IsTheSmallestNormalFloatNotZero()
    {
        IvpTriangleWeights weights = IvpCompactLedgeSolver.TriangleWeights(Wide(), new IvpLedgeEdge(0, 0), (1d, 0d, 0d));

        BitConverter.SingleToInt32Bits(weights.Edge).ShouldBe(SmallestNormalFloatBits);
    }

    /// <remarks>
    /// **The cross product's edge is subtracted in double and the divisor's in float, then `1e-18f` added**
    /// (`18007d300`). From `0.1f` to `0.3f` the double difference is `0.20000001043…` and the float one rounds to
    /// `0.20000001788…`, so the point a unit off the line is at `0.999999925494202`, not `1`.
    /// </remarks>
    [Test]
    public void LineDistanceSquared_SubtractsTheCrossInDoubleAndTheDivisorInFloat()
    {
        IvpLedgeSide side = Side([(0.1f, 0f, 0f), (0.3f, 0f, 0f), (0f, 1f, 0f)], (0d, 0d, 0d));

        double squared = IvpCompactLedgeSolver.LineDistanceSquared(side, new IvpLedgeEdge(0, 0), (0d, 1d, 0d));

        BitConverter.DoubleToInt64Bits(squared).ShouldBe(0x3FEF_FFFF_D800_0048L);
    }

    /// <remarks>**A zero-length edge's line distance is zero, not NaN** — the divisor's `1e-18f` keeps it finite.</remarks>
    [Test]
    public void LineDistanceSquared_OfAZeroLengthEdge_IsZero()
    {
        IvpLedgeSide side = Side([(1f, 1f, 1f), (1f, 1f, 1f), (0f, 0f, 0f)], (0d, 0d, 0d));

        IvpCompactLedgeSolver.LineDistanceSquared(side, new IvpLedgeEdge(0, 0), (3d, 4d, 5d)).ShouldBe(0d);
    }

    /// <remarks>
    /// **A segment's distance is the line's when both weights are inside, and the nearer end's otherwise**
    /// (`18007c1a0`): before `(0, 0, 0) → (2, 0, 0)` it is the start's, past it the end's.
    /// </remarks>
    [TestCase(-1d, 1d, 0d, 2d)]
    [TestCase(4d, 1d, 0d, 5d)]
    [TestCase(1d, 3d, 4d, 25d)]
    public void SegmentDistanceSquared_APointBeforeAlongOrPastTheEdge_IsToTheNearestPartOfIt(
        double x, double y, double z, double squared) =>
        IvpCompactLedgeSolver.SegmentDistanceSquared(Ramp(), new IvpLedgeEdge(0, 0), (x, y, z)).ShouldBe(squared);

    /// <remarks>
    /// **A triangle's plane is its normal scaled to unit length with the offset scaled alongside** (`18007b780`): the
    /// triangle at `z = 1` wound counter-clockwise from above is `(0, 0, 1, −1)`.
    /// </remarks>
    [Test]
    public void Plane_OfATriangle_IsItsUnitNormalAndTheNegatedOffset()
    {
        IvpLedgeSide side = Side([(0f, 0f, 1f), (2f, 0f, 1f), (0f, 2f, 1f)], (0d, 0d, 0d));

        IvpCompactLedgeSolver.Plane(side, new IvpLedgeEdge(0, 0)).ShouldBe((0d, 0d, 1d, -1d));
    }

    /// <remarks>
    /// **A perpendicular swaps the largest component with the one before it, negating the one moved up, and crosses
    /// that with the vector** (`18006db60`). For `(1, 2, 3)` that is `(1, −3, 2) × (1, 2, 3) = (−13, −1, 5)`. On a tie
    /// the component checked first — `z` — wins, so `(3, 0, 3)` gives `(−9, −9, 9)`, where `x` winning would give
    /// `(0, −18, 0)`.
    /// </remarks>
    [TestCase(1d, 2d, 3d, -13d, -1d, 5d)]
    [TestCase(3d, 0d, 3d, -9d, -9d, 9d)]
    public void Perpendicular_AVector_SwapsItsLargestComponentBackAndCrosses(
        double x, double y, double z, double px, double py, double pz) =>
        IvpVector.Perpendicular((x, y, z)).ShouldBe((px, py, pz));

    /// <remarks>
    /// **Two crossing edges' weights are each edge's parameter at the crossing, scaled by the square of the other's
    /// span** (`18007c870`). `K` runs `(0, 0, 0) → (2, 0, 0)` and `L` runs `(0.5, −1, 1) → (0.5, 3, 1)`; they cross a
    /// quarter along each, so both pairs are `1024` and `3072`, the first pair with `FLT_MIN` added and the second
    /// without.
    /// </remarks>
    [Test]
    public void EdgeEdgeWeights_OfCrossingEdges_AreEachEdgesParameterAtTheCrossing()
    {
        IvpEdgeEdgeInput input = IvpCompactLedgeSolver.EdgeEdge(new IvpLedgeEdge(0, 0), Along(), new IvpLedgeEdge(0, 0), Across(0.5f));

        IvpCompactLedgeSolver.EdgeEdgeWeights(input).ShouldBe(new IvpEdgeEdgeWeights(true, 1024f, 3072f, 1024f, 3072f));
    }

    /// <remarks>
    /// **Parallel edges are sampled** (`18007c870`'s second path): `K` at eleven parameters against `L`'s line, keeping
    /// the first least distance — every sample is a unit away, so the first, `−1`, is kept, with `1 − (−1)` beside it and
    /// `L`'s weights for the point there, `−4` and `8`. Then `L` at nine parameters against `K`'s line, which is never
    /// closer. The routine reports the edges as not crossing.
    /// </remarks>
    [Test]
    public void EdgeEdgeWeights_OfParallelEdges_KeepTheFirstSampleOfTheLeastDistance()
    {
        IvpLedgeSide k = Side([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));
        IvpLedgeSide l = Side([(0f, 1f, 0f), (2f, 1f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));

        IvpEdgeEdgeInput input = IvpCompactLedgeSolver.EdgeEdge(new IvpLedgeEdge(0, 0), k, new IvpLedgeEdge(0, 0), l);

        IvpCompactLedgeSolver.EdgeEdgeWeights(input).ShouldBe(new IvpEdgeEdgeWeights(false, -1f, 2f, -4f, 8f));
    }

    /// <remarks>
    /// **`L`'s samples replace `K`'s when they are closer, and write `K`'s weights inline** — `FLT_MIN` less the
    /// projection, and the end's projection plus `FLT_MIN`. Against a zero-length `K` every `L` sample is at distance
    /// zero, so the first, `−1`, replaces the unit distance `K`'s samples found, and both of `K`'s weights are `FLT_MIN`.
    /// </remarks>
    [Test]
    public void EdgeEdgeWeights_AgainstAZeroLengthEdge_TakeTheOtherEdgesFirstSample()
    {
        IvpLedgeSide k = Side([(0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));
        IvpLedgeSide l = Side([(1f, 1f, 0f), (3f, 1f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));

        IvpEdgeEdgeInput input = IvpCompactLedgeSolver.EdgeEdge(new IvpLedgeEdge(0, 0), k, new IvpLedgeEdge(0, 0), l);
        IvpEdgeEdgeWeights weights = IvpCompactLedgeSolver.EdgeEdgeWeights(input);

        weights.Crossing.ShouldBeFalse();
        BitConverter.SingleToInt32Bits(weights.KStart).ShouldBe(SmallestNormalFloatBits);
        BitConverter.SingleToInt32Bits(weights.KEnd).ShouldBe(SmallestNormalFloatBits);
        (weights.LStart, weights.LEnd).ShouldBe((-1f, 2f));
    }

    /// <remarks>
    /// **Two crossing edges' distance is along their common normal** (`18007bbd0`): `L` a unit above `K`, squared `1`.
    /// Moved so the crossing is before `K`'s start, it is `K`'s start against `L`'s segment instead, `20 / 16`.
    /// </remarks>
    [TestCase(0f, 1d)]
    [TestCase(1f, 1.25d)]
    public void EdgeEdgeDistanceSquared_CrossingOrPastAnEnd_IsAlongTheNormalOrFromTheEnd(float kStart, double squared)
    {
        IvpLedgeSide k = Side([(kStart, 0f, 0f), (kStart + 2f, 0f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));

        IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(new IvpLedgeEdge(0, 0), k, new IvpLedgeEdge(0, 0), Across(0.5f))
            .ShouldBe(squared);
    }

    /// <remarks>
    /// **When the crossing is off both edges, the distance is the least of four endpoint-to-segment distances**, each
    /// kept only if it is less. `K` runs from the origin to `(2, 0, 0)` and `L` from `(1, 1, 0)` to `(5, 2, 0)`; their lines
    /// meet before both starts. `L`'s start is `1` above `K`, `K`'s start `2` from `L`'s start, `L`'s end `13` from `K`'s
    /// end, and `K`'s end `25 / 17` from `L` — so only the first gives `1`, and a comparison kept the wrong way round
    /// gives one of the others.
    /// </remarks>
    [Test]
    public void EdgeEdgeDistanceSquared_WithTheCrossingOffBothEdges_IsTheLeastOfTheFourEnds()
    {
        IvpLedgeSide k = Side([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));
        IvpLedgeSide l = Side([(1f, 1f, 0f), (5f, 2f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));

        IvpCompactLedgeSolver.EdgeEdgeDistanceSquared(new IvpLedgeEdge(0, 0), k, new IvpLedgeEdge(0, 0), l).ShouldBe(1d);
    }

    /// <summary>The edge <c>(0, 0, 0) → (2, 0, 0)</c> and a third point above it.</summary>
    private static IvpLedgeSide Ramp() => Side([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 2f, 0f)], (0d, 0d, 0d));

    /// <summary>The triangle <c>(0, 0, 0), (4, 0, 0), (0, 4, 0)</c>.</summary>
    private static IvpLedgeSide Wide() => Side([(0f, 0f, 0f), (4f, 0f, 0f), (0f, 4f, 0f)], (0d, 0d, 0d));

    /// <summary><c>K</c> for the edge-edge tests: <c>(0, 0, 0) → (2, 0, 0)</c>.</summary>
    private static IvpLedgeSide Along() => Side([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 0f, 5f)], (0d, 0d, 0d));

    /// <summary><c>L</c> for the edge-edge tests: along <c>y</c> from <c>−1</c> to <c>3</c> at the given <c>x</c>, a unit up.</summary>
    private static IvpLedgeSide Across(float x) => Side([(x, -1f, 1f), (x, 3f, 1f), (9f, 9f, 9f)], (0d, 0d, 0d));

    /// <remarks>A live PSI has only a decoded ledge and a placement — <see cref="IvpLedgeSide.FromLedge"/> is what turns those into a side.</remarks>
    [Test]
    public void FromLedge_ADecodedLedgeAndAPlacement_CarriesItsPointsAndTopology()
    {
        Vector3[] points = [new(0f, 0f, 0f), new(2f, 0f, 0f), new(0f, 2f, 0f)];
        PhysicsLedge ledge = new(points, [(0, 1, 2)], [(0, 0, 0)], [0], [0], Vector3.Zero, 1f);
        IvpMatrix current = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (5d, 6d, 7d));

        IvpLedgeSide side = IvpLedgeSide.FromLedge(ledge, current, (5d, 6d, 7d));

        side.Points.ShouldBe([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 2f, 0f)]);
        side.Current.ShouldBe(current);
        side.CorePosition.ShouldBe((5d, 6d, 7d));
        side.StartOf(new IvpLedgeEdge(0, 0)).ShouldBe((0f, 0f, 0f));
        side.EndOf(new IvpLedgeEdge(0, 0)).ShouldBe((2f, 0f, 0f));
    }

    /// <summary>One triangle whose edges hop to themselves, on a body at a position and rotation.</summary>
    private static IvpLedgeSide Side(
        IReadOnlyList<(float X, float Y, float Z)> points,
        (double X, double Y, double Z) position,
        (float X, float Y, float Z, float W)? rotation = null) =>
        new(
            points,
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            IvpMatrix.FromRotation(rotation ?? (0f, 0f, 0f, 1f), position),
            position);
}
