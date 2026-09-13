namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How steeply one of a hull point's edges runs toward another body's edge line — the evaluator IVP's point-edge time
/// of impact refines the point's ring with (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1ff0` for each edge of the point's ring and measured by `FUN_1800a37f0`**, slot 0 of vtable
/// `1800fe740`, both read from the disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="Vertex">The point, widened, in its body's frame (<c>+0x28</c>).</param>
/// <param name="Direction">The ring edge's unit direction, in the point's body's frame (<c>+0x48</c>).</param>
/// <param name="LinePoint">The line's start, widened, in its body's frame (<c>+0x68</c>).</param>
/// <param name="LineDirection">The line's unit direction, in its body's frame (<c>+0x88</c>).</param>
/// <param name="ApproachSpeed">The ring's speed (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpEdgeLineEvaluator(
    (double X, double Y, double Z) Vertex,
    (double X, double Y, double Z) Direction,
    (double X, double Y, double Z) LinePoint,
    (double X, double Y, double Z) LineDirection,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>The edge's cosine toward the vertex's side of the line — <c>FUN_1800a37f0</c>.</summary>
    /// <param name="first">Where the point's body is.</param>
    /// <param name="second">Where the line's body is.</param>
    /// <returns>Negative when the edge runs toward the line.</returns>
    /// <remarks>
    /// **`(e × w) × e` inlined, each component narrowed to FLOAT** (`CVTPD2PS`, `CVTPS2PD`) before the five-step scaling
    /// to unit length, `w` the vertex less the line's start. The dot adds its `y` and `x` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) vertex = first.ToWorld(Vertex);
        (double X, double Y, double Z) along = first.Rotate(Direction);
        (double X, double Y, double Z) start = second.ToWorld(LinePoint);
        (double X, double Y, double Z) line = second.Rotate(LineDirection);

        (double X, double Y, double Z) offset = (vertex.X - start.X, vertex.Y - start.Y, vertex.Z - start.Z);
        (double X, double Y, double Z) across = IvpVector.Cross(IvpVector.Cross(line, offset), line);

        (double X, double Y, double Z) toward = ((float)across.X, (float)across.Y, (float)across.Z);
        _ = IvpVector.TryScaleToUnitLength(ref toward, IvpVector.FiveSteps);

        return (along.Y * toward.Y) + (along.X * toward.X) + (along.Z * toward.Z);
    }
}
