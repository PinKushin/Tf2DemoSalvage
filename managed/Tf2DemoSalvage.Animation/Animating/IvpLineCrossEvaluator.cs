namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How far two bodies' edges are from parallel — the evaluator IVP's edge-edge time of impact refines toward
/// <c>1e-19</c> (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1420` and measured by `FUN_1800a33e0`**, slot 0 of vtable `1800fe760`, both read from the
/// disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="FirstDirection">The first edge's unit direction, in its body's frame (<c>+0x28</c>).</param>
/// <param name="SecondDirection">The second edge's unit direction, in its body's frame (<c>+0x48</c>).</param>
/// <param name="ApproachSpeed">Twice the two cores' angular bounds plus <c>1e-19</c> (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpLineCrossEvaluator(
    (double X, double Y, double Z) FirstDirection,
    (double X, double Y, double Z) SecondDirection,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>The squared length of the directions' cross product — <c>FUN_1800a33e0</c>.</summary>
    /// <param name="first">Where the first edge's body is.</param>
    /// <param name="second">Where the second edge's body is.</param>
    /// <returns><c>(c.x² + c.y²) + c.z²</c>; zero for parallel edges.</returns>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) across =
            IvpVector.Cross(first.Rotate(FirstDirection), second.Rotate(SecondDirection));

        return (across.X * across.X) + (across.Y * across.Y) + (across.Z * across.Z);
    }
}
