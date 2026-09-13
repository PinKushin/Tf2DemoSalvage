using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The search context <c>FUN_1800a1b50</c> is handed: the pair's approach speed and the interval.</summary>
/// <param name="ApproachSpeed">The bound on how fast the pair can close — <c>context+0x10</c>.</param>
/// <param name="Start">The interval's start time — <c>context+0x30</c>.</param>
/// <param name="End">The interval's end time — <c>context+0x38</c>.</param>
public readonly record struct IvpImpactContext(double ApproachSpeed, double Start, double End);

/// <summary>The three fields of the pair's mindist the vertex-face search reads.</summary>
/// <param name="ExtraRadius">The pair's extra radius — <c>mindist+0x98</c>.</param>
/// <param name="Length">The pair's distance less that radius — <c>mindist+0xa8</c>.</param>
/// <param name="MarginClass">The byte at bits 22–29 of <c>mindist+0x20</c>, which picks the margin.</param>
public readonly record struct IvpMindistState(float ExtraRadius, float Length, int MarginClass);

/// <summary>One side of the pair: a ledge, how it is joined, where its body is going, and its core's bounds.</summary>
/// <param name="Points">The ledge's points, in the object's frame.</param>
/// <param name="Topology">The ledge's triangles and edge words.</param>
/// <param name="Motion">The body's motion cache for the interval.</param>
/// <param name="AngularSpeedBound">The core's angular speed bound — <c>core+0x80</c>.</param>
/// <param name="CoreInverseDiameter">The core's <c>+0x54</c>, <c>0.5f / core+0x4</c>; read from the face's side only.</param>
public sealed record IvpSearchSide(
    IReadOnlyList<(float X, float Y, float Z)> Points,
    IvpLedgeTopology Topology,
    IvpMotionCache Motion,
    float AngularSpeedBound,
    float CoreInverseDiameter);

/// <summary>What a time-of-impact search leaves in its context: the event kind, if one was raised, and the time.</summary>
/// <param name="Event">The kind last written to <c>context+0x40</c>, or null when neither search wrote one.</param>
/// <param name="Time">The time at <c>context+0x48</c> — the interval's end when there was no event.</param>
public readonly record struct IvpImpact(int? Event, double Time);

/// <summary>
/// The time of impact between a vertex of one ledge and a face of another over an interval — <c>FUN_1800a1b50</c>
/// (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *`FUN_1800a1b50` field by
/// field*): a point-plane search for the moment the vertex reaches the margin, then a walk of the edges leaving the
/// vertex for the moment one of them turns into the face. *The labels "collision" for `0x20` and "feature change"
/// for `0x21` are INFERRED*; the constants are named by the search that raises them.
/// </remarks>
public static class IvpVertexFaceSearch
{
    /// <summary>Written to <c>context+0x40</c> when the point-plane search finds a root.</summary>
    public const int PointPlaneEvent = 0x20;

    /// <summary>Written to <c>context+0x40</c> when an edge's refinement finds a root.</summary>
    public const int EdgeEvent = 0x21;

    /// <summary><c>DAT_1800ea984</c>: the share of the extra radius in the point-plane tolerance.</summary>
    private const float ToleranceShare = 0.5f;

    /// <summary><c>DAT_1800ea968</c>: the share of the extra radius in the edge target.</summary>
    private const float EdgeTargetShare = 0.1f;

    /// <summary>Searches one vertex against one face, as <c>FUN_1800a1b50</c> does.</summary>
    /// <param name="context">The approach speed and the interval.</param>
    /// <param name="mindist">The pair's extra radius, length and margin class.</param>
    /// <param name="vertexSide">The side the vertex belongs to.</param>
    /// <param name="vertexEdge">An edge starting at the vertex.</param>
    /// <param name="faceSide">The side the face belongs to.</param>
    /// <param name="faceEdge">The face's own edge, whose start is the face's first point.</param>
    /// <returns>The event kind and time left in the context.</returns>
    /// <exception cref="ArgumentNullException">A side is null.</exception>
    /// <remarks>
    /// 1. **The time starts at the interval's end** (`1800a1c13`), and each root moves it earlier.
    /// 2. **The point-plane search** gets `target = (double)margin + (double)extra`, `tolerance = (double)(0.5f·extra
    ///    + ε)` and a KNOWN starting distance `(double)(extra + length)`, not a measured one.
    /// 3. **The edge target** is `((double)MINSS(length, margin) + (double)(0.1f·extra)) × (double)(−(0.1·d ·
    ///    face core+0x54)) / (double)margin`; the face normal goes into the vertex's frame through both CURRENT
    ///    matrices; and the slope limit `(double)(float)(time − start) × speed` is taken ONCE, after step 2.
    /// 4. **The ring**: each edge's slope is the dot of its float-subtracted difference with that normal, times the
    ///    float reciprocal root; any slope not at or above the limit — NaN included, `COMISD`/`JNC` — is refined from
    ///    the start to the current time, handed the slope as its known distance.
    /// </remarks>
    public static IvpImpact Search(
        IvpImpactContext context,
        IvpMindistState mindist,
        IvpSearchSide vertexSide,
        IvpLedgeEdge vertexEdge,
        IvpSearchSide faceSide,
        IvpLedgeEdge faceEdge)
    {
        ArgumentNullException.ThrowIfNull(vertexSide);
        ArgumentNullException.ThrowIfNull(faceSide);

        double time = context.End;
        int? kind = null;

        IvpLedgeTopology faces = faceSide.Topology;
        (float X, float Y, float Z) vertex = vertexSide.Points[vertexSide.Topology.Start(vertexEdge)];

        IvpPointPlaneEvaluator pointPlane = IvpPointPlaneEvaluator.ForFace(
            vertex,
            faceSide.Points[faces.Start(faceEdge)],
            faceSide.Points[faces.Start(faces.Next(faceEdge))],
            faceSide.Points[faces.Start(faces.Previous(faceEdge))],
            context.ApproachSpeed);

        float extra = mindist.ExtraRadius;
        float margin = IvpCollisionTolerance.MarginFor(mindist.MarginClass);

        double tolerance = (extra * ToleranceShare) + IvpCollisionTolerance.Epsilon;
        double target = (double)margin + (double)extra;
        double known = extra + mindist.Length;

        if (IvpRootFinder.Advance(
            pointPlane, target, tolerance, context.Start, time, vertexSide.Motion, faceSide.Motion, known, ref time))
        {
            kind = PointPlaneEvent;
        }

        double speed = IvpEdgeEvaluator.SpeedBound(vertexSide.AngularSpeedBound, faceSide.AngularSpeedBound);

        // MINSS: the length when it is under the margin, and the margin otherwise — a NaN included.
        float lesser = mindist.Length < margin ? mindist.Length : margin;
        float factor = -(IvpCollisionTolerance.EdgeTargetScale * faceSide.CoreInverseDiameter);
        double edgeTarget = (((double)lesser + (double)(extra * EdgeTargetShare)) * factor) / margin;

        (double X, double Y, double Z) normal =
            vertexSide.Motion.Current.RotateInverse(faceSide.Motion.Current.Rotate(pointPlane.Normal));

        double limit = (double)(float)(time - context.Start) * speed;

        foreach (IvpLedgeEdge edge in vertexSide.Topology.Ring(vertexEdge))
        {
            (float X, float Y, float Z) neighbor =
                vertexSide.Points[vertexSide.Topology.Start(vertexSide.Topology.Next(edge))];

            ((double X, double Y, double Z) difference, double scale) = IvpEdgeEvaluator.EdgeDifference(vertex, neighbor);

            double slope =
                ((difference.X * normal.X) + (difference.Y * normal.Y) + (difference.Z * normal.Z)) * scale;

            if (slope >= limit)
            {
                continue;
            }

            IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge(vertex, neighbor, pointPlane.Normal, speed);

            if (IvpRootFinder.Refine(
                evaluator, edgeTarget, context.Start, time, 0, vertexSide.Motion, faceSide.Motion, slope, ref time))
            {
                kind = EdgeEvent;
            }
        }

        return new IvpImpact(kind, time);
    }
}
