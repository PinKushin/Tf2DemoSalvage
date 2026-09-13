using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The time of impact between a hull point of one ledge and an edge of another over an interval — <c>FUN_1800a1ff0</c>
/// (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *The other three times of impact*): a
/// point-line search for the moment the point reaches the margin, then the two planes through the edge square to its
/// triangles for the moment the point passes one, then a walk of the edges leaving the point for the moment one turns
/// toward the line. *The labels "collision" for `0x30` and "feature change" for `0x31` and `0x32` are INFERRED*, from
/// the fire routine's test of the low four bits.
/// </remarks>
public static class IvpPointEdgeSearch
{
    /// <summary>Written to <c>context+0x40</c> when the point-line search finds a root.</summary>
    public const int PointLineEvent = 0x30;

    /// <summary>Written to <c>context+0x40</c> when a plane's refinement finds a root.</summary>
    public const int PlaneEvent = 0x31;

    /// <summary>Written to <c>context+0x40</c> when a ring edge's refinement finds a root.</summary>
    public const int RingEvent = 0x32;

    /// <summary><c>DAT_1800ee18c</c>: the share of the extra radius in the point-line tolerance.</summary>
    private const float ToleranceShare = 0.9f;

    /// <summary><c>DAT_1800ee388</c>.</summary>
    private const double Half = 0.5d;

    /// <summary><c>DAT_1800fe7c8</c>, the float <c>−0.3f</c> widened.</summary>
    private const double RingTargetShare = -0.3f;

    /// <summary><c>DAT_1800fb100</c>, the floor under the gap the ring's speed divides by — a distance in IVP's metres.</summary>
    private const double GapFloorMetres = 1e-8d;

    private const double GapFloor = GapFloorMetres * IvpTransform.InchesPerMetre;

    /// <summary>Searches one point against one edge, as <c>FUN_1800a1ff0</c> does.</summary>
    /// <param name="context">The speed bounds and the interval.</param>
    /// <param name="mindist">The pair's extra radius, length and margin class.</param>
    /// <param name="pointSide">Synapse A's side, whose feature is the point.</param>
    /// <param name="pointEdge">An edge starting at the point.</param>
    /// <param name="edgeSide">Synapse B's side, whose feature is the edge.</param>
    /// <param name="edge">The edge.</param>
    /// <returns>The event kind and time left in the context.</returns>
    /// <exception cref="ArgumentNullException">A side is null.</exception>
    /// <remarks>
    /// 1. **The point-line search measures its start** to `(double)margin + (double)extra`, with tolerance `(double)(0.9f·extra
    ///    + 0.1·d)`.
    /// 2. **The planes run at the context's TOTAL bound**, to `−0.1·d`: the edge's own triangle, then its twin's.
    /// 3. **The ring's speed divides by the gap still left**, `max(length − (double)(float)(time − start)·totalBound,
    ///    1e-8 m)`, and adds both cores' angular bounds summed in float; its target takes the edge's core's `+0x54`.
    /// </remarks>
    public static IvpImpact Search(
        IvpImpactContext context,
        IvpMindistState mindist,
        IvpSearchSide pointSide,
        IvpLedgeEdge pointEdge,
        IvpSearchSide edgeSide,
        IvpLedgeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(pointSide);
        ArgumentNullException.ThrowIfNull(edgeSide);

        double angular = edgeSide.Core.AngularSpeedBound + pointSide.Core.AngularSpeedBound;
        double time = context.End;
        int? kind = null;

        (float X, float Y, float Z) point = pointSide.StartOf(pointEdge);
        (float X, float Y, float Z) lineStart = edgeSide.StartOf(edge);
        (double X, double Y, double Z) lineDirection =
            IvpVector.UnitDifference(lineStart, edgeSide.StartOf(edgeSide.Topology.Next(edge)));

        float extra = mindist.ExtraRadius;
        float margin = IvpCollisionTolerance.MarginFor(mindist.MarginClass);

        IvpPointLineEvaluator line = IvpPointLineEvaluator.ForEdge(
            point,
            lineStart,
            lineDirection,
            ((double)margin + (double)extra) * Half,
            context.ApproachSpeed,
            pointSide.Motion.Current,
            edgeSide.Motion.Current);

        double tolerance = (extra * ToleranceShare) + IvpCollisionTolerance.Epsilon;
        double target = (double)margin + (double)extra;

        if (IvpRootFinder.Advance(
            line, target, tolerance, context.Start, time, pointSide.Motion, edgeSide.Motion, null, ref time))
        {
            kind = PointLineEvent;
        }

        double planeTarget = -IvpCollisionTolerance.EdgeTargetScale;
        IvpLedgeEdge[] planes = [edge, edgeSide.Topology.Hop(edge)];

        foreach (IvpLedgeEdge side in planes)
        {
            IvpPointPlaneEvaluator plane = PlaneAlong(edgeSide, side, point, context.TotalBound);

            if (IvpRootFinder.Refine(
                plane, planeTarget, context.Start, time, 0, pointSide.Motion, edgeSide.Motion, null, ref time))
            {
                kind = PlaneEvent;
            }
        }

        double gap = mindist.Length - ((double)(float)(time - context.Start) * context.TotalBound);

        // MAXSD and MINSD: the second operand wins a tie and a NaN.
        double floored = gap > GapFloor ? gap : GapFloor;
        float edgeSpeed = (edgeSide.Core.SurfaceSpeedBound * edgeSide.Core.AngularSpeedBound) + edgeSide.Core.LinearSpeed;
        double pointSpeed = pointSide.Core.LinearSpeed + (IvpVector.Length(point) * pointSide.Core.AngularSpeedBound);
        double speed = (((double)edgeSpeed + pointSpeed) / floored) + angular;

        double lesser = margin < mindist.Length ? margin : mindist.Length;
        double ringTarget = lesser * (double)(edgeSide.Core.InverseDiameter + edgeSide.Core.InverseDiameter) * RingTargetShare;
        double inverse = 1d / speed;

        foreach (IvpLedgeEdge leaving in pointSide.Topology.Ring(pointEdge))
        {
            IvpEdgeLineEvaluator slope = new(
                Vertex: (point.X, point.Y, point.Z),
                Direction: IvpVector.UnitDifference(point, pointSide.StartOf(pointSide.Topology.Next(leaving))),
                LinePoint: (lineStart.X, lineStart.Y, lineStart.Z),
                LineDirection: lineDirection,
                ApproachSpeed: speed,
                InverseApproachSpeed: inverse);

            if (IvpRootFinder.Refine(
                slope, ringTarget, context.Start, time, 0, pointSide.Motion, edgeSide.Motion, null, ref time))
            {
                kind = RingEvent;
            }
        }

        return new IvpImpact(kind, time);
    }

    /// <summary>The plane through an edge square to its triangle, measured against the point.</summary>
    /// <param name="side">The edge's side.</param>
    /// <param name="edge">The edge, whose triangle gives the plane.</param>
    /// <param name="point">The point, from the other side's ledge points.</param>
    /// <param name="speed">The context's total bound.</param>
    /// <returns>A point-plane evaluator through the edge's start.</returns>
    /// <remarks>
    /// **`(next − start)` subtracted in float, crossed with the triangle's double-subtracted normal**, then scaled with
    /// five steps and the answer unread (`1800a2425`–`1800a250a`).
    /// </remarks>
    private static IvpPointPlaneEvaluator PlaneAlong(
        IvpSearchSide side, IvpLedgeEdge edge, (float X, float Y, float Z) point, double speed)
    {
        (float X, float Y, float Z) start = side.StartOf(edge);

        (double X, double Y, double Z) normal = IvpVector.Cross(
            IvpVector.FloatDifference(start, side.StartOf(side.Topology.Next(edge))),
            side.FaceNormal(edge));

        _ = IvpVector.TryScaleToUnitLength(ref normal, IvpVector.FiveSteps);

        return new IvpPointPlaneEvaluator(
            Vertex: (point.X, point.Y, point.Z),
            Normal: normal,
            PlanePoint: (start.X, start.Y, start.Z),
            ApproachSpeed: speed,
            InverseApproachSpeed: 1d / speed);
    }
}
