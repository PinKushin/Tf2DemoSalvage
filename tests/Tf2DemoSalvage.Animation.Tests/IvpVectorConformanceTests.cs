using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The three double-precision vector routines IVP builds a face's plane with before measuring a vertex
/// against it (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *The four routines under the
/// point-plane evaluator*:
///
/// <code>
///   FUN_18007b940  a = P₁ − P₀, b = P₂ − P₀, each point widened to double BEFORE subtracting;
///                  n = (a.y·b.z − a.z·b.y,  a.z·b.x − a.x·b.z,  a.x·b.y − a.y·b.x), NOT normalised
///   FUN_18006e080  s = (x·x + y·y) + z·z;  COMISD s, 1e-19;  JNC — so s ≥ 1e-19 and not NaN
///                  scales x, y, z by FUN_18006ecf0(s) and returns 1; otherwise touches nothing, returns 0
///   FUN_18006ecf0  high word h of x;  guess = double with high word ((0x7ff00000 − h) SAR 1) + 0x1ff00000,
///                  low word 0;  four times  r = r · ((0.5 − (r·r)·(x·0.5)) + 1.0)
/// </code>
/// </remarks>
public sealed class IvpVectorConformanceTests
{
    private const double Tolerance = 1e-12d;

    /// <remarks>
    /// `(2,0,0) × (0,3,0)` = `(0,0,6)`, **and left at length six**: the routine does not normalise, so an
    /// implementation that did would land on `(0,0,1)`.
    /// </remarks>
    [Test]
    public void FaceNormal_ACounterClockwiseTriangleInXy_IsTheUnnormalisedCrossUpZ()
    {
        IvpVector.FaceNormal((0f, 0f, 0f), (2f, 0f, 0f), (0f, 3f, 0f)).ShouldBe((0d, 0d, 6d));
    }

    /// <remarks>
    /// **The winding control.** The same triangle with its last two points exchanged points the other
    /// way; an implementation that took an absolute value, or crossed in the wrong order, cannot pass both.
    /// </remarks>
    [Test]
    public void FaceNormal_TheSameTriangleWoundTheOtherWay_PointsDownZ()
    {
        IvpVector.FaceNormal((0f, 0f, 0f), (0f, 3f, 0f), (2f, 0f, 0f)).ShouldBe((0d, 0d, -6d));
    }

    /// <remarks>
    /// **Edges from the first point, every component distinct.** The points are `(1,1,1)` plus `(0,0,0)`,
    /// `(1,2,3)` and `(4,5,6)`, so the edges are `(1,2,3)` and `(4,5,6)` and their cross is `(−3, 6, −3)`.
    /// Crossing the points themselves gives `(−1, 2, −1)`; an exchanged component formula moves a sign or
    /// a value between lanes.
    /// </remarks>
    [Test]
    public void FaceNormal_ATriangleAwayFromTheOrigin_CrossesItsEdgesFromTheFirstPoint()
    {
        IvpVector.FaceNormal((1f, 1f, 1f), (2f, 3f, 4f), (5f, 6f, 7f)).ShouldBe((-3d, 6d, -3d));
    }

    /// <remarks>
    /// **A vector too short to have a direction is left alone**, and the call says so: three points on
    /// one line cross to exactly zero.
    /// </remarks>
    [Test]
    public void Normalise_ADegenerateTriangle_LeavesItAndReportsFalse()
    {
        (double X, double Y, double Z) normal = IvpVector.FaceNormal((0f, 0f, 0f), (1f, 0f, 0f), (2f, 0f, 0f));

        IvpVector.Normalise(ref normal).ShouldBeFalse();

        normal.ShouldBe((0d, 0d, 0d));
    }

    /// <remarks>
    /// **The threshold is `1e-19` on the squared length, inclusive.** `3.2e-10` squares to `1.024e-19` and
    /// is normalised; `3.1e-10` squares to `9.61e-20` and is not. A threshold of `1e-12`, or one on the
    /// length rather than its square, fails the first; no threshold at all fails the second.
    /// </remarks>
    [TestCase(3.2e-10d, true)]
    [TestCase(3.1e-10d, false)]
    public void Normalise_AroundTheThreshold_ScalesOnlyAtOrAbove1e19Squared(double length, bool normalised)
    {
        (double X, double Y, double Z) vector = (length, 0d, 0d);

        IvpVector.Normalise(ref vector).ShouldBe(normalised);

        vector.X.ShouldBe(normalised ? 1d : length, normalised ? Tolerance : 0d);
    }

    /// <remarks>
    /// **`JNC` after `COMISD` does not take the unordered case**, so a NaN reports false and is left
    /// alone. `!(s &lt; 1e-19)` would normalise it.
    /// </remarks>
    [Test]
    public void Normalise_ANaNComponent_LeavesItAndReportsFalse()
    {
        (double X, double Y, double Z) vector = (double.NaN, 1d, 1d);

        IvpVector.Normalise(ref vector).ShouldBeFalse();

        vector.Y.ShouldBe(1d);
    }

    /// <remarks>
    /// `(3, 0, 4)` has length five, so it scales to `(0.6, 0, 0.8)`.
    /// </remarks>
    [Test]
    public void Normalise_ALongVector_ScalesToUnitLength()
    {
        (double X, double Y, double Z) vector = (3d, 0d, 4d);

        IvpVector.Normalise(ref vector).ShouldBeTrue();

        vector.X.ShouldBe(0.6d, Tolerance);
        vector.Y.ShouldBe(0d);
        vector.Z.ShouldBe(0.8d, Tolerance);
    }

    /// <remarks>
    /// **Exact bits, and two of them are not `1/√x`.** Four and a quarter start from an exact guess and
    /// stay exact. Three starts from `0.625` and thirty-six from `0.1875`, and four Newton steps leave them
    /// short of convergence: `0.5773502691896244` against `1/√3`'s `…258`, and `0.1666666666666665` against
    /// `…666`. So `1 / Math.Sqrt(x)` fails those two, as does any other guess or step count. Replicated from
    /// the disassembly's instruction order; exchanging the order within one Newton step's products was
    /// checked and lands on the same bits for three.
    /// </remarks>
    [TestCase(4d, 0x3FE0000000000000L)]
    [TestCase(0.25d, 0x4000000000000000L)]
    [TestCase(3d, 0x3FE279A745903310L)]
    [TestCase(36d, 0x3FC555555555554FL)]
    public void ReciprocalSquareRoot_FourNewtonStepsFromTheBitGuess_LandOnTheEnginesBits(double square, long bits)
    {
        BitConverter.DoubleToInt64Bits(IvpVector.ReciprocalSquareRoot(square)).ShouldBe(bits);
    }
}
