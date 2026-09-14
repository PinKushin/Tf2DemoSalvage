using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The time of impact between an edge of one ledge and an edge of another over an interval — <c>FUN_1800a1420</c>
/// (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *The other three times of impact*): a
/// line-line search for the moment the lines reach the margin, a refinement for the moment the edges turn parallel, and
/// four checks for the moment either edge runs into a face beside the other. *The labels "collision" for `0x40` and
/// "feature change" for `0x41` and `0x42` are INFERRED*, from the fire routine's test of the low four bits.
/// </remarks>
public static class IvpEdgeEdgeSearch
{
    /// <summary>Written to <c>context+0x40</c> when the line-line search finds a root.</summary>
    public const int LineEvent = 0x40;

    /// <summary>Written to <c>context+0x40</c> when the parallel refinement finds a root.</summary>
    public const int ParallelEvent = 0x41;

    /// <summary>Written to <c>context+0x40</c> when a face check's refinement finds a root.</summary>
    public const int FaceEvent = 0x42;

    /// <summary>Searches one edge against another, as <c>FUN_1800a1420</c> does.</summary>
    /// <param name="context">The approach speed and the interval.</param>
    /// <param name="mindist">The pair's margin class and normal.</param>
    /// <param name="first">Synapse A's side.</param>
    /// <param name="firstEdge">A's edge.</param>
    /// <param name="second">Synapse B's side.</param>
    /// <param name="secondEdge">B's edge.</param>
    /// <returns>The event kind and time left in the context.</returns>
    /// <exception cref="ArgumentNullException">A side is null.</exception>
    /// <remarks>
    /// 1. **The sign is `+1` when the normal's dot with `A·a × B·b` is at or above `−0.0`**, through the CURRENT matrices,
    ///    and `−1` below it or for a NaN.
    /// 2. **The line-line search runs to the margin alone, with tolerance `0.1·d`** — neither carries the extra radius.
    /// 3. **The four face checks**: B's direction against A's twin and then A's own triangle, then A's against B's twin
    ///    and own — the first of each pair negated when the sign is `−1`, the second when it is `+1` — each to
    ///    `−(0.1·d · f)` with `f` the `+0x54` of the core whose edge is dotted, and each handing that edge's side to the
    ///    finder first.
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

        double angular = first.Core.AngularSpeedBound + second.Core.AngularSpeedBound;
        double time = context.End;
        int? kind = null;

        (float X, float Y, float Z) firstStart = first.StartOf(firstEdge);
        (float X, float Y, float Z) secondStart = second.StartOf(secondEdge);

        (double X, double Y, double Z) firstDirection =
            IvpVector.UnitDifference(firstStart, first.StartOf(first.Topology.Next(firstEdge)));

        (double X, double Y, double Z) secondDirection =
            IvpVector.UnitDifference(secondStart, second.StartOf(second.Topology.Next(secondEdge)));

        (double X, double Y, double Z) across = IvpVector.Cross(
            first.Motion.Current.Rotate(firstDirection), second.Motion.Current.Rotate(secondDirection));

        (float X, float Y, float Z) normal = mindist.Normal;
        double dot = ((double)normal.Y * across.Y) + ((double)normal.X * across.X) + ((double)normal.Z * across.Z);
        bool agrees = dot >= 0d;

        IvpLineLineEvaluator line = new(
            First: (firstStart.X, firstStart.Y, firstStart.Z),
            FirstDirection: firstDirection,
            Second: (secondStart.X, secondStart.Y, secondStart.Z),
            SecondDirection: secondDirection,
            Sign: agrees ? 1d : -1d,
            ApproachSpeed: context.ApproachSpeed,
            InverseApproachSpeed: 1d / context.ApproachSpeed);

        float margin = IvpCollisionTolerance.MarginFor(mindist.MarginClass);

        if (IvpRootFinder.Advance(
            line, margin, IvpCollisionTolerance.Epsilon, context.Start, time, first.Motion, second.Motion, null, ref time))
        {
            kind = LineEvent;
        }

        double crossSpeed = angular + angular + IvpVector.DirectionThreshold;
        IvpLineCrossEvaluator parallel = new(firstDirection, secondDirection, crossSpeed, 1d / crossSpeed);

        if (IvpRootFinder.Refine(
            parallel, IvpVector.DirectionThreshold, context.Start, time, 0, first.Motion, second.Motion, null, ref time))
        {
            kind = ParallelEvent;
        }

        double faceSpeed = angular + IvpVector.DirectionThreshold;
        double faceInverse = 1d / faceSpeed;

        FaceCheck[] checks =
        [
            new(second, secondDirection, !agrees, first, first.Topology.Hop(firstEdge)),
            new(second, secondDirection, agrees, first, firstEdge),
            new(first, firstDirection, !agrees, second, second.Topology.Hop(secondEdge)),
            new(first, firstDirection, agrees, second, secondEdge),
        ];

        foreach (FaceCheck check in checks)
        {
            (double X, double Y, double Z) direction = check.Negated
                ? (-check.Direction.X, -check.Direction.Y, -check.Direction.Z)
                : check.Direction;

            (double X, double Y, double Z) faceNormal = check.FaceSide.FaceNormal(check.Face);
            _ = IvpVector.TryScaleToUnitLength(ref faceNormal);

            IvpEdgeFaceEvaluator evaluator = new(direction, faceNormal, faceSpeed, faceInverse);
            double target = -(IvpCollisionTolerance.EdgeTargetScale * check.EdgeSide.Core.InverseDiameter);

            if (IvpRootFinder.Refine(
                evaluator, target, context.Start, time, 0, check.EdgeSide.Motion, check.FaceSide.Motion, null, ref time))
            {
                kind = FaceEvent;
            }
        }

        return new IvpImpact(kind, time);
    }

    /// <summary>One of the four face checks.</summary>
    /// <param name="EdgeSide">The side whose edge direction is dotted, and whose core gives the target.</param>
    /// <param name="Direction">That edge's unit direction.</param>
    /// <param name="Negated">Whether the direction is negated for this check.</param>
    /// <param name="FaceSide">The side whose face normal is dotted.</param>
    /// <param name="Face">An edge of that face's triangle.</param>
    private readonly record struct FaceCheck(
        IvpSearchSide EdgeSide,
        (double X, double Y, double Z) Direction,
        bool Negated,
        IvpSearchSide FaceSide,
        IvpLedgeEdge Face);
}
