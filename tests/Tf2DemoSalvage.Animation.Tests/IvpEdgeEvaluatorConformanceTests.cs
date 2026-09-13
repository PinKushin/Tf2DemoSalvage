using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How steeply one of a vertex's edges runs toward a face of another body — the edge evaluator IVP's
/// vertex-face search uses to find the moment the closest feature leaves the vertex (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *The edge evaluator*.
/// `FUN_1800a1b50` fills it for each edge of the vertex's ring, and `FUN_1800a3660`, slot 0 of vtable
/// `1800fe750`, measures it:
///
/// <code>
///   d         = Q − P, subtracted in FLOAT (SUBSS), then widened
///   s         = (float)((d.x·d.x + d.y·d.y) + d.z·d.z)
///   r         = (double)FUN_18006edb0(s)      -- (float)FUN_18006ecf0((double)s)
///   direction = d · r                          +0x28, in the edge's body's frame
///   normal    = the face normal, copied        +0x48, in the face's body's frame
///
///   value     = A.RotateInverse(B.Rotate(normal)) · direction      -- FUN_1800709f0, then FUN_1800706c0
/// </code>
///
/// with the dot adding its `x` and `y` terms before `z`.
/// </remarks>
public sealed class IvpEdgeEvaluatorConformanceTests
{
    private const double Tolerance = 1e-6d;

    private static readonly IvpMatrix Identity = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

    private static readonly IvpMatrix QuarterTurnAboutX =
        IvpMatrix.FromRotation((0.70710677f, 0f, 0f, 0.70710677f), (0d, 0d, 0d));

    /// <remarks>
    /// **The scale is a float, widened.** The edge `(3, 4, 0)` has squared length 25, whose engine
    /// reciprocal root rounds to the float `0.2`, which widens to `0.20000000298023224`. So the direction is
    /// `0.6000000089406967` and `0.800000011920929`, not `0.6` and `0.8` — a double scale lands on the
    /// latter.
    /// </remarks>
    [Test]
    public void ForEdge_AThreeFourFiveEdge_ScalesByAWidenedFloatReciprocalRoot()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((1f, 1f, 1f), (4f, 5f, 1f), (0d, 0d, 1d), 1d);

        BitConverter.DoubleToInt64Bits(evaluator.Direction.X).ShouldBe(0x3FE3333338000000L);
        BitConverter.DoubleToInt64Bits(evaluator.Direction.Y).ShouldBe(0x3FE99999A0000000L);
        evaluator.Direction.Z.ShouldBe(0d);
        evaluator.Normal.ShouldBe((0d, 0d, 1d));
    }

    /// <remarks>
    /// Both bodies unmoved, an edge running straight along the face normal: the value is one.
    /// </remarks>
    [Test]
    public void Distance_AnEdgeAlongTheFaceNormal_IsOne()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((0f, 0f, 0f), (0f, 0f, 2f), (0d, 0d, 1d), 1d);

        evaluator.Distance(Identity, Identity).ShouldBe(1d);
    }

    /// <remarks>
    /// **The face's body turns the normal into the world.** A quarter turn about X sends the face's `+Z`
    /// to `(0, −1, 0)`, and an edge along `+Y` in an unmoved body measures **−1**. Leaving the normal
    /// unturned measures 0.
    /// </remarks>
    [Test]
    public void Distance_WithTheFacesBodyTurnedAQuarterAboutX_TurnsTheNormalIntoTheWorld()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((0f, 0f, 0f), (0f, 2f, 0f), (0d, 0d, 1d), 1d);

        evaluator.Distance(Identity, QuarterTurnAboutX).ShouldBe(-1d, Tolerance);
    }

    /// <remarks>
    /// **The edge's body takes the world normal back into its own frame, by the transpose.** The face is
    /// unmoved, so the world normal is `+Z`; the edge's body is a quarter turn about X, whose inverse sends
    /// `+Z` to `+Y`, so an edge along its own `+Y` measures **+1**. Turning forward instead of back sends
    /// `+Z` to `−Y` and measures −1.
    /// </remarks>
    [Test]
    public void Distance_WithTheEdgesBodyTurnedAQuarterAboutX_TakesTheNormalIntoTheEdgesFrame()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((0f, 0f, 0f), (0f, 2f, 0f), (0d, 0d, 1d), 1d);

        evaluator.Distance(QuarterTurnAboutX, Identity).ShouldBe(1d, Tolerance);
    }

    /// <remarks>
    /// **Directions do not translate.** Both bodies moved and neither turned: still one.
    /// </remarks>
    [Test]
    public void Distance_WithBothBodiesTranslated_IgnoresTheTranslation()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((0f, 0f, 0f), (0f, 0f, 2f), (0d, 0d, 1d), 1d);

        IvpMatrix edgeBody = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (5d, 6d, 7d));
        IvpMatrix faceBody = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (-8d, 9d, -10d));

        evaluator.Distance(edgeBody, faceBody).ShouldBe(1d);
    }

    /// <remarks>
    /// **The dot adds x and y before z.** A normal of `(1, 1, 1)` against a direction of `(0.1, 0.2, 2.2)`
    /// is exactly `2.5`; both other groupings land at `2.5000000000000004`. Set directly, because only the
    /// grouping is under test.
    /// </remarks>
    [Test]
    public void Distance_TermsThatRoundDifferentlyByGrouping_SumsXAndYBeforeZ()
    {
        IvpEdgeEvaluator evaluator = new(
            Direction: (0.1d, 0.2d, 2.2d), Normal: (1d, 1d, 1d), ApproachSpeed: 1d, InverseApproachSpeed: 1d);

        BitConverter.DoubleToInt64Bits(evaluator.Distance(Identity, Identity))
            .ShouldBe(BitConverter.DoubleToInt64Bits(2.5d));
    }

    /// <remarks>
    /// **The edge evaluator's speed is the two cores' angular bounds summed in FLOAT, widened, plus `1e-19`**
    /// (`1800a1bed`–`1800a1c10`, then `ADDSD` of `DAT_1800f4f20` at `1800a1e34`). `16777216f + 1f` is `16777216f`,
    /// so the float sum is `16777216`; a double sum would be `16777217`. The floor is too small to move it.
    /// </remarks>
    [Test]
    public void SpeedBound_TwoCoresBounds_SumInFloatBeforeWidening() =>
        IvpEdgeEvaluator.SpeedBound(16777216f, 1f).ShouldBe(16777216d);

    /// <remarks>
    /// **Two cores that are not turning still give a speed**, the floor alone, so the evaluator's reciprocal is
    /// `1e19` rather than a division by zero.
    /// </remarks>
    [Test]
    public void SpeedBound_TwoCoresNotTurning_IsTheFloor() =>
        IvpEdgeEvaluator.SpeedBound(0f, 0f).ShouldBe(1e-19d);

    /// <remarks>
    /// **The speed is stored with one over it** at `+0x08` and `+0x10` (`1800a1e46`–`1800a1e57`), the pair every
    /// root finder reads.
    /// </remarks>
    [Test]
    public void ForEdge_AnApproachSpeed_IsCarriedWithOneOverIt()
    {
        IvpEdgeEvaluator evaluator = IvpEdgeEvaluator.ForEdge((0f, 0f, 0f), (0f, 0f, 2f), (0d, 0d, 1d), 1e-19d);

        evaluator.ApproachSpeed.ShouldBe(1e-19d);
        evaluator.InverseApproachSpeed.ShouldBe(1e19d);
    }
}
