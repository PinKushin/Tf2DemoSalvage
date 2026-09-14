using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A pair's friction contact point as <c>FUN_180082ed0</c> builds it (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*): synapse A's feature first and
/// the other record's second, each linked at the head of its object's list at `+0x50`; the time now; for a second feature
/// that is a triangle, the reciprocal of its normal's length plus `1e-18f`; and a gap of zero, no slide, and the
/// first-measure flag set.
/// </remarks>
public sealed class IvpContactPointConformanceTests
{
    private static readonly IvpLedgeEdge Edge = new(0, 0);

    /// <remarks>
    /// **Synapse A comes first**: the flags' bits 8–9 name its record, and the other is `((flags ^ 0x100) >> 8) &amp; 3`. With
    /// bit 8 set, record one's feature and object are the first.
    /// </remarks>
    [Test]
    public void Constructor_SynapseAIsRecordOne_PutsRecordOneFirst()
    {
        IvpSynapse zero = new(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point);
        IvpSynapse one = new(new IvpLedgeEdge(0, 1), IvpFeatureKind.Edge);
        IvpCollisionObject objectZero = new();
        IvpCollisionObject objectOne = new();

        IvpContactPoint point = new(
            new IvpMindist(zero, one, 0f) { Flags = 0x100 },
            objectZero,
            IvpContactGeometryConformanceTests.Anywhere(),
            objectOne,
            IvpContactGeometryConformanceTests.Anywhere(),
            now: 0d);

        point.First.ShouldBe(one);
        point.FirstObject.ShouldBeSameAs(objectOne);
        point.Second.ShouldBe(zero);
        point.SecondObject.ShouldBeSameAs(objectZero);
    }

    /// <remarks>
    /// **Each synapse goes in at the head of its object's list**, so the pair's newer contact point is found first by the
    /// walk that looks for an existing one.
    /// </remarks>
    [Test]
    public void Constructor_TwoContactPointsOnOnePair_ListTheNewestFirstOnBothObjects()
    {
        IvpCollisionObject objectZero = new();
        IvpCollisionObject objectOne = new();

        IvpContactPoint older = Between(objectZero, objectOne);
        IvpContactPoint newer = Between(objectZero, objectOne);

        objectZero.ContactPoints.ShouldBe([newer, older]);
        objectOne.ContactPoints.ShouldBe([newer, older]);
    }

    /// <remarks>
    /// **A second feature that is a triangle stores the reciprocal of its normal's length plus `1e-18f`**, narrowed: the
    /// triangle with legs of two crosses to length four, `0.25`; legs of a half cross to a quarter, `4`.
    /// </remarks>
    [TestCase(2f, 0.25f)]
    [TestCase(0.5f, 4f)]
    public void Constructor_ASecondTriangle_StoresTheReciprocalOfItsNormalsLength(float leg, float determinant)
    {
        IvpLedgeSide face = IvpContactGeometryConformanceTests.Side([(0f, 0f, 0f), (leg, 0f, 0f), (0f, leg, 0f)], (0d, 0d, 0d));

        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(
            IvpFeatureKind.Point, IvpContactGeometryConformanceTests.Anywhere(), IvpFeatureKind.Triangle, face);

        point.InverseTriangleDeterminant.ShouldBe(determinant);
    }

    /// <remarks>
    /// **The `1e-18f` keeps a degenerate triangle finite**: a normal of length zero gives about `1e18`, where a bare
    /// reciprocal would be infinite and a larger floor far smaller.
    /// </remarks>
    [Test]
    public void Constructor_ADegenerateSecondTriangle_StaysFiniteThroughTheFloor()
    {
        IvpLedgeSide collapsed = IvpContactGeometryConformanceTests.Side([(1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f)], (0d, 0d, 0d));

        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(
            IvpFeatureKind.Point, IvpContactGeometryConformanceTests.Anywhere(), IvpFeatureKind.Triangle, collapsed);

        float.IsFinite(point.InverseTriangleDeterminant).ShouldBeTrue();
        point.InverseTriangleDeterminant.ShouldBeGreaterThan(1e17f);
    }

    /// <remarks>
    /// **Only a triangle's is written.** The engine leaves the four bytes as the allocation left them, and only the
    /// point–triangle measure reads them; the port's are zero.
    /// </remarks>
    [Test]
    public void Constructor_ASecondFeatureThatIsNotATriangle_LeavesTheDeterminantAtZero()
    {
        IvpLedgeSide collapsed = IvpContactGeometryConformanceTests.Side([(1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f)], (0d, 0d, 0d));

        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(
            IvpFeatureKind.Point, IvpContactGeometryConformanceTests.Anywhere(), IvpFeatureKind.Edge, collapsed);

        point.InverseTriangleDeterminant.ShouldBe(0f);
    }

    /// <remarks>
    /// **A fresh contact point starts at the contact gap, with no slide and its first measure still ahead**, and its time is
    /// when it was built: the gap is `DAT_18012d64c`, the tolerance block's `[0x43]` — zero in the image only because the
    /// block is filled at startup through its base address.
    /// </remarks>
    [Test]
    public void Constructor_AFreshContactPoint_StartsAtTheContactGapWithNoSlideAndItsFirstMeasureAhead()
    {
        IvpContactPoint point = new(
            new IvpMindist(new IvpSynapse(Edge, IvpFeatureKind.Point), new IvpSynapse(Edge, IvpFeatureKind.Point), 0f),
            new IvpCollisionObject(),
            IvpContactGeometryConformanceTests.Anywhere(),
            new IvpCollisionObject(),
            IvpContactGeometryConformanceTests.Anywhere(),
            now: 7.5d);

        point.Gap.ShouldBe(IvpCollisionTolerance.ContactGap);
        point.Slide.ShouldBe((0f, 0f));
        point.FirstMeasure.ShouldBeTrue();
        point.LastMeasured.ShouldBe(7.5d);
        point.Record.ShouldBeNull();
    }

    private static IvpContactPoint Between(IvpCollisionObject objectZero, IvpCollisionObject objectOne) =>
        new(
            new IvpMindist(new IvpSynapse(Edge, IvpFeatureKind.Point), new IvpSynapse(Edge, IvpFeatureKind.Point), 0f),
            objectZero,
            IvpContactGeometryConformanceTests.Anywhere(),
            objectOne,
            IvpContactGeometryConformanceTests.Anywhere(),
            now: 0d);
}
