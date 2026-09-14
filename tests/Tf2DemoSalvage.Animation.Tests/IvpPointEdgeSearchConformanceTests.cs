using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's point-edge time of impact over one step — <c>FUN_1800a1ff0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The other three times of impact*). The point-line evaluator is
/// searched to the margin plus the extra radius and raises `0x30`; the planes through the edge square to its own
/// triangle and then to its twin's are refined to `−0.1·d` and raise `0x31`; then every edge leaving the point is
/// refined toward `min(margin, length) · 2·f · −0.3`, `f` the edge's core's `+0x54`, and raises `0x32`.
///
/// **The edge is a roof ridge along X** from the origin: its own triangle falls away toward `−Y` and its twin's toward
/// `+Y`, so the plane square to the first is `(y + z)/√2` and to the second `(z − y)/√2`, and the point is above the
/// ridge in both. The point is point 0 of a tetrahedron whose neighbours are above it. Units are metres and seconds.
/// </remarks>
public sealed class IvpPointEdgeSearchConformanceTests
{
    private const double End = IvpSearchFixtures.End;

    private static readonly IvpCoreBounds Still =
        new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f);

    /// <summary>A core whose ring target, <c>−0.3 · 2 · 10000 · min(margin, length)</c>, no unit slope reaches.</summary>
    private static readonly IvpCoreBounds Stiff = Still with { InverseDiameter = 10000f };

    /// <remarks>
    /// **A point falling onto the edge raises `0x30` when its distance from the line reaches the margin plus the extra
    /// radius.** Straight above the ridge the handicapped projection passes the distance, so the distance is searched;
    /// half a metre above the margin at fifty metres a second, it arrives at `0.01`.
    /// </remarks>
    [Test]
    public void Search_APointFallingOntoTheEdge_RaisesAPointLineEventWhenItReachesTheMargin()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = Search(
            50d,
            (float)height,
            IvpSearchFixtures.Side(Up(), (0.5d, 0d, height), 50f, Still),
            IvpSearchFixtures.Side(Roof(), (0d, 0d, 0d), 0f, Still));

        impact.Event.ShouldBe(IvpPointEdgeSearch.PointLineEvent);
        impact.Time.ShouldBe(0.01d, 1e-6d);
    }

    /// <remarks>**The control: the same point rising** raises nothing and keeps the interval's end.</remarks>
    [Test]
    public void Search_APointRisingFromTheEdge_RaisesNothingAndKeepsTheEnd()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = Search(
            50d,
            (float)height,
            IvpSearchFixtures.Side(Up(), (0.5d, 0d, height), -50f, Still),
            IvpSearchFixtures.Side(Roof(), (0d, 0d, 0d), 0f, Still));

        impact.Event.ShouldBeNull();
        impact.Time.ShouldBe(End);
    }

    /// <remarks>
    /// **Both planes are searched: the edge's own triangle, then its twin's.** A point three metres off to `−Y` and one
    /// up is `−1.41` past the own plane and in front of the twin's; three metres off to `+Y`, the other way round. Each
    /// raises `0x31` at the start, and a plane built from the wrong winding faces away and raises nothing.
    /// </remarks>
    [TestCase(-3d)]
    [TestCase(3d)]
    public void Search_APointPastEitherPlaneSquareToTheEdge_RaisesAPlaneEventAtTheStart(double across)
    {
        IvpImpact impact = Search(
            1d,
            1f,
            IvpSearchFixtures.Side(Up(), (0.5d, across, 1d), 0f, Still),
            IvpSearchFixtures.Side(Roof(), (0d, 0d, 0d), 0f, Stiff));

        impact.Event.ShouldBe(IvpPointEdgeSearch.PlaneEvent);
        impact.Time.ShouldBe(0d);
    }

    /// <remarks>
    /// **The ring target is `min(margin, length) · (f + f) · −0.3f`, `f` from the EDGE's core.** With the margin now
    /// `0.00634746`, that target's largest reach — at `length` over the margin — is `0.00634746 · 2 · −0.3 = −0.00381`.
    /// An edge of the point's ring about `−0.001` toward the ridge is under `0.001 · 2 · −0.3 = −0.0006` (`length` under
    /// the margin, so `length` wins) and not under `−0.00381` (`length` over the margin, so the margin wins); the
    /// point's core, `f = 0`, would raise both.
    /// </remarks>
    [TestCase(0.001f, true)]
    [TestCase(1f, false)]
    public void Search_AShallowRingEdge_IsMeasuredAgainstTheLesserOfMarginAndLength(float length, bool raised)
    {
        IvpImpact impact = Search(
            1d,
            length,
            IvpSearchFixtures.Side(Shallow(-0.001f), (0.5d, 0d, 3d), 0f, Still with { InverseDiameter = 0f }),
            IvpSearchFixtures.Side(Roof(), (0d, 0d, 0d), 0f, Still));

        (impact.Event == IvpPointEdgeSearch.RingEvent).ShouldBe(raised);
    }

    /// <remarks>
    /// **The ring's gap floor is `1e-8` metres, `DAT_1800fb100`** (<see cref="IvpPointEdgeSearch"/> remarks): with
    /// `Length` at `1e-7`, over the metre floor and under the floor times `39.3700787`, the gap the floor guards IS
    /// `Length` (`TotalBound` is zero, so nothing is subtracted from it) and the metre floor keeps it there. The
    /// point's core speed of `1e-5` over that gives the ring's speed `1e-5 / 1e-7 = 100` and its inverse `0.01`, which
    /// still fits under `Refine`'s first-step check against the `0.015` interval, so the march runs and finds the point
    /// crossing the ridge's own plane — height zero, three metres down at a thousand a second — at `0.003`. The
    /// inches-scaled floor raises the gap to `3.937e-7`, dropping the speed to about `25.4` and raising the inverse
    /// past `0.015`, so the first-step check fails before anything is marched and no ring event is raised at all.
    /// `InverseDiameter` is raised to `Margin / Length` so the ring TARGET keeps the same reach as the margin-driven
    /// cases above — the target and the floor share the one `Length` field, and shrinking it for the floor would
    /// otherwise shrink the target to nearly zero too.
    /// </remarks>
    [Test]
    public void Search_AGapBetweenTheFloorAndItsInchesScaling_FindsTheRingCrossingOnlyAtTheMetreFloor()
    {
        float length = 1e-7f;
        float inverseDiameter = IvpCollisionTolerance.Margin / length;

        IvpImpact impact = Search(
            1d,
            length,
            IvpSearchFixtures.Side(Shallow(-0.001f), (0.5d, 0d, 3d), 1000f, Still with { InverseDiameter = 0f, LinearSpeed = 1e-5f }),
            IvpSearchFixtures.Side(Roof(), (0d, 0d, 0d), 0f, Still with { InverseDiameter = inverseDiameter }));

        impact.Event.ShouldBe(IvpPointEdgeSearch.RingEvent);
        impact.Time.ShouldBe(0.003d, 1e-6d);
    }

    private static IvpImpact Search(double approachSpeed, float length, IvpSearchSide pointSide, IvpSearchSide edgeSide) =>
        IvpPointEdgeSearch.Search(
            new IvpImpactContext(ApproachSpeed: approachSpeed, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: length, MarginClass: 0, Normal: (0f, 0f, 1f)),
            pointSide,
            new IvpLedgeEdge(0, 0),
            edgeSide,
            new IvpLedgeEdge(0, 0));

    /// <summary>A ridge from the origin to <c>(1, 0, 0)</c>, its own triangle falling to <c>−Y</c> and its twin's to <c>+Y</c>.</summary>
    private static (float X, float Y, float Z)[] Roof() =>
        [(0f, 0f, 0f), (1f, 0f, 0f), (0.5f, -1f, -1f), (0.5f, 1f, -1f)];

    /// <summary>Point 0 at the origin and its neighbours a metre above.</summary>
    private static (float X, float Y, float Z)[] Up() =>
        [(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (-1f, -1f, 1f)];

    /// <summary>Point 1 a metre along Y and <paramref name="drop"/> down; the others above.</summary>
    private static (float X, float Y, float Z)[] Shallow(float drop) =>
        [(0f, 0f, 0f), (0f, 1f, drop), (1f, 0f, 1f), (-1f, -1f, 1f)];
}
