using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's time-of-impact table and its polyhedral entry — <c>DAT_18012d910</c>, filled by <c>FUN_1800a3aa0</c>, and
/// <c>FUN_1800a3fe0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The scheduler's near branch and the dispatch into the search* and
/// *The other three times of impact*). The table is indexed `kindA × 4 + kindB`; `(0,0) (0,1) (0,2) (1,1)` reach
/// `FUN_1800a3fe0`, which routes each to its search with synapse A's feature first; a ball first reaches
/// `FUN_1800a3d30` or `FUN_1800a3b60`; every other entry is `FUN_1800a4200`, an assertion at line 1258.
///
/// **One geometry serves all four searches**: a tetrahedron's point 0 falling from half an inch above the margin onto
/// a tetrahedron whose point 0 is at the origin, whose edge `(0, 0)` runs along X and whose triangle 0 lies flat at
/// `z = 0`. Each search raises an event in its own family, so the kind's high nibble names the search that ran.
/// </remarks>
public sealed class IvpImpactDispatchConformanceTests
{
    private static readonly IvpCoreBounds Core =
        new(Radius: 1f, InverseDiameter: 1f, AngularSpeedBound: 0f, LinearSpeed: 0f, SurfaceSpeedBound: 0f);

    private static readonly IvpLedgeEdge Feature = new(0, 0);

    /// <remarks>
    /// **Each legal pair runs its own search**: point-point raises `0x1_`, point-face `0x2_`, point-edge `0x3_` and
    /// edge-edge `0x4_`.
    /// </remarks>
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Point, 0x1)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Triangle, 0x2)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Edge, 0x3)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Edge, 0x4)]
    public void Search_EachLegalPairOfKinds_RunsItsOwnSearch(IvpFeatureKind first, IvpFeatureKind second, int family)
    {
        IvpImpact impact = Search(first, second);

        impact.Event.ShouldNotBeNull();
        (impact.Event.Value >> 4).ShouldBe(family);
    }

    /// <remarks>
    /// **The table does not reorder a pair: the lower kind must already be synapse A's.** An edge against a point, a
    /// face against a face, a point against a backside — whose index, `0·4 + 5`, lands on the `(1,1)` entry and faults
    /// inside `FUN_1800a3fe0` instead — each stop the engine.
    /// </remarks>
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Point)]
    [TestCase(IvpFeatureKind.Triangle, IvpFeatureKind.Triangle)]
    [TestCase(IvpFeatureKind.Edge, IvpFeatureKind.Triangle)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Backside)]
    [TestCase(IvpFeatureKind.Point, IvpFeatureKind.Ball)]
    public void Search_APairTheTableHasNoRoutineFor_Faults(IvpFeatureKind first, IvpFeatureKind second) =>
        Should.Throw<InvalidOperationException>(() => Search(first, second));

    /// <remarks>
    /// **A ball first is legal in the engine and not ported here**: `FUN_1800a3d30` and `FUN_1800a3b60` measure from
    /// the ball's centre, which a ledge side cannot carry. Refused by name rather than routed to a ledge search.
    /// </remarks>
    [TestCase(IvpFeatureKind.Point)]
    [TestCase(IvpFeatureKind.Ball)]
    public void Search_ABallFirst_IsRefusedAsNotPorted(IvpFeatureKind second) =>
        Should.Throw<NotSupportedException>(() => Search(IvpFeatureKind.Ball, second));

    private static IvpImpact Search(IvpFeatureKind first, IvpFeatureKind second)
    {
        double height = IvpCollisionTolerance.Margin + 0.5d;

        return IvpImpactDispatch.Search(
            new IvpImpactContext(ApproachSpeed: 50d, TotalBound: 0d, Start: 0d, End: IvpSearchFixtures.End),
            new IvpMindistState(ExtraRadius: 0f, Length: (float)height, MarginClass: 0, Normal: (0f, 0f, 1f)),
            new IvpSynapse(Feature, first),
            IvpSearchFixtures.Side(
                [(0f, 0f, 0f), (0f, 1f, 1f), (1f, 0f, 1f), (-1f, -1f, 1f)], (0d, 0d, height), 50f, Core),
            new IvpSynapse(Feature, second),
            IvpSearchFixtures.Side(
                [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, -1f)], (0d, 0d, 0d), 0f, Core));
    }
}
