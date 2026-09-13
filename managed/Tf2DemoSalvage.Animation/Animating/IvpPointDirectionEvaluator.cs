namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How far one body's point lies behind another's along one of that body's edges — the evaluator IVP's point-point
/// time of impact refines each ring edge with (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a2b30` for each edge of both rings and measured by `FUN_1800a3990`**, slot 0 of vtable
/// `1800fe738`, both read from the disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="Point">The measured point, widened, in its body's frame (<c>+0x28</c>).</param>
/// <param name="Origin">The ring's point, widened, in its body's frame (<c>+0x48</c>).</param>
/// <param name="Direction">The ring edge's direction, in the ring's body's frame (<c>+0x68</c>).</param>
/// <param name="ApproachSpeed">The ring's speed (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpPointDirectionEvaluator(
    (double X, double Y, double Z) Point,
    (double X, double Y, double Z) Origin,
    (double X, double Y, double Z) Direction,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary>The origin less the point, along the direction — <c>FUN_1800a3990</c>.</summary>
    /// <param name="first">Where the point's body is.</param>
    /// <param name="second">Where the ring's body is.</param>
    /// <returns>Negative when the point is ahead of the origin along the direction.</returns>
    /// <remarks>Every term through a call; the dot adds its `y` and `x` terms before `z`.</remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) point = first.ToWorld(Point);
        (double X, double Y, double Z) origin = second.ToWorld(Origin);
        (double X, double Y, double Z) along = second.Rotate(Direction);

        return ((origin.Y - point.Y) * along.Y) + ((origin.X - point.X) * along.X) + ((origin.Z - point.Z) * along.Z);
    }
}
