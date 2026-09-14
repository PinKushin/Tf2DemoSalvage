using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A hull point's distance from another body's edge line, capped by its handicapped projection on the normal it
/// started along — the evaluator IVP's point-edge time of impact drives to its target (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a1ff0` fills it and
/// `FUN_1800a36d0`, slot 0 of vtable `1800fe748`, measures it:
///
/// <code>
///   fill:   v = lineBody⁻¹·((lineBody·Q − pointBody·P) × lineBody·e), five steps to unit length
///   c     = (second.ToWorld(Q) − first.ToWorld(P)) × second.Rotate(e)
///   x     = ((c.x·v'.x + c.y·v'.y) + c.z·v'.z) + h                    -- v' = second.Rotate(v)
///   value = x ≥ |c|  ?  |c|  :  x                                     -- COMISD, JC
/// </code>
/// </remarks>
public sealed class IvpPointLineEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpSearchFixtures.Identity;

    /// <remarks>
    /// **Facing the line, the handicapped projection passes the distance and the distance is the answer**: three
    /// inches from the X axis, `3 + 0.5` against `3`. Unhandicapped it would not: the five-step normal lands a hair
    /// under unit length, so the bare projection is just under `3` and would be the answer.
    /// </remarks>
    [Test]
    public void Distance_APointFacingTheLine_IsTheLineDistance() =>
        IvpPointLineEvaluator.ForEdge((0f, 3f, 0f), (0f, 0f, 0f), (1d, 0d, 0d), 0.5d, 1d, Identity, Identity)
            .Distance(Identity, Identity)
            .ShouldBe(3d);

    /// <remarks>
    /// **The handicap is added before the comparison**: with the normal turned away the projection is `−3`, so the
    /// answer is `−3 + 0.5`.
    /// </remarks>
    [Test]
    public void Distance_ANormalFacingAway_IsTheHandicappedProjection()
    {
        IvpPointLineEvaluator evaluator = new(
            Point: (0d, 3d, 0d),
            LinePoint: (0d, 0d, 0d),
            LineDirection: (1d, 0d, 0d),
            Normal: (0d, 0d, -1d),
            Handicap: 0.5d,
            ApproachSpeed: 1d,
            InverseApproachSpeed: 1d);

        evaluator.Distance(Identity, Identity).ShouldBe(-2.5d);
    }

    /// <remarks>
    /// **The normal is stored in the line's body's frame, by the transpose.** The world normal is `+Z`; the line's body
    /// a quarter turn about X takes `+Y` to `+Z`, so its inverse stores `+Y`. Turned forward it would store `−Y`.
    /// </remarks>
    [Test]
    public void ForEdge_WithTheLinesBodyTurned_StoresTheNormalInTheLinesFrame()
    {
        IvpMatrix quarterTurnAboutX = IvpMatrix.FromRotation((0.70710677f, 0f, 0f, 0.70710677f), (0d, 0d, 0d));

        IvpPointLineEvaluator evaluator =
            IvpPointLineEvaluator.ForEdge((0f, 3f, 0f), (0f, 0f, 0f), (1d, 0d, 0d), 0.5d, 1d, Identity, quarterTurnAboutX);

        evaluator.Normal.X.ShouldBe(0d, 1e-6d);
        evaluator.Normal.Y.ShouldBe(1d, 1e-6d);
        evaluator.Normal.Z.ShouldBe(0d, 1e-6d);
    }
}
