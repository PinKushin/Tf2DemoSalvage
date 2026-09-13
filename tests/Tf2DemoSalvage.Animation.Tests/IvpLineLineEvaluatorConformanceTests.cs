using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The signed distance between two bodies' edge lines — the evaluator IVP's edge-edge time of impact drives to the
/// margin (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a32b0`, slot 0 of
/// vtable `1800fe758`:
///
/// <code>
///   c     = first.Rotate(+0x48) × second.Rotate(+0x88)
///   value = ((double)rsqrt_f((float)|c|²) · (c·first.ToWorld(+0x28) − second.ToWorld(+0x68)·c)) · sign   -- sign at +0xa8
/// </code>
/// </remarks>
public sealed class IvpLineLineEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpSearchFixtures.Identity;

    /// <remarks>An X line five inches above a Y line: `+X × +Y` is `+Z`, so the separation is `5`, times the sign.</remarks>
    [TestCase(1d, 5d)]
    [TestCase(-1d, -5d)]
    public void Distance_TwoCrossingLinesFiveApart_IsTheSeparationTimesTheSign(double sign, double expected) =>
        Evaluator(sign).Distance(Identity, Identity).ShouldBe(expected);

    /// <remarks>
    /// **The second line goes through the second transform**: lifted by two, the separation is `3`. Through the first
    /// transform it would stay `5`.
    /// </remarks>
    [Test]
    public void Distance_WithTheSecondBodyLifted_MeasuresFromTheLiftedLine() =>
        Evaluator(1d).Distance(Identity, IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 2d))).ShouldBe(3d);

    private static IvpLineLineEvaluator Evaluator(double sign) =>
        new(
            First: (0d, 0d, 5d),
            FirstDirection: (1d, 0d, 0d),
            Second: (0d, 0d, 0d),
            SecondDirection: (0d, 1d, 0d),
            Sign: sign,
            ApproachSpeed: 1d,
            InverseApproachSpeed: 1d);
}
