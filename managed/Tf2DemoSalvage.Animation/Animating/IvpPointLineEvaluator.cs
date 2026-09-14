using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A hull point's distance from another body's edge line, capped by its handicapped projection on the normal it
/// started along — the point-line evaluator IVP's point-edge time of impact drives to its target (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1ff0` and measured by `FUN_1800a36d0`**, slot 0 of vtable `1800fe748`, both read from the
/// disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="Point">The point, from its body's float points (<c>+0x30</c>).</param>
/// <param name="LinePoint">The edge's start, from its body's float points (<c>+0x40</c>).</param>
/// <param name="LineDirection">The edge's direction, in its body's frame (<c>+0x50</c>).</param>
/// <param name="Normal">The direction from the point across the line at the fill, in the line's body's frame (<c>+0x70</c>).</param>
/// <param name="Handicap">Half the margin plus the extra radius (<c>+0x28</c>).</param>
/// <param name="ApproachSpeed">The search context's approach speed (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpPointLineEvaluator(
    (double X, double Y, double Z) Point,
    (double X, double Y, double Z) LinePoint,
    (double X, double Y, double Z) LineDirection,
    (double X, double Y, double Z) Normal,
    double Handicap,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>Fills the evaluator for a point and an edge, as <c>FUN_1800a1ff0</c> does.</summary>
    /// <param name="point">The point, from its body's ledge points.</param>
    /// <param name="lineStart">The edge's start, from the other body's ledge points.</param>
    /// <param name="lineDirection">The edge's direction, from <see cref="IvpVector.UnitDifference"/>.</param>
    /// <param name="handicap"><c>((double)margin + (double)extra) · 0.5</c>.</param>
    /// <param name="approachSpeed">The search context's approach speed, <c>context+0x10</c>.</param>
    /// <param name="pointBody">The point's cache object's CURRENT matrix.</param>
    /// <param name="lineBody">The edge's cache object's CURRENT matrix.</param>
    /// <returns>The evaluator.</returns>
    /// <remarks>
    /// **The normal is built through the cache objects' current matrices, not the motion caches**:
    /// `lineBody⁻¹·((lineBody·start − pointBody·point) × lineBody·direction)` (`FUN_180080720` twice, `FUN_1800809d0`,
    /// `FUN_18006dd30`, `FUN_180080890`), scaled to unit length with five steps and the answer unread.
    /// </remarks>
    public static IvpPointLineEvaluator ForEdge(
        (float X, float Y, float Z) point,
        (float X, float Y, float Z) lineStart,
        (double X, double Y, double Z) lineDirection,
        double handicap,
        double approachSpeed,
        IvpMatrix pointBody,
        IvpMatrix lineBody)
    {
        (double X, double Y, double Z) pointWorld = pointBody.ToWorld((point.X, point.Y, point.Z));
        (double X, double Y, double Z) startWorld = lineBody.ToWorld((lineStart.X, lineStart.Y, lineStart.Z));
        (double X, double Y, double Z) lineWorld = lineBody.Rotate(lineDirection);

        (double X, double Y, double Z) across = IvpVector.Cross(
            (startWorld.X - pointWorld.X, startWorld.Y - pointWorld.Y, startWorld.Z - pointWorld.Z),
            lineWorld);

        (double X, double Y, double Z) normal = lineBody.RotateInverse(across);
        _ = IvpVector.TryScaleToUnitLength(ref normal, IvpVector.FiveSteps);

        return new IvpPointLineEvaluator(
            Point: (point.X, point.Y, point.Z),
            LinePoint: (lineStart.X, lineStart.Y, lineStart.Z),
            LineDirection: lineDirection,
            Normal: normal,
            Handicap: handicap,
            ApproachSpeed: approachSpeed,
            InverseApproachSpeed: 1d / approachSpeed);
    }

    /// <summary>The distance from the line, or the handicapped projection when that is less — <c>FUN_1800a36d0</c>.</summary>
    /// <param name="first">Where the point's body is.</param>
    /// <param name="second">Where the line's body is.</param>
    /// <returns>The projection on the normal plus the handicap, unless that reaches the distance; the distance then.</returns>
    /// <remarks>
    /// **`COMISD` then `JC`**, so the projection is kept below the distance and for a NaN. The distance is
    /// `FUN_18006fc60`, `√((x² + y²) + z²)` in double, and the dot adds its `x` and `y` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) point = first.ToWorld(Point);
        (double X, double Y, double Z) start = second.ToWorld(LinePoint);
        (double X, double Y, double Z) line = second.Rotate(LineDirection);
        (double X, double Y, double Z) normal = second.Rotate(Normal);

        (double X, double Y, double Z) across =
            IvpVector.Cross((start.X - point.X, start.Y - point.Y, start.Z - point.Z), line);

        double length = Math.Sqrt((across.X * across.X) + (across.Y * across.Y) + (across.Z * across.Z));
        double projected = (across.X * normal.X) + (across.Y * normal.Y) + (across.Z * normal.Z) + Handicap;

        return projected >= length ? length : projected;
    }
}
