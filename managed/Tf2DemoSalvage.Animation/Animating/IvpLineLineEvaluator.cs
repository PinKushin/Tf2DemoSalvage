namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The signed distance between two bodies' edge lines — the line-line evaluator IVP's edge-edge time of impact drives
/// to the margin (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1420` and measured by `FUN_1800a32b0`**, slot 0 of vtable `1800fe758`, both read from the
/// disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="First">The first edge's start, widened, in its body's frame (<c>+0x28</c>).</param>
/// <param name="FirstDirection">The first edge's unit direction, in its body's frame (<c>+0x48</c>).</param>
/// <param name="Second">The second edge's start, widened, in its body's frame (<c>+0x68</c>).</param>
/// <param name="SecondDirection">The second edge's unit direction, in its body's frame (<c>+0x88</c>).</param>
/// <param name="Sign">One, or minus one where the pair's normal was against the edges' cross product at the fill (<c>+0xa8</c>).</param>
/// <param name="ApproachSpeed">The search context's approach speed (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpLineLineEvaluator(
    (double X, double Y, double Z) First,
    (double X, double Y, double Z) FirstDirection,
    (double X, double Y, double Z) Second,
    (double X, double Y, double Z) SecondDirection,
    double Sign,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>The lines' separation along their cross product, signed — <c>FUN_1800a32b0</c>.</summary>
    /// <param name="first">Where the first edge's body is.</param>
    /// <param name="second">Where the second edge's body is.</param>
    /// <returns><c>(rsqrt_f(|c|²) · (c·p − q·c)) · sign</c>.</returns>
    /// <remarks>
    /// **The cross product is not scaled to unit length; its reciprocal root is a FLOAT**, `FUN_18006edb0` over the
    /// narrowed squared length, widened. Every sum adds its `x` and `y` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) firstAlong = first.Rotate(FirstDirection);
        (double X, double Y, double Z) firstStart = first.ToWorld(First);
        (double X, double Y, double Z) secondAlong = second.Rotate(SecondDirection);
        (double X, double Y, double Z) secondStart = second.ToWorld(Second);

        (double X, double Y, double Z) across = IvpVector.Cross(firstAlong, secondAlong);

        double firstHeight = (across.X * firstStart.X) + (across.Y * firstStart.Y) + (across.Z * firstStart.Z);
        double secondHeight = (secondStart.X * across.X) + (secondStart.Y * across.Y) + (secondStart.Z * across.Z);
        double squared = (across.X * across.X) + (across.Y * across.Y) + (across.Z * across.Z);

        double scale = IvpVector.ReciprocalSquareRoot((float)squared);

        return scale * (firstHeight - secondHeight) * Sign;
    }
}
