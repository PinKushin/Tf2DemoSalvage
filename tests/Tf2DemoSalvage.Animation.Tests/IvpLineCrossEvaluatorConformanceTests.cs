using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How far two bodies' edges are from parallel — the evaluator IVP's edge-edge time of impact refines toward
/// <c>1e-19</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a33e0`, slot 0 of
/// vtable `1800fe760`: `c = first.Rotate(+0x28) × second.Rotate(+0x48)`, and `(c.x² + c.y²) + c.z²`.
/// </remarks>
public sealed class IvpLineCrossEvaluatorConformanceTests
{
    private static readonly IvpLineCrossEvaluator Perpendicular =
        new(FirstDirection: (1d, 0d, 0d), SecondDirection: (0d, 1d, 0d), ApproachSpeed: 1d, InverseApproachSpeed: 1d);

    /// <remarks>Two perpendicular unit directions: one.</remarks>
    [Test]
    public void Distance_PerpendicularDirections_IsOne() =>
        Perpendicular.Distance(IvpSearchFixtures.Identity, IvpSearchFixtures.Identity).ShouldBe(1d);

    /// <remarks>
    /// **The second direction is turned by the second body**: a quarter turn about Z takes `+Y` to `−X`, parallel to the
    /// first. Unturned, it would stay one.
    /// </remarks>
    [Test]
    public void Distance_WithTheSecondBodyTurnedAQuarterAboutZ_IsParallel() =>
        Perpendicular.Distance(IvpSearchFixtures.Identity, IvpSearchFixtures.QuarterTurnAboutZ).ShouldBe(0d, 1e-12d);
}
