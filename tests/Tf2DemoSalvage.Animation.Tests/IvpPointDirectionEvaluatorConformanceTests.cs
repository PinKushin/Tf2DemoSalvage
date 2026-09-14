using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How far one body's point lies behind another's along one of that body's edges — the evaluator IVP's point-point
/// time of impact refines each ring edge with (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a3990`, slot 0 of
/// vtable `1800fe738`:
///
/// <code>
///   p     = first.ToWorld(+0x28)       q = second.ToWorld(+0x48)       u = second.Rotate(+0x68)
///   value = ((q.y − p.y)·u.y + (q.x − p.x)·u.x) + (q.z − p.z)·u.z
/// </code>
/// </remarks>
public sealed class IvpPointDirectionEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpSearchFixtures.Identity;

    /// <remarks>An origin two inches along the direction from the point: two.</remarks>
    [Test]
    public void Distance_AnOriginAlongTheDirection_IsItsOffset() =>
        Evaluator((0d, 0d, 0d)).Distance(Identity, Identity).ShouldBe(2d);

    /// <remarks>**The origin less the point**: a point three inches past the origin measures `−3`.</remarks>
    [Test]
    public void Distance_APointPastTheOrigin_IsNegative() =>
        Evaluator((5d, 0d, 0d)).Distance(Identity, Identity).ShouldBe(-3d);

    /// <remarks>
    /// **The direction is turned by the origin's body, not the point's.** The point's body a quarter turn about Z
    /// leaves the point at the origin and the direction along `+X`: still two. Turned by that body, the direction would
    /// run along `+Y` and measure zero.
    /// </remarks>
    [Test]
    public void Distance_WithThePointsBodyTurned_KeepsTheDirectionInTheOriginsBody() =>
        Evaluator((0d, 0d, 0d)).Distance(IvpSearchFixtures.QuarterTurnAboutZ, Identity).ShouldBe(2d);

    private static IvpPointDirectionEvaluator Evaluator((double X, double Y, double Z) point) =>
        new(Point: point, Origin: (2d, 0d, 0d), Direction: (1d, 0d, 0d), ApproachSpeed: 1d, InverseApproachSpeed: 1d);
}
