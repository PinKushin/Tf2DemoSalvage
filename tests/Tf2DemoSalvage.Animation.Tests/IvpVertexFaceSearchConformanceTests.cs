using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's vertex-face time of impact over one step — <c>FUN_1800a1b50</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_1800a1b50` field by field*). The search starts its
/// event time at the interval's end; a point-plane root is event `0x20`; then each edge leaving the vertex whose
/// slope toward the face is under `(time − start) × speed` is refined toward the edge target, and a root there is
/// event `0x21`, moving the time earlier.
///
/// **The fixtures are a face at `z = 0` facing up on a body that does not move, and a tetrahedron whose point 0
/// is the vertex**, its three edges leaving it and walked in the order `0 → 2`, `0 → 3`, `0 → 1`. Units are
/// inches and seconds; the interval is one step of `0.015`.
/// </remarks>
public sealed class IvpVertexFaceSearchConformanceTests
{
    private const double End = IvpSearchFixtures.End;

    private static readonly (float X, float Y, float Z) Up = (0f, 0f, 1f);

    /// <remarks>
    /// **A vertex falling onto the face raises `0x20` when its height reaches the margin plus the extra radius.**
    /// Half an inch above that and falling at fifty inches a second, it arrives at `0.01`: the refinement marches
    /// three lattice ticks to the end, brackets, and regula falsi lands on the linear root in one pass.
    /// </remarks>
    [Test]
    public void Search_AVertexFallingOntoTheFace_RaisesAPointPlaneEventWhenItReachesTheMargin()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 50d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: (float)height, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(), height, fallingAt: 50f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBe(IvpVertexFaceSearch.PointPlaneEvent);
        impact.Time.ShouldBe(0.01d, 1e-6d);
    }

    /// <remarks>
    /// **The point-plane search starts from the mindist's length, not from where the vertex is** — the known
    /// distance `(double)(extra + length)` is passed, so slot 0 is never measured. The vertex sits still at `0.05`,
    /// inside the margin, while the length says an inch: from an inch the refinement cannot close within the step,
    /// so nothing is raised. Measured instead, `0.05` is inside and not moving away, an event at the start.
    /// </remarks>
    [Test]
    public void Search_AMindistLengthOutsideTheMargin_IsTrustedOverWhereTheVertexIs()
    {
        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 1d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: 1f, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(), 0.05d, fallingAt: 0f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBeNull();
        impact.Time.ShouldBe(End);
    }

    /// <remarks>**The control: the same vertex rising** finds no root, and the time stays the interval's end.</remarks>
    [Test]
    public void Search_AVertexRisingFromTheFace_RaisesNothingAndKeepsTheEnd()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 50d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: (float)height, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(), height, fallingAt: -50f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBeNull();
        impact.Time.ShouldBe(End);
    }

    /// <remarks>
    /// **Every edge leaving the vertex is examined, the start edge last.** With the vertex ten inches up and not
    /// moving, the point-plane search finds nothing; one edge turned to point down into the face has a slope far
    /// under the edge target `((min(length, margin) + 0.1·extra) · −(0.1·d · f)) / margin`, so its refinement
    /// answers at once: `0x21` at the start. Each of the three neighbours takes a turn as the steep one.
    /// </remarks>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void Search_AnyEdgeOfTheRingPointingIntoTheFace_RaisesAnEdgeEventAtTheStart(int steep)
    {
        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 1d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: 1f, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(steep, down: -1f), 10d, fallingAt: 0f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBe(IvpVertexFaceSearch.EdgeEvent);
        impact.Time.ShouldBe(0d);
    }

    /// <remarks>**The control for the ring: every edge pointing up** raises nothing.</remarks>
    [Test]
    public void Search_NoEdgePointingIntoTheFace_RaisesNothing()
    {
        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 1d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: 1f, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(), 10d, fallingAt: 0f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBeNull();
        impact.Time.ShouldBe(End);
    }

    /// <remarks>
    /// **The edge target takes the LESSER of the length and the margin** (`MINSS`). With `f = 1` and no extra
    /// radius the target is `min(length, 0.2499) · −0.02499 / 0.2499`: `−0.0099996` at a length of `0.1`, and
    /// `−0.02499` at `0.5`, where the length alone would give `−0.05`. An edge whose slope is about `−0.02` is
    /// under the first; one about `−0.03` is under the second and not under `−0.05`.
    /// </remarks>
    [TestCase(0.1f, -0.02f)]
    [TestCase(0.5f, -0.03f)]
    public void Search_AShallowEdge_IsMeasuredAgainstTheLesserOfLengthAndMargin(float length, float drop)
    {
        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 1d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: length, MarginClass: 0, Normal: Up),
            Vertex(Shallow(drop), 10d, fallingAt: 0f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBe(IvpVertexFaceSearch.EdgeEvent);
    }

    /// <remarks>
    /// **Only an edge whose slope is under `(time − start) · speed` is refined** (`COMISD`/`JNC`). With the interval
    /// reversed — start `0.015`, end `0` — and angular bounds summing to ten, that limit is `−0.15`. An edge at
    /// about `−0.1` is under the edge target, so a refinement would answer at once, but it is not under the limit and
    /// is never refined. One pointing straight down is under both.
    /// </remarks>
    [TestCase(-0.1f, false)]
    [TestCase(-10f, true)]
    public void Search_AnEdgeNotUnderTheSlopeLimit_IsNotRefined(float drop, bool raised)
    {
        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 1d, TotalBound: 0d, Start: End, End: 0d),
            new IvpMindistState(ExtraRadius: 0f, Length: 1f, MarginClass: 0, Normal: Up),
            Vertex(Shallow(drop), 10d, fallingAt: 0f, angularBound: 10f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        (impact.Event == IvpVertexFaceSearch.EdgeEvent).ShouldBe(raised);
    }

    /// <remarks>
    /// **An edge root after a point-plane root overwrites the kind and moves the time earlier.** The falling vertex
    /// raises `0x20` near `0.01`; an edge already pointing into the face then answers at the start.
    /// </remarks>
    [Test]
    public void Search_AnEdgeRootAfterAPointPlaneRoot_IsTheEventAndItsTime()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = IvpVertexFaceSearch.Search(
            new IvpImpactContext(ApproachSpeed: 50d, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: (float)height, MarginClass: 0, Normal: Up),
            Vertex(Tetrahedron(2, down: -1f), height, fallingAt: 50f, angularBound: 0f),
            new IvpLedgeEdge(0, 0),
            Face(inverseDiameter: 1f),
            new IvpLedgeEdge(0, 0));

        impact.Event.ShouldBe(IvpVertexFaceSearch.EdgeEvent);
        impact.Time.ShouldBe(0d);
    }

    /// <summary>Point 0 at the origin and its three neighbours above it, one optionally dropped to <paramref name="down"/>.</summary>
    private static (float X, float Y, float Z)[] Tetrahedron(int steep = 0, float down = 1f)
    {
        (float X, float Y, float Z)[] points = [(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (-1f, -1f, 1f)];

        if (steep > 0)
        {
            points[steep] = (points[steep].X, points[steep].Y, down);
        }

        return points;
    }

    /// <summary>Point 1 one inch along X and <paramref name="drop"/> down; the others well above.</summary>
    private static (float X, float Y, float Z)[] Shallow(float drop) =>
        [(0f, 0f, 0f), (1f, 0f, drop), (0f, 1f, 1f), (-1f, -1f, 1f)];

    private static IvpSearchSide Vertex(
        (float X, float Y, float Z)[] points, double height, float fallingAt, float angularBound) =>
        IvpSearchFixtures.Side(
            points,
            IvpSearchFixtures.TetrahedronTopology(),
            (0d, 0d, height),
            fallingAt,
            resting: false,
            new IvpCoreBounds(
                Radius: 0f, InverseDiameter: 0f, AngularSpeedBound: angularBound, LinearSpeed: 0f, SurfaceSpeedBound: 0f));

    /// <summary>A large triangle at <c>z = 0</c>, wound so its normal is <c>+Z</c>, on a body at rest.</summary>
    private static IvpSearchSide Face(float inverseDiameter) =>
        IvpSearchFixtures.Side(
            [(-100f, -100f, 0f), (100f, -100f, 0f), (0f, 100f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            (0d, 0d, 0d),
            fallingAt: 0f,
            resting: true,
            new IvpCoreBounds(
                Radius: 0f, InverseDiameter: inverseDiameter, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f));
}
