using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Where a contact point's two features touch, and which way — the four measures under IVP's contact record builder (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*): `FUN_18008cab0` point–point,
/// `FUN_18008c7c0` point–edge, `FUN_18008c5b0` point–triangle and `FUN_18008cbc0` edge–edge. Each writes the record's
/// position, normal and span and the contact point's gap, and on the contact point's first measure whether the touch lies
/// outside the features. **The normal points from the first feature toward the second.**
///
/// **Every fixture body is unrotated at a whole-number position unless a test says otherwise, and every length that is
/// scaled is a power of two**, so each reciprocal root is exact and each expectation is the value itself.
/// </remarks>
public sealed class IvpContactGeometryConformanceTests
{
    private static readonly IvpLedgeEdge Edge = new(0, 0);

    /// <remarks>
    /// **The normal runs from the first point to the second, the gap is their distance, and the position is the first
    /// point** (`18008cab0`): `(0, 0, 0)` to `(0, 0, 2)` is up `z` two units apart, and a normal with no `x` takes its span
    /// from `n × (1, 0, 0)`, which is `y`.
    /// </remarks>
    [Test]
    public void PointPoint_TwoPointsApart_RunsFromTheFirstAndTheGapIsTheirDistance()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Point, Anywhere());
        IvpContactRecord record = new();

        IvpContactGeometry.PointPoint(point, (0d, 0d, 0d), (0d, 0d, 2d), record);

        record.Position.ShouldBe((0d, 0d, 0d));
        record.Normal.ShouldBe((0f, 0f, 1f));
        record.Span.ShouldBe((0f, 1f, 0f));
        point.Gap.ShouldBe(2f);
    }

    /// <remarks>
    /// **The span is crossed with `x` unless the normal's `x` squared reaches `0.9f`, and then with `z`**: a normal up `y`
    /// spans `(0, 0, −1)`, one along `x` spans `(0, −1, 0)`. Crossing the `x` normal with `x` would leave nothing to scale.
    /// </remarks>
    [TestCase(0d, 2d, 0f, 0f, -1f)]
    [TestCase(2d, 0d, 0f, -1f, 0f)]
    public void PointPoint_ANormalAlongAnAxis_SpansAcrossTheAxisItLeansOnLeast(
        double x, double y, float spanX, float spanY, float spanZ)
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Point, Anywhere());
        IvpContactRecord record = new();

        IvpContactGeometry.PointPoint(point, (0d, 0d, 0d), (x, y, 0d), record);

        record.Span.ShouldBe((spanX, spanY, spanZ));
    }

    /// <remarks>
    /// **The axis switches where the normal's `x` squared reaches `0.9f`**: a direction of length two whose `x` is `1.86`
    /// squares to about `0.865` and still spans along `z`; one whose `x` is `1.92` squares to about `0.922` and spans with
    /// no `z` at all. A threshold outside that bracket moves one of the two.
    /// </remarks>
    [TestCase(1.86d, true)]
    [TestCase(1.92d, false)]
    public void PointPoint_AroundTheAxisThreshold_SwitchesAtANormalXSquaredOf0Point9(double x, bool spansAlongZ)
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Point, Anywhere());
        IvpContactRecord record = new();

        IvpContactGeometry.PointPoint(point, (0d, 0d, 0d), (x, Math.Sqrt(4d - (x * x)), 0d), record);

        (Math.Abs(record.Span.Z) > 0.5f).ShouldBe(spansAlongZ);
    }

    /// <remarks>
    /// **Two points in one place have no direction and no gap, and nothing is divided by zero** (`18006fd40` returns zero
    /// and leaves the vector alone below `1e-19`).
    /// </remarks>
    [Test]
    public void PointPoint_CoincidentPoints_HaveNoDirectionAndNoGap()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Point, Anywhere());
        IvpContactRecord record = new();

        IvpContactGeometry.PointPoint(point, (1d, 2d, 3d), (1d, 2d, 3d), record);

        record.Normal.ShouldBe((0f, 0f, 0f));
        record.Span.ShouldBe((0f, 0f, 0f));
        point.Gap.ShouldBe(0f);
    }

    /// <remarks>
    /// **A point three units above an edge along `x`** (`18008c7c0`): the gap is its distance from the edge's line, the
    /// span is `(edge × offset)` scaled — here `−y` — and the normal is `edge × span` scaled, `−z`, from the point toward
    /// the edge. The position is the point.
    /// </remarks>
    [Test]
    public void PointEdge_APointAboveAnEdge_RunsDownToItAndSpansAcrossIt()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Edge, Flat());
        IvpContactRecord record = new();

        IvpContactGeometry.PointEdge(point, (1d, 0d, 3d), Edge, Flat(), record);

        record.Position.ShouldBe((1d, 0d, 3d));
        record.Normal.ShouldBe((0f, 0f, -1f));
        record.Span.ShouldBe((0f, -1f, 0f));
        point.Gap.ShouldBe(3f);
        record.Outside.ShouldBeFalse();
        point.FirstMeasure.ShouldBeFalse();
    }

    /// <remarks>
    /// **On the first measure only, a point projecting before the edge's start or past its end is outside** — the
    /// fraction `(edge · offset) / |edge|²` below zero or above one. At either end exactly it is inside.
    /// </remarks>
    [TestCase(-1d, true)]
    [TestCase(0d, false)]
    [TestCase(4d, false)]
    [TestCase(5d, true)]
    public void PointEdge_AFirstMeasureBeyondAnEnd_IsOutside(double x, bool outside)
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Edge, Flat());
        IvpContactRecord record = new();

        IvpContactGeometry.PointEdge(point, (x, 0d, 3d), Edge, Flat(), record);

        record.Outside.ShouldBe(outside);
    }

    /// <remarks>
    /// **The range is checked once per contact point, not per measure**: the second record of a point still before the
    /// edge's start is not marked.
    /// </remarks>
    [Test]
    public void PointEdge_ASecondMeasureBeyondAnEnd_IsNotChecked()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Edge, Flat());
        IvpContactGeometry.PointEdge(point, (-1d, 0d, 3d), Edge, Flat(), new IvpContactRecord());

        IvpContactRecord second = new();
        IvpContactGeometry.PointEdge(point, (-1d, 0d, 3d), Edge, Flat(), second);

        second.Outside.ShouldBeFalse();
    }

    /// <remarks>
    /// **A point on the edge's line has no direction off it**: the span falls back to `(1, 0, 0)`, the normal
    /// `edge × span` is zero and left unscaled, and the gap is zero.
    /// </remarks>
    [Test]
    public void PointEdge_APointOnTheLine_SpansAlongXWithNoNormal()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Edge, Flat());
        IvpContactRecord record = new();

        IvpContactGeometry.PointEdge(point, (2d, 0d, 0d), Edge, Flat(), record);

        record.Span.ShouldBe((1f, 0f, 0f));
        record.Normal.ShouldBe((0f, 0f, 0f));
        point.Gap.ShouldBe(0f);
    }

    /// <remarks>
    /// **The span falls back when the distance SQUARED is at most `1e-19`, not the distance**: a point `1e-10` off the edge is
    /// a real distance, but its square is `1e-20`, so the span is still `(1, 0, 0)` and the normal nothing.
    /// </remarks>
    [Test]
    public void PointEdge_APointWithinTheSquaredThreshold_SpansAlongXWithNoNormal()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Edge, Flat());
        IvpContactRecord record = new();

        IvpContactGeometry.PointEdge(point, (2d, 0d, 1e-10d), Edge, Flat(), record);

        record.Span.ShouldBe((1f, 0f, 0f));
        record.Normal.ShouldBe((0f, 0f, 0f));
    }

    /// <remarks>
    /// **The face normal is scaled by the contact point's reciprocal determinant, not to unit length by a root**
    /// (`18008c5b0`): the triangle `(0, 0, 0), (2, 0, 0), (0, 2, 0)` crosses to `(0, 0, 4)`, which the constructor's
    /// `0.25` makes `(0, 0, 1)`. So a point three above has gap `3` and normal `−z`; left unscaled they would be `12` and
    /// `−4`. The span is the passed edge's vector, `(2, 0, 0)`, not yet scaled.
    /// </remarks>
    [Test]
    public void PointTriangle_APointAboveAFace_RunsDownToItAtItsHeight()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Triangle, Face((0d, 0d, 0d)));
        IvpContactRecord record = new();

        IvpContactGeometry.PointTriangle(point, (0.5d, 0.5d, 3d), Edge, Face((0d, 0d, 0d)), record);

        record.Position.ShouldBe((0.5d, 0.5d, 3d));
        record.Normal.ShouldBe((0f, 0f, -1f));
        record.Span.ShouldBe((2f, 0f, 0f));
        point.Gap.ShouldBe(3f);
        record.Outside.ShouldBeFalse();
        point.FirstMeasure.ShouldBeFalse();
    }

    /// <remarks>
    /// **The height is signed and the first measure checks the triangle's weights**: a point under the face has gap `−1`
    /// and is inside; one past the hypotenuse is outside.
    /// </remarks>
    [TestCase(0.5d, 0.5d, -1d, -1f, false)]
    [TestCase(3d, 3d, 1d, 1f, true)]
    public void PointTriangle_AFirstMeasure_IsSignedAndOutsideOnlyPastAnEdge(
        double x, double y, double z, float gap, bool outside)
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Triangle, Face((0d, 0d, 0d)));
        IvpContactRecord record = new();

        IvpContactGeometry.PointTriangle(point, (x, y, z), Edge, Face((0d, 0d, 0d)), record);

        point.Gap.ShouldBe(gap);
        record.Outside.ShouldBe(outside);
    }

    /// <remarks>
    /// **The point goes into the body's frame and the normal and span come back out of it**: the same face half a turn
    /// about `z` at `(10, 0, 0)` has the point `(9.5, −0.5, 3)` above its first corner's `(0.5, 0.5)`, keeps its normal
    /// `−z`, and turns its span to `(−2, 0, 0)`.
    /// </remarks>
    [Test]
    public void PointTriangle_AFaceOnATurnedBody_MeasuresInItsFrameAndTurnsTheSpanOut()
    {
        IvpLedgeSide turned = Face((10d, 0d, 0d), (0f, 0f, 1f, 0f));
        IvpContactPoint point = Contact(IvpFeatureKind.Point, Anywhere(), IvpFeatureKind.Triangle, turned);
        IvpContactRecord record = new();

        IvpContactGeometry.PointTriangle(point, (9.5d, -0.5d, 3d), Edge, turned, record);

        record.Normal.ShouldBe((0f, 0f, -1f));
        record.Span.ShouldBe((-2f, 0f, 0f));
        point.Gap.ShouldBe(3f);
        record.Outside.ShouldBeFalse();
    }

    /// <remarks>
    /// **Two edges crossing a unit apart meet at each one's middle** (`18008cbc0`): the first along `x` at `z = 1`, the
    /// second along `y` at `z = 0`. The position is on the first edge, the normal runs down to the second, the gap is one,
    /// and the span is the first edge's direction.
    /// </remarks>
    [Test]
    public void EdgeEdge_TwoEdgesCrossingApart_TouchAtTheFirstEdgesClosestPoint()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Edge, High(), IvpFeatureKind.Edge, Low(0f, 0f));
        IvpContactRecord record = new();

        IvpContactGeometry.EdgeEdge(point, Edge, High(), Edge, Low(0f, 0f), record);

        record.Position.ShouldBe((0d, 0d, 1d));
        record.Normal.ShouldBe((0f, 0f, -1f));
        record.Span.ShouldBe((1f, 0f, 0f));
        point.Gap.ShouldBe(1f);
        record.Outside.ShouldBeFalse();
        point.FirstMeasure.ShouldBeFalse();
    }

    /// <remarks>
    /// **The second edge's fraction is checked as well as the first's**: a second edge running from `y = 1` to `5` meets the
    /// first edge's middle a quarter of its length before its own start, so the record is outside with the position still on
    /// the first edge.
    /// </remarks>
    [Test]
    public void EdgeEdge_AFirstMeasureBeforeTheSecondEdgesStart_IsOutside()
    {
        IvpLedgeSide beyond = Side([(0f, 1f, 0f), (0f, 5f, 0f), (5f, 0f, 0f)], (0d, 0d, 0d));
        IvpContactPoint point = Contact(IvpFeatureKind.Edge, High(), IvpFeatureKind.Edge, beyond);
        IvpContactRecord record = new();

        IvpContactGeometry.EdgeEdge(point, Edge, High(), Edge, beyond, record);

        record.Position.ShouldBe((0d, 0d, 1d));
        record.Outside.ShouldBeTrue();
    }

    /// <remarks>
    /// **The closest points are on the lines, so a first measure past an edge's end is outside**: the second edge moved to
    /// `x = 3` meets the first's line a quarter past its end.
    /// </remarks>
    [Test]
    public void EdgeEdge_AFirstMeasurePastAnEnd_IsOutsideAtThePointOnTheLine()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Edge, High(), IvpFeatureKind.Edge, Low(3f, 0f));
        IvpContactRecord record = new();

        IvpContactGeometry.EdgeEdge(point, Edge, High(), Edge, Low(3f, 0f), record);

        record.Position.ShouldBe((3d, 0d, 1d));
        record.Outside.ShouldBeTrue();
    }

    /// <remarks>
    /// **Edges that touch have no gap to point along, so the normal is their cross product** — `first × second`, up `z`
    /// here, the opposite way to the `−z` a gap below would give.
    /// </remarks>
    [Test]
    public void EdgeEdge_EdgesThatTouch_TakeTheCrossProductAsTheNormal()
    {
        IvpContactPoint point = Contact(IvpFeatureKind.Edge, High(), IvpFeatureKind.Edge, Low(0f, 1f));
        IvpContactRecord record = new();

        IvpContactGeometry.EdgeEdge(point, Edge, High(), Edge, Low(0f, 1f), record);

        record.Normal.ShouldBe((0f, 0f, 1f));
        point.Gap.ShouldBe(0f);
    }

    /// <remarks>
    /// **Parallel edges have no crossing at all**: the record is marked outside whatever the measure, the position is the
    /// first edge's start, the normal `x`, the span `y`, the gap the tolerance block's `[0x44]` — `DAT_18012d650`, not the
    /// contact gap the point started with — and the first-measure flag is left set.
    /// </remarks>
    [Test]
    public void EdgeEdge_ParallelEdges_TakeTheFirstEdgesStartAndFixedAxes()
    {
        IvpLedgeSide parallel = Side([(-2f, 0f, 0f), (2f, 0f, 0f), (0f, 5f, 0f)], (0d, 0d, 0d));
        IvpContactPoint point = Contact(IvpFeatureKind.Edge, High(), IvpFeatureKind.Edge, parallel);
        IvpContactRecord record = new();

        IvpContactGeometry.EdgeEdge(point, Edge, High(), Edge, parallel, record);

        record.Outside.ShouldBeTrue();
        record.Position.ShouldBe((-2d, 0d, 1d));
        record.Normal.ShouldBe((1f, 0f, 0f));
        record.Span.ShouldBe((0f, 1f, 0f));
        point.Gap.ShouldBe(IvpCollisionTolerance.ParallelEdgeGap);
        point.FirstMeasure.ShouldBeTrue();
    }

    /// <summary>A contact point between two features on two fresh objects, synapse A being the first.</summary>
    internal static IvpContactPoint Contact(
        IvpFeatureKind firstKind, IvpLedgeSide firstSide, IvpFeatureKind secondKind, IvpLedgeSide secondSide) =>
        new(
            new IvpMindist(new IvpSynapse(Edge, firstKind), new IvpSynapse(Edge, secondKind), 0f),
            new IvpCollisionObject(),
            firstSide,
            new IvpCollisionObject(),
            secondSide,
            now: 0d);

    /// <summary>A side nothing is measured on.</summary>
    internal static IvpLedgeSide Anywhere() => Side([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)], (0d, 0d, 0d));

    /// <summary>The edge <c>(0, 0, 0) → (4, 0, 0)</c> of a triangle in <c>z = 0</c>.</summary>
    internal static IvpLedgeSide Flat() => Side([(0f, 0f, 0f), (4f, 0f, 0f), (0f, 4f, 0f)], (0d, 0d, 0d));

    /// <summary>The triangle <c>(0, 0, 0), (2, 0, 0), (0, 2, 0)</c> facing up, on a body at a position and rotation.</summary>
    internal static IvpLedgeSide Face((double X, double Y, double Z) at, (float X, float Y, float Z, float W)? rotation = null) =>
        Side([(0f, 0f, 0f), (2f, 0f, 0f), (0f, 2f, 0f)], at, rotation);

    /// <summary>The first edge-edge fixture: <c>(−2, 0, 1) → (2, 0, 1)</c>.</summary>
    private static IvpLedgeSide High() => Side([(-2f, 0f, 1f), (2f, 0f, 1f), (0f, 5f, 1f)], (0d, 0d, 0d));

    /// <summary>The second: along <c>y</c> from <c>−2</c> to <c>2</c> at the given <c>x</c> and <c>z</c>.</summary>
    private static IvpLedgeSide Low(float x, float z) => Side([(x, -2f, z), (x, 2f, z), (5f, 0f, z)], (0d, 0d, 0d));

    /// <summary>One triangle whose edges hop to themselves, on a body at a position and rotation.</summary>
    internal static IvpLedgeSide Side(
        IReadOnlyList<(float X, float Y, float Z)> points,
        (double X, double Y, double Z) at,
        (float X, float Y, float Z, float W)? rotation = null) =>
        new(
            points,
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            IvpMatrix.FromRotation(rotation ?? (0f, 0f, 0f, 1f), at),
            at);
}
