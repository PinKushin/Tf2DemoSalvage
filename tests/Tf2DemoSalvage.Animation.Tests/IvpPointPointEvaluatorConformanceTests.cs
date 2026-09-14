using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The distance between two hull points, capped by its stretched projection on the pair's normal — the point-point
/// evaluator IVP's point-point time of impact drives to its target (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a2b30` fills it and
/// `FUN_1800a31e0`, slot 0 of vtable `1800fe730`, measures it:
///
/// <code>
///   d     = second.ToWorld(+0x48) − first.ToWorld(+0x28)
///   s     = ((d.y·u.y + d.x·u.x) + d.z·u.z) · (double)1.2f        -- u at +0x68, the mindist normal negated
///   value = |s|·s ≥ (d.y² + d.x²) + d.z²  ?  √that  :  s          -- COMISD, JNC
/// </code>
/// </remarks>
public sealed class IvpPointPointEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpSearchFixtures.Identity;

    /// <remarks>
    /// **A projection shorter than the distance is the answer, stretched by the float `1.2f` widened** — `3 ·
    /// 1.2000000476837158`, not `3.6`.
    /// </remarks>
    [Test]
    public void Distance_AProjectionShorterThanTheDistance_IsTheStretchedProjection() =>
        Evaluator((3d, 0d, 4d), (1d, 0d, 0d)).Distance(Identity, Identity).ShouldBe(3d * 1.2f);

    /// <remarks>**A stretched projection at or past the distance gives the distance**: along `(0.6, 0, 0.8)` it is about `6`, over `5`.</remarks>
    [Test]
    public void Distance_AStretchedProjectionPastTheDistance_IsTheDistance() =>
        Evaluator((3d, 0d, 4d), (0.6d, 0d, 0.8d)).Distance(Identity, Identity).ShouldBe(5d);

    /// <remarks>
    /// **The comparison keeps the projection's sign, `|s|·s`.** Pointing away along `−X`, `s` is `−3.6`: squared it
    /// would pass the `9` and answer `3`; the engine answers `−3.6`.
    /// </remarks>
    [Test]
    public void Distance_AProjectionPointingAway_IsTheNegativeProjection() =>
        Evaluator((3d, 0d, 0d), (-1d, 0d, 0d)).Distance(Identity, Identity).ShouldBe(-3d * 1.2f);

    /// <remarks>
    /// **The second point goes through the second transform.** Lifted by ten, the offset is `(3, 0, 14)`, and along
    /// `+Z` the stretch passes the distance: `√205`. Through the first transform instead the offset points down and
    /// measures `−7.2`.
    /// </remarks>
    [Test]
    public void Distance_WithTheSecondBodyLifted_MeasuresTheLiftedPoint() =>
        Evaluator((3d, 0d, 4d), (0d, 0d, 1d))
            .Distance(Identity, IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 10d)))
            .ShouldBe(Math.Sqrt(205d));

    private static IvpPointPointEvaluator Evaluator((double X, double Y, double Z) second, (double X, double Y, double Z) direction) =>
        new(First: (0d, 0d, 0d), Second: second, Direction: direction, ApproachSpeed: 1d, InverseApproachSpeed: 1d);
}
