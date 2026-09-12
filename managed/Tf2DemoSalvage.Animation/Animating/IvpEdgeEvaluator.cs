namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How steeply one of a vertex's edges runs toward a face of another body — the edge evaluator IVP's
/// vertex-face search uses to find the moment the closest feature leaves the vertex for an edge (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1b50` for each edge of the vertex's ring and measured by `FUN_1800a3660`**, slot 0
/// of vtable `1800fe750`, both read from the disassembly (`docs/findings/51`, *The edge evaluator*). The
/// engine's evaluator also carries a speed bound and its inverse at `+0x08` and `+0x10`, which belong to
/// the refining root finder and arrive with it; the ring walk and the slope check that decides which edges
/// are filled belong to the vertex-face step.
/// </remarks>
/// <param name="Direction">The edge's unit direction, in the edge's body's frame (<c>+0x28</c>).</param>
/// <param name="Normal">The face's normal, in the face's body's frame (<c>+0x48</c>).</param>
public readonly record struct IvpEdgeEvaluator(
    (double X, double Y, double Z) Direction,
    (double X, double Y, double Z) Normal)
{
    /// <summary>Fills the evaluator for the edge from a vertex to its neighbor, as <c>FUN_1800a1b50</c> does.</summary>
    /// <param name="vertex">The vertex, from its body's ledge points.</param>
    /// <param name="neighbor">The start of the next edge around the vertex, from the same points.</param>
    /// <param name="normal">The face's normal, as the point-plane evaluator holds it.</param>
    /// <returns>The evaluator.</returns>
    /// <remarks>
    /// **Subtracted in float, unlike the face normal.** `SUBSS` on the float points, then `CVTPS2PD`; the
    /// squared length is summed in double and narrowed with `CVTPD2PS` for the float reciprocal root, whose
    /// answer is widened again and multiplied in.
    /// </remarks>
    public static IvpEdgeEvaluator ForEdge(
        (float X, float Y, float Z) vertex,
        (float X, float Y, float Z) neighbor,
        (double X, double Y, double Z) normal)
    {
        float differenceX = neighbor.X - vertex.X;
        float differenceY = neighbor.Y - vertex.Y;
        float differenceZ = neighbor.Z - vertex.Z;

        double alongX = differenceX;
        double alongY = differenceY;
        double alongZ = differenceZ;

        double squared = (alongX * alongX) + (alongY * alongY) + (alongZ * alongZ);
        double scale = IvpVector.ReciprocalSquareRoot((float)squared);

        return new IvpEdgeEvaluator(
            Direction: (alongX * scale, alongY * scale, alongZ * scale),
            Normal: normal);
    }

    /// <summary>The face normal's component along the edge — <c>FUN_1800a3660</c>.</summary>
    /// <param name="edgeBody">Where the edge's body is.</param>
    /// <param name="faceBody">Where the face's body is.</param>
    /// <returns>The cosine between the edge and the face normal; negative when the edge runs into the face.</returns>
    /// <remarks>
    /// **Into the world through the face's body, then into the edge's body by the transpose** — calls to
    /// `FUN_1800709f0` and `FUN_1800706c0` — and the dot adds its `x` and `y` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix edgeBody, IvpMatrix faceBody)
    {
        (double X, double Y, double Z) normal = edgeBody.RotateInverse(faceBody.Rotate(Normal));

        return (normal.X * Direction.X) + (normal.Y * Direction.Y) + (normal.Z * Direction.Z);
    }
}
