using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// One body's edge direction against another body's face normal, both in the world — the evaluator IVP's edge-edge
/// time of impact refines each face check with (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a3a30`, slot 0 of
/// vtable `1800fe728`: `a = first.Rotate(+0x28)`, `n = second.Rotate(+0x48)`, and `(n.x·a.x + n.y·a.y) + n.z·a.z`.
/// Unlike `FUN_1800a3660`, nothing is taken back into a body's frame.
/// </remarks>
public sealed class IvpEdgeFaceEvaluatorConformanceTests
{
    /// <remarks>**The direction is turned by the first body**: `+X` a quarter turn about Z is `+Y`, along the normal.</remarks>
    [Test]
    public void Distance_WithTheEdgesBodyTurnedAQuarterAboutZ_TurnsTheDirection() =>
        new IvpEdgeFaceEvaluator(Direction: (1d, 0d, 0d), Normal: (0d, 1d, 0d), ApproachSpeed: 1d, InverseApproachSpeed: 1d)
            .Distance(IvpSearchFixtures.QuarterTurnAboutZ, IvpSearchFixtures.Identity)
            .ShouldBe(1d, 1e-6d);

    /// <remarks>**The normal is turned by the second body**: `+X` a quarter turn about Z is `+Y`, along the direction.</remarks>
    [Test]
    public void Distance_WithTheFacesBodyTurnedAQuarterAboutZ_TurnsTheNormal() =>
        new IvpEdgeFaceEvaluator(Direction: (0d, 1d, 0d), Normal: (1d, 0d, 0d), ApproachSpeed: 1d, InverseApproachSpeed: 1d)
            .Distance(IvpSearchFixtures.Identity, IvpSearchFixtures.QuarterTurnAboutZ)
            .ShouldBe(1d, 1e-6d);
}
