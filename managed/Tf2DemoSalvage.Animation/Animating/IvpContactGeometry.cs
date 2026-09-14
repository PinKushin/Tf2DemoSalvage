using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Where a contact point's two features touch, and which way the first faces the second — the four measures under
/// <see cref="IvpContactRecord.Build"/> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*): `FUN_18008cab0` point–point,
/// `FUN_18008c7c0` point–edge, `FUN_18008c5b0` point–triangle and `FUN_18008cbc0` edge–edge. Each writes the record's
/// position, its normal — pointing from the first feature toward the second — and a span across the normal, and the contact
/// point's gap. **On the contact point's first measure only**, the three whose features have a range mark the record outside
/// when the touch falls beyond one, clearing the first-measure flag as they check.
///
/// Every subtraction's width, every sum's grouping and every comparison's NaN branch is carried as read.
/// </remarks>
public static class IvpContactGeometry
{
    /// <summary><c>DAT_1800ee18c</c>: the squared normal <c>x</c> from which the point–point span is crossed with <c>z</c>.</summary>
    private const float AxisShare = 0.9f;

    /// <summary><c>DAT_1800fcfa0</c>: <c>1e-10f</c> widened — the squared cross product two edges need before they cross.</summary>
    private const double CrossingFloor = (double)1e-10f;

    /// <summary>Two points — <c>FUN_18008cab0</c>.</summary>
    /// <param name="point">The contact point, whose gap is written.</param>
    /// <param name="first">The first feature's point, in the world.</param>
    /// <param name="second">The second feature's point, in the world.</param>
    /// <param name="record">The record, whose position, normal and span are written.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// d = second − first, scaled with five steps (FUN_18006fd40);  gap = the length it had, zero when too short to scale
    /// normal = (float)d;  (a, b) = (0, 1) when normal.x² in float reaches 0.9f, else (1, 0), a NaN taking the else
    /// span = (n.y·b, n.z·a − n.x·b, −(n.y·a)) — normal × x, or normal × z — scaled (FUN_18006dff0)
    /// position = first
    /// </code>
    /// </remarks>
    public static void PointPoint(
        IvpContactPoint point, (double X, double Y, double Z) first, (double X, double Y, double Z) second, IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(record);

        (double X, double Y, double Z) direction = Difference(first, second);
        point.Gap = (float)IvpVector.ScaleToUnitLength(ref direction);

        (float X, float Y, float Z) normal = ((float)direction.X, (float)direction.Y, (float)direction.Z);
        record.Normal = normal;

        bool alongX = normal.X * normal.X >= AxisShare;
        float a = alongX ? 0f : 1f;
        float b = alongX ? 1f : 0f;

        (float X, float Y, float Z) span = (normal.Y * b, (normal.Z * a) - (normal.X * b), -(normal.Y * a));
        _ = IvpVector.TryScaleToUnitLength(ref span);
        record.Span = span;

        record.Position = first;
    }

    /// <summary>A point and an edge — <c>FUN_18008c7c0</c>.</summary>
    /// <param name="point">The contact point, whose gap and first-measure flag are written.</param>
    /// <param name="at">The first feature's point, in the world.</param>
    /// <param name="edge">The second feature's edge.</param>
    /// <param name="side">The edge's side.</param>
    /// <param name="record">The record, whose position, normal, span and range are written.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// E₀, E₁ = the edge's ends in the world;  d = (float)(E₁ − E₀);  o = (float)(at − E₀)
    /// r = five-step 1/√((d.y² + d.x²) + d.z²), the squares summed in float;  c = d × o, in float
    /// gap = |c|·r;  s = c·(r / gap), narrowed, when gap² > 1e-19, else (1, 0, 0)
    /// normal = d × s in float, scaled (FUN_18006dff0);  position = at;  span = s
    /// first measure: outside when (double)((d.x·o.x + d.y·o.y) + d.z·o.z)·r² is below zero, NaN or above one
    /// </code>
    /// **The span is the perpendicular from the edge's line to the point turned about the edge**, and the normal comes back
    /// from it, so both are defined only while the point is off the line.
    /// </remarks>
    public static void PointEdge(
        IvpContactPoint point, (double X, double Y, double Z) at, IvpLedgeEdge edge, IvpLedgeSide side, IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(record);

        (double X, double Y, double Z) start = IvpCompactLedgeSolver.PointInWorld(side, edge);
        (double X, double Y, double Z) end = IvpCompactLedgeSolver.PointInWorld(side, side.Topology.Next(edge));

        (float X, float Y, float Z) along = Narrow(Difference(start, end));
        (float X, float Y, float Z) offset = Narrow(Difference(start, at));

        float squared = (along.Y * along.Y) + (along.X * along.X) + (along.Z * along.Z);
        double inverseLength = IvpVector.ReciprocalSquareRoot(squared, IvpVector.FiveSteps);

        (float X, float Y, float Z) across = (
            (offset.Z * along.Y) - (offset.Y * along.Z),
            (offset.X * along.Z) - (along.X * offset.Z),
            (along.X * offset.Y) - (along.Y * offset.X));

        double distance = IvpVector.Length(across) * inverseLength;
        point.Gap = (float)distance;

        (float X, float Y, float Z) unit = (1f, 0f, 0f);

        if (distance * distance > IvpVector.DirectionThreshold)
        {
            double scale = inverseLength / distance;
            unit = ((float)(across.X * scale), (float)(across.Y * scale), (float)(across.Z * scale));
        }

        (float X, float Y, float Z) normal = (
            (along.Y * unit.Z) - (along.Z * unit.Y),
            (along.Z * unit.X) - (along.X * unit.Z),
            (along.X * unit.Y) - (along.Y * unit.X));
        _ = IvpVector.TryScaleToUnitLength(ref normal);

        record.Normal = normal;
        record.Position = at;
        record.Span = unit;

        if (point.FirstMeasure)
        {
            point.FirstMeasure = false;

            float projected = (along.X * offset.X) + (along.Y * offset.Y) + (along.Z * offset.Z);
            double fraction = projected * (inverseLength * inverseLength);

            if (!(fraction >= 0d) || fraction > 1d)
            {
                record.Outside = true;
            }
        }
    }

    /// <summary>A point and a triangle — <c>FUN_18008c5b0</c>.</summary>
    /// <param name="point">The contact point, whose gap and first-measure flag are written, and whose reciprocal determinant is read.</param>
    /// <param name="at">The first feature's point, in the world.</param>
    /// <param name="edge">The edge the second feature's triangle is named by.</param>
    /// <param name="side">The triangle's side.</param>
    /// <param name="record">The record, whose position, normal, span and range are written.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// position = at;  p = at put into the side's frame (FUN_180080670)
    /// first measure: outside unless the triangle's weights for p have no sign bit set (FUN_18007cdf0)
    /// n = the face normal (FUN_18007b940) times the contact point's reciprocal determinant, in double
    /// gap = ((p.x·n.x + p.y·n.y) + p.z·n.z) − ((e.x·n.x + e.y·n.y) + e.z·n.z), e the edge's start widened
    /// normal = (float)(the side's matrix turning n out, times −1) (FUN_1800809d0)
    /// span = the edge's float vector turned out and narrowed (FUN_180080920), not scaled
    /// </code>
    /// **The normal is not scaled by a root here**: the reciprocal determinant the constructor stored does it, so it is unit
    /// only as nearly as that one float allows.
    /// </remarks>
    public static void PointTriangle(
        IvpContactPoint point, (double X, double Y, double Z) at, IvpLedgeEdge edge, IvpLedgeSide side, IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(record);

        record.Position = at;

        (double X, double Y, double Z) local = side.Current.ToObject(at);

        if (point.FirstMeasure)
        {
            point.FirstMeasure = false;

            if (!IvpCompactLedgeSolver.TriangleWeights(side, edge, local).Inside)
            {
                record.Outside = true;
            }
        }

        (double X, double Y, double Z) face = side.FaceNormal(edge);
        double determinant = point.InverseTriangleDeterminant;
        (double X, double Y, double Z) unit = (face.X * determinant, face.Y * determinant, face.Z * determinant);

        (float X, float Y, float Z) origin = side.StartOf(edge);

        point.Gap = (float)(Dot(local, unit) - Dot((origin.X, origin.Y, origin.Z), unit));

        (double X, double Y, double Z) outward = side.Current.Rotate(unit);
        record.Normal = ((float)(outward.X * -1d), (float)(outward.Y * -1d), (float)(outward.Z * -1d));

        (float X, float Y, float Z) end = side.EndOf(edge);
        (double X, double Y, double Z) turned = side.Current.Rotate((end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z));
        record.Span = Narrow(turned);
    }

    /// <summary>Two edges — <c>FUN_18008cbc0</c>.</summary>
    /// <param name="point">The contact point, whose gap and first-measure flag are written.</param>
    /// <param name="first">The first feature's edge.</param>
    /// <param name="firstSide">Its side.</param>
    /// <param name="second">The second feature's edge.</param>
    /// <param name="secondSide">Its side.</param>
    /// <param name="record">The record, whose position, normal, span and range are written.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// P₀, P₁ and Q₀, Q₁ = the edges' ends in the world;  a = P₁ − P₀ and b = Q₁ − Q₀, each scaled with four steps
    /// c = a × b;  degenerate unless |c|² > 1e-10f
    /// u = a × c, w = b × c;  tQ = (u·Q₀ − u·P₀) / (u·Q₀ − u·Q₁);  tP = (w·P₀ − w·Q₀) / (w·P₀ − w·P₁)
    ///     degenerate when either denominator is under 1e-19 in magnitude, or NaN
    /// A = (1 − tP)·P₀ + tP·P₁;  B = (1 − tQ)·Q₀ + tQ·Q₁;  g = |B − A|
    /// g > 1e-19: gap = g, normal = (B − A)/g;  otherwise gap = 0, normal = c/√|c|² — the cross product's own way
    /// position = A;  span = (float)a
    /// first measure: outside when tQ or tP is below zero, NaN or above one
    /// degenerate: outside, gap 0, position P₀, normal (1, 0, 0), span (0, 1, 0), and the first-measure flag untouched
    /// </code>
    /// The degenerate path also zeroes a velocity its caller has not yet filled; the caller overwrites it or never reads it.
    /// </remarks>
    public static void EdgeEdge(
        IvpContactPoint point,
        IvpLedgeEdge first,
        IvpLedgeSide firstSide,
        IvpLedgeEdge second,
        IvpLedgeSide secondSide,
        IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(firstSide);
        ArgumentNullException.ThrowIfNull(secondSide);
        ArgumentNullException.ThrowIfNull(record);

        (double X, double Y, double Z) firstStart = IvpCompactLedgeSolver.PointInWorld(firstSide, first);
        (double X, double Y, double Z) firstEnd = IvpCompactLedgeSolver.PointInWorld(firstSide, firstSide.Topology.Next(first));
        (double X, double Y, double Z) secondStart = IvpCompactLedgeSolver.PointInWorld(secondSide, second);
        (double X, double Y, double Z) secondEnd = IvpCompactLedgeSolver.PointInWorld(secondSide, secondSide.Topology.Next(second));

        (double X, double Y, double Z) secondDirection = Difference(secondStart, secondEnd);
        _ = IvpVector.TryScaleToUnitLength(ref secondDirection);

        (double X, double Y, double Z) firstDirection = Difference(firstStart, firstEnd);
        _ = IvpVector.TryScaleToUnitLength(ref firstDirection);

        (double X, double Y, double Z) cross = IvpVector.Cross(firstDirection, secondDirection);
        double crossSquared = Dot(cross, cross);

        if (!(crossSquared > CrossingFloor))
        {
            Degenerate(point, firstStart, record);
            return;
        }

        (double X, double Y, double Z) aroundFirst = IvpVector.Cross(firstDirection, cross);
        (double X, double Y, double Z) aroundSecond = IvpVector.Cross(secondDirection, cross);

        double secondStartAcross = Dot(aroundFirst, secondStart);
        double secondSpan = secondStartAcross - Dot(aroundFirst, secondEnd);

        if (!(Math.Abs(secondSpan) >= IvpVector.DirectionThreshold))
        {
            Degenerate(point, firstStart, record);
            return;
        }

        double secondFraction = (secondStartAcross - Dot(aroundFirst, firstStart)) / secondSpan;

        double firstStartAcross = Dot(aroundSecond, firstStart);
        double firstSpan = firstStartAcross - Dot(aroundSecond, firstEnd);

        if (!(Math.Abs(firstSpan) >= IvpVector.DirectionThreshold))
        {
            Degenerate(point, firstStart, record);
            return;
        }

        double firstFraction = (firstStartAcross - Dot(aroundSecond, secondStart)) / firstSpan;

        (double X, double Y, double Z) onFirst = IvpVector.Lerp(firstStart, firstEnd, firstFraction);
        (double X, double Y, double Z) onSecond = IvpVector.Lerp(secondStart, secondEnd, secondFraction);
        (double X, double Y, double Z) between = Difference(onFirst, onSecond);
        double distance = IvpVector.Length(between);

        if (distance > IvpVector.DirectionThreshold)
        {
            double scale = 1d / distance;
            point.Gap = (float)distance;
            record.Normal = Narrow((between.X * scale, between.Y * scale, between.Z * scale));
        }
        else
        {
            double scale = 1d / Math.Sqrt(crossSquared);
            point.Gap = 0f;
            record.Normal = Narrow((cross.X * scale, cross.Y * scale, cross.Z * scale));
        }

        record.Span = Narrow(firstDirection);
        record.Position = onFirst;

        if (point.FirstMeasure)
        {
            point.FirstMeasure = false;

            if (!(secondFraction >= 0d) || secondFraction > 1d || !(firstFraction >= 0d) || firstFraction > 1d)
            {
                record.Outside = true;
            }
        }
    }

    /// <summary>
    /// The edge–edge measure's answer for edges with no crossing — the gap is <c>DAT_18012d650</c>, the tolerance block's
    /// <c>[0x44]</c>, <see cref="IvpCollisionTolerance.ParallelEdgeGap"/>.
    /// </summary>
    private static void Degenerate(IvpContactPoint point, (double X, double Y, double Z) firstStart, IvpContactRecord record)
    {
        record.Outside = true;
        point.Gap = IvpCollisionTolerance.ParallelEdgeGap;
        record.Position = firstStart;
        record.Normal = (1f, 0f, 0f);
        record.Span = (0f, 1f, 0f);
    }

    private static (double X, double Y, double Z) Difference((double X, double Y, double Z) from, (double X, double Y, double Z) to) =>
        (to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    /// <summary>A dot product in double, the <c>x</c> and <c>y</c> terms added before <c>z</c>.</summary>
    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static (float X, float Y, float Z) Narrow((double X, double Y, double Z) vector) =>
        ((float)vector.X, (float)vector.Y, (float)vector.Z);
}
