using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The distance between a hull point of one body and a hull point of another, capped by its stretched projection on
/// the pair's normal — the point-point evaluator IVP's point-point time of impact drives to its target (B369).
/// </summary>
/// <remarks>
/// **Filled by `FUN_1800a2b30` and measured by `FUN_1800a31e0`**, slot 0 of vtable `1800fe730`, both read from the
/// disassembly (`docs/findings/51`, *The other three times of impact*).
/// </remarks>
/// <param name="First">The first body's point, widened, in its frame (<c>+0x28</c>).</param>
/// <param name="Second">The second body's point, widened, in its frame (<c>+0x48</c>).</param>
/// <param name="Direction">The mindist's normal negated in float and widened, in the world (<c>+0x68</c>).</param>
/// <param name="ApproachSpeed">The search context's approach speed (<c>+0x08</c>).</param>
/// <param name="InverseApproachSpeed">One over it, as filled (<c>+0x10</c>).</param>
public readonly record struct IvpPointPointEvaluator(
    (double X, double Y, double Z) First,
    (double X, double Y, double Z) Second,
    (double X, double Y, double Z) Direction,
    double ApproachSpeed,
    double InverseApproachSpeed) : IIvpDistanceEvaluator
{
    /// <summary><c>DAT_1800eed28</c>, the float <c>1.2f</c> widened.</summary>
    private const double Stretch = 1.2f;

    /// <summary>The distance, or the stretched projection when that is shorter — <c>FUN_1800a31e0</c>.</summary>
    /// <param name="first">Where the first body is.</param>
    /// <param name="second">Where the second body is.</param>
    /// <returns>The stretched projection unless its sign-kept square reaches the squared distance; the distance then.</returns>
    /// <remarks>
    /// **`|s|·s` against the squared distance, `COMISD` then `JNC`**, so a projection pointing away is always kept and a
    /// NaN keeps the projection. Both sums add their `y` and `x` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix first, IvpMatrix second)
    {
        (double X, double Y, double Z) from = first.ToWorld(First);
        (double X, double Y, double Z) to = second.ToWorld(Second);

        double x = to.X - from.X;
        double y = to.Y - from.Y;
        double z = to.Z - from.Z;

        double squared = (y * y) + (x * x) + (z * z);
        double along = ((y * Direction.Y) + (x * Direction.X) + (z * Direction.Z)) * Stretch;

        return Math.Abs(along) * along >= squared ? Math.Sqrt(squared) : along;
    }
}
