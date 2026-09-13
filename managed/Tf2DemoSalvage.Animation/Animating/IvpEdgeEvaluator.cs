namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How steeply one of a vertex's edges runs toward a face of another body — the edge evaluator IVP's
/// vertex-face search uses to find the moment the closest feature leaves the vertex for an edge (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1b50` for each edge of the vertex's ring and measured by `FUN_1800a3660`**, slot 0
/// of vtable `1800fe750`, both read from the disassembly (`docs/findings/51`, *The edge evaluator*, and
/// *`FUN_1800a1b50` field by field*).
/// </remarks>
/// <param name="Direction">The edge's unit direction, in the edge's body's frame (<c>+0x28</c>).</param>
/// <param name="Normal">The face's normal, in the face's body's frame (<c>+0x48</c>).</param>
/// <param name="ApproachSpeed">The two cores' angular speed bound (<c>+0x08</c>), from <see cref="SpeedBound"/>.</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpEdgeEvaluator(
    (double X, double Y, double Z) Direction,
    (double X, double Y, double Z) Normal,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary><c>DAT_1800f4f20</c> added to the speed — the same double as the direction threshold.</summary>
    private const double SpeedFloor = IvpVector.DirectionThreshold;

    /// <summary>The evaluator's speed for two cores — <c>(double)(coreA+0x80 + coreB+0x80) + 1e-19</c>.</summary>
    /// <param name="first">The vertex's core's angular speed bound, <c>core+0x80</c>.</param>
    /// <param name="second">The face's core's angular speed bound.</param>
    /// <returns>The speed.</returns>
    /// <remarks>
    /// **Summed in float and only then widened** (`1800a1bed`–`1800a1c10`), and the floor added in double at
    /// `1800a1e34`, so two cores that are not turning still give a speed whose reciprocal is finite.
    /// </remarks>
    public static double SpeedBound(float first, float second) => (double)(first + second) + SpeedFloor;

    /// <summary>An edge's difference and the scale that makes it a unit direction, as <c>FUN_1800a1b50</c> computes both.</summary>
    /// <param name="vertex">The vertex, from its body's ledge points.</param>
    /// <param name="neighbor">The far end of the edge, from the same points.</param>
    /// <returns>The widened difference, and the widened float reciprocal root of its squared length.</returns>
    /// <remarks>
    /// **Subtracted in float, unlike the face normal.** `SUBSS` on the float points, then `CVTPS2PD`; the
    /// squared length is summed in double and narrowed with `CVTPD2PS` for the float reciprocal root, whose
    /// answer is widened. The ring's slope check multiplies the dot with the difference by this same scale,
    /// which is not the dot with the scaled direction.
    /// </remarks>
    public static ((double X, double Y, double Z) Difference, double Scale) EdgeDifference(
        (float X, float Y, float Z) vertex,
        (float X, float Y, float Z) neighbor)
    {
        float differenceX = neighbor.X - vertex.X;
        float differenceY = neighbor.Y - vertex.Y;
        float differenceZ = neighbor.Z - vertex.Z;

        double alongX = differenceX;
        double alongY = differenceY;
        double alongZ = differenceZ;

        double squared = (alongX * alongX) + (alongY * alongY) + (alongZ * alongZ);

        return ((alongX, alongY, alongZ), IvpVector.ReciprocalSquareRoot((float)squared));
    }

    /// <summary>Fills the evaluator for the edge from a vertex to its neighbor, as <c>FUN_1800a1b50</c> does.</summary>
    /// <param name="vertex">The vertex, from its body's ledge points.</param>
    /// <param name="neighbor">The start of the next edge around the vertex, from the same points.</param>
    /// <param name="normal">The face's normal, as the point-plane evaluator holds it.</param>
    /// <param name="approachSpeed">The speed, from <see cref="SpeedBound"/>.</param>
    /// <returns>The evaluator.</returns>
    public static IvpEdgeEvaluator ForEdge(
        (float X, float Y, float Z) vertex,
        (float X, float Y, float Z) neighbor,
        (double X, double Y, double Z) normal,
        double approachSpeed)
    {
        ((double X, double Y, double Z) along, double scale) = EdgeDifference(vertex, neighbor);

        return new IvpEdgeEvaluator(
            Direction: (along.X * scale, along.Y * scale, along.Z * scale),
            Normal: normal,
            ApproachSpeed: approachSpeed,
            InverseApproachSpeed: 1d / approachSpeed);
    }

    /// <summary>The face normal's component along the edge — <c>FUN_1800a3660</c>.</summary>
    /// <param name="first">Where the edge's body is.</param>
    /// <param name="second">Where the face's body is.</param>
    /// <returns>The cosine between the edge and the face normal; negative when the edge runs into the face.</returns>
    /// <remarks>
    /// **Into the world through the face's body, then into the edge's body by the transpose** — calls to
    /// `FUN_1800709f0` and `FUN_1800706c0` — and the dot adds its `x` and `y` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) normal = first.RotateInverse(second.Rotate(Normal));

        return (normal.X * Direction.X) + (normal.Y * Direction.Y) + (normal.Z * Direction.Z);
    }
}
