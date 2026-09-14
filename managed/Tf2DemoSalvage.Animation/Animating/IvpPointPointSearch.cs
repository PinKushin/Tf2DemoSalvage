using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The time of impact between a hull point of one ledge and a hull point of another over an interval —
/// <c>FUN_1800a2b30</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *The other three times of impact*): a
/// point-point search for the moment the pair reaches the margin, then a walk of the edges leaving each point for the
/// moment one of them turns toward the other point. *The labels "collision" for `0x10` and "feature change" for `0x11`
/// are INFERRED*, from the fire routine's test of the low four bits.
/// </remarks>
public static class IvpPointPointSearch
{
    /// <summary>Written to <c>context+0x40</c> when the point-point search finds a root.</summary>
    public const int PointPointEvent = 0x10;

    /// <summary>Written to <c>context+0x40</c> when a ring edge's refinement finds a root.</summary>
    public const int RingEvent = 0x11;

    /// <summary><c>DAT_1800f1fc8</c>.</summary>
    private const double RingTargetShare = -0.5d;

    /// <summary>Searches one point against another, as <c>FUN_1800a2b30</c> does.</summary>
    /// <param name="context">The speed bounds and the interval.</param>
    /// <param name="mindist">The pair's extra radius, length, margin class and normal.</param>
    /// <param name="first">Synapse A's side.</param>
    /// <param name="firstEdge">An edge starting at A's point.</param>
    /// <param name="second">Synapse B's side.</param>
    /// <param name="secondEdge">An edge starting at B's point.</param>
    /// <returns>The event kind and time left in the context.</returns>
    /// <exception cref="ArgumentNullException">A side is null.</exception>
    /// <remarks>
    /// 1. **The point-point search measures its start** — no known distance — to `(double)extra + (double)margin`.
    /// 2. `reach = (double)(float)(time − start) · totalBound + length`, taken after it.
    /// 3. **The ring target is `|d| · (min(margin², reach²) · −0.5) / max(radius)`**, a length squared the engine
    ///    computes in metres — this project's callers run in metres too, so nothing scales it.
    /// 4. **B's ring first, then A's with the motion caches exchanged**, each at `core+0x80 · reach` plus both points'
    ///    speeds.
    /// </remarks>
    public static IvpImpact Search(
        IvpImpactContext context,
        IvpMindistState mindist,
        IvpSearchSide first,
        IvpLedgeEdge firstEdge,
        IvpSearchSide second,
        IvpLedgeEdge secondEdge)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        double time = context.End;
        int? kind = null;

        (float X, float Y, float Z) firstPoint = first.StartOf(firstEdge);
        (float X, float Y, float Z) secondPoint = second.StartOf(secondEdge);

        float extra = mindist.ExtraRadius;
        float margin = IvpCollisionTolerance.MarginFor(mindist.MarginClass);

        IvpPointPointEvaluator distance = new(
            First: (firstPoint.X, firstPoint.Y, firstPoint.Z),
            Second: (secondPoint.X, secondPoint.Y, secondPoint.Z),
            Direction: (-mindist.Normal.X, -mindist.Normal.Y, -mindist.Normal.Z),
            ApproachSpeed: context.ApproachSpeed,
            InverseApproachSpeed: 1d / context.ApproachSpeed);

        double tolerance = (extra * IvpVertexFaceSearch.ToleranceShare) + IvpCollisionTolerance.Epsilon;
        double target = (double)extra + (double)margin;

        if (IvpRootFinder.Advance(
            distance, target, tolerance, context.Start, time, first.Motion, second.Motion, null, ref time))
        {
            kind = PointPointEvent;
        }

        double reach = ((double)(float)(time - context.Start) * context.TotalBound) + mindist.Length;
        double speeds = PointSpeed(second.Core, secondPoint) + PointSpeed(first.Core, firstPoint);

        double marginSquared = (double)margin * margin;
        double reachSquared = reach * reach;

        // MINSD and MAXSS: the second operand wins a tie and a NaN.
        double lesser = marginSquared < reachSquared ? marginSquared : reachSquared;
        float radius = first.Core.Radius > second.Core.Radius ? first.Core.Radius : second.Core.Radius;
        double factor = lesser * RingTargetShare / radius;

        if (Ring(second, secondEdge, first, firstEdge, (context.Start, (second.Core.AngularSpeedBound * reach) + speeds, factor), ref time))
        {
            kind = RingEvent;
        }

        if (Ring(first, firstEdge, second, secondEdge, (context.Start, (first.Core.AngularSpeedBound * reach) + speeds, factor), ref time))
        {
            kind = RingEvent;
        }

        return new IvpImpact(kind, time);
    }

    /// <summary>A point's speed bound: <c>(double)core+0x1dc + |point| · (double)core+0x80</c>.</summary>
    private static double PointSpeed(IvpCoreBounds core, (float X, float Y, float Z) point) =>
        core.LinearSpeed + (IvpVector.Length(point) * core.AngularSpeedBound);

    /// <summary>Refines every edge leaving one side's point against the other side's point.</summary>
    /// <param name="ringSide">The side whose edges are walked, handed to the finder second.</param>
    /// <param name="ringEdge">An edge starting at its point.</param>
    /// <param name="pointSide">The side whose point is measured, handed to the finder first.</param>
    /// <param name="pointEdge">An edge starting at that point.</param>
    /// <param name="search">The interval's start, the ring's speed and the target factor.</param>
    /// <param name="time">The event time, moved earlier by each root.</param>
    /// <returns>Whether any edge raised an event.</returns>
    private static bool Ring(
        IvpSearchSide ringSide,
        IvpLedgeEdge ringEdge,
        IvpSearchSide pointSide,
        IvpLedgeEdge pointEdge,
        (double Start, double Speed, double Factor) search,
        ref double time)
    {
        (float X, float Y, float Z) origin = ringSide.StartOf(ringEdge);
        (float X, float Y, float Z) point = pointSide.StartOf(pointEdge);
        double inverse = 1d / search.Speed;
        bool raised = false;

        foreach (IvpLedgeEdge edge in ringSide.Topology.Ring(ringEdge))
        {
            (float X, float Y, float Z) neighbor = ringSide.StartOf(ringSide.Topology.Next(edge));

            ((double X, double Y, double Z) difference, double scale) = IvpEdgeEvaluator.EdgeDifference(origin, neighbor);

            double squared =
                (difference.X * difference.X) + (difference.Y * difference.Y) + (difference.Z * difference.Z);

            IvpPointDirectionEvaluator evaluator = new(
                Point: (point.X, point.Y, point.Z),
                Origin: (origin.X, origin.Y, origin.Z),
                Direction: (difference.X * scale, difference.Y * scale, difference.Z * scale),
                ApproachSpeed: search.Speed,
                InverseApproachSpeed: inverse);

            double target = squared * search.Factor * scale;

            if (IvpRootFinder.Refine(
                evaluator, target, search.Start, time, 0, pointSide.Motion, ringSide.Motion, null, ref time))
            {
                raised = true;
            }
        }

        return raised;
    }
}
