using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How steeply one of a hull point's edges runs toward another body's edge line — the evaluator IVP's point-edge time
/// of impact refines the point's ring with (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** — `docs/findings/51`, *The other three times of impact*. `FUN_1800a37f0`, slot 0 of
/// vtable `1800fe740`:
///
/// <code>
///   w     = first.ToWorld(+0x28) − second.ToWorld(+0x68)
///   e     = second.Rotate(+0x88)          a = first.Rotate(+0x48)
///   r     = (e × w) × e, each component narrowed to float, five steps to unit length
///   value = (a.y·r.y + a.x·r.x) + a.z·r.z
/// </code>
/// </remarks>
public sealed class IvpEdgeLineEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpSearchFixtures.Identity;

    /// <remarks>
    /// **`r` points from the line toward the vertex.** An edge running along `−Y` from a vertex two inches up `+Y` runs
    /// straight at the line, `−1`; from two inches down `−Y` it runs straight away, `+1`.
    /// </remarks>
    [TestCase(2d, -1d)]
    [TestCase(-2d, 1d)]
    public void Distance_AnEdgeAlongMinusY_IsItsCosineTowardTheVertexSide(double side, double expected) =>
        Evaluator((0d, side, 0d)).Distance(Identity, Identity).ShouldBe(expected);

    /// <remarks>
    /// **Only the offset across the line counts** — the double cross removes the part along it. Seven inches along the
    /// line and two across still measures `−1`; the raw offset would measure `−2/√53`.
    /// </remarks>
    [Test]
    public void Distance_AVertexAlongTheLine_MeasuresOnlyTheOffsetAcrossIt() =>
        Evaluator((7d, 2d, 0d)).Distance(Identity, Identity).ShouldBe(-1d);

    private static IvpEdgeLineEvaluator Evaluator((double X, double Y, double Z) vertex) =>
        new(
            Vertex: vertex,
            Direction: (0d, -1d, 0d),
            LinePoint: (0d, 0d, 0d),
            LineDirection: (1d, 0d, 0d),
            ApproachSpeed: 1d,
            InverseApproachSpeed: 1d);
}
