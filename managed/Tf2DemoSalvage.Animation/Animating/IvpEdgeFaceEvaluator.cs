namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One body's edge direction against another body's face normal, both in the world — the evaluator IVP's edge-edge
/// time of impact refines each of its four face checks with (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a1420` and measured by `FUN_1800a3a30`**, slot 0 of vtable `1800fe728`, both read from the
/// disassembly (`docs/findings/51`, *The other three times of impact*). Unlike <see cref="IvpEdgeEvaluator"/>, the dot
/// is taken in the world, so neither vector goes back into a body's frame.
/// </remarks>
/// <param name="Direction">The edge's unit direction, possibly negated, in its body's frame (<c>+0x28</c>).</param>
/// <param name="Normal">The face's unit normal, in its body's frame (<c>+0x48</c>).</param>
/// <param name="ApproachSpeed">The two cores' angular bounds plus <c>1e-19</c> (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpEdgeFaceEvaluator(
    (double X, double Y, double Z) Direction,
    (double X, double Y, double Z) Normal,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>The cosine between the edge and the face normal — <c>FUN_1800a3a30</c>.</summary>
    /// <param name="first">Where the edge's body is.</param>
    /// <param name="second">Where the face's body is.</param>
    /// <returns>Negative when the edge runs into the face.</returns>
    /// <remarks>Both through a call to `FUN_1800709f0`; the dot adds its `x` and `y` terms before `z`.</remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) along = first.Rotate(Direction);
        (double X, double Y, double Z) normal = second.Rotate(Normal);

        return (normal.X * along.X) + (normal.Y * along.Y) + (normal.Z * along.Z);
    }
}
