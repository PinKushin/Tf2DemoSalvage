using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Routes a pair to its time of impact by its two synapses' feature kinds — the table <c>DAT_18012d910</c> and its
/// polyhedral entry <c>FUN_1800a3fe0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The scheduler's near branch and the dispatch into the search*).
/// `FUN_1800a3aa0` fills the table, indexed `kindA × 4 + kindB`: `(0,0) (0,1) (0,2) (1,1)` reach `FUN_1800a3fe0`, a ball
/// first reaches `FUN_1800a3d30` or `FUN_1800a3b60`, and every other slot is `FUN_1800a4200`, an assertion. The entry
/// builds each side from its synapse — the cache object, the real object, and the ledge found from the feature's
/// triangle header — clears the context's event kind, and hands synapse A's feature to the search first; here the
/// caller builds the sides.
/// </remarks>
public static class IvpImpactDispatch
{
    /// <summary>Runs the time of impact the table holds for a pair's kinds.</summary>
    /// <param name="context">The speed bounds and the interval.</param>
    /// <param name="mindist">The pair's extra radius, length, margin class and normal.</param>
    /// <param name="first">Synapse A — the record the flags' bit 8 selects — which must hold the lower kind.</param>
    /// <param name="firstSide">Synapse A's side.</param>
    /// <param name="second">Synapse B.</param>
    /// <param name="secondSide">Synapse B's side.</param>
    /// <returns>The event kind and time the search left in the context.</returns>
    /// <exception cref="InvalidOperationException">
    /// The engine would assert: a pair of kinds the table has no routine for (<c>ivp_mindist_event.cxx:1258</c>), or a
    /// point against a backside, whose index lands on the <c>(1,1)</c> slot and faults inside the entry (1231).
    /// </exception>
    /// <exception cref="NotSupportedException">A ball is synapse A's feature; its routines are not ported.</exception>
    public static IvpImpact Search(
        IvpImpactContext context,
        IvpMindistState mindist,
        IvpSynapse first,
        IvpSearchSide firstSide,
        IvpSynapse second,
        IvpSearchSide secondSide) =>
        (first.Kind, second.Kind) switch
        {
            (IvpFeatureKind.Point, IvpFeatureKind.Point) =>
                IvpPointPointSearch.Search(context, mindist, firstSide, first.Feature, secondSide, second.Feature),
            (IvpFeatureKind.Point, IvpFeatureKind.Edge) =>
                IvpPointEdgeSearch.Search(context, mindist, firstSide, first.Feature, secondSide, second.Feature),
            (IvpFeatureKind.Point, IvpFeatureKind.Triangle) =>
                IvpVertexFaceSearch.Search(context, mindist, firstSide, first.Feature, secondSide, second.Feature),
            (IvpFeatureKind.Edge, IvpFeatureKind.Edge) =>
                IvpEdgeEdgeSearch.Search(context, mindist, firstSide, first.Feature, secondSide, second.Feature),
            (IvpFeatureKind.Ball, IvpFeatureKind.Point or IvpFeatureKind.Edge or IvpFeatureKind.Triangle or IvpFeatureKind.Ball) =>
                throw new NotSupportedException(
                    "A ball's time of impact (FUN_1800a3d30, FUN_1800a3b60) is not ported; a ledge side cannot carry a ball."),
            (IvpFeatureKind.Point, IvpFeatureKind.Backside) =>
                throw new InvalidOperationException(
                    "A point against a backside indexes the (1,1) slot and faults in its entry (ivp_mindist_event.cxx:1231)."),
            _ => throw new InvalidOperationException(
                "The time-of-impact table has no routine for this pair of kinds (ivp_mindist_event.cxx:1258)."),
        };
}
