using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's point-point time of impact over one step — <c>FUN_1800a2b30</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The other three times of impact*). The point-point evaluator is
/// searched to the margin plus the extra radius and raises `0x10`; then every edge leaving the second point, and then
/// every edge leaving the first, is refined toward `|d| · min(margin², reach²) · −0.5 / max(radius)` — a length squared,
/// so carried in inches it is scaled by `0.0254` — and raises `0x11`.
///
/// **The fixtures are two tetrahedra with point 0 at their bodies' origins**, the first ten inches above the second
/// unless a test says otherwise: the first's neighbours above it and the second's below, so neither ring runs toward the
/// other point until a test turns one edge. Units are inches and seconds; the interval is one step of `0.015`.
/// </remarks>
public sealed class IvpPointPointSearchConformanceTests
{
    private const double End = IvpSearchFixtures.End;

    private static readonly IvpCoreBounds Still =
        new(Radius: 1f, InverseDiameter: 0f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f);

    /// <remarks>
    /// **A point falling onto a point raises `0x10` when their distance reaches the margin plus the extra radius.**
    /// Along the pair's normal the stretched projection passes the distance, so the distance is what is searched; half
    /// an inch above the margin at fifty inches a second, it arrives at `0.01`.
    /// </remarks>
    [Test]
    public void Search_APointFallingOntoAPoint_RaisesAPointPointEventWhenItReachesTheMargin()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = Search(
            50d,
            (float)height,
            IvpSearchFixtures.Side(Up(), (0d, 0d, height), 50f, Still),
            IvpSearchFixtures.Side(Down(), (0d, 0d, 0d), 0f, Still));

        impact.Event.ShouldBe(IvpPointPointSearch.PointPointEvent);
        impact.Time.ShouldBe(0.01d, 1e-6d);
    }

    /// <remarks>**The control: the same point rising** raises nothing and keeps the interval's end.</remarks>
    [Test]
    public void Search_APointRisingFromAPoint_RaisesNothingAndKeepsTheEnd()
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = Search(
            50d,
            (float)height,
            IvpSearchFixtures.Side(Up(), (0d, 0d, height), -50f, Still),
            IvpSearchFixtures.Side(Down(), (0d, 0d, 0d), 0f, Still));

        impact.Event.ShouldBeNull();
        impact.Time.ShouldBe(End);
    }

    /// <remarks>
    /// **Both rings are walked.** An edge of the second point's ring turned up at the first point, or an edge of the
    /// first point's ring turned down at the second, lies about seven inches behind the other point along itself, far
    /// under the target: `0x11` at the start. *What this does not pin is each ring's cache order*: walked the wrong way
    /// round, the untouched edges point at the other point instead and raise the same event. The falling and rising
    /// tests are what redden then, and did when it was tried.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Search_AnEdgeOfEitherRingRunningAtTheOtherPoint_RaisesARingEventAtTheStart(bool firstRing)
    {
        IvpImpact impact = Search(
            1d,
            10f,
            IvpSearchFixtures.Side(firstRing ? Up(turned: 1) : Up(), (0d, 0d, 10d), 0f, Still),
            IvpSearchFixtures.Side(firstRing ? Down() : Down(turned: 1), (0d, 0d, 0d), 0f, Still));

        impact.Event.ShouldBe(IvpPointPointSearch.RingEvent);
        impact.Time.ShouldBe(0d);
    }

    /// <remarks>
    /// **The ring target takes the LESSER of margin² and reach², over the LARGER radius, in metres.** A nearly level
    /// edge of the second ring lies `−0.0004` along itself from the first point. With radii `1` and `0.25` and nothing
    /// moving, reach is the length: at `0.1` the target is `0.01 · −0.5 / 1 · 0.0254 = −0.000127`, which the edge is
    /// under; at `10` the margin's square wins, `−0.000793`, which it is not. The smaller radius would make the first
    /// `−0.000508`, and inches unconverted `−0.005`, both missed.
    /// </remarks>
    [TestCase(0.1f, true)]
    [TestCase(10f, false)]
    public void Search_ANearlyLevelRingEdge_IsMeasuredAgainstTheLesserSquareOverTheLargerRadius(float length, bool raised)
    {
        (float X, float Y, float Z)[] level = Down();
        level[1] = (0f, 1f, 4e-5f);

        IvpImpact impact = Search(
            1d,
            length,
            IvpSearchFixtures.Side(Up(), (0d, 0d, 10d), 0f, Still),
            IvpSearchFixtures.Side(level, (0d, 0d, 0d), 0f, Still with { Radius = 0.25f }));

        (impact.Event == IvpPointPointSearch.RingEvent).ShouldBe(raised);
    }

    private static IvpImpact Search(double approachSpeed, float length, IvpSearchSide first, IvpSearchSide second) =>
        IvpPointPointSearch.Search(
            new IvpImpactContext(ApproachSpeed: approachSpeed, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: length, MarginClass: 0, Normal: (0f, 0f, 1f)),
            first,
            new IvpLedgeEdge(0, 0),
            second,
            new IvpLedgeEdge(0, 0));

    /// <summary>Point 0 at the origin and its neighbours an inch above, one optionally turned an inch below.</summary>
    private static (float X, float Y, float Z)[] Up(int turned = 0)
    {
        (float X, float Y, float Z)[] points = [(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (-1f, -1f, 1f)];

        if (turned > 0)
        {
            points[turned] = (points[turned].X, points[turned].Y, -1f);
        }

        return points;
    }

    /// <summary>Point 0 at the origin and its neighbours an inch below, one optionally turned an inch above.</summary>
    private static (float X, float Y, float Z)[] Down(int turned = 0)
    {
        (float X, float Y, float Z)[] points = [(0f, 0f, 0f), (0f, 1f, -1f), (1f, 0f, -1f), (-1f, -1f, -1f)];

        if (turned > 0)
        {
            points[turned] = (points[turned].X, points[turned].Y, 1f);
        }

        return points;
    }
}
