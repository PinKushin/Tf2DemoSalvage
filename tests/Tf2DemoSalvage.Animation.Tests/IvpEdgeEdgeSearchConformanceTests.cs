using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's edge-edge time of impact over one step — <c>FUN_1800a1420</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The other three times of impact*). The line-line evaluator, signed
/// by the pair's normal against the edges' cross product, is searched to the margin and raises `0x40`; the edges turning
/// parallel raises `0x41`; and four checks of one edge's direction against the other's two faces, each refined to
/// `−0.1·d · f` with `f` from the core of the edge whose direction is dotted, raise `0x42`.
///
/// **The first edge runs along X from its body's origin, ten inches above the second unless a test says otherwise, and
/// the second along Y.** Each is edge `(0, 0)` of a tetrahedron, so its own triangle's third point is point 2 and its
/// twin's is point 3. Units are inches and seconds.
/// </remarks>
public sealed class IvpEdgeEdgeSearchConformanceTests
{
    private const double End = IvpSearchFixtures.End;

    private static readonly IvpCoreBounds Soft =
        new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f);

    /// <summary>A core whose face target, <c>−0.1·d · 100</c>, no unit dot reaches.</summary>
    private static readonly IvpCoreBounds Stiff = Soft with { InverseDiameter = 100f };

    /// <remarks>
    /// **The sign follows the pair's normal against `a × b`.** With the normal along `+Z` the falling X edge is
    /// `+height` from the Y edge and reaches the margin at `0.01`: `0x40`. With it along `−Z` the distance is negative
    /// from the start, inside the margin and rising toward zero, and nothing is raised.
    /// </remarks>
    [TestCase(1f, true)]
    [TestCase(-1f, false)]
    public void Search_AnEdgeFallingAcrossAnEdge_RaisesALineEventWhenTheNormalAgreesWithTheCross(float normal, bool raised)
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        IvpImpact impact = Search(
            50d,
            normal,
            IvpSearchFixtures.Side(AlongX(), (0d, 0d, height), 50f, Stiff),
            IvpSearchFixtures.Side(AlongY(), (0d, 0d, 0d), 0f, Stiff));

        if (raised)
        {
            impact.Event.ShouldBe(IvpEdgeEdgeSearch.LineEvent);
            impact.Time.ShouldBe(0.01d, 1e-6d);
        }
        else
        {
            impact.Event.ShouldBeNull();
            impact.Time.ShouldBe(End);
        }
    }

    /// <remarks>
    /// **Two parallel edges raise `0x41` at the start**, after the line search: their cross product is zero, which is
    /// already under `1e-19`.
    /// </remarks>
    [Test]
    public void Search_TwoParallelEdges_RaisesAParallelEventAtTheStart()
    {
        IvpImpact impact = Search(
            1d,
            1f,
            IvpSearchFixtures.Side(AlongX(), (0d, 0d, 10d), 0f, Stiff),
            IvpSearchFixtures.Side(AlongX(), (0d, 0d, 0d), 0f, Stiff));

        impact.Event.ShouldBe(IvpEdgeEdgeSearch.ParallelEvent);
        impact.Time.ShouldBe(0d);
    }

    /// <remarks>
    /// **Each of the four face checks pairs one direction with one face and its own sign**, and takes its target from
    /// the core of the edge whose direction it dots. Each case tilts exactly one face so that exactly one check is under
    /// `−0.1·d` and levels its partner to zero, with the other edge's core stiff:
    ///
    /// 1. the Y edge, as it is, against the X edge's twin triangle;
    /// 2. the Y edge, negated, against the X edge's own triangle;
    /// 3. the X edge, as it is, against the Y edge's twin triangle;
    /// 4. the X edge, negated, against the Y edge's own triangle.
    ///
    /// The normal is `+Z`, so the sign is `+1`. A check paired with the wrong face or sign, or taking the face's core,
    /// raises nothing.
    /// </remarks>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void Search_AnEdgeRunningIntoAFaceOfTheOther_RaisesAFaceEventAtTheStart(int check)
    {
        (float X, float Y, float Z)[] alongX = check switch
        {
            1 => AlongX(own: (0.5f, -1f, 0f), twin: (0.5f, 1f, -1f)),
            2 => AlongX(own: (0.5f, -1f, -1f), twin: (0.5f, 1f, 0f)),
            _ => AlongX(),
        };

        (float X, float Y, float Z)[] alongY = check switch
        {
            3 => AlongY(own: (-1f, 0.5f, 0f), twin: (0f, 0.5f, 1f)),
            4 => AlongY(own: (0f, 0.5f, 1f), twin: (1f, 0.5f, 0f)),
            _ => AlongY(),
        };

        IvpImpact impact = Search(
            1d,
            1f,
            IvpSearchFixtures.Side(alongX, (0d, 0d, 10d), 0f, check <= 2 ? Stiff : Soft),
            IvpSearchFixtures.Side(alongY, (0d, 0d, 0d), 0f, check <= 2 ? Soft : Stiff));

        impact.Event.ShouldBe(IvpEdgeEdgeSearch.FaceEvent);
        impact.Time.ShouldBe(0d);
    }

    private static IvpImpact Search(double approachSpeed, float normal, IvpSearchSide first, IvpSearchSide second) =>
        IvpEdgeEdgeSearch.Search(
            new IvpImpactContext(ApproachSpeed: approachSpeed, TotalBound: 0d, Start: 0d, End: End),
            new IvpMindistState(ExtraRadius: 0f, Length: 1f, MarginClass: 0, Normal: (0f, 0f, normal)),
            first,
            new IvpLedgeEdge(0, 0),
            second,
            new IvpLedgeEdge(0, 0));

    /// <summary>An edge from the origin to <c>(1, 0, 0)</c>, with its own and twin triangles' third points.</summary>
    private static (float X, float Y, float Z)[] AlongX(
        (float X, float Y, float Z)? own = null, (float X, float Y, float Z)? twin = null) =>
        [(0f, 0f, 0f), (1f, 0f, 0f), own ?? (0.5f, -1f, 1f), twin ?? (0.5f, 1f, 1f)];

    /// <summary>An edge from the origin to <c>(0, 1, 0)</c>, with its own and twin triangles' third points.</summary>
    private static (float X, float Y, float Z)[] AlongY(
        (float X, float Y, float Z)? own = null, (float X, float Y, float Z)? twin = null) =>
        [(0f, 0f, 0f), (0f, 1f, 0f), own ?? (-1f, 0.5f, -1f), twin ?? (1f, 0.5f, -1f)];
}
