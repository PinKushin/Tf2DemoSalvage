namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// What IVP's time-of-impact searches measure: an object whose slot 0 returns a pair's distance for two
/// transforms, and which carries a bound on how fast that distance can close (B369).
/// </summary>
/// <remarks>
/// **The engine's evaluators are small objects on the stack of the routine that fills them** — vtable
/// `1800fe720` for point-plane and `1800fe750` for edge — and `FUN_1800b6210` and `FUN_1800b6590` read
/// exactly three things from one: slot 0, `+0x08` and `+0x10` (`docs/findings/51`, *The root finder*).
/// </remarks>
public interface IIvpDistanceEvaluator
{
    /// <summary>The bound on how fast the distance can close — <c>+0x08</c>, which the advancing search divides one by.</summary>
    public double ApproachSpeed { get; }

    /// <summary>One over <see cref="ApproachSpeed"/>, as the filling routine stored it — <c>+0x10</c>, which the refinement reads.</summary>
    public double InverseApproachSpeed { get; }

    /// <summary>The pair's distance with each body at the given transform — slot 0.</summary>
    /// <param name="first">The first body's transform.</param>
    /// <param name="second">The second body's transform.</param>
    /// <returns>The distance.</returns>
    public double Distance(IvpMatrix first, IvpMatrix second);
}
