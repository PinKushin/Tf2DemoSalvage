using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's minimize: the closest features of a mindist's two ledges, recomputed before a queued event is decided —
/// <c>FUN_180095cb0</c> and the eight feature routines under it (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The minimize, routine by routine*). Every fixture body is
/// unrotated at a whole-number position, and every distance the minimize takes a root of is a power of four, whose
/// reciprocal root the engine's Newton steps reach exactly — so lengths and normals are exact floats.
///
/// **The tetrahedra are wound outward**, as a compact ledge is: `(P₁ − P₀) × (P₂ − P₀)` over each triangle points away
/// from the fourth point. A point-point routine never reads a normal, so the two pinned only there are not required to
/// be.
/// </remarks>
public sealed class IvpMindistMinimizeConformanceTests
{
    private const int Step = 7;

    /// <summary>A tetrahedron's point 0 at the origin with its three neighbors a unit above.</summary>
    private static readonly (float X, float Y, float Z)[] Bottom = [(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (-1f, -1f, 1f)];

    /// <summary>A tetrahedron's point 0 at the origin with its three neighbors a unit below.</summary>
    private static readonly (float X, float Y, float Z)[] Top = [(0f, 0f, 0f), (1f, 0f, -1f), (0f, 1f, -1f), (-1f, -1f, -1f)];

    /// <remarks>
    /// **Once per PSI**: `FUN_180095cb0` returns `4` when the mindist's `+0xc0` already holds the environment's step
    /// counter, before it builds a solver — so nothing is recomputed.
    /// </remarks>
    [Test]
    public void Minimize_TwiceInOneStep_ReturnsAlreadyMinimizedWithoutRecomputing()
    {
        IvpMindist mindist = Points();
        IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, 0d)), Tetra(Bottom, (0d, 0d, 2d)), Step);
        mindist.Length = 99f;

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, 0d)), Tetra(Bottom, (0d, 0d, 2d)), Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.AlreadyMinimized);
        mindist.Length.ShouldBe(99f);
    }

    /// <remarks>
    /// **Two vertices whose neighbors all lead away settle on the point pair** (`FUN_1800b1b80`): the length is the
    /// distance less the extra radius, the normal the unit vector from the second to the first, and `+0x9c` the cores'
    /// difference dotted with it in float. Bits 14–15 are cleared on a settled result.
    /// </remarks>
    [Test]
    public void Minimize_TwoVerticesWithEveryNeighborLeadingAway_SettleOnThePointPair()
    {
        IvpMindist mindist = Points();
        mindist.Flags = 0xC000;

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, 0d)), Tetra(Bottom, (0d, 0d, 2d)), Step);

        outcome.ShouldBe(new IvpMinimizeOutcome(IvpMindistMinimize.Settled, false));
        mindist.Length.ShouldBe(2f);
        mindist.Normal.ShouldBe((0f, 0f, -1f));
        mindist.ContactDot.ShouldBe(2f);
        mindist.Flags.ShouldBe(0);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(0, 0), Point(0, 0)));
    }

    /// <remarks>
    /// **A neighbor leading toward the other vertex takes the feature there** — the steepest rising edge, measured as
    /// `(N·w − P·w) × rsqrt_f(|N − P|² + 1e-18f)`, and past its far end it is the point pair again. Point 1 of the first
    /// tetrahedron is a unit toward the second's vertex, three away: the minimize walks onto it and settles two apart, on
    /// the edge `1 → 0` — the ring's edges end at the vertex and start at the neighbor.
    /// </remarks>
    [Test]
    public void Minimize_ANeighborLeadingTowardTheOtherVertex_MovesTheFeatureToIt()
    {
        IvpMindist mindist = Points();

        IvpMinimizeOutcome outcome = IvpMindistMinimize.Minimize(
            mindist,
            Tetra([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, -1f), (-1f, -1f, -1f)], (0d, 0d, 0d)),
            Tetra([(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (0f, -1f, 1f)], (3d, 0d, 0d)),
            Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(1, 2), Point(0, 0)));
        mindist.Length.ShouldBe(2f);
        mindist.Normal.ShouldBe((-1f, 0f, 0f));
        mindist.ContactDot.ShouldBe(3f);
        mindist.Flags.ShouldBe(0);
    }

    /// <remarks>
    /// **When the move is on the second synapse's side, bit 8 is flipped so synapse A is the first argument again.**
    /// The same fixture mirrored: the second tetrahedron's point 1 leads toward the first's vertex, so the minimize calls
    /// on with that side first, flipping bit 8, and its normal and `+0x9c` are taken from that side.
    /// </remarks>
    [Test]
    public void Minimize_AMoveOnTheSecondSynapsesSide_FlipsWhichSynapseIsFirst()
    {
        IvpMindist mindist = Points();

        IvpMinimizeOutcome outcome = IvpMindistMinimize.Minimize(
            mindist,
            Tetra([(0f, 0f, 0f), (0f, 1f, 1f), (-1f, 0f, 1f), (0f, -1f, 1f)], (0d, 0d, 0d)),
            Tetra([(0f, 0f, 0f), (-1f, 0f, 0f), (0f, 1f, -1f), (1f, -1f, -1f)], (3d, 0d, 0d)),
            Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        mindist.Flags.ShouldBe(0x100);
        mindist.SynapseA.ShouldBe(1);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(0, 0), Point(1, 2)));
        mindist.Normal.ShouldBe((1f, 0f, 0f));
        mindist.ContactDot.ShouldBe(3f);
    }

    /// <remarks>
    /// **Two vertices at one place give up** — the point-point routine refuses a squared distance at or below `1e-12` —
    /// and a result other than settled sets bit 14, clears bit 15, and calls the mindist's virtual `+0x28`, which for a
    /// plain mindist reports it to the environment's `+0x40` object (`FUN_1800947e0`).
    /// </remarks>
    [Test]
    public void Minimize_TwoVerticesAtOnePlace_GiveUpAndReportToTheEnvironment()
    {
        IvpMindist mindist = Points();
        mindist.Flags = 0x8000;

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, 0d)), Tetra(Bottom, (0d, 0d, 0d)), Step);

        outcome.ShouldBe(new IvpMinimizeOutcome(IvpMindistMinimize.GaveUp, true));
        mindist.Flags.ShouldBe(0x4000);
    }

    /// <remarks>**The report is skipped when `flags &amp; 0x3000` is `0x1000`, or `flags &amp; 0x3C0000` is `0x100000`.**</remarks>
    [TestCase(0x1000)]
    [TestCase(0x100000)]
    public void Minimize_GivingUpWithAQuietFlag_DoesNotReport(int flags)
    {
        IvpMindist mindist = Points();
        mindist.Flags = flags;

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, 0d)), Tetra(Bottom, (0d, 0d, 0d)), Step);

        outcome.ShouldBe(new IvpMinimizeOutcome(IvpMindistMinimize.GaveUp, false));
    }

    /// <remarks>
    /// **A vertex over a face, inside its triangle, with no neighbor dipping toward it, settles on the face**
    /// (`FUN_1800b1910`, `FUN_1800b0c20`): the length is the height less the extra radius, and the normal the face's,
    /// in the world.
    /// </remarks>
    [Test]
    public void Minimize_AVertexOverAFaceItProjectsInside_SettlesOnTheFace()
    {
        IvpMindist mindist = new(Point(0, 0), Triangle(0, 0), extraRadius: 0.5f);

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Bottom, (0d, 0d, 2d)), Face((0d, 0d, 0d)), Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(0, 0), Triangle(0, 0)));
        mindist.Length.ShouldBe(1.5f);
        mindist.Normal.ShouldBe((0f, 0f, 1f));
        mindist.ContactDot.ShouldBe(2f);
    }

    /// <remarks>
    /// **A vertex behind a face reports a backside, and the minimize retries twice** (`FUN_1800b0c20`, `FUN_180095cb0`):
    /// the face's synapse is marked `5`, walked from its header's triangle toward the point (`FUN_180094e30`) and set back
    /// to a triangle, and after the second retry the result `3` stands. The normal is the face's; only the ring's
    /// direction is flipped.
    /// </remarks>
    [Test]
    public void Minimize_AVertexBehindAFace_ReportsABacksideAfterTwoRetries()
    {
        IvpMindist mindist = new(Point(0, 0), Triangle(0, 0), extraRadius: 0f);

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Top, (0d, 0d, -2d)), Face((0d, 0d, 0d)), Step);

        outcome.ShouldBe(new IvpMinimizeOutcome(IvpMindistMinimize.Backside, true));
        mindist.Synapse(1).ShouldBe(Triangle(0, 0));
        mindist.Length.ShouldBe(-2f);
        mindist.Normal.ShouldBe((0f, 0f, 1f));
        mindist.Flags.ShouldBe(0x4000);
    }

    /// <remarks>
    /// **A vertex over a ridge, outside both faces beside it, settles on the edge** (`FUN_1800b1aa0`, `FUN_1800b11c0`):
    /// the normal is `−(K × (K × w))` scaled by the reciprocal root over `|K|²`, turned into the world.
    /// </remarks>
    [Test]
    public void Minimize_AVertexOverARidge_SettlesOnTheEdge()
    {
        IvpMindist mindist = new(Point(0, 0), Edge(0, 0), extraRadius: 0f);

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Bottom, (0d, 0d, 2d)), Ridge((0d, 0d, 0d)), Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(0, 0), Edge(0, 0)));
        mindist.Length.ShouldBe(2f);
        mindist.Normal.ShouldBe((0f, 0f, 1f));
        mindist.ContactDot.ShouldBe(2f);
    }

    /// <remarks>
    /// **Two crossing ridges settle on the edge pair** (`FUN_1800afa40`, `FUN_1800b0280`): the length is the separation
    /// along their common normal and the normal points from the second to the first.
    /// </remarks>
    [Test]
    public void Minimize_TwoCrossingRidges_SettleOnTheEdgePair()
    {
        IvpMindist mindist = new(Edge(0, 0), Edge(0, 0), extraRadius: 0f);

        IvpMinimizeOutcome outcome = IvpMindistMinimize.Minimize(
            mindist,
            Tetra([(0f, -1f, 0f), (0f, 1f, 0f), (1f, 0f, 1f), (-1f, 0f, 1f)], (0d, 0d, 2d)),
            Ridge((0d, 0d, 0d)),
            Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Edge(0, 0), Edge(0, 0)));
        mindist.Length.ShouldBe(2f);
        mindist.Normal.ShouldBe((0f, 0f, 1f));
        mindist.ContactDot.ShouldBe(2f);
    }

    /// <remarks>
    /// **Two faces pick their closest feature pair and hand it on** (`FUN_180094f80`): the vertex a height of `2` over the
    /// face beats the nearest vertex pair at `√8`, becomes a point against the face, and settles there.
    /// </remarks>
    [Test]
    public void Minimize_TwoFaces_PickTheirClosestFeaturesAndSettle()
    {
        IvpMindist mindist = new(Triangle(0, 0), Triangle(0, 0), extraRadius: 0f);

        IvpMinimizeOutcome outcome =
            IvpMindistMinimize.Minimize(mindist, Tetra(Bottom, (0d, 0d, 2d)), Face((0d, 0d, 0d)), Step);

        outcome.Result.ShouldBe(IvpMindistMinimize.Settled);
        (mindist.Synapse(0), mindist.Synapse(1)).ShouldBe((Point(0, 0), Triangle(0, 0)));
        mindist.Length.ShouldBe(2f);
    }

    /// <remarks>
    /// **A pair of kinds the table does not route is the default entry's assertion** (`FUN_180094e10`), and an edge
    /// against a triangle is one of them.
    /// </remarks>
    [Test]
    public void Minimize_AnEdgeAgainstATriangle_IsTheTablesAssertion()
    {
        IvpMindist mindist = new(Edge(0, 0), Triangle(0, 0), extraRadius: 0f);

        Should.Throw<InvalidOperationException>(() =>
            IvpMindistMinimize.Minimize(mindist, Tetra(Bottom, (0d, 0d, 2d)), Face((0d, 0d, 0d)), Step));
    }

    /// <remarks>
    /// **The backside walk starts from the header's triangle and hops across each edge at or below zero into a triangle
    /// it has not entered** (`FUN_180094e30`). From triangle 0 the header names triangle 3; the point `(3, 0, 2)` is
    /// behind that triangle's second edge, so the walk crosses into triangle 2, behind whose first edge it crosses into
    /// triangle 0, and there every edge at or below zero leads back into a triangle already entered — so it stops on the
    /// edge it came in by, `2 → 0`.
    /// </remarks>
    [Test]
    public void BacksideWalk_APointOffTheHeadersTriangle_CrossesIntoUnvisitedTrianglesUntilNoneRemain()
    {
        IvpLedgeEdge stopped =
            IvpMindistMinimize.BacksideWalk(Tetra(Bottom, (0d, 0d, 0d)), new IvpLedgeEdge(0, 0), (3d, 0d, 2d));

        stopped.ShouldBe(new IvpLedgeEdge(0, 2));
    }

    /// <remarks>
    /// **The loop check stores unordered pairs and refuses past 256** (`FUN_180094600`): a pair seen either way round is
    /// seen, and once 256 are stored every new pair is reported seen without being stored.
    /// </remarks>
    [Test]
    public void LoopCheck_APairEitherWayRoundOrPastTheCap_IsSeen()
    {
        IvpMinimizeLoopCheck check = new();

        check.Seen(1, 2).ShouldBeFalse();
        check.Seen(2, 1).ShouldBeTrue();

        for (long pair = 3; pair < 258; pair++)
        {
            check.Seen(pair, -pair).ShouldBeFalse();
        }

        check.Seen(1000, 1001).ShouldBeTrue();
        check.Seen(1000, 1001).ShouldBeTrue();
    }

    private static IvpMindist Points() => new(Point(0, 0), Point(0, 0), extraRadius: 0f);

    private static IvpSynapse Point(int triangle, int slot) => new(new IvpLedgeEdge(triangle, slot), IvpFeatureKind.Point);

    private static IvpSynapse Edge(int triangle, int slot) => new(new IvpLedgeEdge(triangle, slot), IvpFeatureKind.Edge);

    private static IvpSynapse Triangle(int triangle, int slot) => new(new IvpLedgeEdge(triangle, slot), IvpFeatureKind.Triangle);

    /// <summary>A tetrahedron whose every edge word hops to its twin, as in <c>IvpLedgeTopologyConformanceTests</c>.</summary>
    private static IvpLedgeSide Tetra(IReadOnlyList<(float X, float Y, float Z)> points, (double X, double Y, double Z) at) =>
        new(
            points,
            new IvpLedgeTopology(
                [(0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2)],
                [(6, 13, 6), (6, 7, -6), (-6, 4, -6), (-7, -4, -13)],
                [3, 3, 1, 0],
                [0, 0, 0, 0]),
            IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            at);

    /// <summary>A tetrahedron with its edge <c>0 → 1</c> along <c>x</c> on top and its faces falling away on both sides.</summary>
    private static IvpLedgeSide Ridge((double X, double Y, double Z) at) =>
        Tetra([(-1f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, -1f), (0f, -1f, -1f)], at);

    /// <summary>One triangle at <c>z = 0</c> facing up, its normal <c>(0, 0, 16)</c> before scaling.</summary>
    private static IvpLedgeSide Face((double X, double Y, double Z) at) =>
        new(
            [(-2f, -2f, 0f), (2f, -2f, 0f), (0f, 2f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            at);
}
