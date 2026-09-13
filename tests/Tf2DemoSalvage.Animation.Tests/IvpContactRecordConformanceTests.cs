using System;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The record IVP builds each time a pair's contact point collides — <c>FUN_18008d0c0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*): the measure the two features'
/// kinds pick; the first object's extra radius moving the position along the normal and both coming off the gap, which
/// never goes below zero; the span scaled and crossed with the normal; for each core not flagged `2`, the arm and normal
/// put into its own frame, their cross product, the velocity at the arm and the inverse mass along the normal; and the
/// contact point's slide advanced by the relative velocity along each span over the time since its last measure.
///
/// **The fixture**: a vertex at `(0.5, 0.5, 3)` on a core at `(0, 0.25, 4)`, falling at 2 and spinning about `z` at 1, with
/// inverse inertia `(2, 4, 8)` and inverse mass `0.5`, three units over the face `(0, 0, 0), (2, 0, 0), (0, 2, 0)` of a
/// static body, measured half a second after the contact point was built. Every number in it is exact in float.
/// </remarks>
public sealed class IvpContactRecordConformanceTests
{
    private const double Built = 10d;

    private const double Now = 10.5d;

    private static readonly IvpLedgeEdge Edge = new(0, 0);

    private static readonly (float X, float Y, float Z, float W) Unturned = (0f, 0f, 0f, 1f);

    /// <remarks>
    /// **The kinds pick the point–triangle measure, and the tail scales its span and crosses it with the normal**: the
    /// face's edge `(2, 0, 0)` becomes `(1, 0, 0)`, and `n × s` with `n = −z` is `(0, −1, 0)`.
    /// </remarks>
    [Test]
    public void Build_AVertexOverAFace_MeasuresTheFaceAndCrossesTheScaledSpanWithTheNormal()
    {
        IvpContactPoint point = VertexOverFace();

        IvpContactRecord record = IvpContactRecord.Build(point, Vertex(Falling()), StaticFace(), Now);

        point.Record.ShouldBeSameAs(record);
        record.Position.ShouldBe((0.5d, 0.5d, 3d));
        record.Normal.ShouldBe((0f, 0f, -1f));
        point.Gap.ShouldBe(3f);
        record.Span.ShouldBe((1f, 0f, 0f));
        record.CrossSpan.ShouldBe((0f, -1f, 0f));
    }

    /// <remarks>
    /// **A moving core's terms are taken in its own frame** (`FUN_1800708a0`, the inlined transpose): the arm is the
    /// position less the core's, `(0.5, 0.25, −1)`, and the turn is `arm × normal`, `(−0.25, 0.5, 0)`. The inverse mass along
    /// the normal is `((t.y·I.y)·t.y + (t.x·I.x)·t.x) + (t.z·I.z)·t.z + m` — `1 + 0.125 + 0 + 0.5 = 1.625`; **each lane
    /// pairs with its own inertia**, and exchanging `x`'s and `y`'s would give `1.25`. The static face contributes nothing
    /// and names no core, and the virtual mass is the reciprocal.
    /// </remarks>
    [Test]
    public void Build_AMovingCoreAgainstAStaticOne_TakesOnlyTheMovingCoresTermsInItsFrame()
    {
        IvpRigidBody falling = Falling();

        IvpContactRecord record = IvpContactRecord.Build(VertexOverFace(), Vertex(falling), StaticFace(), Now);

        record.FirstCore.ShouldBeSameAs(falling);
        record.FirstArm.ShouldBe((0.5f, 0.25f, -1f));
        record.FirstTurn.ShouldBe((-0.25f, 0.5f, 0f));
        record.SecondCore.ShouldBeNull();
        record.SecondArm.ShouldBe((0f, 0f, 0f));
        record.SecondTurn.ShouldBe((0f, 0f, 0f));
        record.InverseMass.ShouldBe(1.625f);
        record.VirtualMass.ShouldBe(1f / 1.625f);
    }

    /// <remarks>
    /// **The slide is advanced by the relative velocity along each span times the float time since the last measure**:
    /// the vertex moves at `v + R·(ω × arm)` = `(−0.25, 0.5, −2)` (`FUN_180077fa0`), which is `−0.25` along the span and
    /// `−0.5` along its cross, so over half a second the slide goes from `(0, 0)` to `(0.125, 0.25)`. The time, position and
    /// normal are kept for the next measure.
    /// </remarks>
    [Test]
    public void Build_HalfASecondOn_AdvancesTheSlideByTheRelativeVelocityAlongEachSpan()
    {
        IvpContactPoint point = VertexOverFace();

        IvpContactRecord.Build(point, Vertex(Falling()), StaticFace(), Now);

        point.Slide.ShouldBe((0.125f, 0.25f));
        point.LastMeasured.ShouldBe(Now);
        point.LastPosition.ShouldBe((0.5f, 0.5f, 3f));
        point.LastNormal.ShouldBe((0f, 0f, -1f));
    }

    /// <remarks>
    /// **Two moving cores add their inverse masses and the slide follows their difference**: a face core at `(0, 0, −1)`
    /// sliding along `x` at 1 has arm `(0.5, 0.5, 4)`, turn `(−0.5, 0.5, 0)` and, with unit inverse inertia and inverse mass
    /// `0.25`, adds `0.75`. The relative velocity `(−1.25, 0.5, −2)` slides `0.625` along the span.
    /// </remarks>
    [Test]
    public void Build_TwoMovingCores_AddTheirInverseMassesAndSlideByTheirDifference()
    {
        IvpContactPoint point = VertexOverFace();
        IvpRigidBody sliding = new() { Velocity = (1f, 0f, 0f), InverseInertia = (1f, 1f, 1f), InverseMass = 0.25f };
        IvpContactBody face = new(IvpContactGeometryConformanceTests.Face((0d, 0d, 0d)), At(sliding, (0d, 0d, -1d)), 0f);

        IvpContactRecord record = IvpContactRecord.Build(point, Vertex(Falling()), face, Now);

        record.SecondCore.ShouldBeSameAs(sliding);
        record.SecondArm.ShouldBe((0.5f, 0.5f, 4f));
        record.SecondTurn.ShouldBe((-0.5f, 0.5f, 0f));
        record.InverseMass.ShouldBe(2.375f);
        record.VirtualMass.ShouldBe(1f / 2.375f);
        point.Slide.ShouldBe((0.625f, 0.25f));
    }

    /// <remarks>
    /// **The time since the last measure is narrowed to float before it scales the slide**: `1234567.1` seconds narrows to
    /// `1234567.125`, and three units a second along the span over that is `3703701.375` — exactly between two floats, so it
    /// rounds to the even `3703701.5`. Scaled by the unnarrowed time it would be `3703701.3`, which rounds to `3703701.25`.
    /// </remarks>
    [Test]
    public void Build_ALongTimeSinceTheLastMeasure_NarrowsItToFloatBeforeScaling()
    {
        IvpContactPoint point = VertexOverFace();
        IvpRigidBody drifting = new() { Velocity = (-3f, 0f, 0f), InverseInertia = (1f, 1f, 1f), InverseMass = 1f };

        IvpContactRecord.Build(point, Vertex(drifting), StaticFace(), Built + 1234567.1d);

        point.Slide.Span.ShouldBe(3703701.5f);
    }

    /// <remarks>
    /// **The first object's extra radius moves the position along the normal, and both radii come off the gap, which is
    /// never left below zero**: radii `0.25` and `0.5` put the position at `z = 2.75` and leave a gap of `2.25`; a second
    /// radius of `5` would leave `−2.25`, and the gap is zero instead.
    /// </remarks>
    [TestCase(0.5f, 2.25f)]
    [TestCase(5f, 0f)]
    public void Build_ExtraRadii_MoveThePositionAlongTheNormalAndComeOffTheGap(float secondRadius, float gap)
    {
        IvpContactPoint point = VertexOverFace();

        IvpContactRecord record = IvpContactRecord.Build(
            point, Vertex(Falling()) with { ExtraRadius = 0.25f }, StaticFace() with { ExtraRadius = secondRadius }, Now);

        record.Position.ShouldBe((0.5d, 0.5d, 2.75d));
        point.Gap.ShouldBe(gap);
    }

    /// <remarks>
    /// **A ball is measured from its object's position, and its range is checked on every measure**: the builder sets
    /// the first-measure flag again before measuring from a ball, so a ball before the edge's start is outside the second
    /// time too. The ball's ledge points are nowhere near, so reading them instead would move the position.
    /// </remarks>
    [Test]
    public void Build_ABallFirst_MeasuresFromItsPositionAndChecksTheRangeEveryTime()
    {
        IvpLedgeSide ball = IvpContactGeometryConformanceTests.Side([(9f, 9f, 9f), (9f, 8f, 9f), (8f, 9f, 9f)], (-1d, 0d, 3d));
        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(
            IvpFeatureKind.Ball, ball, IvpFeatureKind.Edge, IvpContactGeometryConformanceTests.Flat());
        IvpContactBody first = new(ball, At(Falling(), (-1d, 0d, 4d)), 0f);
        IvpContactBody second = Static(IvpContactGeometryConformanceTests.Flat());

        IvpContactRecord.Build(point, first, second, Now);
        IvpContactRecord again = IvpContactRecord.Build(point, first, second, Now + 1d);

        again.Position.ShouldBe((-1d, 0d, 3d));
        again.Outside.ShouldBeTrue();
    }

    /// <remarks>**The control: a point in the same place is checked only on its first measure.**</remarks>
    [Test]
    public void Build_APointFirst_ChecksTheRangeOnlyOnTheFirstMeasure()
    {
        IvpLedgeSide vertex = IvpContactGeometryConformanceTests.Side([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)], (-1d, 0d, 3d));
        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(
            IvpFeatureKind.Point, vertex, IvpFeatureKind.Edge, IvpContactGeometryConformanceTests.Flat());
        IvpContactBody first = new(vertex, At(Falling(), (-1d, 0d, 4d)), 0f);
        IvpContactBody second = Static(IvpContactGeometryConformanceTests.Flat());

        IvpContactRecord firstRecord = IvpContactRecord.Build(point, first, second, Now);
        IvpContactRecord again = IvpContactRecord.Build(point, first, second, Now + 1d);

        firstRecord.Outside.ShouldBeTrue();
        again.Outside.ShouldBeFalse();
    }

    /// <remarks>
    /// **The engine asserts on a first feature that is a triangle, and on any kind it has no measure for** — lines `0x1c4`
    /// and `0x1ba` of its file.
    /// </remarks>
    [TestCase(IvpFeatureKind.Triangle, IvpFeatureKind.Point)]
    [TestCase(IvpFeatureKind.Backside, IvpFeatureKind.Point)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Backside)]
    public void Build_AFirstTriangleOrAKindWithNoMeasure_IsRefused(IvpFeatureKind firstKind, IvpFeatureKind secondKind)
    {
        IvpLedgeSide anywhere = IvpContactGeometryConformanceTests.Anywhere();
        IvpContactPoint point = IvpContactGeometryConformanceTests.Contact(firstKind, anywhere, secondKind, anywhere);

        Should.Throw<InvalidOperationException>(() => IvpContactRecord.Build(point, Static(anywhere), Static(anywhere), Now));
    }

    private static IvpContactPoint VertexOverFace() =>
        new(
            new IvpMindist(new IvpSynapse(Edge, IvpFeatureKind.Point), new IvpSynapse(Edge, IvpFeatureKind.Triangle), 0f),
            new IvpCollisionObject(),
            VertexSide(),
            new IvpCollisionObject(),
            IvpContactGeometryConformanceTests.Face((0d, 0d, 0d)),
            now: Built);

    private static IvpLedgeSide VertexSide() =>
        IvpContactGeometryConformanceTests.Side([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)], (0.5d, 0.5d, 3d));

    private static IvpRigidBody Falling() =>
        new() { Velocity = (0f, 0f, -2f), AngularVelocity = (0f, 0f, 1f), InverseInertia = (2f, 4f, 8f), InverseMass = 0.5f };

    private static IvpContactBody Vertex(IvpRigidBody core) =>
        new(VertexSide(), At(core, (0d, 0.25d, 4d)), 0f);

    private static IvpContactBody StaticFace() => Static(IvpContactGeometryConformanceTests.Face((0d, 0d, 0d)));

    private static IvpContactBody Static(IvpLedgeSide side) =>
        new(side, At(new IvpRigidBody { Immovable = true }, (0d, 0d, 0d)), 0f);

    /// <summary>Places a core unturned at a position, which is where its <see cref="IvpRigidBody.CoreMatrix"/> puts the arm.</summary>
    private static IvpRigidBody At(IvpRigidBody core, (double X, double Y, double Z) position)
    {
        core.CoreMatrix = IvpMatrix.FromRotation(Unturned, position);
        return core;
    }
}
